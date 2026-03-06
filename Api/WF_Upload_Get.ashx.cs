using System;
using System.Configuration;
using System.Data;
using System.Data.SqlClient;
using System.IO;
using System.Web;

public class WF_Upload_Get : IHttpHandler
{
    private static string Cnn => ConfigurationManager.ConnectionStrings["DefaultConnection"].ConnectionString;

    public void ProcessRequest(HttpContext context)
    {
        var inst = (context.Request["inst"] ?? "").Trim();
        var tarea = (context.Request["tarea"] ?? "").Trim();          // carpeta física del archivo
        var authTarea = (context.Request["authTarea"] ?? "").Trim();  // tarea usada para validar permiso
        var f = (context.Request["f"] ?? "").Trim();

        if (string.IsNullOrWhiteSpace(inst) || string.IsNullOrWhiteSpace(tarea) || string.IsNullOrWhiteSpace(f))
        {
            context.Response.StatusCode = 400;
            context.Response.Write("Bad request");
            return;
        }

        long tareaAuthId;
        if (!long.TryParse(string.IsNullOrWhiteSpace (authTarea) ? tarea : authTarea, out tareaAuthId))
        {
            context.Response.StatusCode = 400;
            context.Response.Write("Bad request");
            return;
        }

        var userKey = (context.User?.Identity?.Name ?? "").Trim();
        if (!PuedeAbrirTarea(tareaAuthId, userKey))
        {
            context.Response.StatusCode = 403;
            context.Response.Write("Forbidden");
            return;
        }

        var baseDir = context.Server.MapPath("~/App_Data/WFUploads");
        var path = Path.Combine(baseDir, inst, tarea, f);

        if (!File.Exists(path))
        {
            context.Response.StatusCode = 404;
            context.Response.Write("Not found");
            return;
        }

        var fileName = f;
        var idx = f.IndexOf("__", StringComparison.Ordinal);
        if (idx >= 0 && idx + 2 < f.Length) fileName = f.Substring(idx + 2);

        context.Response.ContentType = "application/octet-stream";
        context.Response.AddHeader("Content-Disposition", "inline; filename=\"" + fileName.Replace("\"", "") + "\"");
        context.Response.TransmitFile(path);
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
            if (v == null || v == DBNull.Value) return false;
            return Convert.ToBoolean(v);
        }
    }

    public bool IsReusable => false;
}