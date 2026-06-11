using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.RegularExpressions;
using Intranet.WorkflowStudio.WebForms.App_Code.Handlers;

namespace Intranet.WorkflowStudio.WebForms.DocumentProcessing
{
    public static class NotaCreditoElectronicaArExtractor
    {
        public static void Apply(ContextoEjecucion ctx, string prefix, string text)
        {
            if (ctx == null) throw new ArgumentNullException("ctx");
            if (string.IsNullOrWhiteSpace(prefix)) prefix = "notaCredito";
            if (text == null) text = "";

            var warnings = new List<string>();
            string normalized = NormalizeText(text);
            string firstCopy = TakeFirstCopy(normalized);
            string baseKey = "biz." + prefix + ".";

            ctx.Estado[baseKey + "extractor"] = "NC_AR";
            ctx.Estado[baseKey + "rawTextLen"] = text.Length;
            ctx.Estado[baseKey + "textLen"] = normalized.Length;
            ctx.Estado[baseKey + "tipoDocumento"] = "NOTA_CREDITO";
            ctx.Estado[baseKey + "esNotaCredito"] = true;
            ctx.Estado[baseKey + "signo"] = -1;
            ctx.Estado[baseKey + "tipoMovimiento"] = "CREDITO";

            string descripcionComprobante = ExtractDescripcionComprobante(firstCopy);
            string letra = ExtractLetra(firstCopy);
            string codigoAfip = ExtractCodigoAfip(firstCopy);
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

            SetIfNotEmpty(ctx, baseKey + "descripcionComprobante", descripcionComprobante);
            SetIfNotEmpty(ctx, baseKey + "codigoAfip", codigoAfip);
            SetIfNotEmpty(ctx, baseKey + "tipoComprobante", letra);
            SetIfNotEmpty(ctx, baseKey + "letra", letra);
            SetIfNotEmpty(ctx, baseKey + "numero", numero);
            SetIfNotEmpty(ctx, baseKey + "fecha", fecha);
            SetIfNotEmpty(ctx, baseKey + "periodoDesde", periodoDesde);
            SetIfNotEmpty(ctx, baseKey + "periodoHasta", periodoHasta);
            SetIfNotEmpty(ctx, baseKey + "cae", cae);
            SetIfNotEmpty(ctx, baseKey + "caeVencimiento", caeVto);
            SetIfNotEmpty(ctx, baseKey + "ejemplar", ejemplar);
            SetIfNotEmpty(ctx, baseKey + "vencimientoPago", vtoPago);
            SetIfNotEmpty(ctx, baseKey + "puntoVenta", puntoVenta);
            SetIfNotEmpty(ctx, baseKey + "numeroComprobante", numeroComprobante);

            string condicionVenta = CleanupValueBeforeMarkers(
                ExtractValueAfterLabelOrNextLine(firstCopy, "Condición de venta"),
                "Factura de Crédito", "Código Producto", "Producto / Servicio");
            SetIfNotEmpty(ctx, baseKey + "condicionVenta", condicionVenta);

            var asociado = ExtractComprobanteAsociado(firstCopy);
            SetIfNotEmpty(ctx, baseKey + "comprobanteAsociado.tipo", asociado.Tipo);
            SetIfNotEmpty(ctx, baseKey + "comprobanteAsociado.numero", asociado.Numero);
            SetIfNotEmpty(ctx, baseKey + "comprobanteAsociado.letra", asociado.Letra);
            SetIfNotEmpty(ctx, baseKey + "comprobanteAsociado.puntoVenta", asociado.PuntoVenta);
            SetIfNotEmpty(ctx, baseKey + "comprobanteAsociado.numeroComprobante", asociado.NumeroComprobante);

            string emisorNombre = CleanupValueBeforeMarkers(
                ExtractFirst(firstCopy, @"\bRaz[oó]n\s+Social\s*:\s*([^\n]+)"),
                "Fecha de Emisión", "CUIT:", "Domicilio Comercial:", "Condición frente al IVA");
            string emisorDireccion = ExtractEmisorDireccion(firstCopy);
            string emisorIva = CleanupValueBeforeMarkers(
                ExtractEmisorCondicionIva(firstCopy),
                "Fecha de Inicio de Actividades", "CUIT:", "Ingresos Brutos:");
            string emisorIngBrutos = ExtractValueAfterLabelOrNextLine(firstCopy, "Ingresos Brutos");
            string emisorInicioActividades = ExtractDateAfterLabel(firstCopy, "Fecha de Inicio de Actividades");

            string receptorNombre = ExtractReceptorNombre(firstCopy);
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

            decimal? importeNetoGravado = ExtractMoneyAfterLabel(firstCopy, "Importe Neto Gravado");
            decimal? otrosTributos = ExtractMoneyAfterLabel(firstCopy, "Importe Otros Tributos", "Otros Tributos");
            decimal? iva27 = ExtractMoneyAfterLabel(firstCopy, "IVA 27%");
            decimal? iva21 = ExtractMoneyAfterLabel(firstCopy, "IVA 21%", "IVA 21");
            decimal? iva105 = ExtractMoneyAfterLabel(firstCopy, "IVA 10.5%", "IVA 10,5%", "IVA 10.5", "IVA 10,5");
            decimal? iva5 = ExtractMoneyAfterLabel(firstCopy, "IVA 5%", "IVA 5");
            decimal? iva25 = ExtractMoneyAfterLabel(firstCopy, "IVA 2.5%", "IVA 2,5%", "IVA 2.5", "IVA 2,5");
            decimal? iva0 = ExtractMoneyAfterLabel(firstCopy, "IVA 0%", "IVA 0");
            decimal? total = ExtractMoneyAfterLabel(firstCopy, "Importe Total", "TOTAL", "Total");

            SetIfHasValue(ctx, baseKey + "importeNetoGravado", importeNetoGravado);
            SetIfHasValue(ctx, baseKey + "subtotal", importeNetoGravado);
            SetIfHasValue(ctx, baseKey + "otrosTributos", otrosTributos);
            SetIfHasValue(ctx, baseKey + "importeOtrosTributos", otrosTributos);
            SetIfHasValue(ctx, baseKey + "iva27", iva27);
            SetIfHasValue(ctx, baseKey + "iva21", iva21);
            SetIfHasValue(ctx, baseKey + "iva105", iva105);
            SetIfHasValue(ctx, baseKey + "iva5", iva5);
            SetIfHasValue(ctx, baseKey + "iva25", iva25);
            SetIfHasValue(ctx, baseKey + "iva0", iva0);
            SetIfHasValue(ctx, baseKey + "total", total);

            var items = ExtractItems(firstCopy, warnings);
            ctx.Estado[baseKey + "items"] = items;
            ctx.Estado[baseKey + "itemsCount"] = items.Count;

            bool validacionBasicaOk =
                !string.IsNullOrWhiteSpace(numero) &&
                !string.IsNullOrWhiteSpace(fecha) &&
                !string.IsNullOrWhiteSpace(cae) &&
                !string.IsNullOrWhiteSpace(asociado.Numero) &&
                total.HasValue;

            ctx.Estado[baseKey + "validacionBasicaOk"] = validacionBasicaOk;

            if (items.Count == 0)
                warnings.Add("No se detectaron items[].");
            if (string.IsNullOrWhiteSpace(emisorNombre))
                warnings.Add("No se pudo resolver emisor.nombre.");
            if (string.IsNullOrWhiteSpace(receptorNombre))
                warnings.Add("No se pudo resolver receptor.nombre.");
            if (string.IsNullOrWhiteSpace(asociado.Numero))
                warnings.Add("No se pudo resolver comprobante asociado.");
            if (!importeNetoGravado.HasValue)
                warnings.Add("No se pudo resolver importeNetoGravado.");
            if (!total.HasValue)
                warnings.Add("No se pudo resolver total.");

            if (warnings.Count > 0)
                ctx.Estado[baseKey + "warnings"] = warnings;

            ctx.Log(
                "[NotaCreditoElectronicaArExtractor] OK" +
                " letra=" + SafeLog(letra) +
                " codigoAfip=" + SafeLog(codigoAfip) +
                " numero=" + SafeLog(numero) +
                " fecha=" + SafeLog(fecha) +
                " asociado=" + SafeLog(asociado.Numero) +
                " cae=" + SafeLog(cae) +
                " items=" + items.Count +
                " total=" + (total.HasValue ? total.Value.ToString("0.00", CultureInfo.InvariantCulture) : "")
            );
        }

