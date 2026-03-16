
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.RegularExpressions;
using Intranet.WorkflowStudio.WebForms.App_Code.Handlers;

namespace Intranet.WorkflowStudio.WebForms.DocumentProcessing
{
    public static class FacturaElectronicaArExtractor
    {
        public static void Apply(ContextoEjecucion ctx, string prefix, string text)
        {
            if (ctx == null) throw new ArgumentNullException("ctx");
            if (string.IsNullOrWhiteSpace(prefix)) prefix = "factura";
            if (text == null) text = "";

            var warnings = new List<string>();
            string normalized = NormalizeText(text);
            string firstCopy = TakeFirstCopy(normalized);
            string baseKey = "biz." + prefix + ".";

            ctx.Estado[baseKey + "extractor"] = "FACTURA_AR";
            ctx.Estado[baseKey + "rawTextLen"] = text.Length;
            ctx.Estado[baseKey + "textLen"] = normalized.Length;

            string letra = ExtractTipoComprobante(firstCopy);
            string puntoVenta = ExtractPuntoVenta(firstCopy);
            string numeroComprobante = ExtractNumeroComprobanteSolo(firstCopy);
            string numero = BuildNumeroComprobante(puntoVenta, numeroComprobante);

            string fecha = ExtractDateAfterLabel(firstCopy, "Fecha de Emisión");
            string[] periodo = ExtractPeriodoFacturado(firstCopy);
            string periodoDesde = periodo[0];
            string periodoHasta = periodo[1];
            string vtoPago = periodo[2];

            string cae = ExtractFirst(firstCopy,
                @"\bCAE\s*N[°ºo]?\s*:\s*([0-9]{8,20})\b",
                @"\bCAE\s*:\s*([0-9]{8,20})\b");
            string caeVto = ExtractDateAfterLabel(firstCopy, "Fecha de Vto. de CAE");
            string ejemplar = ExtractFirst(firstCopy, @"\b(ORIGINAL|DUPLICADO|TRIPLICADO)\b");
            string remito = ExtractFirst(firstCopy, @"\bRemito\s+Asociado\s*:\s*([0-9]{4}-[0-9]{8})\b");

            SetIfNotEmpty(ctx, baseKey + "tipoComprobante", letra);
            SetIfNotEmpty(ctx, baseKey + "letra", letra);
            SetIfNotEmpty(ctx, baseKey + "numero", numero);
            SetIfNotEmpty(ctx, baseKey + "fecha", fecha);
            SetIfNotEmpty(ctx, baseKey + "periodoDesde", periodoDesde);
            SetIfNotEmpty(ctx, baseKey + "periodoHasta", periodoHasta);
            SetIfNotEmpty(ctx, baseKey + "cae", cae);
            SetIfNotEmpty(ctx, baseKey + "caeVencimiento", caeVto);
            SetIfNotEmpty(ctx, baseKey + "ejemplar", ejemplar);
            SetIfNotEmpty(ctx, baseKey + "remitoAsociado", remito);
            SetIfNotEmpty(ctx, baseKey + "vencimientoPago", vtoPago);
            SetIfNotEmpty(ctx, baseKey + "puntoVenta", puntoVenta);
            SetIfNotEmpty(ctx, baseKey + "numeroComprobante", numeroComprobante);

            string emisorNombre = ExtractFirst(firstCopy, @"\bRaz[oó]n\s+Social\s*:\s*([^\n]+)");
            string emisorDireccion = ExtractEmisorDireccion(firstCopy);
            string emisorIva = ExtractEmisorCondicionIva(firstCopy);
            string emisorIngBrutos = ExtractValueAfterLabelOrNextLine(firstCopy, "Ingresos Brutos");
            string emisorInicioActividades = ExtractDateAfterLabel(firstCopy, "Fecha de Inicio de Actividades");

            string receptorNombre = ExtractFirst(firstCopy,
                @"\bApellido\s+y\s+Nombre\s*/\s*Raz[oó]n\s+Social\s*:\s*([^\n]+)",
                @"\bCliente\s*:\s*([^\n]+)",
                @"\bSeñor(?:es)?\s*:\s*([^\n]+)");
            string receptorDireccion = ExtractReceptorDireccion(firstCopy);
            string receptorIva = ExtractReceptorCondicionIva(firstCopy);

            var cuits = ExtractCuits(firstCopy);
            if (cuits.Count > 0) SetIfNotEmpty(ctx, baseKey + "emisor.cuit", cuits[0]);
            if (cuits.Count > 1) SetIfNotEmpty(ctx, baseKey + "receptor.cuit", cuits[1]);

            SetIfNotEmpty(ctx, baseKey + "emisor.nombre", emisorNombre);
            SetIfNotEmpty(ctx, baseKey + "emisor.direccion", emisorDireccion);
            SetIfNotEmpty(ctx, baseKey + "emisor.condicionIva", emisorIva);
            SetIfNotEmpty(ctx, baseKey + "emisor.ingresosBrutos", emisorIngBrutos);
            SetIfNotEmpty(ctx, baseKey + "emisor.fechaInicioActividades", emisorInicioActividades);

            SetIfNotEmpty(ctx, baseKey + "receptor.nombre", receptorNombre);
            SetIfNotEmpty(ctx, baseKey + "receptor.direccion", receptorDireccion);
            SetIfNotEmpty(ctx, baseKey + "receptor.condicionIva", receptorIva);

            decimal? subtotal = ExtractMoneyAfterLabel(firstCopy, "Subtotal");
            decimal? otrosTributos = ExtractMoneyAfterLabel(firstCopy, "Importe Otros Tributos", "Otros Tributos");
            decimal? iva21 = ExtractMoneyAfterLabel(firstCopy, "IVA (21%)", "IVA 21%", "IVA 21");
            decimal? total = ExtractMoneyAfterLabel(firstCopy, "Importe Total", "TOTAL", "Total");

            SetIfHasValue(ctx, baseKey + "subtotal", subtotal);
            SetIfHasValue(ctx, baseKey + "otrosTributos", otrosTributos);
            SetIfHasValue(ctx, baseKey + "iva21", iva21);
            SetIfHasValue(ctx, baseKey + "total", total);

            var items = ExtractItems(firstCopy, warnings);
            ctx.Estado[baseKey + "items"] = items;
            ctx.Estado[baseKey + "itemsCount"] = items.Count;

            bool validacionBasicaOk =
                !string.IsNullOrWhiteSpace(numero) &&
                !string.IsNullOrWhiteSpace(fecha) &&
                !string.IsNullOrWhiteSpace(cae) &&
                total.HasValue;

            ctx.Estado[baseKey + "validacionBasicaOk"] = validacionBasicaOk;

            if (items.Count == 0)
                warnings.Add("No se detectaron items[].");
            if (string.IsNullOrWhiteSpace(emisorNombre))
                warnings.Add("No se pudo resolver emisor.nombre.");
            if (string.IsNullOrWhiteSpace(receptorNombre))
                warnings.Add("No se pudo resolver receptor.nombre.");
            if (!subtotal.HasValue)
                warnings.Add("No se pudo resolver subtotal.");
            if (!total.HasValue)
                warnings.Add("No se pudo resolver total.");

            if (warnings.Count > 0)
                ctx.Estado[baseKey + "warnings"] = warnings;

            ctx.Log(
                "[FacturaElectronicaArExtractor] OK" +
                " tipo=" + SafeLog(letra) +
                " numero=" + SafeLog(numero) +
                " fecha=" + SafeLog(fecha) +
                " cae=" + SafeLog(cae) +
                " items=" + items.Count +
                " total=" + (total.HasValue ? total.Value.ToString("0.00", CultureInfo.InvariantCulture) : "")
            );
        }

