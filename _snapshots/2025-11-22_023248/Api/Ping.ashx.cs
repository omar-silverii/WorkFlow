using System;
using System.Web;
using System.Web.Script.Serialization;

namespace Api
{
    public class Ping : IHttpHandler
    {
        public void ProcessRequest(HttpContext ctx)
        {
            ctx.Response.ContentType = "application/json";
            var js = new JavaScriptSerializer();
            var payload = new
            {
                ok = true,
                now = DateTime.UtcNow.ToString("o"),
                machine = Environment.MachineName,
                version = "1.0"
            };
            ctx.Response.Write(js.Serialize(payload));
        }
        public bool IsReusable { get { return true; } }
    }
}