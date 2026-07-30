using Intranet.WorkflowStudio.Runtime;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Configuration;
using System.Data;
using System.Data.SqlClient;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Web;

namespace Intranet.WorkflowStudio.Dispatcher.WatchFolder
{
    public sealed class WatchFolderDispatcher
    {
        private const string DispatcherUser = "watchfolder";
        private const string ReceiptSuffix = ".wf.json";
        private const string LockSuffix = ".wf.lock";

        private readonly WatchFolderOptions _opt;
        private readonly string _cnn;
        private readonly IngressRouter _router;

        public WatchFolderDispatcher(WatchFolderOptions opt)
        {
            _opt = opt ?? throw new ArgumentNullException(nameof(opt));

            var cs = ConfigurationManager.ConnectionStrings["DefaultConnection"];
            if (cs == null || string.IsNullOrWhiteSpace(cs.ConnectionString))
                throw new ConfigurationErrorsException("Falta connectionString 'DefaultConnection' en App.config.");

            _cnn = cs.ConnectionString;

            if (_opt.RouterEnabled)
            {
                _router = new IngressRouter(_cnn, _opt.ChannelCode);
                _router.EnsureSchema();
            }
        }

        public void RunLoop()
        {
            DispatcherDailyLog.Info("Presioná Ctrl+C para salir.");

            var quit = new ManualResetEvent(false);
            Console.CancelKeyPress += (s, e) =>
            {
                e.Cancel = true;
                quit.Set();
            };

            TickSafe();

            while (!quit.WaitOne(TimeSpan.FromSeconds(_opt.PollSeconds)))
                TickSafe();
        }

        private void TickSafe()
        {
            try
            {
                TickOnce();
            }
            catch (Exception ex)
            {
                DispatcherDailyLog.Error("[Tick/error] " + ex.GetType().Name + ": " + ex.Message);
            }
        }

        private void TickOnce()
        {
            ReconcileProcessingReceipts();
            AdoptProcessingFilesWithoutReceipt();
            ClaimInputFiles();
        }

        private void ClaimInputFiles()
        {
            var files = Directory.GetFiles(_opt.InputFolder, _opt.Pattern)
                                 .Where(IsDocumentCandidate)
                                 .OrderBy(f => f, StringComparer.OrdinalIgnoreCase)
                                 .ToList();

            foreach (var inputPath in files)
            {
                if (!IsStableFile(inputPath))
                    continue;

                string processingPath = UniqueDocumentPath(_opt.ProcessingFolder, Path.GetFileName(inputPath));

                try
                {
                    // Claim atómico: dos workers no pueden mover el mismo archivo.
                    File.Move(inputPath, processingPath);
                }
                catch (FileNotFoundException)
                {
                    continue;
                }
                catch (IOException)
                {
                    // Otro worker pudo reclamarlo entre el listado y el move.
                    continue;
                }

                DispatcherDailyLog.Info("");
                DispatcherDailyLog.Info("[Claim] " + Path.GetFileName(inputPath));
                DispatcherDailyLog.Info("[Claim] -> " + processingPath);

                // El recibo también se crea bajo lease. Así, si otro worker ve el
                // archivo inmediatamente después del move, uno solo inicializa
                // el IngressId y ninguno sobrescribe el recibo del otro.
                ProcessDocumentWithLease(processingPath, Path.GetFileName(inputPath));
            }
        }

        private void ReconcileProcessingReceipts()
        {
            var receipts = Directory.GetFiles(_opt.ProcessingFolder, "*" + ReceiptSuffix)
                                    .OrderBy(f => f, StringComparer.OrdinalIgnoreCase)
                                    .ToList();

            foreach (var receiptPath in receipts)
            {
                string documentPath = receiptPath.Substring(0, receiptPath.Length - ReceiptSuffix.Length);
                ProcessDocumentWithLease(documentPath, null);
            }
        }

        private void AdoptProcessingFilesWithoutReceipt()
        {
            var documents = Directory.GetFiles(_opt.ProcessingFolder, _opt.Pattern)
                                     .Where(IsDocumentCandidate)
                                     .OrderBy(f => f, StringComparer.OrdinalIgnoreCase)
                                     .ToList();

            foreach (var documentPath in documents)
            {
                if (File.Exists(ReceiptPath(documentPath)))
                    continue;

                ProcessDocumentWithLease(documentPath, null);
            }
        }

