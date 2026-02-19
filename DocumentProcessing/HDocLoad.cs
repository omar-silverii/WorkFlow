using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using Intranet.WorkflowStudio.WebForms.App_Code.Handlers;
using System;
using System.Collections.Generic;
using System.Configuration;
using System.Data;
using System.Data.SqlClient;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using UglyToad.PdfPig;

namespace Intranet.WorkflowStudio.WebForms.DocumentProcessing
{
    /// <summary>
    /// doc.load (MODO SIMPLE)
    ///
    /// Params:
    ///   path: string (puede incluir ${...})
    ///   mode: auto|pdf|word|text|image (default auto)
    ///   docTipoCodigo: string (opcional, pero recomendado)
    ///   connectionStringName: string (opcional, default DefaultConnection)
    ///
    /// Salida:
    ///   wf.docTipoCodigo / wf.docTipoId / wf.contextPrefix
    ///   biz.{prefix}.{campo} = valor extraído (según WF_DocTipoReglaExtract)
    ///
    /// Además (metadatos útiles):
    ///   input.filename / input.ext / input.text / input.textLen / input.hasText / input.modeUsed / input.sizeBytes
    /// </summary>
    public class HDocLoad : IManejadorNodo
    {
        public string TipoNodo => "doc.load";

        public Task<ResultadoEjecucion> EjecutarAsync(ContextoEjecucion ctx, NodeDef nodo, CancellationToken ct)
        {
            var p = nodo.Parameters ?? new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);

            string rawPath = Get(p, "path");
            string path = TemplateUtil.Expand(ctx, rawPath);

            string mode = (TemplateUtil.Expand(ctx, Get(p, "mode")) ?? "auto").Trim().ToLowerInvariant();
            string docTipoCodigo = (TemplateUtil.Expand(ctx, Get(p, "docTipoCodigo")) ?? "").Trim();

            string cnnName = p.ContainsKey("connectionStringName")
                ? Convert.ToString(p["connectionStringName"])
                : "DefaultConnection";

            if (string.IsNullOrWhiteSpace(path))
                return Task.FromResult(Error(ctx, "[doc.load] Falta parámetro 'path'"));

            if (!File.Exists(path))
                return Task.FromResult(Error(ctx, "[doc.load] Archivo no encontrado: " + path));

            try
            {
                // 1) Leer bytes
                byte[] bytes = File.ReadAllBytes(path);
                string ext = Path.GetExtension(path).ToLowerInvariant();

                // 2) Auto-detección de modo
                string modeUsed = mode;
                if (modeUsed == "auto")
                {
                    if (ext == ".pdf") modeUsed = "pdf";
                    else if (ext == ".docx") modeUsed = "word";
                    else modeUsed = "text";
                }

                // 3) Extraer texto
                string text = "";
                string warning = null;

                if (modeUsed == "pdf")
                {
                    text = ExtractPdf(bytes, ctx);
                    if (string.IsNullOrWhiteSpace(text))
                        warning = "PDF sin texto extraíble (posible escaneo / imagen).";
                }
                else if (modeUsed == "word")
                {
                    text = ExtractWord(bytes);
                }
                else if (modeUsed == "image")
                {
                    text = "";
                    warning = "Imagen sin OCR (text vacío).";
                    ctx.Log("[doc.load] mode=image (sin OCR). Se carga metadata, text vacío.");
                }
                else
                {
                    // texto plano
                    text = Encoding.UTF8.GetString(bytes);
                }

                int textLen = string.IsNullOrEmpty(text) ? 0 : text.Length;
                bool hasText = !string.IsNullOrWhiteSpace(text);

                // 4) Guardar metadatos “input.*” (interno, útil)
                ctx.Estado["input.filename"] = Path.GetFileName(path);
                ctx.Estado["input.ext"] = ext;
                ctx.Estado["input.text"] = text;
                ctx.Estado["input.textLen"] = textLen;
                ctx.Estado["input.hasText"] = hasText;
                ctx.Estado["input.modeUsed"] = modeUsed;
                ctx.Estado["input.warning"] = warning ?? "";
                ctx.Estado["input.error"] = "";
                ctx.Estado["input.sizeBytes"] = bytes.Length;

                // 5) Resolver DocTipo (DB) -> DocTipoId + ContextPrefix
                if (string.IsNullOrWhiteSpace(docTipoCodigo))
                {
                    // En modo simple: si no viene, cortamos (para evitar “magia” confusa)
                    return Task.FromResult(Error(ctx, "[doc.load] Falta 'docTipoCodigo' (modo simple)."));
                }

                int docTipoId;
                string prefix;

                ResolveDocTipo(ctx, cnnName, docTipoCodigo, out docTipoId, out prefix);

                ctx.Estado["wf.docTipoCodigo"] = docTipoCodigo;
                ctx.Estado["wf.docTipoId"] = docTipoId;
                ctx.Estado["wf.contextPrefix"] = prefix;

                // 6) Cargar reglas y extraer -> biz.{prefix}.*
                var rules = LoadRules(ctx, cnnName, docTipoId);

                ApplyRules(ctx, prefix, text ?? "", rules);

                ctx.Log("[doc.load] OK — docTipo=" + docTipoCodigo + " id=" + docTipoId + " prefix=" + prefix + " reglas=" + rules.Count);
                return Task.FromResult(new ResultadoEjecucion { Etiqueta = "ok" });
            }
            catch (Exception ex)
            {
                return Task.FromResult(Error(ctx, "[doc.load] Excepción: " + ex.Message));
            }
        }

