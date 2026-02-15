using Intranet.WorkflowStudio.WebForms.App_Code.Handlers;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using UglyToad.PdfPig;
using DocumentFormat.OpenXml.Packaging;

namespace Intranet.WorkflowStudio.WebForms.DocumentProcessing
{
    /// <summary>
    /// doc.load
    /// Carga un archivo desde disco y extrae texto.
    ///
    /// Params:
    ///   path: string (puede incluir ${...})
    ///   mode: auto|pdf|word|text|image (default auto)
    ///   outputPrefix: string (default 'input')
    ///
    /// Salida:
    ///   {outputPrefix}.filename
    ///   {outputPrefix}.ext
    ///   {outputPrefix}.text
    ///   {outputPrefix}.textLen
    ///   {outputPrefix}.hasText
    ///   {outputPrefix}.modeUsed
    ///   {outputPrefix}.warning
    ///   {outputPrefix}.error
    ///   {outputPrefix}.sizeBytes
    /// </summary>
    public class HDocLoad : IManejadorNodo
    {
        public string TipoNodo => "doc.load";

        public Task<ResultadoEjecucion> EjecutarAsync(
            ContextoEjecucion ctx,
            NodeDef nodo,
            CancellationToken ct)
        {
            var p = nodo.Parameters ?? new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);

            string rawPath = Get(p, "path");
            string path = TemplateUtil.Expand(ctx, rawPath);

            string mode = (TemplateUtil.Expand(ctx, Get(p, "mode")) ?? "auto").Trim().ToLowerInvariant();
            string outputPrefix = (TemplateUtil.Expand(ctx, Get(p, "outputPrefix")) ?? "input").Trim();
            if (string.IsNullOrWhiteSpace(outputPrefix)) outputPrefix = "input";

            if (string.IsNullOrWhiteSpace(path))
                return Task.FromResult(Error(ctx, outputPrefix, "[doc.load] Falta parámetro 'path'"));

            if (!File.Exists(path))
                return Task.FromResult(Error(ctx, outputPrefix, "[doc.load] Archivo no encontrado: " + path));

            try
            {
                byte[] bytes = File.ReadAllBytes(path);
                string ext = Path.GetExtension(path).ToLowerInvariant();
                string text = "";
                string modeUsed = mode;
                string warning = null;

                // AUTO-DETECCIÓN
                if (modeUsed == "auto")
                {
                    if (ext == ".pdf") modeUsed = "pdf";
                    else if (ext == ".docx") modeUsed = "word";
                    else modeUsed = "text";
                }

                // PDF
                if (modeUsed == "pdf")
                {
                    text = ExtractPdf(bytes, ctx);

                    // Si el PDF no trae texto, avisamos (sin OCR)
                    if (string.IsNullOrWhiteSpace(text))
                        warning = "PDF sin texto extraíble (posible escaneo / imagen).";
                }
                // WORD
                else if (modeUsed == "word")
                {
                    text = ExtractWord(bytes, ctx);
                }
                // IMAGE (placeholder: sin OCR)
                else if (modeUsed == "image")
                {
                    text = "";
                    warning = "Imagen sin OCR (text vacío).";
                    ctx.Log("[doc.load] mode=image (sin OCR). Se carga metadata, text vacío.");
                }
                // TEXTO PLANO
                else
                {
                    text = Encoding.UTF8.GetString(bytes);
                }

                //int textLen = string.IsNullOrEmpty(text) ? 0 : text.Length;
                //bool hasText = textLen > 0;


                int textLen = string.IsNullOrEmpty(text) ? 0 : text.Length;
                bool hasText = !string.IsNullOrWhiteSpace(text); // ✅ evita True por CR/LF/espacios
               

                    // Salidas (por clave directa)
                    ctx.Estado[outputPrefix + ".filename"] = Path.GetFileName(path);
                ctx.Estado[outputPrefix + ".ext"] = ext;
                ctx.Estado[outputPrefix + ".text"] = text;
                ctx.Estado[outputPrefix + ".textLen"] = textLen;
                ctx.Estado[outputPrefix + ".hasText"] = hasText;
                ctx.Estado[outputPrefix + ".modeUsed"] = modeUsed;
                ctx.Estado[outputPrefix + ".warning"] = warning ?? "";
                ctx.Estado[outputPrefix + ".error"] = "";
                ctx.Estado[outputPrefix + ".sizeBytes"] = bytes.Length;

                // También guardamos un objeto simple si aún no existe
                if (!ctx.Estado.ContainsKey(outputPrefix))
                {
                    var obj = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase)
                    {
                        ["filename"] = Path.GetFileName(path),
                        ["ext"] = ext,
                        ["text"] = text,
                        ["textLen"] = textLen,
                        ["hasText"] = hasText,
                        ["modeUsed"] = modeUsed,
                        ["warning"] = warning ?? "",
                        ["error"] = "",
                        ["sizeBytes"] = bytes.Length
                    };
                    ctx.Estado[outputPrefix] = obj;
                }

                ctx.Log("[doc.load] OK — Archivo cargado y texto extraído. outputPrefix=" + outputPrefix);

                return Task.FromResult(new ResultadoEjecucion { Etiqueta = "ok" });
            }
            catch (Exception ex)
            {
                return Task.FromResult(Error(ctx, outputPrefix, "[doc.load] Excepción: " + ex.Message));
            }
        }

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

        private string Get(IDictionary<string, object> p, string key)
        {
            if (p != null && p.TryGetValue(key, out var v) && v != null) return v.ToString();
            return null;
        }

        private ResultadoEjecucion Error(ContextoEjecucion ctx, string outputPrefix, string msg)
        {
            ctx.Log(msg);
            ctx.Estado["wf.error"] = true;
            ctx.Estado["doc.load.lastError"] = msg;

            // ✅ para que el logger no muestre todo vacío
            if (!string.IsNullOrWhiteSpace(outputPrefix))
            {
                ctx.Estado[outputPrefix + ".error"] = msg;
                ctx.Estado[outputPrefix + ".hasText"] = false;
                ctx.Estado[outputPrefix + ".textLen"] = 0;
                ctx.Estado[outputPrefix + ".warning"] = "";
            }

            return new ResultadoEjecucion { Etiqueta = "error" };
        }
    }
}