        private static string TakeFirstCopy(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return "";
            int idxDup = text.IndexOf("\nDUPLICADO", StringComparison.OrdinalIgnoreCase);
            int idxTri = text.IndexOf("\nTRIPLICADO", StringComparison.OrdinalIgnoreCase);
            int cut = -1;
            if (idxDup >= 0 && idxTri >= 0) cut = Math.Min(idxDup, idxTri);
            else if (idxDup >= 0) cut = idxDup;
            else if (idxTri >= 0) cut = idxTri;
            if (cut > 0) return text.Substring(0, cut).Trim();
            return text;
        }

        private static string ExtractPuntoVenta(string text)
        {
            var lines = GetLines(text);
            for (int i = 0; i < lines.Count; i++)
            {
                var m = Regex.Match(lines[i], @"Punto\s+de\s+Venta\s*:\s*(\d{1,5})", RegexOptions.IgnoreCase);
                if (m.Success) return m.Groups[1].Value.PadLeft(5, '0');

                if (Regex.IsMatch(lines[i], @"Punto\s+de\s+Venta\s*:\s*Comp\.?\s*Nro\.?\s*:", RegexOptions.IgnoreCase))
                {
                    if (i + 1 < lines.Count)
                    {
                        var m2 = Regex.Match(lines[i + 1], @"^(\d{1,5})\s+(\d{1,8})$");
                        if (m2.Success) return m2.Groups[1].Value.PadLeft(5, '0');
                    }
                }
            }

            var mx = Regex.Match(text,
                @"Punto\s+de\s+Venta\s*:\s*(\d{1,5})[\s\S]{0,80}?Comp\.?\s*Nro\.?\s*:\s*(\d{1,8})",
                RegexOptions.IgnoreCase | RegexOptions.Multiline | RegexOptions.Singleline);
            if (mx.Success) return mx.Groups[1].Value.PadLeft(5, '0');

            mx = Regex.Match(text,
                @"Punto\s+de\s+Venta\s*:\s*Comp\.?\s*Nro\.?\s*:\s*(\d{1,5})\s+(\d{1,8})",
                RegexOptions.IgnoreCase | RegexOptions.Multiline | RegexOptions.Singleline);
            if (mx.Success) return mx.Groups[1].Value.PadLeft(5, '0');

            return null;
        }

