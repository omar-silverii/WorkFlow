using System;
using System.Configuration;
using System.Data;
using System.Data.SqlClient;
using System.IO;
using System.Web;

public class WF_IngresoDocumento_Get : IHttpHandler
{
    private static string Cnn =>
        ConfigurationManager.ConnectionStrings["DefaultConnection"].ConnectionString;

    private sealed class DocumentRow
    {
        public long InstanciaId;
        public string RutaActual;
        public string ArchivoNombre;
        public string Extension;
        public string IngressId;
    }

    public void ProcessRequest(HttpContext context)
    {
        long tareaId;
        if (!long.TryParse((context.Request["tarea"] ?? "").Trim(), out tareaId) || tareaId <= 0)
        {
            WriteError(context, 400, "Tarea inválida.");
            return;
        }

        var userKey = (context.User?.Identity?.Name ?? "").Trim();
        if (!PuedeAbrirTarea(tareaId, userKey))
        {
            WriteError(context, 403, "No tenés permisos para abrir este documento.");
            return;
        }

        DocumentRow row;
        try
        {
            row = ObtenerDocumento(tareaId);
        }
        catch
        {
            WriteError(context, 404, "La instancia no posee un documento de ingreso disponible.");
            return;
        }

        if (row == null || string.IsNullOrWhiteSpace(row.RutaActual))
        {
            WriteError(context, 404, "La instancia no posee un documento de ingreso disponible.");
            return;
        }

        string path;
        try { path = Path.GetFullPath(row.RutaActual); }
        catch
        {
            WriteError(context, 404, "La ruta registrada del documento no es válida.");
            return;
        }

        if (!File.Exists(path))
        {
            WriteError(context, 404, "El documento ya no existe en la ubicación registrada.");
            return;
        }

        var mode = (context.Request["mode"] ?? "inline").Trim().ToLowerInvariant();
        var disposition = mode == "download" ? "attachment" : "inline";
        var fileName = Path.GetFileName(
            string.IsNullOrWhiteSpace(row.ArchivoNombre) ? path : row.ArchivoNombre);

        context.Response.Clear();
        context.Response.ContentType = ResolveContentType(path);
        context.Response.AddHeader("X-Content-Type-Options", "nosniff");
        context.Response.AddHeader(
            "Content-Disposition",
            disposition + "; filename=\"" + (fileName ?? "documento").Replace("\"", "") + "\"");

        TryAudit(row.InstanciaId, tareaId, userKey, fileName, disposition, row.IngressId);
        context.Response.TransmitFile(path);
    }

    private static DocumentRow ObtenerDocumento(long tareaId)
    {
        using (var cn = new SqlConnection(Cnn))
        using (var cmd = new SqlCommand(@"
SELECT TOP 1
    t.WF_InstanciaId,
    i.RutaActual,
    i.ArchivoNombre,
    i.Extension,
    i.IngressId
FROM dbo.WF_Tarea t
INNER JOIN dbo.WF_IngresoDocumento i
    ON i.WF_InstanciaId = t.WF_InstanciaId
WHERE t.Id = @TareaId
ORDER BY i.Id DESC;", cn))
        {
            cmd.Parameters.Add("@TareaId", SqlDbType.BigInt).Value = tareaId;
            cn.Open();

            using (var dr = cmd.ExecuteReader())
            {
                if (!dr.Read()) return null;

                return new DocumentRow
                {
                    InstanciaId = Convert.ToInt64(dr["WF_InstanciaId"]),
                    RutaActual = Convert.ToString(dr["RutaActual"] ?? ""),
                    ArchivoNombre = Convert.ToString(dr["ArchivoNombre"] ?? ""),
                    Extension = Convert.ToString(dr["Extension"] ?? ""),
                    IngressId = Convert.ToString(dr["IngressId"] ?? "")
                };
            }
        }
    }

    private static bool PuedeAbrirTarea(long tareaId, string userKey)
    {
        using (var cn = new SqlConnection(Cnn))
        using (var cmd = new SqlCommand("dbo.WF_Tarea_PuedeAbrir", cn))
        {
            cmd.CommandType = CommandType.StoredProcedure;
            cmd.Parameters.Add("@TareaId", SqlDbType.BigInt).Value = tareaId;
            cmd.Parameters.Add("@UserKey", SqlDbType.NVarChar, 200).Value = userKey ?? "";
            cn.Open();
            var v = cmd.ExecuteScalar();
            return v != null && v != DBNull.Value && Convert.ToBoolean(v);
        }
    }

    private static void TryAudit(
        long instanciaId,
        long tareaId,
        string userKey,
        string fileName,
        string action,
        string ingressId)
    {
        try
        {
            using (var cn = new SqlConnection(Cnn))
            using (var cmd = new SqlCommand(@"
INSERT INTO dbo.WF_InstanciaLog
    (WF_InstanciaId, FechaLog, Nivel, Mensaje, NodoId, NodoTipo)
VALUES
    (@InstanciaId, GETDATE(), N'Info', @Mensaje, NULL, N'ingreso.document.view');", cn))
            {
                cmd.Parameters.Add("@InstanciaId", SqlDbType.BigInt).Value = instanciaId;
                cmd.Parameters.Add("@Mensaje", SqlDbType.NVarChar, 4000).Value =
                    "[IngresoDocumento] " + action +
                    " archivo=" + (fileName ?? "") +
                    "; tareaId=" + tareaId +
                    "; usuario=" + (userKey ?? "") +
                    "; ingressId=" + (ingressId ?? "");
                cn.Open();
                cmd.ExecuteNonQuery();
            }
        }
        catch
        {
            // La visualización no debe fallar si la auditoría no puede escribirse.
        }
    }

    private static string ResolveContentType(string path)
    {
        switch ((Path.GetExtension(path) ?? "").ToLowerInvariant())
        {
            case ".pdf": return "application/pdf";
            case ".txt": return "text/plain; charset=utf-8";
            case ".docx": return "application/vnd.openxmlformats-officedocument.wordprocessingml.document";
            case ".doc": return "application/msword";
            case ".xlsx": return "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";
            case ".xls": return "application/vnd.ms-excel";
            case ".png": return "image/png";
            case ".jpg":
            case ".jpeg": return "image/jpeg";
            default: return "application/octet-stream";
        }
    }

    private static void WriteError(HttpContext context, int statusCode, string message)
    {
        context.Response.StatusCode = statusCode;
        context.Response.ContentType = "text/plain; charset=utf-8";
        context.Response.Write(message ?? "Error");
    }

    public bool IsReusable => false;
}
