using System;
using System.Web;
using System.Web.Script.Serialization;
using System.Threading;

namespace Api
{
    public class Score : IHttpHandler
    {
        public void ProcessRequest(HttpContext ctx)
        {
            var req = ctx.Request;
            var res = ctx.Response;
            res.ContentType = "application/json";

            string clienteId = (req["clienteId"] ?? "").Trim();

            // Simulaciones opcionales
            int delayMs; if (int.TryParse(req["delay"], out delayMs) && delayMs > 0) Thread.Sleep(delayMs);
            int forcedStatus;
            if (int.TryParse(req["status"], out forcedStatus) && forcedStatus >= 100)
            {
                res.StatusCode = forcedStatus;
                WriteJson(res, new { ok = false, status = forcedStatus, error = "Estado forzado para pruebas" });
                return;
            }
            if (req["fail"] == "1")
            {
                res.StatusCode = 500;
                WriteJson(res, new { ok = false, status = 500, error = "Fallo simulado" });
                return;
            }

            // Score determinístico a partir de clienteId (para que siempre dé igual en tus pruebas)
            int seed = Math.Abs((clienteId == "" ? Guid.NewGuid().ToString() : clienteId).GetHashCode());
            int score = 400 + (seed % 600); // 400..999

            string risk = (score >= 850) ? "A" :
                          (score >= 750) ? "B" :
                          (score >= 650) ? "C" : "D";

            bool aprobado = score >= 700;

            res.StatusCode = 200;
            WriteJson(res, new
            {
                ok = true,
                status = 200,
                clienteId = clienteId,
                score = score,
                risk = risk,
                aprobado = aprobado,
                // Campo que solemos leer en el IF: ${payload.score}
            });
        }

        private static void WriteJson(HttpResponse res, object obj)
        {
            var js = new JavaScriptSerializer();
            res.Write(js.Serialize(obj));
        }

        public bool IsReusable { get { return true; } }
    }
}
