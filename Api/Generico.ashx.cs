using System;
using System.Web;
using System.Web.Script.Serialization;
using System.Threading;

namespace Api
{
    public class MockInventario : IHttpHandler
    {
        public void ProcessRequest(HttpContext ctx)
        {
            string item = ctx.Request["item"] ?? "X";
            ctx.Response.ContentType = "application/json";
            ctx.Response.Write("{\"cantidadDisponible\": 7, \"item\": \"" + item + "\"}");
        }
        public bool IsReusable => false;
    }
}