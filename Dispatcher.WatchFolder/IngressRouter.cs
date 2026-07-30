using System;
using System.Collections.Generic;
using System.Data;
using System.Data.SqlClient;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace Intranet.WorkflowStudio.Dispatcher.WatchFolder
{
    internal sealed class IngressRouter
    {
        private readonly string _cnn;
        private readonly string _channelCode;

        public IngressRouter(string connectionString, string channelCode)
        {
            if (string.IsNullOrWhiteSpace(connectionString))
                throw new ArgumentException("Connection string vacía.", nameof(connectionString));

            _cnn = connectionString;
            _channelCode = NormalizeChannel(channelCode);
        }

        public void EnsureSchema()
        {
            using (var cn = new SqlConnection(_cnn))
            using (var cmd = cn.CreateCommand())
            {
                cmd.CommandText = @"
SELECT
    CASE WHEN OBJECT_ID(N'dbo.WF_IngresoRuta', N'U') IS NULL THEN 0 ELSE 1 END AS HasRoutes,
    CASE WHEN OBJECT_ID(N'dbo.WF_IngresoDocumento', N'U') IS NULL THEN 0 ELSE 1 END AS HasDocuments;";

                cn.Open();
                using (var dr = cmd.ExecuteReader())
                {
                    if (!dr.Read() || Convert.ToInt32(dr["HasRoutes"]) != 1 || Convert.ToInt32(dr["HasDocuments"]) != 1)
                    {
                        throw new InvalidOperationException(
                            "El Enrutador de Ingreso Documental requiere ejecutar " +
                            "fix76_ingreso_documental_enrutador_base.sql en la base Workflow.");
                    }
                }
            }
        }

        public void RegisterClaim(WatchFolderReceipt receipt)
        {
            if (receipt == null) throw new ArgumentNullException(nameof(receipt));

            receipt.ChannelCode = NormalizeChannel(receipt.ChannelCode ?? _channelCode);

            using (var cn = new SqlConnection(_cnn))
            using (var cmd = cn.CreateCommand())
            {
                cmd.CommandText = @"
UPDATE dbo.WF_IngresoDocumento
SET CanalCodigo = @Canal,
    ArchivoNombre = @Archivo,
    Extension = @Extension,
    RutaActual = @Ruta,
    FechaActualizacion = GETDATE()
WHERE IngressId = @IngressId;

IF @@ROWCOUNT = 0
BEGIN
    INSERT INTO dbo.WF_IngresoDocumento
    (
        IngressId, CanalCodigo, ArchivoNombre, Extension, RutaActual,
        Estado, FechaIngreso, FechaActualizacion
    )
    VALUES
    (
        @IngressId, @Canal, @Archivo, @Extension, @Ruta,
        N'RECIBIDO', GETDATE(), GETDATE()
    );
END;";

                AddCommonReceiptParameters(cmd, receipt);
                cn.Open();

                try
                {
                    cmd.ExecuteNonQuery();
                }
                catch (SqlException ex) when (ex.Number == 2601 || ex.Number == 2627)
                {
                    // Otro proceso pudo insertar el mismo ingressId entre UPDATE e INSERT.
                    using (var retry = cn.CreateCommand())
                    {
                        retry.CommandText = @"
UPDATE dbo.WF_IngresoDocumento
SET CanalCodigo = @Canal,
    ArchivoNombre = @Archivo,
    Extension = @Extension,
    RutaActual = @Ruta,
    FechaActualizacion = GETDATE()
WHERE IngressId = @IngressId;";
                        AddCommonReceiptParameters(retry, receipt);
                        retry.ExecuteNonQuery();
                    }
                }
            }
        }

        public IngressRouteDecision Resolve(WatchFolderReceipt receipt)
        {
            if (receipt == null) throw new ArgumentNullException(nameof(receipt));

            RegisterClaim(receipt);

            var persisted = LoadPersistedDecision(receipt.IngressId);
            if (persisted != null && persisted.DefinitionId.HasValue)
            {
                var definition = LoadActiveDefinition(persisted.DefinitionId.Value);
                if (definition == null)
                {
                    string reason = "La definición seleccionada ya no existe o está inactiva (Id=" +
                                    persisted.DefinitionId.Value + ").";
                    ClearDecisionAndMarkPending(receipt, reason);
                    return IngressRouteDecision.Pending(reason);
                }

                return new IngressRouteDecision
                {
                    IsResolved = true,
                    DefinitionId = definition.Id,
                    WorkflowName = definition.Name,
                    RouteId = persisted.RouteId,
                    Source = string.IsNullOrWhiteSpace(persisted.Source) ? "USUARIO" : persisted.Source,
                    Reason = persisted.Reason,
                    Confidence = persisted.Confidence
                };
            }

            var candidates = LoadActiveRoutes(
                    NormalizeChannel(receipt.ChannelCode ?? _channelCode))
                .Where(r => RouteMatches(r, receipt))
                .OrderByDescending(r => r.Priority)
                .ThenByDescending(r => r.Specificity)
                .ThenBy(r => r.Id)
                .ToList();

            if (candidates.Count == 0)
            {
                string reason =
                    "No existe una ruta determinística para el canal '" +
                    NormalizeChannel(receipt.ChannelCode ?? _channelCode) +
                    "' y el archivo '" + receipt.OriginalFileName + "'.";

                MarkPending(receipt, reason);
                return IngressRouteDecision.Pending(reason);
            }

            int topPriority = candidates[0].Priority;
            int topSpecificity = candidates[0].Specificity;
            var top = candidates
                .Where(r => r.Priority == topPriority && r.Specificity == topSpecificity)
                .ToList();

            var targetDefinitions = top.Select(r => r.DefinitionId).Distinct().ToList();
            if (targetDefinitions.Count > 1)
            {
                string routes = string.Join(", ", top.Select(r => r.Code).Distinct());
                string reason =
                    "Hay rutas incompatibles con igual prioridad y especificidad: " + routes + ".";

                MarkPending(receipt, reason);
                return IngressRouteDecision.Pending(reason);
            }

            var selected = top[0];
            var decision = new IngressRouteDecision
            {
                IsResolved = true,
                DefinitionId = selected.DefinitionId,
                WorkflowName = selected.WorkflowName,
                RouteId = selected.Id,
                Source = "REGLA",
                Reason = BuildRouteReason(selected),
                Confidence = 100m
            };

            return PersistRuleDecision(receipt, decision);
        }

        private IngressRouteDecision PersistRuleDecision(
            WatchFolderReceipt receipt,
            IngressRouteDecision decision)
        {
            if (receipt == null) throw new ArgumentNullException(nameof(receipt));
            if (decision == null || !decision.IsResolved || !decision.DefinitionId.HasValue)
                throw new ArgumentException("La decisión de enrutamiento no está resuelta.", nameof(decision));

            using (var cn = new SqlConnection(_cnn))
            using (var cmd = cn.CreateCommand())
            {
                cmd.CommandText = @"
UPDATE dbo.WF_IngresoDocumento
SET WF_IngresoRutaId = @RutaId,
    WF_DefinicionId = @DefId,
    Estado = N'RUTA_RESUELTA',
    OrigenDecision = @Origen,
    Confianza = @Confianza,
    MotivoDecision = @Motivo,
    UltimoError = NULL,
    FechaDecision = ISNULL(FechaDecision, GETDATE()),
    FechaActualizacion = GETDATE()
WHERE IngressId = @IngressId
  AND WF_InstanciaId IS NULL
  AND WF_DefinicionId IS NULL;";

                cmd.Parameters.Add("@RutaId", SqlDbType.Int).Value =
                    decision.RouteId.HasValue ? (object)decision.RouteId.Value : DBNull.Value;
                cmd.Parameters.Add("@DefId", SqlDbType.Int).Value = decision.DefinitionId.Value;
                cmd.Parameters.Add("@Origen", SqlDbType.NVarChar, 30).Value =
                    (object)NullIfEmpty(decision.Source) ?? "REGLA";
                cmd.Parameters.Add("@Confianza", SqlDbType.Decimal).Value =
                    decision.Confidence.HasValue ? (object)decision.Confidence.Value : DBNull.Value;
                cmd.Parameters["@Confianza"].Precision = 5;
                cmd.Parameters["@Confianza"].Scale = 2;
                cmd.Parameters.Add("@Motivo", SqlDbType.NVarChar, 1000).Value =
                    (object)NullIfEmpty(decision.Reason) ?? DBNull.Value;
                cmd.Parameters.Add("@IngressId", SqlDbType.NVarChar, 40).Value = receipt.IngressId;

                cn.Open();
                int rows = cmd.ExecuteNonQuery();

                if (rows > 0)
                    return decision;
            }

            // Una decisión humana pudo grabarse entre la lectura inicial y la
            // evaluación de reglas. Esa decisión siempre tiene precedencia.
            var persisted = LoadPersistedDecision(receipt.IngressId);
            if (persisted != null && persisted.DefinitionId.HasValue)
            {
                var definition = LoadActiveDefinition(persisted.DefinitionId.Value);
                if (definition != null)
                {
                    return new IngressRouteDecision
                    {
                        IsResolved = true,
                        DefinitionId = definition.Id,
                        WorkflowName = definition.Name,
                        RouteId = persisted.RouteId,
                        Source = string.IsNullOrWhiteSpace(persisted.Source)
                            ? "USUARIO"
                            : persisted.Source,
                        Reason = persisted.Reason,
                        Confidence = persisted.Confidence
                    };
                }
            }

            string reason = "La decisión de enrutamiento cambió mientras el documento se procesaba.";
            ClearDecisionAndMarkPending(receipt, reason);
            return IngressRouteDecision.Pending(reason);
        }

        public void MarkPending(WatchFolderReceipt receipt, string reason)
        {
            if (receipt == null) throw new ArgumentNullException(nameof(receipt));

            using (var cn = new SqlConnection(_cnn))
            using (var cmd = cn.CreateCommand())
            {
                cmd.CommandText = @"
UPDATE dbo.WF_IngresoDocumento
SET Estado = N'PENDIENTE_RUTA',
    MotivoDecision = @Motivo,
    UltimoError = NULL,
    FechaActualizacion = GETDATE()
WHERE IngressId = @IngressId
  AND WF_InstanciaId IS NULL
  AND WF_DefinicionId IS NULL;";

                cmd.Parameters.Add("@Motivo", SqlDbType.NVarChar, 1000).Value =
                    (object)NullIfEmpty(reason) ?? DBNull.Value;
                cmd.Parameters.Add("@IngressId", SqlDbType.NVarChar, 40).Value = receipt.IngressId;

                cn.Open();
                cmd.ExecuteNonQuery();
            }
        }

        private void ClearDecisionAndMarkPending(WatchFolderReceipt receipt, string reason)
        {
            if (receipt == null) throw new ArgumentNullException(nameof(receipt));

            using (var cn = new SqlConnection(_cnn))
            using (var cmd = cn.CreateCommand())
            {
                cmd.CommandText = @"
UPDATE dbo.WF_IngresoDocumento
SET WF_IngresoRutaId = NULL,
    WF_DefinicionId = NULL,
    Estado = N'PENDIENTE_RUTA',
    OrigenDecision = NULL,
    Confianza = NULL,
    MotivoDecision = @Motivo,
    DecisionPor = NULL,
    UltimoError = NULL,
    FechaDecision = NULL,
    FechaActualizacion = GETDATE()
WHERE IngressId = @IngressId
  AND WF_InstanciaId IS NULL;";

                cmd.Parameters.Add("@Motivo", SqlDbType.NVarChar, 1000).Value =
                    (object)NullIfEmpty(reason) ?? DBNull.Value;
                cmd.Parameters.Add("@IngressId", SqlDbType.NVarChar, 40).Value = receipt.IngressId;

                cn.Open();
                cmd.ExecuteNonQuery();
            }
        }

        public void MarkInstance(WatchFolderReceipt receipt, long instanceId, string instanceState)
        {
            if (receipt == null) throw new ArgumentNullException(nameof(receipt));

            string ingressState = MapInstanceState(instanceState, "INSTANCIA_CREADA");

            using (var cn = new SqlConnection(_cnn))
            using (var cmd = cn.CreateCommand())
            {
                cmd.CommandText = @"
UPDATE dbo.WF_IngresoDocumento
SET WF_InstanciaId = @InstanciaId,
    WF_DefinicionId = ISNULL(WF_DefinicionId, @DefId),
    Estado = @Estado,
    RutaActual = @Ruta,
    UltimoError = NULL,
    FechaInstancia = ISNULL(FechaInstancia, GETDATE()),
    FechaActualizacion = GETDATE()
WHERE IngressId = @IngressId;";

                cmd.Parameters.Add("@InstanciaId", SqlDbType.BigInt).Value = instanceId;
                cmd.Parameters.Add("@DefId", SqlDbType.Int).Value =
                    receipt.DefinitionId.HasValue ? (object)receipt.DefinitionId.Value : DBNull.Value;
                cmd.Parameters.Add("@Estado", SqlDbType.NVarChar, 40).Value = ingressState;
                cmd.Parameters.Add("@Ruta", SqlDbType.NVarChar, 1000).Value = receipt.CurrentFilePath;
                cmd.Parameters.Add("@IngressId", SqlDbType.NVarChar, 40).Value = receipt.IngressId;

                cn.Open();
                cmd.ExecuteNonQuery();
            }
        }

        public void MarkState(WatchFolderReceipt receipt, string instanceState, string currentPath, string error)
        {
            if (receipt == null) throw new ArgumentNullException(nameof(receipt));

            string state = MapInstanceState(instanceState, "EN_CURSO");

            using (var cn = new SqlConnection(_cnn))
            using (var cmd = cn.CreateCommand())
            {
                cmd.CommandText = @"
UPDATE dbo.WF_IngresoDocumento
SET Estado = @Estado,
    RutaActual = @Ruta,
    UltimoError = @Error,
    FechaActualizacion = GETDATE()
WHERE IngressId = @IngressId;";

                cmd.Parameters.Add("@Estado", SqlDbType.NVarChar, 40).Value = state;
                cmd.Parameters.Add("@Ruta", SqlDbType.NVarChar, 1000).Value =
                    (object)NullIfEmpty(currentPath) ?? receipt.CurrentFilePath;
                cmd.Parameters.Add("@Error", SqlDbType.NVarChar).Value =
                    (object)NullIfEmpty(error) ?? DBNull.Value;
                cmd.Parameters.Add("@IngressId", SqlDbType.NVarChar, 40).Value = receipt.IngressId;

                cn.Open();
                cmd.ExecuteNonQuery();
            }
        }

        public void MarkDispatcherError(WatchFolderReceipt receipt, string error)
        {
            if (receipt == null) return;

            using (var cn = new SqlConnection(_cnn))
            using (var cmd = cn.CreateCommand())
            {
                cmd.CommandText = @"
UPDATE dbo.WF_IngresoDocumento
SET Estado = N'ERROR_INGRESO',
    UltimoError = @Error,
    RutaActual = @Ruta,
    FechaActualizacion = GETDATE()
WHERE IngressId = @IngressId;";

                cmd.Parameters.Add("@Error", SqlDbType.NVarChar).Value =
                    (object)NullIfEmpty(error) ?? DBNull.Value;
                cmd.Parameters.Add("@Ruta", SqlDbType.NVarChar, 1000).Value = receipt.CurrentFilePath;
                cmd.Parameters.Add("@IngressId", SqlDbType.NVarChar, 40).Value = receipt.IngressId;

                cn.Open();
                cmd.ExecuteNonQuery();
            }
        }

        private PersistedDecision LoadPersistedDecision(string ingressId)
        {
            using (var cn = new SqlConnection(_cnn))
            using (var cmd = cn.CreateCommand())
            {
                cmd.CommandText = @"
SELECT
    WF_IngresoRutaId,
    WF_DefinicionId,
    OrigenDecision,
    MotivoDecision,
    Confianza
FROM dbo.WF_IngresoDocumento
WHERE IngressId = @IngressId;";

                cmd.Parameters.Add("@IngressId", SqlDbType.NVarChar, 40).Value = ingressId;
                cn.Open();

                using (var dr = cmd.ExecuteReader())
                {
                    if (!dr.Read()) return null;

                    return new PersistedDecision
                    {
                        RouteId = dr["WF_IngresoRutaId"] == DBNull.Value
                            ? (int?)null
                            : Convert.ToInt32(dr["WF_IngresoRutaId"]),
                        DefinitionId = dr["WF_DefinicionId"] == DBNull.Value
                            ? (int?)null
                            : Convert.ToInt32(dr["WF_DefinicionId"]),
                        Source = dr["OrigenDecision"] == DBNull.Value
                            ? null
                            : Convert.ToString(dr["OrigenDecision"]),
                        Reason = dr["MotivoDecision"] == DBNull.Value
                            ? null
                            : Convert.ToString(dr["MotivoDecision"]),
                        Confidence = dr["Confianza"] == DBNull.Value
                            ? (decimal?)null
                            : Convert.ToDecimal(dr["Confianza"])
                    };
                }
            }
        }

        private DefinitionInfo LoadActiveDefinition(int definitionId)
        {
            using (var cn = new SqlConnection(_cnn))
            using (var cmd = cn.CreateCommand())
            {
                cmd.CommandText = @"
SELECT Id, Nombre, Codigo
FROM dbo.WF_Definicion
WHERE Id = @Id
  AND Activo = 1;";

                cmd.Parameters.Add("@Id", SqlDbType.Int).Value = definitionId;
                cn.Open();

                using (var dr = cmd.ExecuteReader())
                {
                    if (!dr.Read()) return null;

                    return new DefinitionInfo
                    {
                        Id = Convert.ToInt32(dr["Id"]),
                        Name = Convert.ToString(dr["Nombre"]),
                        Code = dr["Codigo"] == DBNull.Value ? null : Convert.ToString(dr["Codigo"])
                    };
                }
            }
        }

        private List<RouteRule> LoadActiveRoutes(string channelCode)
        {
            using (var cn = new SqlConnection(_cnn))
            using (var cmd = cn.CreateCommand())
            {
                cmd.CommandText = @"
SELECT
    r.Id,
    r.Codigo,
    r.Nombre,
    r.CanalCodigo,
    r.PatronArchivo,
    r.Extension,
    r.Prioridad,
    r.WF_DefinicionId,
    d.Nombre AS WorkflowNombre,
    d.Codigo AS WorkflowCodigo
FROM dbo.WF_IngresoRuta r
INNER JOIN dbo.WF_Definicion d ON d.Id = r.WF_DefinicionId
WHERE r.Activo = 1
  AND d.Activo = 1
  AND
  (
      r.CanalCodigo IS NULL
      OR LTRIM(RTRIM(r.CanalCodigo)) = N''
      OR UPPER(LTRIM(RTRIM(r.CanalCodigo))) = @Canal
  );";

                cmd.Parameters.Add("@Canal", SqlDbType.NVarChar, 80).Value =
                    NormalizeChannel(channelCode);

                var result = new List<RouteRule>();
                cn.Open();
                using (var dr = cmd.ExecuteReader())
                {
                    while (dr.Read())
                    {
                        var rule = new RouteRule
                        {
                            Id = Convert.ToInt32(dr["Id"]),
                            Code = Convert.ToString(dr["Codigo"]),
                            Name = Convert.ToString(dr["Nombre"]),
                            ChannelCode = dr["CanalCodigo"] == DBNull.Value ? null : Convert.ToString(dr["CanalCodigo"]),
                            FilePattern = dr["PatronArchivo"] == DBNull.Value ? null : Convert.ToString(dr["PatronArchivo"]),
                            Extension = dr["Extension"] == DBNull.Value ? null : NormalizeExtension(Convert.ToString(dr["Extension"])),
                            Priority = Convert.ToInt32(dr["Prioridad"]),
                            DefinitionId = Convert.ToInt32(dr["WF_DefinicionId"]),
                            WorkflowName = Convert.ToString(dr["WorkflowNombre"]),
                            WorkflowCode = dr["WorkflowCodigo"] == DBNull.Value ? null : Convert.ToString(dr["WorkflowCodigo"])
                        };

                        rule.Specificity =
                            (string.IsNullOrWhiteSpace(rule.ChannelCode) ? 0 : 1) +
                            (string.IsNullOrWhiteSpace(rule.FilePattern) ? 0 : 1) +
                            (string.IsNullOrWhiteSpace(rule.Extension) ? 0 : 1);

                        result.Add(rule);
                    }
                }

                return result;
            }
        }

        private bool RouteMatches(RouteRule route, WatchFolderReceipt receipt)
        {
            string receiptChannel = NormalizeChannel(receipt.ChannelCode ?? _channelCode);
            string routeChannel = string.IsNullOrWhiteSpace(route.ChannelCode)
                ? null
                : NormalizeChannel(route.ChannelCode);

            if (!string.IsNullOrWhiteSpace(routeChannel) &&
                !string.Equals(routeChannel, receiptChannel, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            string fileName = receipt.OriginalFileName ?? Path.GetFileName(receipt.CurrentFilePath) ?? "";
            string extension = NormalizeExtension(Path.GetExtension(fileName));

            if (!string.IsNullOrWhiteSpace(route.Extension) &&
                !string.Equals(route.Extension, extension, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            if (!string.IsNullOrWhiteSpace(route.FilePattern) &&
                !WildcardMatch(fileName, route.FilePattern))
            {
                return false;
            }

            return true;
        }

        private static string BuildRouteReason(RouteRule route)
        {
            var parts = new List<string>();

            if (!string.IsNullOrWhiteSpace(route.ChannelCode))
                parts.Add("canal=" + NormalizeChannel(route.ChannelCode));

            if (!string.IsNullOrWhiteSpace(route.Extension))
                parts.Add("extensión=" + route.Extension);

            if (!string.IsNullOrWhiteSpace(route.FilePattern))
                parts.Add("archivo=" + route.FilePattern);

            string conditions = parts.Count == 0
                ? "ruta predeterminada"
                : string.Join("; ", parts);

            return "Regla " + route.Code + " (" + route.Name + "): " + conditions + ".";
        }

        private static bool WildcardMatch(string value, string pattern)
        {
            string regex = "^" + Regex.Escape(pattern ?? "")
                .Replace("\\*", ".*")
                .Replace("\\?", ".") + "$";

            return Regex.IsMatch(value ?? "", regex, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        }

        private static void AddCommonReceiptParameters(SqlCommand cmd, WatchFolderReceipt receipt)
        {
            string fileName = receipt.OriginalFileName ?? Path.GetFileName(receipt.CurrentFilePath) ?? "documento";

            cmd.Parameters.Add("@IngressId", SqlDbType.NVarChar, 40).Value = receipt.IngressId;
            cmd.Parameters.Add("@Canal", SqlDbType.NVarChar, 80).Value = NormalizeChannel(receipt.ChannelCode);
            cmd.Parameters.Add("@Archivo", SqlDbType.NVarChar, 260).Value = fileName;
            cmd.Parameters.Add("@Extension", SqlDbType.NVarChar, 20).Value =
                (object)NullIfEmpty(NormalizeExtension(Path.GetExtension(fileName))) ?? DBNull.Value;
            cmd.Parameters.Add("@Ruta", SqlDbType.NVarChar, 1000).Value = receipt.CurrentFilePath;
        }

        private static string MapInstanceState(string instanceState, string fallback)
        {
            if (string.Equals(instanceState, "Finalizado", StringComparison.OrdinalIgnoreCase))
                return "FINALIZADO";

            if (string.Equals(instanceState, "Error", StringComparison.OrdinalIgnoreCase))
                return "ERROR_WORKFLOW";

            if (string.Equals(instanceState, "EnCurso", StringComparison.OrdinalIgnoreCase))
                return "EN_CURSO";

            if (string.IsNullOrWhiteSpace(instanceState))
                return fallback;

            return "ESTADO_" + NormalizeToken(instanceState);
        }

        private static string NormalizeChannel(string value)
        {
            string normalized = NormalizeToken(value);
            return string.IsNullOrWhiteSpace(normalized) ? "GENERAL" : normalized;
        }

        private static string NormalizeToken(string value)
        {
            var sb = new StringBuilder();
            foreach (char ch in (value ?? "").Trim().ToUpperInvariant())
            {
                if (char.IsLetterOrDigit(ch) || ch == '_' || ch == '-')
                    sb.Append(ch);
                else if (char.IsWhiteSpace(ch) && sb.Length > 0 && sb[sb.Length - 1] != '_')
                    sb.Append('_');
            }

            return sb.ToString().Trim('_');
        }

        private static string NormalizeExtension(string value)
        {
            string extension = (value ?? "").Trim().ToLowerInvariant();
            if (extension.Length == 0) return null;
            if (!extension.StartsWith(".")) extension = "." + extension;
            return extension;
        }

        private static string NullIfEmpty(string value)
        {
            return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
        }

        private sealed class PersistedDecision
        {
            public int? RouteId { get; set; }
            public int? DefinitionId { get; set; }
            public string Source { get; set; }
            public string Reason { get; set; }
            public decimal? Confidence { get; set; }
        }

        private sealed class DefinitionInfo
        {
            public int Id { get; set; }
            public string Name { get; set; }
            public string Code { get; set; }
        }

        private sealed class RouteRule
        {
            public int Id { get; set; }
            public string Code { get; set; }
            public string Name { get; set; }
            public string ChannelCode { get; set; }
            public string FilePattern { get; set; }
            public string Extension { get; set; }
            public int Priority { get; set; }
            public int Specificity { get; set; }
            public int DefinitionId { get; set; }
            public string WorkflowName { get; set; }
            public string WorkflowCode { get; set; }
        }
    }

    internal sealed class IngressRouteDecision
    {
        public bool IsResolved { get; set; }
        public int? DefinitionId { get; set; }
        public int? RouteId { get; set; }
        public string WorkflowName { get; set; }
        public string Source { get; set; }
        public string Reason { get; set; }
        public decimal? Confidence { get; set; }

        public static IngressRouteDecision Pending(string reason)
        {
            return new IngressRouteDecision
            {
                IsResolved = false,
                Reason = reason
            };
        }
    }
}