        private void ProcessDocumentWithLease(string documentPath, string originalFileNameHint)
        {
            string lockPath = documentPath + LockSuffix;
            FileStream lease = null;

            try
            {
                lease = TryAcquireLease(lockPath);
                if (lease == null)
                    return;

                string receiptPath = ReceiptPath(documentPath);
                var receipt = LoadReceipt(receiptPath);

                if (receipt == null)
                {
                    if (!File.Exists(documentPath))
                        return;

                    receipt = WatchFolderReceipt.Create(
                        documentPath,
                        string.IsNullOrWhiteSpace(originalFileNameHint)
                            ? Path.GetFileName(documentPath)
                            : originalFileNameHint,
                        _opt.ChannelCode,
                        _opt.RouterEnabled ? null : _opt.WorkflowName);

                    receipt.DispatcherState = string.IsNullOrWhiteSpace(originalFileNameHint)
                        ? "RecoveredWithoutReceipt"
                        : "Claimed";
                    SaveReceipt(receiptPath, receipt);

                    if (string.IsNullOrWhiteSpace(originalFileNameHint))
                    {
                        DispatcherDailyLog.Info("");
                        DispatcherDailyLog.Info("[Recover] Archivo en Processing sin recibo: " + documentPath);
                        DispatcherDailyLog.Info("[Recover] ingressId=" + receipt.IngressId);
                    }
                    else
                    {
                        DispatcherDailyLog.Info("[Claim] ingressId=" + receipt.IngressId);
                    }
                }

                bool receiptChanged = false;

                if (string.IsNullOrWhiteSpace(receipt.ChannelCode))
                {
                    receipt.ChannelCode = _opt.ChannelCode;
                    receiptChanged = true;
                }

                if (!_opt.RouterEnabled && string.IsNullOrWhiteSpace(receipt.WorkflowName))
                {
                    receipt.WorkflowName = _opt.WorkflowName;
                    receiptChanged = true;
                }

                if (receipt.Version < 2)
                {
                    receipt.Version = 2;
                    receiptChanged = true;
                }

                if (receiptChanged)
                    SaveReceipt(receiptPath, receipt);

                ProcessReceipt(receiptPath, receipt);
            }
            catch (Exception ex)
            {
                DispatcherDailyLog.Error("[Processing/error] " + Path.GetFileName(documentPath) + " - " + ex.Message);
            }
            finally
            {
                if (lease != null)
                    lease.Dispose();

                TryDelete(lockPath);
            }
        }