        private static string ExtractNumeroComprobanteSolo(string text)
        {
            var lines = GetLines(text);
            for (int i = 0; i < lines.Count; i++)
            {
                var m = Regex.Match(lines[i], @"Comp\.?\s*Nro\.?\s*:\s*(\d{1,8})", RegexOptions.IgnoreCase);
                if (m.Success) return m.Groups[1].Value.PadLeft(8, '0');

                if (Regex.IsMatch(lines[i], @"Punto\s+de\s+Venta\s*:\s*Comp\.?\s*Nro\.?\s*:", RegexOptions.IgnoreCase))
                {
                    if (i + 1 < lines.Count)
                    {
                        var m2 = Regex.Match(lines[i + 1], @"^(\d{1,5})\s+(\d{1,8})$");
                        if (m2.Success) return m2.Groups[2].Value.PadLeft(8, '0');
                    }
                }
            }

            var mx = Regex.Match(text,
                @"Punto\s+de\s+Venta\s*:\s*(\d{1,5})[\s\S]{0,80}?Comp\.?\s*Nro\.?\s*:\s*(\d{1,8})",
                RegexOptions.IgnoreCase | RegexOptions.Multiline | RegexOptions.Singleline);
            if (mx.Success) return mx.Groups[2].Value.PadLeft(8, '0');

            mx = Regex.Match(text,
                @"Punto\s+de\s+Venta\s*:\s*Comp\.?\s*Nro\.?\s*:\s*(\d{1,5})\s+(\d{1,8})",
                RegexOptions.IgnoreCase | RegexOptions.Multiline | RegexOptions.Singleline);
            if (mx.Success) return mx.Groups[2].Value.PadLeft(8, '0');

            return null;
        }

        private static string BuildNumeroComprobante(string puntoVenta, string numeroComprobante)
        {
            if (string.IsNullOrWhiteSpace(puntoVenta) || string.IsNullOrWhiteSpace(numeroComprobante))
                return null;

            return puntoVenta.PadLeft(5, '0') + "-" + numeroComprobante.PadLeft(8, '0');
        }

        private static string NormalizeText(string text)
        {
            if (string.IsNullOrEmpty(text)) return "";

            string s = text
                .Replace('\u00A0', ' ')
                .Replace("\r\n", "\n")
                .Replace("\r", "\n")
                .Replace("\t", " ");

            var rawLines = s.Split('\n');
            var lines = new List<string>();

            foreach (var raw in rawLines)
            {
                var line = Regex.Replace(raw ?? "", @"\s+", " ").Trim();
                if (line.Length == 0) continue;
                lines.Add(line);
            }

            return string.Join("\n", lines.ToArray());
        }

        private static void SetIfNotEmpty(ContextoEjecucion ctx, string key, string value)
        {
            if (ctx == null) return;
            if (string.IsNullOrWhiteSpace(key)) return;
            if (string.IsNullOrWhiteSpace(value)) return;
            ctx.Estado[key] = value.Trim();
        }

        private static void SetIfHasValue(ContextoEjecucion ctx, string key, decimal? value)
        {
            if (ctx == null) return;
            if (string.IsNullOrWhiteSpace(key)) return;
            if (!value.HasValue) return;
            ctx.Estado[key] = value.Value;
        }

