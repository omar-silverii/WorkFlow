using System;
using System.IO;
using System.Web;
using Newtonsoft.Json;

namespace Intranet.WorkflowStudio.WebForms.Api
{
    /// <summary>
    /// Descripción breve de Echo
    /// </summary>
    public class Echo : IHttpHandler
    {

        public void ProcessRequest(HttpContext ctx)
        {
            int status = 200;
            int.TryParse(ctx.Request["status"], out status);
            string body = "";
            using (var sr = new StreamReader(ctx.Request.InputStream))
                body = sr.ReadToEnd();

            var result = new
            {
                method = ctx.Request.HttpMethod,
                query = ctx.Request.QueryString.ToString(),
                body = body,
                headers = ctx.Request.ContentType,
                ts = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")
            };

            ctx.Response.StatusCode = (status == 0 ? 200 : status);
            ctx.Response.ContentType = "application/json";
            ctx.Response.Write(JsonConvert.SerializeObject(result));
        }

        public bool IsReusable
        {
            get
            {
                return true;
            }
        }
    }
}