        private void ProcessReceipt(string receiptPath, WatchFolderReceipt receipt)
        {
            receipt.UpdatedUtc = UtcNow();

            // Registra también recibos provenientes de fix75 o recuperados después
            // de una caída, aunque la instancia ya exista.
            if (_router != null)
                _router.RegisterClaim(receipt);

            // Si una caída ocurrió después del move final, el recibo de Processing
            // conserva FinalFilePath y permite completar DB + sidecar sin duplicar.
            if (!string.IsNullOrWhiteSpace(receipt.FinalFilePath) &&
                !File.Exists(receipt.CurrentFilePath) &&
                File.Exists(receipt.FinalFilePath))
            {
                CompleteFinalization(receiptPath, receipt);
                return;
            }

            if (!receipt.InstanceId.HasValue)
            {
                var recovered = FindInstanceByIngressId(receipt.IngressId);
                if (recovered != null)
                {
                    receipt.InstanceId = recovered.Id;
                    receipt.InstanceState = recovered.State;
                    receipt.DispatcherState = "InstanceRecovered";
                    SaveReceipt(receiptPath, receipt);

                    if (_router != null)
                        _router.MarkInstance(receipt, recovered.Id, recovered.State);

                    DispatcherDailyLog.Info("[Recover] instanciaId=" + recovered.Id + " ingressId=" + receipt.IngressId);
                }
            }

            if (!receipt.InstanceId.HasValue)
            {
                if (_opt.RouterEnabled)
                {
                    var decision = _router.Resolve(receipt);
                    if (!decision.IsResolved)
                    {
                        bool changed =
                            !string.Equals(receipt.DispatcherState, "WaitingRoute", StringComparison.OrdinalIgnoreCase) ||
                            !string.Equals(receipt.RouteReason, decision.Reason, StringComparison.Ordinal);

                        receipt.DispatcherState = "WaitingRoute";
                        receipt.RouteReason = decision.Reason;
                        receipt.LastError = null;

                        if (changed)
                        {
                            SaveReceipt(receiptPath, receipt);
                            DispatcherDailyLog.Info("[Route/pending] " + receipt.OriginalFileName + " - " + decision.Reason);
                        }

                        return;
                    }

                    ApplyRouteDecision(receipt, decision);
                    SaveReceipt(receiptPath, receipt);

                    DispatcherDailyLog.Info(
                        "[Route] " + receipt.OriginalFileName +
                        " -> defId=" + receipt.DefinitionId +
                        " workflow=" + receipt.WorkflowName +
                        " origen=" + receipt.RouteSource);
                }

                if (!File.Exists(receipt.CurrentFilePath))
                {
                    receipt.DispatcherState = "MissingFile";
                    receipt.LastError = "No existe el archivo reclamado: " + receipt.CurrentFilePath;
                    SaveReceipt(receiptPath, receipt);

                    if (_router != null)
                        _router.MarkDispatcherError(receipt, receipt.LastError);

                    DispatcherDailyLog.Error("[Missing] " + receipt.LastError);
                    return;
                }

                CreateAndRunInstance(receiptPath, receipt);

                if (!receipt.InstanceId.HasValue)
                    return;
            }

            var instance = GetInstance(receipt.InstanceId.Value);
            if (instance == null)
            {
                receipt.DispatcherState = "MissingInstance";
                receipt.LastError = "No existe WF_Instancia.Id=" + receipt.InstanceId.Value;
                SaveReceipt(receiptPath, receipt);

                if (_router != null)
                    _router.MarkDispatcherError(receipt, receipt.LastError);

                DispatcherDailyLog.Error("[Missing] " + receipt.LastError);
                return;
            }

            string previousInstanceState = receipt.InstanceState;
            string previousDispatcherState = receipt.DispatcherState;
            string previousError = receipt.LastError;

            receipt.InstanceState = instance.State;
            receipt.LastError = null;

            if (_router != null)
                _router.MarkState(receipt, instance.State, receipt.CurrentFilePath, null);

            if (string.Equals(instance.State, "Finalizado", StringComparison.OrdinalIgnoreCase))
            {
                FinalizeByInstanceState(receiptPath, receipt, _opt.ProcessedFolder, "Completed");
                return;
            }

            if (string.Equals(instance.State, "Error", StringComparison.OrdinalIgnoreCase))
            {
                FinalizeByInstanceState(receiptPath, receipt, _opt.ErrorFolder, "WorkflowError");
                return;
            }

            if (string.Equals(instance.State, "EnCurso", StringComparison.OrdinalIgnoreCase))
            {
                bool changed =
                    !string.Equals(previousDispatcherState, "WaitingWorkflow", StringComparison.OrdinalIgnoreCase) ||
                    !string.Equals(previousInstanceState, instance.State, StringComparison.OrdinalIgnoreCase) ||
                    !string.IsNullOrWhiteSpace(previousError);

                receipt.DispatcherState = "WaitingWorkflow";
                if (changed)
                {
                    SaveReceipt(receiptPath, receipt);
                    DispatcherDailyLog.Info("[Waiting] instanciaId=" + instance.Id +
                                      " estado=EnCurso; archivo permanece en Processing.");
                }
                return;
            }

            receipt.DispatcherState = "UnknownInstanceState";
            receipt.LastError = "Estado no reconocido: " + (instance.State ?? "(null)");

            if (_router != null)
                _router.MarkState(receipt, instance.State, receipt.CurrentFilePath, receipt.LastError);

            bool unknownChanged =
                !string.Equals(previousDispatcherState, receipt.DispatcherState, StringComparison.OrdinalIgnoreCase) ||
                !string.Equals(previousInstanceState, instance.State, StringComparison.OrdinalIgnoreCase) ||
                !string.Equals(previousError, receipt.LastError, StringComparison.Ordinal);

            if (unknownChanged)
            {
                SaveReceipt(receiptPath, receipt);
                DispatcherDailyLog.Error("[State/warn] instanciaId=" + instance.Id + " " + receipt.LastError);
            }
        }

        private void CreateAndRunInstance(string receiptPath, WatchFolderReceipt receipt)
        {
            int defId;
            string inputJson;

            try
            {
                if (receipt.DefinitionId.HasValue)
                {
                    var definition = ResolveActiveDefinitionById(receipt.DefinitionId.Value);
                    defId = definition.Id;
                    receipt.WorkflowName = definition.Name;
                }
                else
                {
                    string configuredWorkflow = receipt.WorkflowName ?? _opt.WorkflowName;
                    defId = ResolveDefIdByName(configuredWorkflow);
                    receipt.DefinitionId = defId;
                    receipt.WorkflowName = configuredWorkflow;
                }

                receipt.DispatcherState = "StartingInstance";
                SaveReceipt(receiptPath, receipt);

                var input = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase)
                {
                    [_opt.WorkflowInputField] = receipt.CurrentFilePath,
                    ["filePath"] = receipt.CurrentFilePath,
                    ["fileName"] = receipt.OriginalFileName,
                    ["watchFolderIngressId"] = receipt.IngressId,
                    ["watchFolderOriginalFileName"] = receipt.OriginalFileName,
                    ["watchFolderCurrentFilePath"] = receipt.CurrentFilePath,
                    ["watchFolderClaimedUtc"] = receipt.ClaimedUtc,
                    ["watchFolderChannelCode"] = receipt.ChannelCode,
                    ["watchFolderWorkflowName"] = receipt.WorkflowName,
                    ["watchFolderDefinitionId"] = receipt.DefinitionId,
                    ["watchFolderRouteId"] = receipt.RouteId,
                    ["watchFolderRouteSource"] = receipt.RouteSource,
                    ["watchFolderRouteReason"] = receipt.RouteReason,
                    ["watchFolderRouteConfidence"] = receipt.RouteConfidence
                };

                inputJson = JsonConvert.SerializeObject(input, Formatting.None);
                EnsureHttpContext();
            }
            catch (Exception ex)
            {
                HandleDispatchErrorWithoutRuntime(receiptPath, receipt, ex);
                return;
            }