        private static string ExtractFirst(string text, params string[] patterns)
        {
            if (string.IsNullOrWhiteSpace(text) || patterns == null) return null;

            foreach (var pattern in patterns)
            {
                var v = ExtractFirst(text, pattern);
                if (!string.IsNullOrWhiteSpace(v))
                    return v;
            }

            return null;
        }

        private static string ExtractFirst(string text, string pattern)
        {
            if (string.IsNullOrWhiteSpace(text) || string.IsNullOrWhiteSpace(pattern))
                return null;

            try
            {
                var m = Regex.Match(
                    text,
                    pattern,
                    RegexOptions.IgnoreCase | RegexOptions.Multiline | RegexOptions.Singleline);

                if (!m.Success) return null;

                if (m.Groups.Count > 1 && m.Groups[1].Success)
                    return CleanupValue(m.Groups[1].Value);

                return CleanupValue(m.Value);
            }
            catch
            {
                return null;
            }
        }

        private static string CleanupValue(string value)
        {
            if (string.IsNullOrWhiteSpace(value)) return null;
            value = value.Replace('\u00A0', ' ');
            value = Regex.Replace(value, @"\s+", " ").Trim();
            return value;
        }

        private static string ExtractTipoComprobante(string text)
        {
            var t = ExtractFirst(text,
                @"\bFACTURA\s+([ABCEMT])\b",
                @"\b([ABCEMT])\s+COD\.\s*\d{3}\s+FACTURA\b",
                @"^\s*([ABCEMT])\s*$");

            if (string.IsNullOrWhiteSpace(t))
            {
                var lines = GetLines(text);
                for (int i = 0; i < lines.Count - 1; i++)
                {
                    if (Regex.IsMatch(lines[i], @"^[ABCEMT]$", RegexOptions.IgnoreCase) &&
                        lines[i + 1].Equals("COD. 011", StringComparison.OrdinalIgnoreCase))
                        return lines[i].ToUpperInvariant();
                }
                return null;
            }

            t = t.Trim().ToUpperInvariant();
            if (t == "A" || t == "B" || t == "C" || t == "E" || t == "M" || t == "T")
                return t;

            return null;
        }

        private static List<string> ExtractCuits(string text)
        {
            var result = new List<string>();
            if (string.IsNullOrWhiteSpace(text)) return result;

            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var ms = Regex.Matches(text, @"\b(?:\d{2}-\d{8}-\d|\d{11})\b");
            foreach (Match m in ms)
            {
                if (!m.Success) continue;
                var cuit = CleanupValue(m.Value);
                if (string.IsNullOrWhiteSpace(cuit)) continue;
                cuit = cuit.Replace("-", "");
                if (seen.Add(cuit))
                    result.Add(cuit);
            }

            return result;
        }

        private static string ExtractEmisorNombre(string text)
        {
            return ExtractFirst(text,
                @"\bRaz[oó]n\s+Social\s*:\s*([^\n]+)");
        }

        private static string ExtractEmisorDireccion(string text)
        {
            var lines = GetLines(text);

            for (int i = 0; i < lines.Count; i++)
            {
                var m = Regex.Match(lines[i], @"^Domicilio\s+Comercial\s*:\s*(.*)$", RegexOptions.IgnoreCase);
                if (!m.Success) continue;

                var parts = new List<string>();
                string first = CleanupValue(m.Groups[1].Value);
                if (!string.IsNullOrWhiteSpace(first))
                    parts.Add(first);

                for (int j = i + 1; j < lines.Count && j <= i + 3; j++)
                {
                    if (Regex.IsMatch(lines[j], @"^(Ingresos\s+Brutos|Fecha\s+de\s+Inicio\s+de\s+Actividades|Condición\s+frente\s+al\s+IVA)\b", RegexOptions.IgnoreCase))
                        continue;

                    if (Regex.IsMatch(lines[j], @"^(Apellido\s+y\s+Nombre\s*/\s*Raz[oó]n\s+Social|CUIT\s*:|Condición\s+de\s+venta)\b", RegexOptions.IgnoreCase))
                        break;

                    if (!IsMetadataLine(lines[j]))
                        parts.Add(lines[j]);
                }

                if (parts.Count > 0)
                    return CleanupValue(string.Join(" ", parts.ToArray()));
            }

            return null;
        }