        // =========================
        // DB: Resolve DocTipo
        // =========================
        private void ResolveDocTipo(ContextoEjecucion ctx, string cnnName, string codigo, out int docTipoId, out string prefix)
        {
            docTipoId = 0;
            prefix = "";

            var cs = GetConnectionString(cnnName);
            using (var cn = new SqlConnection(cs))
            using (var cmd = cn.CreateCommand())
            {
                cmd.CommandText = @"
SELECT TOP 1 DocTipoId, ContextPrefix
FROM WF_DocTipo
WHERE Codigo = @Codigo AND EsActivo = 1";
                cmd.Parameters.AddWithValue("@Codigo", codigo);

                cn.Open();
                using (var dr = cmd.ExecuteReader())
                {
                    if (!dr.Read())
                        throw new Exception("[doc.load] DocTipo no encontrado o inactivo: " + codigo);

                    docTipoId = Convert.ToInt32(dr["DocTipoId"]);
                    prefix = Convert.ToString(dr["ContextPrefix"] ?? "").Trim();
                }
            }

            if (string.IsNullOrWhiteSpace(prefix))
                throw new Exception("[doc.load] DocTipo sin ContextPrefix: " + codigo);
        }

        // =========================
        // DB: Load rules
        // =========================
        private List<RuleRow> LoadRules(ContextoEjecucion ctx, string cnnName, int docTipoId)
        {
            var list = new List<RuleRow>();

            var cs = GetConnectionString(cnnName);
            using (var cn = new SqlConnection(cs))
            using (var cmd = cn.CreateCommand())
            {
                cmd.CommandText = @"
SELECT Campo, Regex, ISNULL(Grupo,1) AS Grupo, ISNULL(Orden,0) AS Orden, ISNULL(TipoDato,'') AS TipoDato
FROM WF_DocTipoReglaExtract
WHERE DocTipoId = @DocTipoId AND Activo = 1
ORDER BY Grupo, Orden, Id";
                cmd.Parameters.AddWithValue("@DocTipoId", docTipoId);

                cn.Open();
                using (var dr = cmd.ExecuteReader())
                {
                    while (dr.Read())
                    {
                        list.Add(new RuleRow
                        {
                            Campo = Convert.ToString(dr["Campo"] ?? "").Trim(),
                            Regex = Convert.ToString(dr["Regex"] ?? ""),
                            Grupo = Convert.ToInt32(dr["Grupo"]),
                            Orden = Convert.ToInt32(dr["Orden"]),
                            TipoDato = Convert.ToString(dr["TipoDato"] ?? "").Trim()
                        });
                    }
                }
            }

            return list;
        }