            long instId;
            try
            {
                instId = WorkflowRuntime
                    .CrearInstanciaYEjecutarAsync(defId, inputJson, DispatcherUser)
                    .GetAwaiter().GetResult();
            }
            catch (Exception ex)
            {
                HandleRuntimeException(receiptPath, receipt, ex);
                return;
            }

            // A partir de aquí el runtime ya retornó correctamente. Un fallo al
            // guardar el recibo no debe convertir una human.task EnCurso en Error:
            // al reiniciar se recuperará la instancia por ingressId.
            receipt.InstanceId = instId;
            receipt.DispatcherState = "InstanceCreated";
            receipt.UpdatedUtc = UtcNow();
            SaveReceipt(receiptPath, receipt);

            if (_router != null)
                _router.MarkInstance(receipt, instId, null);

            TryAddInstanceLog(
                instId,
                "Info",
                "[WatchFolder] Ingreso aceptado. ingressId=" + receipt.IngressId +
                "; canal=" + receipt.ChannelCode +
                "; origenRuta=" + (receipt.RouteSource ?? "DIRECTO") +
                "; archivo=" + receipt.OriginalFileName +
                "; ruta=" + receipt.CurrentFilePath);

            DispatcherDailyLog.Info("[Instance] instanciaId=" + instId);
        }

        private void HandleDispatchErrorWithoutRuntime(
            string receiptPath,
            WatchFolderReceipt receipt,
            Exception ex)
        {
            receipt.DispatcherState = "DispatchErrorWithoutInstance";
            receipt.InstanceState = "SinInstancia";
            receipt.LastError = ex.GetType().Name + ": " + ex.Message;
            SaveReceipt(receiptPath, receipt);

            if (_router != null)
                _router.MarkDispatcherError(receipt, receipt.LastError);

            DispatcherDailyLog.Error("[FAIL] " + receipt.LastError);
            FinalizeByInstanceState(receiptPath, receipt, _opt.ErrorFolder, "DispatcherError");
        }

        private void HandleRuntimeException(
            string receiptPath,
            WatchFolderReceipt receipt,
            Exception ex)
        {
            // La creación puede haber insertado la instancia antes de lanzar.
            // Se recupera por ingressId para no crearla nuevamente al reiniciar.
            var recovered = FindInstanceByIngressId(receipt.IngressId);
            if (recovered != null)
            {
                receipt.InstanceId = recovered.Id;
                receipt.InstanceState = recovered.State;

                if (!string.Equals(recovered.State, "Finalizado", StringComparison.OrdinalIgnoreCase) &&
                    !string.Equals(recovered.State, "Error", StringComparison.OrdinalIgnoreCase))
                {
                    MarkInstanceDispatchError(recovered.Id, ex);
                    receipt.InstanceState = "Error";
                }

                receipt.DispatcherState = "RuntimeExceptionRecovered";
                receipt.LastError = ex.GetType().Name + ": " + ex.Message;
                SaveReceipt(receiptPath, receipt);

                if (_router != null)
                {
                    _router.MarkInstance(receipt, recovered.Id, receipt.InstanceState);
                    _router.MarkState(receipt, receipt.InstanceState, receipt.CurrentFilePath, receipt.LastError);
                }

                DispatcherDailyLog.Error("[FAIL] instanciaId=" + recovered.Id + " - " + receipt.LastError);
                return;
            }

            HandleDispatchErrorWithoutRuntime(receiptPath, receipt, ex);
        }