        private static string ExtractEmisorCondicionIva(string text)
        {
            return ExtractValueAfterLabelOrNextLine(text, "Condición frente al IVA");
        }

        private static string ExtractReceptorCondicionIva(string text)
        {
            var lines = GetLines(text);
            int found = 0;

            for (int i = 0; i < lines.Count; i++)
            {
                if (!Regex.IsMatch(lines[i], @"^Condición\s+frente\s+al\s+IVA\s*:?", RegexOptions.IgnoreCase))
                    continue;

                found++;
                if (found != 2) continue;

                var sameLine = Regex.Match(lines[i], @"^Condición\s+frente\s+al\s+IVA\s*:\s*(.+)$", RegexOptions.IgnoreCase);
                if (sameLine.Success)
                {
                    string value = CleanupValue(sameLine.Groups[1].Value);
                    int idxDom = IndexOfIgnoreCase(value, "Domicilio:");
                    if (idxDom >= 0)
                        value = CleanupValue(value.Substring(0, idxDom));

                    if (!string.IsNullOrWhiteSpace(value))
                        return value;
                }

                for (int j = i + 1; j < lines.Count && j <= i + 3; j++)
                {
                    if (IsMetadataLine(lines[j])) continue;
                    return lines[j];
                }
            }

            return null;
        }

        private static string ExtractSecondCondicionIva(string text)
        {
            var lines = GetLines(text);
            int found = 0;
            for (int i = 0; i < lines.Count; i++)
            {
                if (!Regex.IsMatch(lines[i], @"^Condición\s+frente\s+al\s+IVA\s*:?", RegexOptions.IgnoreCase))
                    continue;

                found++;
                if (found != 2) continue;

                string sameLine = ExtractFirst(lines[i], @"^Condición\s+frente\s+al\s+IVA\s*:\s*(.+)$");
                if (!string.IsNullOrWhiteSpace(sameLine)) return sameLine;

                for (int j = i + 1; j < lines.Count && j <= i + 4; j++)
                {
                    if (IsMetadataLine(lines[j])) continue;
                    return lines[j];
                }
            }
            return null;
        }

        private static string ExtractReceptorDireccion(string text)
        {
            var lines = GetLines(text);

            for (int i = 0; i < lines.Count; i++)
            {
                int idx = IndexOfIgnoreCase(lines[i], "Domicilio:");
                if (idx < 0) continue;

                string tail = CleanupValue(lines[i].Substring(idx + "Domicilio:".Length));
                var parts = new List<string>();

                if (!string.IsNullOrWhiteSpace(tail))
                    parts.Add(tail);

                for (int j = i + 1; j < lines.Count && j <= i + 2; j++)
                {
                    if (Regex.IsMatch(lines[j], @"^(Condición\s+de\s+venta|Precio\s+Unit\.|Código\s+Producto\s*/\s*Servicio|CAE\b|P[áa]g\.)", RegexOptions.IgnoreCase))
                        break;

                    if (!IsMetadataLine(lines[j]))
                        parts.Add(lines[j]);
                }

                if (parts.Count > 0)
                    return CleanupValue(string.Join(" ", parts.ToArray()));
            }

            return null;
        }

        private static string TakeHeaderChunk(string text, int maxLines)
        {
            var lines = GetLines(text);
            if (lines.Count == 0) return "";

            if (maxLines < 1) maxLines = 1;
            if (maxLines > lines.Count) maxLines = lines.Count;

            var list = new List<string>();
            for (int i = 0; i < maxLines; i++)
                list.Add(lines[i]);

            return string.Join("\n", list.ToArray());
        }

        private static bool IsMetadataLine(string line)
        {
            if (string.IsNullOrWhiteSpace(line)) return true;

            line = line.Trim();

            if (Regex.IsMatch(line, @"^[ABCEMT]$", RegexOptions.IgnoreCase)) return true;
            if (Regex.IsMatch(line, @"^(FACTURA|COD\.|N[°ºo]|Fecha|CUIT|Cliente|Direcci[oó]n|Domicilio|IVA|Remito|Vto|CAE|TOTAL|Subtotal|Importe|Pág\.|Cantidad|Producto / Servicio|U\. Medida|Precio Unit\.|% Bonif|Imp\. Bonif\.)\b", RegexOptions.IgnoreCase)) return true;

            return false;
        }

        private static int IndexOfIgnoreCase(string text, string value)
        {
            if (text == null || value == null) return -1;
            return text.IndexOf(value, StringComparison.OrdinalIgnoreCase);
        }

