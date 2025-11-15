using System;
using System.Web;
using System.Web.Script.Serialization;

namespace Api
{
    public class Ping : IHttpHandler
    {
        public void ProcessRequest(HttpContext context)
        {
            context.Response.ContentType = "application/json";
            var obj = new
            {
                ok = true,
                status = 200,
                message = "ping ok",
                serverTime = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")
            };
            var json = new JavaScriptSerializer().Serialize(obj);
            context.Response.Write(json);
        }

        public bool IsReusable { get { return false; } }
    }
}