        private void FinalizeByInstanceState(
            string receiptPath,
            WatchFolderReceipt receipt,
            string destinationFolder,
            string dispatcherState)
        {
            if (!_opt.MoveAfter)
            {
                bool changed = !string.Equals(receipt.DispatcherState, dispatcherState + "InProcessing", StringComparison.OrdinalIgnoreCase);
                receipt.DispatcherState = dispatcherState + "InProcessing";

                if (changed)
                {
                    SaveReceipt(receiptPath, receipt);
                    DispatcherDailyLog.Info("[State] instanciaId=" +
                                      (receipt.InstanceId.HasValue ? receipt.InstanceId.Value.ToString() : "(sin instancia)") +
                                      " estado=" + (receipt.InstanceState ?? dispatcherState) +
                                      "; MoveAfter=false, permanece en Processing.");
                }
                return;
            }

            if (string.IsNullOrWhiteSpace(receipt.FinalFilePath))
            {
                receipt.FinalFilePath = UniqueDocumentPath(destinationFolder, receipt.OriginalFileName);
                receipt.DispatcherState = dispatcherState + "MovePending";
                receipt.UpdatedUtc = UtcNow();
                SaveReceipt(receiptPath, receipt);
            }

            if (File.Exists(receipt.CurrentFilePath))
            {
                if (File.Exists(receipt.FinalFilePath))
                {
                    throw new IOException(
                        "Existen simultáneamente el archivo de Processing y el destino final: " + receipt.FinalFilePath);
                }

                File.Move(receipt.CurrentFilePath, receipt.FinalFilePath);
            }
            else if (!File.Exists(receipt.FinalFilePath))
            {
                throw new FileNotFoundException(
                    "No existe el archivo ni en Processing ni en el destino final.",
                    receipt.CurrentFilePath);
            }

            receipt.DispatcherState = dispatcherState + "FileMoved";
            receipt.UpdatedUtc = UtcNow();
            SaveReceipt(receiptPath, receipt);

            CompleteFinalization(receiptPath, receipt, dispatcherState);
        }

        private void CompleteFinalization(string receiptPath, WatchFolderReceipt receipt)
        {
            CompleteFinalization(receiptPath, receipt, DetermineFinalDispatcherState(receipt));
        }

        private static string DetermineFinalDispatcherState(WatchFolderReceipt receipt)
        {
            string state = receipt == null ? null : receipt.DispatcherState;

            if (!string.IsNullOrWhiteSpace(state))
            {
                if (state.StartsWith("DispatcherError", StringComparison.OrdinalIgnoreCase))
                    return "DispatcherError";

                if (state.StartsWith("WorkflowError", StringComparison.OrdinalIgnoreCase))
                    return "WorkflowError";
            }

            if (receipt != null &&
                string.Equals(receipt.InstanceState, "Error", StringComparison.OrdinalIgnoreCase))
            {
                return "WorkflowError";
            }

            return "Completed";
        }

        private void CompleteFinalization(string receiptPath, WatchFolderReceipt receipt, string dispatcherState)
        {
            if (receipt.InstanceId.HasValue)
            {
                UpdateInstanceInputPath(
                    receipt.InstanceId.Value,
                    receipt.FinalFilePath,
                    dispatcherState);

                TryAddInstanceLog(
                    receipt.InstanceId.Value,
                    "Info",
                    "[WatchFolder] Archivo ubicado en destino final. estado=" +
                    (receipt.InstanceState ?? dispatcherState) +
                    "; ruta=" + receipt.FinalFilePath +
                    "; ingressId=" + receipt.IngressId);
            }

            receipt.CurrentFilePath = receipt.FinalFilePath;
            receipt.DispatcherState = dispatcherState;
            receipt.UpdatedUtc = UtcNow();

            if (_router != null)
            {
                if (string.Equals(dispatcherState, "DispatcherError", StringComparison.OrdinalIgnoreCase))
                    _router.MarkDispatcherError(receipt, receipt.LastError);
                else
                    _router.MarkState(receipt, receipt.InstanceState, receipt.FinalFilePath, receipt.LastError);
            }

            string finalReceiptPath = ReceiptPath(receipt.FinalFilePath);
            SaveReceipt(finalReceiptPath, receipt);

            if (!string.Equals(receiptPath, finalReceiptPath, StringComparison.OrdinalIgnoreCase))
                TryDelete(receiptPath);

            DispatcherDailyLog.Info("[Move] estado=" + (receipt.InstanceState ?? dispatcherState) +
                              " -> " + receipt.FinalFilePath);
        }

        private bool IsStableFile(string path)
        {
            try
            {
                long lastLen = -1;
                DateTime lastWrite = DateTime.MinValue;

                for (int i = 0; i < _opt.StableChecks; i++)
                {
                    var fi = new FileInfo(path);
                    if (!fi.Exists) return false;

                    if (i > 0)
                    {
                        if (fi.Length != lastLen) return false;
                        if (fi.LastWriteTimeUtc != lastWrite) return false;
                    }

                    lastLen = fi.Length;
                    lastWrite = fi.LastWriteTimeUtc;

                    if (i < _opt.StableChecks - 1 && _opt.StableDelayMs > 0)
                        Thread.Sleep(_opt.StableDelayMs);
                }

                // Se exige apertura exclusiva: el productor ya debe haber cerrado el archivo.
                using (File.Open(path, FileMode.Open, FileAccess.Read, FileShare.None))
                {
                    return true;
                }
            }
            catch
            {
                return false;
            }
        }