        private static List<string> GetLines(string text)
        {
            var result = new List<string>();
            if (string.IsNullOrWhiteSpace(text)) return result;

            var parts = text.Split('\n');
            foreach (var p in parts)
            {
                var line = CleanupValue(p);
                if (!string.IsNullOrWhiteSpace(line))
                    result.Add(line);
            }

            return result;
        }

        private static string ExtractDateAfterLabel(string text, string label)
        {
            string direct = ExtractFirst(text,
                Regex.Escape(label) + @"\s*:\s*(\d{2}/\d{2}/\d{4})");
            if (!string.IsNullOrWhiteSpace(direct))
                return direct;

            var lines = GetLines(text);
            for (int i = 0; i < lines.Count; i++)
            {
                if (!Regex.IsMatch(lines[i], "^" + Regex.Escape(label) + @"\s*:?\s*$", RegexOptions.IgnoreCase))
                    continue;

                for (int j = i + 1; j < lines.Count && j <= i + 5; j++)
                {
                    if (Regex.IsMatch(lines[j], @"^\d{2}/\d{2}/\d{4}$"))
                        return lines[j];
                }
            }

            return null;
        }

        private static string[] ExtractPeriodoFacturado(string text)
        {
            var result = new string[] { null, null, null };

            var m = Regex.Match(
                text,
                @"Per[ií]odo\s+Facturado\s+Desde\s*:\s*(\d{2}/\d{2}/\d{4})\s+Hasta\s*:\s*(\d{2}/\d{2}/\d{4})\s+Fecha\s+de\s+Vto\.\s+para\s+el\s+pago\s*:\s*(\d{2}/\d{2}/\d{4})",
                RegexOptions.IgnoreCase | RegexOptions.Multiline | RegexOptions.Singleline);

            if (m.Success)
            {
                result[0] = m.Groups[1].Value;
                result[1] = m.Groups[2].Value;
                result[2] = m.Groups[3].Value;
            }

            return result;
        }

        private static string ExtractValueAfterLabelOrNextLine(string text, string label)
        {
            string direct = ExtractFirst(text,
                Regex.Escape(label) + @"\s*:\s*([^\n]+)");
            if (!string.IsNullOrWhiteSpace(direct))
                return direct;

            var lines = GetLines(text);
            for (int i = 0; i < lines.Count; i++)
            {
                if (!Regex.IsMatch(lines[i], "^" + Regex.Escape(label) + @"\s*:?\s*$", RegexOptions.IgnoreCase))
                    continue;

                for (int j = i + 1; j < lines.Count && j <= i + 4; j++)
                {
                    if (IsMetadataLine(lines[j])) continue;
                    return lines[j];
                }
            }

            return null;
        }

        private static string ExtractWrappedValue(string text, string label)
        {
            var lines = GetLines(text);
            for (int i = 0; i < lines.Count; i++)
            {
                var m = Regex.Match(lines[i], "^" + Regex.Escape(label) + @"\s*:\s*(.*)$", RegexOptions.IgnoreCase);
                if (!m.Success) continue;

                string first = CleanupValue(m.Groups[1].Value);
                var parts = new List<string>();
                if (!string.IsNullOrWhiteSpace(first))
                    parts.Add(first);

                for (int j = i + 1; j < lines.Count && j <= i + 2; j++)
                {
                    if (IsMetadataLine(lines[j])) break;
                    parts.Add(lines[j]);
                }

                if (parts.Count > 0)
                    return string.Join(" ", parts.ToArray());
            }

            return null;
        }

        private static decimal? ExtractMoneyAfterLabel(string text, params string[] labels)
        {
            if (string.IsNullOrWhiteSpace(text) || labels == null) return null;

            foreach (var label in labels)
            {
                if (string.IsNullOrWhiteSpace(label)) continue;

                string p1 = Regex.Escape(label) + @"\s*[:\-]?\s*\$?\s*([0-9\.\,]+)";
                string p2 = Regex.Escape(label) + @"\s*(?:\n)+\s*\$?\s*([0-9\.\,]+)";
                string p3 = Regex.Escape(label) + @"\s*:\s*\$\s*\n\s*([0-9\.\,]+)";

                string raw = ExtractFirst(text, p1, p2, p3);
                var dec = ParseMoney(raw);
                if (dec.HasValue) return dec;
            }

            return null;
        }

