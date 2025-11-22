using System;
using System.Web;
using System.Web.Script.Serialization;
using System.Threading;

namespace Api
{
    public class Cliente : IHttpHandler
    {
        public void ProcessRequest(HttpContext ctx)
        {
            var req = ctx.Request;
            var res = ctx.Response;
            res.ContentType = "application/json";

            string clienteId = (req["clienteId"] ?? "").Trim();

            // Simulaciones opcionales, igual que en Score
            if (int.TryParse(req["delay"], out var delayMs) && delayMs > 0) Thread.Sleep(delayMs);
            if (int.TryParse(req["status"], out var forced) && forced >= 100)
            {
                res.StatusCode = forced;
                WriteJson(res, new { ok = false, status = forced, error = "Estado forzado para pruebas" });
                return;
            }
            if (req["fail"] == "1")
            {
                res.StatusCode = 500;
                WriteJson(res, new { ok = false, status = 500, error = "Fallo simulado" });
                return;
            }

            // Datos determinísticos a partir de clienteId
            var seed = Math.Abs((clienteId == "" ? "0" : clienteId).GetHashCode());
            var segmentos = new[] { "Retail", "Premium", "PyME", "Corp" };
            var nombres = new[] { "Juan", "Ana", "Luis", "Sofía", "María", "Carlos" };
            var apellidos = new[] { "García", "Pérez", "López", "Rodríguez", "Fernández", "Gómez" };
            var productos = new[] { "Auto", "Hogar", "Vida", "Salud" };

            string nombre = nombres[seed % nombres.Length] + " " + apellidos[(seed / 7) % apellidos.Length];
            string documento = (20000000 + (seed % 30000000)).ToString();
            string segmento = segmentos[(seed / 11) % segmentos.Length];
            bool tieneMora = (seed % 5 == 0); // ~20% mora
            decimal deudaVencida = tieneMora ? ((seed % 20000) + 5000) : 0;
            var listaProductos = new[]
            {
                new { codigo = "PRD-" + (100 + (seed % 900)).ToString(), nombre = productos[seed % productos.Length] },
                new { codigo = "PRD-" + (200 + (seed % 900)).ToString(), nombre = productos[(seed / 5) % productos.Length] }
            };

            res.StatusCode = 200;
            WriteJson(res, new
            {
                ok = true,
                status = 200,
                clienteId = clienteId,
                nombre = nombre,
                documento = documento,
                segmento = segmento,
                tieneMora = tieneMora,
                deudaVencida = deudaVencida,
                productos = listaProductos
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