        private static void ApplyRouteDecision(WatchFolderReceipt receipt, IngressRouteDecision decision)
        {
            receipt.DefinitionId = decision.DefinitionId;
            receipt.WorkflowName = decision.WorkflowName;
            receipt.RouteId = decision.RouteId;
            receipt.RouteSource = decision.Source;
            receipt.RouteReason = decision.Reason;
            receipt.RouteConfidence = decision.Confidence;
            receipt.DispatcherState = "RouteResolved";
            receipt.LastError = null;
        }

        private DefinitionInfo ResolveActiveDefinitionById(int definitionId)
        {
            using (var cn = new SqlConnection(_cnn))
            using (var cmd = cn.CreateCommand())
            {
                cmd.CommandText = @"
SELECT Id, Nombre
FROM dbo.WF_Definicion
WHERE Id = @Id
  AND Activo = 1;";

                cmd.Parameters.Add("@Id", SqlDbType.Int).Value = definitionId;
                cn.Open();

                using (var dr = cmd.ExecuteReader())
                {
                    if (!dr.Read())
                    {
                        throw new InvalidOperationException(
                            "No existe una WF_Definicion activa con Id=" + definitionId + ".");
                    }

                    return new DefinitionInfo
                    {
                        Id = Convert.ToInt32(dr["Id"]),
                        Name = Convert.ToString(dr["Nombre"])
                    };
                }
            }
        }

        private int ResolveDefIdByName(string workflowName)
        {
            using (var cn = new SqlConnection(_cnn))
            using (var cmd = cn.CreateCommand())
            {
                cmd.CommandText = @"
SELECT Id
FROM dbo.WF_Definicion
WHERE Nombre = @Nombre
ORDER BY Id;";

                cmd.Parameters.Add("@Nombre", SqlDbType.NVarChar, 200).Value = workflowName;
                cn.Open();

                var ids = new List<int>();
                using (var dr = cmd.ExecuteReader())
                {
                    while (dr.Read() && ids.Count < 2)
                        ids.Add(Convert.ToInt32(dr["Id"]));
                }

                if (ids.Count == 0)
                {
                    throw new InvalidOperationException(
                        "No existe WF_Definicion.Nombre='" + workflowName + "'.");
                }

                if (ids.Count > 1)
                {
                    throw new InvalidOperationException(
                        "Hay más de una WF_Definicion con Nombre='" + workflowName +
                        "'. El nombre configurado debe identificar una sola definición.");
                }

                return ids[0];
            }
        }

        private InstanceInfo FindInstanceByIngressId(string ingressId)
        {
            if (string.IsNullOrWhiteSpace(ingressId))
                return null;

            using (var cn = new SqlConnection(_cnn))
            using (var cmd = cn.CreateCommand())
            {
                cmd.CommandText = @"
SELECT TOP (2) Id, Estado
FROM dbo.WF_Instancia
WHERE DatosEntrada LIKE @Needle
ORDER BY Id ASC;";

                cmd.Parameters.Add("@Needle", SqlDbType.NVarChar, 300).Value =
                    "%\"watchFolderIngressId\":\"" + ingressId + "\"%";

                cn.Open();

                var rows = new List<InstanceInfo>();
                using (var dr = cmd.ExecuteReader())
                {
                    while (dr.Read())
                    {
                        rows.Add(new InstanceInfo
                        {
                            Id = Convert.ToInt64(dr["Id"]),
                            State = dr["Estado"] == DBNull.Value ? null : Convert.ToString(dr["Estado"])
                        });
                    }
                }

                if (rows.Count > 1)
                {
                    DispatcherDailyLog.Error(
                        "[Duplicate/warn] ingressId=" + ingressId +
                        " aparece en más de una instancia. Se conserva la primera: " + rows[0].Id);
                }

                return rows.FirstOrDefault();
            }
        }

        private InstanceInfo GetInstance(long instanceId)
        {
            using (var cn = new SqlConnection(_cnn))
            using (var cmd = cn.CreateCommand())
            {
                cmd.CommandText = @"
SELECT Id, Estado
FROM dbo.WF_Instancia
WHERE Id = @Id;";

                cmd.Parameters.Add("@Id", SqlDbType.BigInt).Value = instanceId;
                cn.Open();

                using (var dr = cmd.ExecuteReader())
                {
                    if (!dr.Read())
                        return null;

                    return new InstanceInfo
                    {
                        Id = Convert.ToInt64(dr["Id"]),
                        State = dr["Estado"] == DBNull.Value ? null : Convert.ToString(dr["Estado"])
                    };
                }
            }
        }