        private class ComprobanteAsociado
        {
            public string Tipo { get; set; }
            public string Numero { get; set; }
            public string Letra { get; set; }
            public string PuntoVenta { get; set; }
            public string NumeroComprobante { get; set; }
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

        private static string CleanupValueBeforeMarkers(string value, params string[] markers)
        {
            value = CleanupValue(value);
            if (string.IsNullOrWhiteSpace(value) || markers == null) return value;

            int cut = -1;
            foreach (var marker in markers)
            {
                if (string.IsNullOrWhiteSpace(marker)) continue;
                int idx = IndexOfIgnoreCase(value, marker);
                if (idx >= 0 && (cut < 0 || idx < cut)) cut = idx;
            }

            if (cut >= 0)
                value = CleanupValue(value.Substring(0, cut));

            return value;
        }

        private static string ExtractDescripcionComprobante(string text)
        {
            string value = ExtractFirst(text,
                @"\b(NOTA\s+DE\s+CR[ÉE]DITO\s+ELECTR[ÓO]NICA\s+MiPyMEs\s*\(FCE\))\b",
                @"\b(NOTA\s+DE\s+CR[ÉE]DITO\s+ELECTR[ÓO]NICA)\b");

            return value;
        }

        private static string ExtractLetra(string text)
        {
            string letra = ExtractFirst(text,
                @"\b([ABCEMT])\s+C[ÓO]D\.?\s*\d{3}\b",
                @"\b([ABCEMT])\s+NOTA\s+DE\s+CR[ÉE]DITO\b",
                @"^\s*([ABCEMT])\s*$");

            if (!string.IsNullOrWhiteSpace(letra))
            {
                letra = letra.Trim().ToUpperInvariant();
                if (letra == "A" || letra == "B" || letra == "C" || letra == "E" || letra == "M" || letra == "T")
                    return letra;
            }

            var lines = GetLines(text);
            for (int i = 0; i < lines.Count; i++)
            {
                if (Regex.IsMatch(lines[i], @"^[ABCEMT]$", RegexOptions.IgnoreCase))
                    return lines[i].Trim().ToUpperInvariant();
            }

            return null;
        }