        private static decimal? ParseMoney(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw)) return null;

            raw = raw.Replace("$", "").Replace("ARS", "").Trim();

            decimal value;
            if (WfNumbers.TryParseDecimalAR(raw, out value))
                return value;

            return null;
        }

        private static decimal? ParseDecimalLoose(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw)) return null;

            raw = raw.Trim();

            decimal value;
            if (WfNumbers.TryParseDecimalAR(raw, out value))
                return value;

            return null;
        }

        private static List<Dictionary<string, object>> ExtractItems(string text, List<string> warnings)
        {
            var items = ExtractItemsVerticalAfip(text, warnings);
            if (items.Count > 0) return items;

            items = ExtractItemsPdfInlineRows(text, warnings);
            if (items.Count > 0) return items;

            items = ExtractItemsAfipTable(text, warnings);
            if (items.Count > 0) return items;

            items = ExtractItemsLabeled(text, warnings);
            if (items.Count > 0) return items;

            return new List<Dictionary<string, object>>();
        }

        private static List<Dictionary<string, object>> ExtractItemsVerticalAfip(string text, List<string> warnings)
        {
            var items = new List<Dictionary<string, object>>();
            var lines = GetLines(text);
            if (lines.Count == 0) return items;

            int idxHeader = -1;
            int iLine = -1;

            for (int i = 0; i < lines.Count; i++)
            {
                bool esHeaderCodigo = Regex.IsMatch(
                    lines[i],
                    @"C[oó]digo\s+Producto\s*/\s*Servicio\s+Cantidad\s+U\.\s*Medida",
                    RegexOptions.IgnoreCase);

                bool esHeaderImportes = Regex.IsMatch(
                    lines[i],
                    @"Precio\s+Unit\.\s+%\s*Bonif\s+Imp\.\s*Bonif\.\s+Subtotal",
                    RegexOptions.IgnoreCase);

                if (esHeaderCodigo)
                {
                    idxHeader = i;
                    iLine = i + 1;
                    break;
                }

                if (esHeaderImportes && i + 1 < lines.Count &&
                    Regex.IsMatch(lines[i + 1],
                        @"C[oó]digo\s+Producto\s*/\s*Servicio\s+Cantidad\s+U\.\s*Medida",
                        RegexOptions.IgnoreCase))
                {
                    idxHeader = i;
                    iLine = i + 2;
                    break;
                }
            }

            if (idxHeader < 0 || iLine < 0) return items;

            string unidadDefault = null;

            if (iLine < lines.Count &&
                Regex.IsMatch(lines[iLine], @"^(unidades?|unidad|hrs?|horas?)$", RegexOptions.IgnoreCase))
            {
                unidadDefault = lines[iLine];
                iLine++;
            }

            while (iLine < lines.Count)
            {
                string line = lines[iLine];

                if (Regex.IsMatch(line, @"^Subtotal\s*:\s*\$?\s*[0-9\.\,]+$", RegexOptions.IgnoreCase) ||
                    Regex.IsMatch(line, @"^Importe\s+Otros\s+Tributos", RegexOptions.IgnoreCase) ||
                    Regex.IsMatch(line, @"^Importe\s+Total", RegexOptions.IgnoreCase) ||
                    Regex.IsMatch(line, @"^CAE\b", RegexOptions.IgnoreCase) ||
                    Regex.IsMatch(line, @"^P[áa]g\.", RegexOptions.IgnoreCase))
                    break;

                var m = Regex.Match(
                    line,
                    @"^(?<desc>.+?)\s+(?<cant>\d+(?:[.,]\d+)?)\s+(?<pu>\d+(?:\.\d{3})*,\d{2})\s+(?<bonifPct>\d+(?:[.,]\d+)?)\s+(?<bonifImp>\d+(?:\.\d{3})*,\d{2})\s+(?<sub>\d+(?:\.\d{3})*,\d{2})$",
                    RegexOptions.IgnoreCase | RegexOptions.Multiline | RegexOptions.Singleline);

                if (!m.Success)
                {
                    iLine++;
                    continue;
                }

                var item = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);

                decimal? cantidad = ParseDecimalLoose(m.Groups["cant"].Value);
                decimal? precioUnitario = ParseMoney(m.Groups["pu"].Value);
                decimal? bonifPct = ParseDecimalLoose(m.Groups["bonifPct"].Value);
                decimal? bonifImp = ParseMoney(m.Groups["bonifImp"].Value);
                decimal? subtotal = ParseMoney(m.Groups["sub"].Value);

                item["descripcion"] = CleanupValue(m.Groups["desc"].Value);

                if (cantidad.HasValue) item["cantidad"] = cantidad.Value;
                else item["cantidadTexto"] = m.Groups["cant"].Value;

                if (!string.IsNullOrWhiteSpace(unidadDefault))
                    item["unidadMedida"] = CleanupValue(unidadDefault);

                if (precioUnitario.HasValue) item["precioUnitario"] = precioUnitario.Value;
                else item["precioUnitarioTexto"] = m.Groups["pu"].Value;

                if (bonifPct.HasValue) item["bonificacionPorcentaje"] = bonifPct.Value;
                else item["bonificacionPorcentajeTexto"] = m.Groups["bonifPct"].Value;

                if (bonifImp.HasValue) item["bonificacionImporte"] = bonifImp.Value;
                else item["bonificacionImporteTexto"] = m.Groups["bonifImp"].Value;

                if (subtotal.HasValue) item["subtotal"] = subtotal.Value;
                else item["subtotalTexto"] = m.Groups["sub"].Value;

                items.Add(item);
                iLine++;
            }

            return DeduplicateItems(items);
        }

        private static List<Dictionary<string, object>> DeduplicateItems(List<Dictionary<string, object>> items)
        {
            var result = new List<Dictionary<string, object>>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var item in items)
            {
                string desc = item.ContainsKey("descripcion") ? Convert.ToString(item["descripcion"]) : "";
                string subtotal = item.ContainsKey("subtotal") ? Convert.ToString(item["subtotal"], CultureInfo.InvariantCulture) :
                                  item.ContainsKey("subtotalTexto") ? Convert.ToString(item["subtotalTexto"]) : "";
                string key = (desc ?? "").Trim() + "|" + (subtotal ?? "").Trim();

                if (seen.Add(key))
                    result.Add(item);
            }

            return result;
        }

        private static List<Dictionary<string, object>> ExtractItemsPdfInlineRows(string text, List<string> warnings)
        {
            var items = new List<Dictionary<string, object>>();
            return items;
        }

        private static List<Dictionary<string, object>> ExtractItemsAfipTable(string text, List<string> warnings)
        {
            var items = new List<Dictionary<string, object>>();
            return items;
        }

        private static List<Dictionary<string, object>> ExtractItemsLabeled(string text, List<string> warnings)
        {
            var items = new List<Dictionary<string, object>>();
            if (string.IsNullOrWhiteSpace(text)) return items;

            try
            {
                var rx = new Regex(
                    @"Item\s*:\s*(.+?)\s+Cantidad\s*:\s*([0-9\.,]+)(?:\s+[A-Za-z]+)?\s+Precio\s*Unitario\s*:\s*\$?\s*([0-9\.\,]+)\s+Subtotal\s*:\s*\$?\s*([0-9\.\,]+)",
                    RegexOptions.IgnoreCase | RegexOptions.Singleline);

                var ms = rx.Matches(text);
                foreach (Match m in ms)
                {
                    if (!m.Success) continue;

                    var item = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);

                    string desc = CleanupValue(m.Groups[1].Value);
                    decimal? cantidad = ParseDecimalLoose(m.Groups[2].Value);
                    decimal? precioUnitario = ParseMoney(m.Groups[3].Value);
                    decimal? subtotal = ParseMoney(m.Groups[4].Value);

                    if (!string.IsNullOrWhiteSpace(desc)) item["descripcion"] = desc;
                    if (cantidad.HasValue) item["cantidad"] = cantidad.Value;
                    if (precioUnitario.HasValue) item["precioUnitario"] = precioUnitario.Value;
                    if (subtotal.HasValue) item["subtotal"] = subtotal.Value;

                    if (item.Count > 0)
                        items.Add(item);
                }
            }
            catch
            {
                warnings.Add("Falló el parser fallback de items etiquetados.");
            }

            return items;
        }

        private static bool LooksLikeMoney(string line)
        {
            if (string.IsNullOrWhiteSpace(line)) return false;
            return Regex.IsMatch(line.Trim(), @"^\$?\s*[0-9\.\,]+$");
        }

        private static string SafeLog(string s)
        {
            if (string.IsNullOrWhiteSpace(s)) return "";
            s = s.Replace("\r", " ").Replace("\n", " ").Trim();
            return s;
        }
    }
}