        private void UpdateInstanceInputPath(long instanceId, string finalPath, string dispatcherState)
        {
            if (string.IsNullOrWhiteSpace(finalPath))
                return;

            using (var cn = new SqlConnection(_cnn))
            {
                cn.Open();

                string datosEntrada;
                string datosContexto;
                using (var cmdRead = cn.CreateCommand())
                {
                    cmdRead.CommandText = @"
SELECT DatosEntrada, DatosContexto
FROM dbo.WF_Instancia
WHERE Id = @Id;";
                    cmdRead.Parameters.Add("@Id", SqlDbType.BigInt).Value = instanceId;

                    using (var dr = cmdRead.ExecuteReader())
                    {
                        if (!dr.Read())
                            throw new InvalidOperationException("No existe WF_Instancia.Id=" + instanceId + ".");

                        datosEntrada = dr["DatosEntrada"] == DBNull.Value
                            ? "{}"
                            : Convert.ToString(dr["DatosEntrada"]);

                        datosContexto = dr["DatosContexto"] == DBNull.Value
                            ? null
                            : Convert.ToString(dr["DatosContexto"]);
                    }
                }

                JObject input;
                try
                {
                    input = string.IsNullOrWhiteSpace(datosEntrada)
                        ? new JObject()
                        : JObject.Parse(datosEntrada);
                }
                catch
                {
                    throw new InvalidOperationException(
                        "WF_Instancia.Id=" + instanceId + " tiene DatosEntrada que no es JSON válido.");
                }

                string finalizedUtc = UtcNow();
                input[_opt.WorkflowInputField] = finalPath;
                input["filePath"] = finalPath;
                input["watchFolderCurrentFilePath"] = finalPath;
                input["watchFolderFinalState"] = dispatcherState;
                input["watchFolderFinalizedUtc"] = finalizedUtc;

                string contextoActualizado = datosContexto;
                if (!string.IsNullOrWhiteSpace(datosContexto))
                {
                    try
                    {
                        var context = JObject.Parse(datosContexto);
                        var estado = context["estado"] as JObject;
                        if (estado != null)
                        {
                            var stateInput = estado["input"] as JObject;
                            if (stateInput == null)
                            {
                                stateInput = new JObject();
                                estado["input"] = stateInput;
                            }

                            stateInput[_opt.WorkflowInputField] = finalPath;
                            stateInput["filePath"] = finalPath;
                            stateInput["watchFolderCurrentFilePath"] = finalPath;
                            stateInput["watchFolderFinalState"] = dispatcherState;
                            stateInput["watchFolderFinalizedUtc"] = finalizedUtc;

                            // Alias plano útil para pantallas o diagnósticos que lean Estado.
                            estado["input.filePath"] = finalPath;
                        }

                        contextoActualizado = context.ToString(Formatting.None);
                    }
                    catch
                    {
                        // DatosEntrada es el contrato de reanudación. Si un contexto histórico
                        // no puede parsearse, se conserva sin bloquear el movimiento final.
                        contextoActualizado = datosContexto;
                    }
                }

                using (var cmdUpdate = cn.CreateCommand())
                {
                    cmdUpdate.CommandText = @"
UPDATE dbo.WF_Instancia
SET DatosEntrada = @DatosEntrada,
    DatosContexto = @DatosContexto
WHERE Id = @Id;";

                    cmdUpdate.Parameters.Add("@DatosEntrada", SqlDbType.NVarChar).Value =
                        input.ToString(Formatting.None);
                    cmdUpdate.Parameters.Add("@DatosContexto", SqlDbType.NVarChar).Value =
                        (object)contextoActualizado ?? DBNull.Value;
                    cmdUpdate.Parameters.Add("@Id", SqlDbType.BigInt).Value = instanceId;
                    cmdUpdate.ExecuteNonQuery();
                }
            }
        }

        private void MarkInstanceDispatchError(long instanceId, Exception ex)
        {
            using (var cn = new SqlConnection(_cnn))
            {
                cn.Open();

                using (var cmd = cn.CreateCommand())
                {
                    cmd.CommandText = @"
UPDATE dbo.WF_Instancia
SET Estado = 'Error',
    FechaFin = ISNULL(FechaFin, GETDATE())
WHERE Id = @Id
  AND Estado NOT IN ('Finalizado', 'Error');";

                    cmd.Parameters.Add("@Id", SqlDbType.BigInt).Value = instanceId;
                    cmd.ExecuteNonQuery();
                }

                AddInstanceLog(
                    cn,
                    instanceId,
                    "Error",
                    "[WatchFolder] La invocación del runtime lanzó " + ex.GetType().Name +
                    ": " + ex.Message);
            }
        }

        private void TryAddInstanceLog(long instanceId, string level, string message)
        {
            try
            {
                AddInstanceLog(instanceId, level, message);
            }
            catch (Exception ex)
            {
                DispatcherDailyLog.Error(
                    "[Log/warn] No se pudo registrar WF_InstanciaLog para instanciaId=" +
                    instanceId + ": " + ex.Message);
            }
        }

        private void AddInstanceLog(long instanceId, string level, string message)
        {
            using (var cn = new SqlConnection(_cnn))
            {
                cn.Open();
                AddInstanceLog(cn, instanceId, level, message);
            }
        }

