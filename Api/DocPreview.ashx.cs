using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Web;
using UglyToad.PdfPig;

namespace Intranet.WorkflowStudio.WebForms.Api
{
    public class DocPreview : IHttpHandler
    {
        public bool IsReusable => false;

        public void ProcessRequest(HttpContext context)
        {
            context.Response.ContentType = "application/json; charset=utf-8";

            try
            {
                var file = context.Request.Files["file"];
                if (file == null || file.ContentLength <= 0)
                {
                    Write(context, ok: false, error: "No se recibió archivo (field='file').");
                    return;
                }

                var name = Path.GetFileName(file.FileName ?? "");
                var ext = (Path.GetExtension(name) ?? "").ToLowerInvariant();

                byte[] bytes;
                using (var ms = new MemoryStream())
                {
                    file.InputStream.CopyTo(ms);
                    bytes = ms.ToArray();
                }

                string modeUsed = "text";
                string text = "";
                string warning = "";

                if (ext == ".pdf")
                {
                    modeUsed = "pdf";
                    text = ExtractPdf(bytes, null);
                    if (string.IsNullOrWhiteSpace(text))
                        warning = "PDF sin texto extraíble (posible escaneo / imagen).";
                }
                else if (ext == ".docx")
                {
                    modeUsed = "word";
                    text = ExtractWord(bytes);
                }
                else
                {
                    modeUsed = "text";
                    text = Encoding.UTF8.GetString(bytes);
                }

                Write(context, ok: true, text: text, modeUsed: modeUsed, warning: warning);
            }
            catch (Exception ex)
            {
                Write(context, ok: false, error: ex.Message);
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
                    {
                        var words = page.GetWords();

                        // Agrupar por coordenada Y (líneas)
                        var lines = words
                            .GroupBy(w => Math.Round(w.BoundingBox.Bottom, 1))
                            .OrderByDescending(g => g.Key);

                        foreach (var line in lines)
                        {
                            var orderedWords = line
                                .OrderBy(w => w.BoundingBox.Left)
                                .Select(w => w.Text);

                            var textLine = string.Join(" ", orderedWords);

                            if (!string.IsNullOrWhiteSpace(textLine))
                                sb.AppendLine(textLine);
                        }

                        sb.AppendLine(); // separación entre páginas
                    }

                    return sb.ToString().TrimEnd('\r', '\n');
                }
            }
            catch (Exception ex)
            {
                ctx.Log("[doc.load/pdf-error] " + ex.Message);
                return "";
            }
        }


        private static string NormalizePdfText(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";

            // 1) Normalizar saltos a \n primero
            s = s.Replace("\r\n", "\n").Replace("\r", "\n");

            // 2) Reemplazar NBSP por espacio normal
            s = s.Replace('\u00A0', ' ');

            // 3) Quitar espacios al final de cada línea + normalizar tabs
            var lines = s.Split('\n');
            for (int i = 0; i < lines.Length; i++)
            {
                var line = lines[i].Replace('\t', ' ');
                line = CollapseSpaces(line).TrimEnd();

                // 4) (opcional útil) unir palabras cortadas por guión al final de línea:
                //    si termina en "-" lo pegamos con la siguiente línea (sin espacio)
                if (line.EndsWith("-") && i + 1 < lines.Length)
                {
                    var next = (lines[i + 1] ?? "").TrimStart();
                    lines[i] = line.Substring(0, line.Length - 1) + next;
                    lines[i + 1] = ""; // anulamos la siguiente (ya la consumimos)
                }
                else
                {
                    lines[i] = line;
                }
            }

            // 5) Reconstruir y colapsar líneas vacías múltiples
            var sb = new StringBuilder();
            int emptyCount = 0;

            foreach (var ln in lines)
            {
                var line = (ln ?? "").TrimEnd();

                if (string.IsNullOrWhiteSpace(line))
                {
                    emptyCount++;
                    // permitimos como máximo 1 línea vacía seguida
                    if (emptyCount <= 1) sb.Append("\r\n");
                    continue;
                }

                emptyCount = 0;
                sb.Append(line);
                sb.Append("\r\n");
            }

            return sb.ToString().TrimEnd('\r', '\n');
        }

        private static string CollapseSpaces(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            var sb = new StringBuilder(s.Length);
            bool prevSpace = false;

            foreach (char c in s)
            {
                bool isSpace = (c == ' ');
                if (isSpace)
                {
                    if (!prevSpace) sb.Append(' ');
                    prevSpace = true;
                }
                else
                {
                    sb.Append(c);
                    prevSpace = false;
                }
            }
            return sb.ToString();
        }


        private static string ExtractWord(byte[] bytes)
        {
            try
            {
                using (var ms = new MemoryStream(bytes))
                using (var doc = WordprocessingDocument.Open(ms, false))
                {
                    var sb = new StringBuilder();

                    var body = doc.MainDocumentPart?.Document?.Body;
                    if (body == null)
                        return "";

                    foreach (var paragraph in body.Elements<Paragraph>())
                    {
                        var text = paragraph.InnerText?.Trim();

                        if (!string.IsNullOrWhiteSpace(text))
                        {
                            sb.Append(text);
                            sb.Append("\r\n");
                        }
                    }

                    return sb.ToString().TrimEnd('\r', '\n');
                }
            }
            catch
            {
                return "";
            }
        }


        private static void Write(HttpContext ctx, bool ok, string text = null, string modeUsed = null, string warning = null, string error = null)
        {
            string esc(string s) => (s ?? "")
                .Replace("\\", "\\\\")
                .Replace("\"", "\\\"")
                .Replace("\r", "\\r")
                .Replace("\n", "\\n");

            var json =
                "{"
                + "\"ok\":" + (ok ? "true" : "false")
                + ",\"text\":\"" + esc(text) + "\""
                + ",\"modeUsed\":\"" + esc(modeUsed) + "\""
                + ",\"warning\":\"" + esc(warning) + "\""
                + ",\"error\":\"" + esc(error) + "\""
                + "}";

            ctx.Response.Write(json);
        }
    }
}