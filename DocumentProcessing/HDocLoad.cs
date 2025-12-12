using System;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using UglyToad.PdfPig;
using UglyToad.PdfPig.Content;
using DocumentFormat.OpenXml.Packaging;

namespace Intranet.WorkflowStudio.WebForms.DocumentProcessing
{
    public class HDocLoad : IManejadorNodo
    {
        public string TipoNodo => "doc.load";

        public Task<ResultadoEjecucion> EjecutarAsync(
            ContextoEjecucion ctx,
            NodeDef nodo,
            CancellationToken ct)
        {
            var p = nodo.Parameters ?? new System.Collections.Generic.Dictionary<string, object>();

            string path = Get(p, "path");
            string mode = (Get(p, "mode") ?? "auto").ToLower();

            if (string.IsNullOrEmpty(path))
                return Task.FromResult(Error(ctx, "[doc.load] Falta parámetro 'path'"));

            if (!File.Exists(path))
                return Task.FromResult(Error(ctx, "[doc.load] Archivo no encontrado: " + path));

            try
            {
                byte[] bytes = File.ReadAllBytes(path);
                string ext = Path.GetExtension(path).ToLower();
                string text = "";

                // AUTO-DETECCIÓN
                if (mode == "auto")
                {
                    if (ext == ".pdf") mode = "pdf";
                    else if (ext == ".docx") mode = "word";
                    else mode = "text";
                }

                // PDF
                if (mode == "pdf")
                {
                    text = ExtractPdf(bytes, ctx);
                }
                // WORD
                else if (mode == "word")
                {
                    text = ExtractWord(bytes, ctx);
                }
                // TEXTO PLANO
                else
                {
                    text = Encoding.UTF8.GetString(bytes);
                }

                // 🟢 SALIDA FIJA — AHORA SIEMPRE EN input.*
                ctx.Estado["input.filename"] = Path.GetFileName(path);
                ctx.Estado["input.text"] = text;

                ctx.Log("[doc.load] OK — Archivo cargado y texto extraído.");

                return Task.FromResult(new ResultadoEjecucion { Etiqueta = "ok" });
            }
            catch (Exception ex)
            {
                return Task.FromResult(Error(ctx, "[doc.load] Excepción: " + ex.Message));
            }
        }

        // ============================================================
        private string ExtractPdf(byte[] bytes, ContextoEjecucion ctx)
        {
            try
            {
                using (var ms = new MemoryStream(bytes))
                using (var pdf = PdfDocument.Open(ms))
                {
                    var sb = new StringBuilder();

                    foreach (var page in pdf.GetPages())
                        sb.AppendLine(page.Text);

                    return sb.ToString();
                }
            }
            catch (Exception ex)
            {
                ctx.Log("[doc.load/pdf-error] " + ex.Message);
                return "";
            }
        }

        private string ExtractWord(byte[] bytes, ContextoEjecucion ctx)
        {
            try
            {
                using (var ms = new MemoryStream(bytes))
                using (var doc = WordprocessingDocument.Open(ms, false))
                {
                    return doc.MainDocumentPart.Document.InnerText;
                }
            }
            catch (Exception ex)
            {
                ctx.Log("[doc.load/word-error] " + ex.Message);
                return "";
            }
        }

        // ============================================================
        private string Get(System.Collections.Generic.IDictionary<string, object> p, string key)
        {
            if (p.TryGetValue(key, out var v) && v != null) return v.ToString();
            return null;
        }

        private ResultadoEjecucion Error(ContextoEjecucion ctx, string msg)
        {
            ctx.Log(msg);
            ctx.Estado["wf.error"] = true;
            ctx.Estado["doc.load.lastError"] = msg;

            return new ResultadoEjecucion { Etiqueta = "error" };
        }
    }
}