        private static string ExtractCodigoAfip(string text)
        {
            return ExtractFirst(text,
                @"\bC[ÓO]D\.?\s*(\d{3})\b",
                @"\bC[oó]digo\s+AFIP\s*:\s*(\d{3})\b");
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

        private static ComprobanteAsociado ExtractComprobanteAsociado(string text)
        {
            var result = new ComprobanteAsociado();
            if (string.IsNullOrWhiteSpace(text)) return result;

            var patterns = new[]
            {
                @"\b((?:Factura|FACTURA)\s+de\s+Cr[eé]dito\s*\(FCE\)\s*([ABCEMT]))\s*:\s*(\d{1,5})\s*-\s*(\d{1,8})",
                @"\b((?:Factura|FACTURA)\s+([ABCEMT]))\s*:\s*(\d{1,5})\s*-\s*(\d{1,8})"
            };

            foreach (var pattern in patterns)
            {
                var m = Regex.Match(text, pattern, RegexOptions.IgnoreCase | RegexOptions.Multiline | RegexOptions.Singleline);
                if (!m.Success) continue;

                result.Tipo = CleanupValue(m.Groups[1].Value);
                result.Letra = CleanupValue(m.Groups[2].Value);
                result.PuntoVenta = m.Groups[3].Value.PadLeft(5, '0');
                result.NumeroComprobante = m.Groups[4].Value.PadLeft(8, '0');
                result.Numero = BuildNumeroComprobante(result.PuntoVenta, result.NumeroComprobante);
                return result;
            }

            return result;
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

        private static string ExtractEmisorDireccion(string text)
        {
            var lines = GetLines(text);

            for (int i = 0; i < lines.Count; i++)
            {
                var m = Regex.Match(lines[i], @"^Domicilio\s+Comercial\s*:\s*(.*)$", RegexOptions.IgnoreCase);
                if (!m.Success) continue;

                var parts = new List<string>();
                string first = CleanupValueBeforeMarkers(
                    m.Groups[1].Value,
                    "CUIT:", "Ingresos Brutos:", "Fecha de Inicio de Actividades:", "Condición frente al IVA:");
                if (!string.IsNullOrWhiteSpace(first))
                    parts.Add(first);

                for (int j = i + 1; j < lines.Count && j <= i + 3; j++)
                {
                    if (Regex.IsMatch(lines[j], @"^(Ingresos\s+Brutos|Fecha\s+de\s+Inicio\s+de\s+Actividades|Condición\s+frente\s+al\s+IVA)\b", RegexOptions.IgnoreCase))
                        continue;

                    if (Regex.IsMatch(lines[j], @"^(Apellido\s+y\s+Nombre\s*/\s*Raz[oó]n\s+Social|CUIT\s*:|Condición\s+de\s+venta|Per[ií]odo\s+Facturado)\b", RegexOptions.IgnoreCase))
                        break;

                    if (!IsMetadataLine(lines[j]))
                    {
                        string part = CleanupValueBeforeMarkers(
                            lines[j],
                            "CUIT:", "Ingresos Brutos:", "Fecha de Inicio de Actividades:", "Condición frente al IVA:");
                        if (!string.IsNullOrWhiteSpace(part))
                            parts.Add(part);
                    }
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

        private static string ExtractReceptorNombre(string text)
        {
            var lines = GetLines(text);
            for (int i = 0; i < lines.Count; i++)
            {
                int idx = IndexOfIgnoreCase(lines[i], "Apellido y Nombre / Razón Social:");
                if (idx < 0) idx = IndexOfIgnoreCase(lines[i], "Cliente:");
                if (idx < 0) idx = IndexOfIgnoreCase(lines[i], "Señor:");
                if (idx < 0) idx = IndexOfIgnoreCase(lines[i], "Señores:");
                if (idx < 0) continue;

                string label;
                if (IndexOfIgnoreCase(lines[i], "Apellido y Nombre / Razón Social:") >= 0)
                    label = "Apellido y Nombre / Razón Social:";
                else if (IndexOfIgnoreCase(lines[i], "Cliente:") >= 0)
                    label = "Cliente:";
                else if (IndexOfIgnoreCase(lines[i], "Señores:") >= 0)
                    label = "Señores:";
                else
                    label = "Señor:";

                int idxLabel = IndexOfIgnoreCase(lines[i], label);
                var parts = new List<string>();
                string first = CleanupValueBeforeMarkers(
                    lines[i].Substring(idxLabel + label.Length),
                    "Domicilio Comercial:", "Domicilio:", "Condición frente al IVA:", "Condición de venta:", "Factura de Crédito");

                if (!string.IsNullOrWhiteSpace(first))
                    parts.Add(first);

                for (int j = i + 1; j < lines.Count && j <= i + 3; j++)
                {
                    if (Regex.IsMatch(lines[j], @"^(Condición\s+frente\s+al\s+IVA|Domicilio\s+Comercial|Domicilio\s*:|Condición\s+de\s+venta|Factura\s+de\s+Cr[eé]dito|C[oó]digo\s+Producto)", RegexOptions.IgnoreCase))
                        break;

                    if (!IsMetadataLine(lines[j]))
                        parts.Add(lines[j]);
                }

                if (parts.Count > 0)
                    return CleanupValue(string.Join(" ", parts.ToArray()));
            }

            return ExtractFirst(text,
                @"\bApellido\s+y\s+Nombre\s*/\s*Raz[oó]n\s+Social\s*:\s*([^\n]+)",
                @"\bCliente\s*:\s*([^\n]+)",
                @"\bSeñor(?:es)?\s*:\s*([^\n]+)");
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
                    int idxDom = IndexOfIgnoreCase(value, "Domicilio");
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

        private static string ExtractReceptorDireccion(string text)
        {
            var lines = GetLines(text);

            for (int i = 0; i < lines.Count; i++)
            {
                int idx = IndexOfIgnoreCase(lines[i], "Domicilio Comercial:");
                if (idx < 0) idx = IndexOfIgnoreCase(lines[i], "Domicilio:");
                if (idx < 0) continue;

                bool esPrimerDomicilio = false;
                for (int j = Math.Max(0, i - 4); j <= i; j++)
                {
                    if (IndexOfIgnoreCase(lines[j], "Razón Social:") >= 0 && IndexOfIgnoreCase(lines[j], "EDI SA") >= 0)
                    {
                        esPrimerDomicilio = true;
                        break;
                    }
                }
                if (esPrimerDomicilio) continue;

                string label = IndexOfIgnoreCase(lines[i], "Domicilio Comercial:") >= 0 ? "Domicilio Comercial:" : "Domicilio:";
                int idxLabel = IndexOfIgnoreCase(lines[i], label);
                string tail = CleanupValue(lines[i].Substring(idxLabel + label.Length));
                var parts = new List<string>();

                if (!string.IsNullOrWhiteSpace(tail))
                    parts.Add(tail);

                for (int j = i + 1; j < lines.Count && j <= i + 2; j++)
                {
                    if (Regex.IsMatch(lines[j], @"^(Condición\s+de\s+venta|Factura\s+de\s+Cr[eé]dito|Precio\s+Unit\.|Código\s+Producto\s*/\s*Servicio|CAE\b|P[áa]g\.)", RegexOptions.IgnoreCase))
                        break;

                    if (!IsMetadataLine(lines[j]))
                        parts.Add(lines[j]);
                }

                if (parts.Count > 0)
                    return CleanupValue(string.Join(" ", parts.ToArray()));
            }

            return null;
        }

        private static bool IsMetadataLine(string line)
        {
            if (string.IsNullOrWhiteSpace(line)) return true;
            return Regex.IsMatch(line,
                @"^(CUIT\s*:|Ingresos\s+Brutos\s*:|Fecha\s+de\s+Inicio\s+de\s+Actividades\s*:|Punto\s+de\s+Venta\s*:|Comp\.?\s*Nro\.?\s*:|Fecha\s+de\s+Emisi[oó]n\s*:|C[ÓO]D\.|A$|B$|C$|ORIGINAL$|DUPLICADO$|TRIPLICADO$)",
                RegexOptions.IgnoreCase);
        }

        private static int IndexOfIgnoreCase(string text, string value)
        {
            if (text == null || value == null) return -1;
            return text.IndexOf(value, StringComparison.OrdinalIgnoreCase);
        }

        private static List<string> GetLines(string text)
        {
            var result = new List<string>();
            if (string.IsNullOrEmpty(text)) return result;

            string normalized = text.Replace("\r\n", "\n").Replace("\r", "\n");
            var arr = normalized.Split('\n');
            foreach (var raw in arr)
            {
                var line = CleanupValue(raw);
                if (!string.IsNullOrWhiteSpace(line)) result.Add(line);
            }
            return result;
        }

        private static string ExtractDateAfterLabel(string text, string label)
        {
            if (string.IsNullOrWhiteSpace(text) || string.IsNullOrWhiteSpace(label)) return null;

            var m = Regex.Match(text,
                Regex.Escape(label) + @"\s*:?\s*([0-3]?\d/[01]?\d/\d{4})",
                RegexOptions.IgnoreCase | RegexOptions.Multiline | RegexOptions.Singleline);
            if (m.Success) return CleanupValue(m.Groups[1].Value);

            var lines = GetLines(text);
            for (int i = 0; i < lines.Count; i++)
            {
                if (IndexOfIgnoreCase(lines[i], label) < 0) continue;

                for (int j = i; j <= i + 2 && j < lines.Count; j++)
                {
                    var md = Regex.Match(lines[j], @"\b([0-3]?\d/[01]?\d/\d{4})\b");
                    if (md.Success) return md.Groups[1].Value;
                }
            }

            return null;
        }

        private static string[] ExtractPeriodoFacturado(string text)
        {
            string desde = null;
            string hasta = null;
            string vtoPago = null;

            var m = Regex.Match(text,
                @"Per[ií]odo\s+Facturado\s+Desde\s*:?\s*([0-3]?\d/[01]?\d/\d{4})\s+Hasta\s*:?\s*([0-3]?\d/[01]?\d/\d{4})",
                RegexOptions.IgnoreCase | RegexOptions.Multiline | RegexOptions.Singleline);
            if (m.Success)
            {
                desde = m.Groups[1].Value;
                hasta = m.Groups[2].Value;
            }

            vtoPago = ExtractDateAfterLabel(text, "Fecha de Vto. para el pago");

            return new[] { desde, hasta, vtoPago };
        }

        private static string ExtractValueAfterLabelOrNextLine(string text, string label)
        {
            if (string.IsNullOrWhiteSpace(text) || string.IsNullOrWhiteSpace(label)) return null;

            var lines = GetLines(text);
            for (int i = 0; i < lines.Count; i++)
            {
                var m = Regex.Match(lines[i], @"^" + Regex.Escape(label) + @"\s*:\s*(.*)$", RegexOptions.IgnoreCase);
                if (!m.Success) continue;

                string same = CleanupValue(m.Groups[1].Value);
                if (!string.IsNullOrWhiteSpace(same)) return same;

                for (int j = i + 1; j < lines.Count && j <= i + 3; j++)
                {
                    if (IsMetadataLine(lines[j])) continue;
                    return lines[j];
                }
            }

            var mx = Regex.Match(text,
                Regex.Escape(label) + @"\s*:\s*([^\n]+)",
                RegexOptions.IgnoreCase | RegexOptions.Multiline);
            if (mx.Success) return CleanupValue(mx.Groups[1].Value);

            return null;
        }

        private static decimal? ExtractMoneyAfterLabel(string text, params string[] labels)
        {
            if (string.IsNullOrWhiteSpace(text) || labels == null) return null;

            foreach (var label in labels)
            {
                if (string.IsNullOrWhiteSpace(label)) continue;

                var m = Regex.Match(text,
                    Regex.Escape(label) + @"\s*:?\s*\$?\s*([0-9]{1,3}(?:\.[0-9]{3})*,[0-9]{2}|[0-9]+,[0-9]{2})",
                    RegexOptions.IgnoreCase | RegexOptions.Multiline | RegexOptions.Singleline);
                if (m.Success)
                {
                    var val = ParseMoney(m.Groups[1].Value);
                    if (val.HasValue) return val;
                }
            }

            return null;
        }

        private static decimal? ParseMoney(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw)) return null;
            string s = raw.Trim().Replace("$", "").Replace(" ", "");
            s = s.Replace(".", "").Replace(",", ".");

            decimal value;
            if (decimal.TryParse(s, NumberStyles.Number, CultureInfo.InvariantCulture, out value))
                return value;

            return null;
        }

        private static decimal? ParseDecimalLoose(string raw)
        {
            return ParseMoney(raw);
        }

        private static List<Dictionary<string, object>> ExtractItems(string text, List<string> warnings)
        {
            var items = new List<Dictionary<string, object>>();
            var lines = GetLines(text);
            if (lines.Count == 0) return items;

            int idxHeader = -1;
            for (int i = 0; i < lines.Count; i++)
            {
                if (Regex.IsMatch(lines[i], @"C[oó]digo\s+Producto\s*/\s*Servicio", RegexOptions.IgnoreCase) ||
                    Regex.IsMatch(lines[i], @"Producto\s*/\s*Servicio", RegexOptions.IgnoreCase))
                {
                    idxHeader = i;
                    break;
                }
            }

            int startSearch = idxHeader >= 0 ? idxHeader + 1 : 0;

            string unidadPattern = @"unidades?|unidad|servicios?|servicio|hrs?|horas?";
            string moneyPattern = @"\d+(?:\.\d{3})*,\d{2}";
            string decimalPattern = @"\d+(?:[.,]\d+)?";

            var rxSameLine = new Regex(
                @"^(?<desc>.*?)\s*(?<cant>" + decimalPattern + @")\s+(?<unidad>" + unidadPattern + @")\s+(?<pu>" + moneyPattern + @")\s+(?<bonif>" + decimalPattern + @")\s+(?<sub>" + moneyPattern + @")\s+(?<aliq>" + decimalPattern + @"%)\s+(?<subiva>" + moneyPattern + @")$",
                RegexOptions.IgnoreCase | RegexOptions.Multiline | RegexOptions.Singleline);

            var rxSplitUnit = new Regex(
                @"^(?<desc>.*?)\s*(?<cant>" + decimalPattern + @")\s+(?<pu>" + moneyPattern + @")\s+(?<bonif>" + decimalPattern + @")\s+(?<sub>" + moneyPattern + @")\s+(?<aliq>" + decimalPattern + @"%)\s+(?<subiva>" + moneyPattern + @")$",
                RegexOptions.IgnoreCase | RegexOptions.Multiline | RegexOptions.Singleline);

            int descStart = startSearch;

            for (int i = startSearch; i < lines.Count; i++)
            {
                if (IsItemStopLine(lines[i])) break;

                Match m = rxSameLine.Match(lines[i]);
                bool unidadEnMismaLinea = m.Success;

                if (!m.Success)
                    m = rxSplitUnit.Match(lines[i]);

                if (!m.Success)
                    continue;

                string unidad = unidadEnMismaLinea ? CleanupValue(m.Groups["unidad"].Value) : FindUnidadAround(lines, descStart, i);
                if (string.IsNullOrWhiteSpace(unidad))
                    unidad = "";

                var descParts = new List<string>();
                string descInline = CleanupValue(m.Groups["desc"].Value);
                if (!string.IsNullOrWhiteSpace(descInline))
                    descParts.Add(descInline);

                for (int j = descStart; j < i; j++)
                {
                    if (IsUnitOnlyLine(lines[j])) continue;
                    if (IsItemDescriptionLine(lines[j]))
                        descParts.Add(lines[j]);
                }

                int lastDescriptionLine = i;
                for (int j = i + 1; j < lines.Count; j++)
                {
                    if (IsItemStopLine(lines[j])) break;
                    if (IsPotentialItemAmountLine(lines[j], rxSameLine, rxSplitUnit)) break;
                    if (IsUnitOnlyLine(lines[j])) continue;
                    if (!IsItemDescriptionLine(lines[j])) break;

                    descParts.Add(lines[j]);
                    lastDescriptionLine = j;
                }

                string desc = CleanupValue(string.Join(" ", descParts.ToArray()));

                var item = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
                if (!string.IsNullOrWhiteSpace(desc)) item["descripcion"] = desc;

                decimal? cantidad = ParseDecimalLoose(m.Groups["cant"].Value);
                decimal? precioUnitario = ParseMoney(m.Groups["pu"].Value);
                decimal? bonifPct = ParseDecimalLoose(m.Groups["bonif"].Value);
                decimal? subtotal = ParseMoney(m.Groups["sub"].Value);
                decimal? subtotalConIva = ParseMoney(m.Groups["subiva"].Value);

                if (cantidad.HasValue) item["cantidad"] = cantidad.Value;
                else item["cantidadTexto"] = m.Groups["cant"].Value;

                if (!string.IsNullOrWhiteSpace(unidad))
                    item["unidadMedida"] = unidad;

                if (precioUnitario.HasValue) item["precioUnitario"] = precioUnitario.Value;
                else item["precioUnitarioTexto"] = m.Groups["pu"].Value;

                if (bonifPct.HasValue) item["bonificacionPorcentaje"] = bonifPct.Value;
                else item["bonificacionPorcentajeTexto"] = m.Groups["bonif"].Value;

                if (subtotal.HasValue) item["subtotal"] = subtotal.Value;
                else item["subtotalTexto"] = m.Groups["sub"].Value;

                item["alicuotaIva"] = CleanupValue(m.Groups["aliq"].Value);

                if (subtotalConIva.HasValue) item["subtotalConIva"] = subtotalConIva.Value;
                else item["subtotalConIvaTexto"] = m.Groups["subiva"].Value;

                items.Add(item);

                descStart = Math.Max(i + 1, lastDescriptionLine + 1);
                i = lastDescriptionLine;
            }

            return items;
        }

        private static bool IsPotentialItemAmountLine(string line, Regex rxSameLine, Regex rxSplitUnit)
        {
            if (string.IsNullOrWhiteSpace(line)) return false;
            if (rxSameLine != null && rxSameLine.IsMatch(line)) return true;
            if (rxSplitUnit != null && rxSplitUnit.IsMatch(line)) return true;
            return false;
        }

        private static string FindUnidadAround(List<string> lines, int start, int itemLineIndex)
        {
            if (lines == null || itemLineIndex < 0) return null;

            for (int i = itemLineIndex - 1; i >= start && i >= itemLineIndex - 5; i--)
            {
                if (IsUnitOnlyLine(lines[i]))
                    return CleanupValue(lines[i]).ToLowerInvariant();
            }

            for (int i = itemLineIndex + 1; i < lines.Count && i <= itemLineIndex + 2; i++)
            {
                if (IsUnitOnlyLine(lines[i]))
                    return CleanupValue(lines[i]).ToLowerInvariant();
            }

            return null;
        }

        private static bool IsUnitOnlyLine(string line)
        {
            if (string.IsNullOrWhiteSpace(line)) return false;
            return Regex.IsMatch(line.Trim(), @"^(unidades?|unidad|servicios?|servicio|hrs?|horas?)$", RegexOptions.IgnoreCase);
        }

        private static bool IsItemStopLine(string line)
        {
            if (string.IsNullOrWhiteSpace(line)) return true;

            return Regex.IsMatch(line,
                @"^(Otros\s+Tributos|Importe\s+Neto\s+Gravado|IVA\s+27|IVA\s+21|IVA\s+10[\.,]5|IVA\s+5|IVA\s+2[\.,]5|IVA\s+0|Importe\s+Total|SON\s+\$|P[áa]g\.|Comprobante\s+Autorizado|CAE\b|Fecha\s+de\s+Vto\.\s+de\s+CAE)",
                RegexOptions.IgnoreCase);
        }

        private static bool IsItemDescriptionLine(string line)
        {
            if (string.IsNullOrWhiteSpace(line)) return false;

            if (IsUnitOnlyLine(line)) return false;
            if (IsItemStopLine(line)) return false;

            if (Regex.IsMatch(line, @"^(Factura\s+de\s+Cr[eé]dito|Per[ií]odo\s+Facturado|C[oó]digo|Producto\s*/\s*Servicio|Cantidad\b|U\.\s*medida|Precio\s+Unit\.|%\s*Bonif|Subtotal\b|Alicuota|IVA\b|Descripci[oó]n\b|Detalle\b|Importe\b|Per\./Ret\.|Impuestos\b)", RegexOptions.IgnoreCase))
                return false;

            if (Regex.IsMatch(line, @"^(\d+(?:[.,]\d+)?\s+(unidades?|unidad|servicios?|servicio|hrs?|horas?)\s+)", RegexOptions.IgnoreCase))
                return false;

            // En algunos PDFs AFIP el año final de la descripción puede quedar en una línea separada.
            if (Regex.IsMatch(line.Trim(), @"^(19|20)\d{2}$"))
                return true;

            if (Regex.IsMatch(line, @"^\$?\s*[0-9\.\,]+$"))
                return false;

            return true;
        }

        private static string SafeLog(string s)
        {
            if (string.IsNullOrWhiteSpace(s)) return "";
            s = s.Replace("\r", " ").Replace("\n", " ").Trim();
            return s;
        }
    }
}