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
            if (rules == null || rules.Count == 0) return;

            const string ITEM_PREFIX = "items[].";
            const string ITEM_BLOCK_CAMPO = "items[].__block";

            // 1) Separar reglas
            RuleRow itemBlock = null;
            var itemFields = new List<RuleRow>();
            var headerFields = new List<RuleRow>();

            foreach (var r in rules)
            {
                var campo = (r.Campo ?? "").Trim();
                if (string.IsNullOrWhiteSpace(campo) || string.IsNullOrWhiteSpace(r.Regex))
                    continue;

                if (campo.Equals(ITEM_BLOCK_CAMPO, StringComparison.OrdinalIgnoreCase))
                {
                    itemBlock = r;
                    continue;
                }

                if (campo.StartsWith(ITEM_PREFIX, StringComparison.OrdinalIgnoreCase))
                {
                    itemFields.Add(r);
                    continue;
                }

                headerFields.Add(r);
            }

            // 2) Campos únicos (header) -> biz.{prefix}.{campo}
            foreach (var r in headerFields)
            {
                string val = TryMatchValue(text, r.Regex, r.Grupo);

                if (string.IsNullOrWhiteSpace(val))
                    continue;

                string key = "biz." + prefix + "." + (r.Campo ?? "").Trim();

                // Normalización de importes
                if (IsImporte(r.TipoDato, r.Campo))
                {
                    var dec = TryParseImporte(val);
                    if (dec.HasValue)
                    {
                        ctx.Estado[key] = dec.Value;
                        continue;
                    }
                }

                ctx.Estado[key] = val.Trim();
            }

            // 3) Items[] -> requiere ItemBlock
            if (itemBlock == null || itemFields.Count == 0)
                return;

            List<Dictionary<string, object>> items = new List<Dictionary<string, object>>();

            Regex rxBlock;
            try
            {
                rxBlock = new Regex(itemBlock.Regex, RegexOptions.Multiline);
            }
            catch (Exception ex)
            {
                ctx.Log("[doc.load/items-error] ItemBlock regex inválida: " + ex.Message);
                return;
            }

            var blocks = rxBlock.Matches(text ?? "");
            foreach (Match bm in blocks)
            {
                if (!bm.Success) continue;

                // Si el block tiene grupo 1, usamos eso como “contenido del bloque”.
                // Si no, usamos el match completo.
                string blockText = null;
                if (bm.Groups != null && bm.Groups.Count > 1 && bm.Groups[1].Success)
                    blockText = bm.Groups[1].Value;
                else
                    blockText = bm.Value;

                if (string.IsNullOrWhiteSpace(blockText))
                    continue;

                var item = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);

                foreach (var r in itemFields)
                {
                    // campo real dentro del item: "codigo", "descripcion", etc.
                    var campoFull = (r.Campo ?? "").Trim();         // items[].codigo
                    var innerPath = campoFull.Substring(ITEM_PREFIX.Length).Trim(); // codigo

                    if (string.IsNullOrWhiteSpace(innerPath))
                        continue;

                    string val = TryMatchValue(blockText, r.Regex, r.Grupo);
                    if (string.IsNullOrWhiteSpace(val))
                        continue;

                    object finalVal = val.Trim();

                    if (IsImporte(r.TipoDato, innerPath))
                    {
                        var dec = TryParseImporte(val);
                        if (dec.HasValue) finalVal = dec.Value;
                    }

                    SetDictPath(item, innerPath, finalVal);
                }

                if (item.Count > 0)
                    items.Add(item);
            }

            // guardar en estado
            string itemsKey = "biz." + prefix + ".items";
            ctx.Estado[itemsKey] = items;
            ctx.Estado["biz." + prefix + ".itemsCount"] = items.Count;

            ctx.Log("[doc.load] Items: blocks=" + blocks.Count + " items=" + items.Count);
        }

        private string TryMatchValue(string text, string regex, int grupo)
        {
            if (string.IsNullOrWhiteSpace(regex)) return null;

            int g = grupo; // si viene 0, devuelve match completo (Group 0)
            if (g < 0) g = 1;

            try
            {
                var m = Regex.Match(text ?? "", regex, RegexOptions.Multiline);
                if (!m.Success) return null;

                if (m.Groups != null && g < m.Groups.Count && m.Groups[g].Success)
                    return m.Groups[g].Value;

                return m.Value;
            }
            catch
            {
                return null;
            }
        }

        private void SetDictPath(Dictionary<string, object> root, string path, object value)
        {
            // Soporta "a", "a.b", "a.b.c"
            if (root == null) return;
            path = (path ?? "").Trim();
            if (path.Length == 0) return;

            var parts = path.Split(new[] { '.' }, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 0) return;

            Dictionary<string, object> cur = root;

            for (int i = 0; i < parts.Length; i++)
            {
                var key = parts[i].Trim();
                if (key.Length == 0) continue;

                bool isLast = (i == parts.Length - 1);
                if (isLast)
                {
                    cur[key] = value;
                    return;
                }

                if (!cur.TryGetValue(key, out var next) || !(next is Dictionary<string, object>))
                {
                    var nd = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
                    cur[key] = nd;
                    cur = nd;
                }
                else
                {
                    cur = (Dictionary<string, object>)next;
                }
            }
        }

        private bool IsItemCampo(string campo)
        {
            if (string.IsNullOrWhiteSpace(campo))
                return false;

            campo = campo.Trim();

            return campo.StartsWith("items[].", StringComparison.OrdinalIgnoreCase)
                || campo.StartsWith("items[ ].", StringComparison.OrdinalIgnoreCase);
        }

        private string ItemFieldName(string campo)
        {
            if (string.IsNullOrWhiteSpace(campo))
                return null;

            campo = campo.Trim();

            if (campo.StartsWith("items[].", StringComparison.OrdinalIgnoreCase))
                return campo.Substring("items[].".Length);

            if (campo.StartsWith("items[ ].", StringComparison.OrdinalIgnoreCase))
                return campo.Substring("items[ ].".Length);

            return null;
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