        // =========================
        // Apply regex rules -> biz.{prefix}.{campo}
        // =========================
        private void ApplyRules(ContextoEjecucion ctx, string prefix, string text, List<RuleRow> rules)
        {
            foreach (var r in rules)
            {
                if (string.IsNullOrWhiteSpace(r.Campo) || string.IsNullOrWhiteSpace(r.Regex))
                    continue;

                string val = null;
                try
                {
                    var m = Regex.Match(text, r.Regex, RegexOptions.Multiline);
                    if (m.Success)
                    {
                        int grp = 1;
                        if (grp < m.Groups.Count)
                            val = m.Groups[grp].Value;
                        else
                            val = m.Value;

                        if (val != null) val = val.Trim();
                    }
                }
                catch (Exception ex)
                {
                    ctx.Log("[doc.load/rule-error] campo=" + r.Campo + " msg=" + ex.Message);
                    continue;
                }

                if (string.IsNullOrEmpty(val))
                    continue;

                string key = "biz." + prefix + "." + r.Campo;

                // Normalización de importes (para que >= funcione)
                if (IsImporte(r.TipoDato, r.Campo))
                {
                    var dec = TryParseImporte(val);
                    if (dec.HasValue)
                    {
                        ctx.Estado[key] = dec.Value; // decimal real
                        continue;
                    }
                }

                ctx.Estado[key] = val;
            }
        }

        private bool IsImporte(string tipoDato, string campo)
        {
            var td = (tipoDato ?? "").Trim().ToLowerInvariant();
            if (td == "importe" || td == "decimal" || td == "numero" || td == "número") return true;

            // fallback por nombre si alguna regla no tiene TipoDato
            var c = (campo ?? "").Trim().ToLowerInvariant();
            if (c.Contains("monto") || c.Contains("importe") || c.Contains("total")) return true;

            return false;
        }

        private decimal? TryParseImporte(string s)
        {
            if (string.IsNullOrWhiteSpace(s)) return null;

            // Ej: "154.000,00" -> "154000.00"
            var x = s.Trim();
            x = x.Replace(" ", "");
            x = x.Replace(".", "");
            x = x.Replace(",", ".");

            decimal v;
            if (decimal.TryParse(x, NumberStyles.Number, CultureInfo.InvariantCulture, out v))
                return v;

            return null;
        }

        // =========================
        // Extractors
        
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



        // =========================
        // Helpers
        // =========================
        private string Get(IDictionary<string, object> p, string key)
        {
            if (p != null && p.TryGetValue(key, out var v) && v != null) return v.ToString();
            return null;
        }

        private string GetConnectionString(string name)
        {
            var cs = ConfigurationManager.ConnectionStrings[name];
            if (cs == null || string.IsNullOrWhiteSpace(cs.ConnectionString))
                throw new Exception("[doc.load] connectionString no encontrada en web.config: " + name);
            return cs.ConnectionString;
        }

        private ResultadoEjecucion Error(ContextoEjecucion ctx, string msg)
        {
            ctx.Log(msg);
            ctx.Estado["wf.error"] = true;
            ctx.Estado["doc.load.lastError"] = msg;

            // para que el logger no muestre todo vacío
            ctx.Estado["input.error"] = msg;
            ctx.Estado["input.hasText"] = false;
            ctx.Estado["input.textLen"] = 0;
            ctx.Estado["input.warning"] = "";

            return new ResultadoEjecucion { Etiqueta = "error" };
        }

        private class RuleRow
        {
            public string Campo;
            public string Regex;
            public int Grupo;
            public int Orden;
            public string TipoDato;
        }
    }
}
