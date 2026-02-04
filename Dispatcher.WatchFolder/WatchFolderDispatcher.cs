using Intranet.WorkflowStudio.Runtime;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Configuration;
using System.Data;
using System.Data.SqlClient;
using System.IO;
using System.Linq;
using System.Threading;
using System.Web;

namespace Intranet.WorkflowStudio.Dispatcher.WatchFolder
{
    public sealed class WatchFolderDispatcher
    {
        private readonly WatchFolderOptions _opt;
        private readonly string _cnn;

        public WatchFolderDispatcher(WatchFolderOptions opt)
        {
            _opt = opt ?? throw new ArgumentNullException(nameof(opt));

            var cs = ConfigurationManager.ConnectionStrings["DefaultConnection"];
            if (cs == null || string.IsNullOrWhiteSpace(cs.ConnectionString))
                throw new ConfigurationErrorsException("Falta connectionString 'DefaultConnection' en App.config.");

            _cnn = cs.ConnectionString;
        }

        public void RunLoop()
        {
            Console.WriteLine("Presioná Ctrl+C para salir.");

            var quit = new ManualResetEvent(false);
            Console.CancelKeyPress += (s, e) =>
            {
                e.Cancel = true;
                quit.Set();
            };

            while (!quit.WaitOne(TimeSpan.FromSeconds(_opt.PollSeconds)))
            {
                try
                {
                    TickOnce();
                }
                catch (Exception ex)
                {
                    Console.Error.WriteLine("[Tick/error] " + ex.GetType().Name + ": " + ex.Message);
                }
            }
        }

        private void TickOnce()
        {
            var files = Directory.GetFiles(_opt.InputFolder, _opt.Pattern)
                                 .OrderBy(f => f, StringComparer.OrdinalIgnoreCase)
                                 .ToList();

            foreach (var path in files)
            {
                if (!IsStableFile(path))
                    continue;

                ProcessOne(path);
            }
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

                    if (_opt.StableDelayMs > 0)
                        Thread.Sleep(_opt.StableDelayMs);
                }

                // intentar abrir en shared read para asegurar que no está lockeado en exclusivo
                using (File.Open(path, FileMode.Open, FileAccess.Read, FileShare.Read))
                {
                    return true;
                }
            }
            catch
            {
                return false;
            }
        }

        private void ProcessOne(string path)
        {
            var fileName = Path.GetFileName(path);
            Console.WriteLine("");
            Console.WriteLine("[Process] " + fileName);

            string workKey = _opt.WorkflowKey;
            int defId = ResolveDefIdByKey(workKey);

            // Crear input para el workflow
            var input = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase)
            {
                [_opt.WorkflowInputField] = path,
                ["fileName"] = fileName,
                ["filePath"] = path
            };

            // Contexto web mínimo (WorkflowRuntime usa HttpContext.Current.Items para seed/estado)
            EnsureHttpContext();

            try
            {
                // ✅ FIX: WorkflowRuntime espera string (JSON)
                string inputJson = JsonConvert.SerializeObject(input);

                var instId = WorkflowRuntime
                    .CrearInstanciaYEjecutarAsync(defId, inputJson, "watchfolder")
                    .GetAwaiter().GetResult();

                Console.WriteLine("[OK] instanciaId=" + instId);

                if (_opt.MoveAfter)
                {
                    var dest = UniqueDestPath(_opt.ProcessedFolder, fileName);
                    File.Move(path, dest);
                    Console.WriteLine("[Move] -> " + dest);
                }
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine("[FAIL] " + ex.GetType().Name + ": " + ex.Message);

                if (_opt.MoveAfter)
                {
                    try
                    {
                        var dest = UniqueDestPath(_opt.ErrorFolder, fileName);
                        File.Move(path, dest);
                        Console.Error.WriteLine("[Move] -> " + dest);
                    }
                    catch (Exception ex2)
                    {
                        Console.Error.WriteLine("[Move/error] " + ex2.Message);
                    }
                }
            }
        }


        private int ResolveDefIdByKey(string workflowKey)
        {
            using (var cn = new SqlConnection(_cnn))
            using (var cmd = cn.CreateCommand())
            {
                cmd.CommandText = @"
                                    select Id
                                    from WF_Definicion
                                    where Nombre = @Nombre
                                    ";

                cmd.Parameters.Add("@Nombre", SqlDbType.NVarChar, 200).Value = workflowKey;

                cn.Open();

                var obj = cmd.ExecuteScalar();

                if (obj == null || obj == DBNull.Value)
                    throw new InvalidOperationException(
                        "No existe WF_Definicion.Nombre='" + workflowKey + "'.");

                return Convert.ToInt32(obj);
            }
        }


        private static string UniqueDestPath(string folder, string fileName)
        {
            var baseName = Path.GetFileNameWithoutExtension(fileName);
            var ext = Path.GetExtension(fileName);
            var ts = DateTime.Now.ToString("yyyyMMdd_HHmmss");
            var dest = Path.Combine(folder, baseName + "_" + ts + ext);

            int i = 1;
            while (File.Exists(dest))
            {
                dest = Path.Combine(folder, baseName + "_" + ts + "_" + i + ext);
                i++;
            }

            return dest;
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

            // SIEMPRE: amarrar el Items al ambient
            Intranet.WorkflowStudio.Runtime.WorkflowAmbient.Items.Value = HttpContext.Current.Items;
        }

    }
}