        private static void AddInstanceLog(SqlConnection cn, long instanceId, string level, string message)
        {
            using (var cmd = cn.CreateCommand())
            {
                cmd.CommandText = @"
INSERT INTO dbo.WF_InstanciaLog
    (WF_InstanciaId, FechaLog, Nivel, Mensaje, NodoId, NodoTipo)
VALUES
    (@InstId, GETDATE(), @Nivel, @Mensaje, NULL, 'watchfolder');";

                cmd.Parameters.Add("@InstId", SqlDbType.BigInt).Value = instanceId;
                cmd.Parameters.Add("@Nivel", SqlDbType.NVarChar, 20).Value = level ?? "Info";
                cmd.Parameters.Add("@Mensaje", SqlDbType.NVarChar).Value = message ?? "";
                cmd.ExecuteNonQuery();
            }
        }

        private static FileStream TryAcquireLease(string lockPath)
        {
            try
            {
                return new FileStream(
                    lockPath,
                    FileMode.OpenOrCreate,
                    FileAccess.ReadWrite,
                    FileShare.None);
            }
            catch (IOException)
            {
                return null;
            }
            catch (UnauthorizedAccessException)
            {
                return null;
            }
        }

        private static WatchFolderReceipt LoadReceipt(string receiptPath)
        {
            if (!File.Exists(receiptPath))
                return null;

            string json = File.ReadAllText(receiptPath, Encoding.UTF8);
            var receipt = JsonConvert.DeserializeObject<WatchFolderReceipt>(json);

            if (receipt == null || string.IsNullOrWhiteSpace(receipt.IngressId))
                throw new InvalidDataException("Recibo Watch Folder inválido: " + receiptPath);

            return receipt;
        }

        private static void SaveReceipt(string receiptPath, WatchFolderReceipt receipt)
        {
            if (receipt == null)
                throw new ArgumentNullException(nameof(receipt));

            receipt.UpdatedUtc = UtcNow();

            string folder = Path.GetDirectoryName(receiptPath);
            if (!string.IsNullOrWhiteSpace(folder))
                Directory.CreateDirectory(folder);

            string tempPath = receiptPath + ".tmp." + Guid.NewGuid().ToString("N");
            string json = JsonConvert.SerializeObject(receipt, Formatting.Indented);
            File.WriteAllText(tempPath, json, new UTF8Encoding(false));

            try
            {
                if (File.Exists(receiptPath))
                    File.Replace(tempPath, receiptPath, null);
                else
                    File.Move(tempPath, receiptPath);
            }
            catch
            {
                TryDelete(tempPath);
                throw;
            }
        }

        private static string ReceiptPath(string documentPath)
        {
            return documentPath + ReceiptSuffix;
        }

        private static bool IsDocumentCandidate(string path)
        {
            return !path.EndsWith(ReceiptSuffix, StringComparison.OrdinalIgnoreCase) &&
                   !path.EndsWith(LockSuffix, StringComparison.OrdinalIgnoreCase) &&
                   path.IndexOf(".wf.json.tmp.", StringComparison.OrdinalIgnoreCase) < 0;
        }

        private static string UniqueDocumentPath(string folder, string fileName)
        {
            Directory.CreateDirectory(folder);

            string cleanName = Path.GetFileName(fileName);
            string dest = Path.Combine(folder, cleanName);
            if (!File.Exists(dest) && !File.Exists(ReceiptPath(dest)))
                return dest;

            string baseName = Path.GetFileNameWithoutExtension(cleanName);
            string ext = Path.GetExtension(cleanName);
            string stamp = DateTime.Now.ToString("yyyyMMdd_HHmmss_fff");
            int sequence = 1;

            do
            {
                dest = Path.Combine(folder, baseName + "_" + stamp + "_" + sequence + ext);
                sequence++;
            }
            while (File.Exists(dest) || File.Exists(ReceiptPath(dest)));

            return dest;
        }

        private static void TryDelete(string path)
        {
            try
            {
                if (File.Exists(path))
                    File.Delete(path);
            }
            catch
            {
                // Best effort. Un .lock residual no conserva el bloqueo sin handle abierto.
            }
        }

        private static string UtcNow()
        {
            return DateTime.UtcNow.ToString("o");
        }

        private static void EnsureHttpContext()
        {
            if (HttpContext.Current == null)
            {
                var req = new HttpRequest("", "http://localhost/dispatcher", "");
                var sw = new StringWriter();
                var resp = new HttpResponse(sw);
                HttpContext.Current = new HttpContext(req, resp);
            }

            WorkflowAmbient.Items.Value = HttpContext.Current.Items;
        }

        private sealed class DefinitionInfo
        {
            public int Id { get; set; }
            public string Name { get; set; }
        }

        private sealed class InstanceInfo
        {
            public long Id { get; set; }
            public string State { get; set; }
        }
    }
}
