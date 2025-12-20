using System;
using System.Globalization;

public static class WfNumbers
{
    // Convierte "154.000,00" -> 154000.00m
    public static bool TryParseDecimalAR(object value, out decimal result)
    {
        result = 0m;
        if (value == null) return false;

        if (value is decimal dm) { result = dm; return true; }
        if (value is int i) { result = i; return true; }
        if (value is long l) { result = l; return true; }
        if (value is double dbl) { result = (decimal)dbl; return true; }

        var s = Convert.ToString(value);
        if (string.IsNullOrWhiteSpace(s)) return false;

        s = s.Trim();

        // limpiar símbolos típicos
        s = s.Replace("$", "").Replace("ARS", "").Trim();

        // Normalización robusta: miles '.' y decimal ','
        // 154.000,00 -> 154000.00
        if (s.Contains(","))
        {
            s = s.Replace(".", "");
            s = s.Replace(",", ".");
            return decimal.TryParse(s, NumberStyles.Number, CultureInfo.InvariantCulture, out result);
        }

        // fallback: intentar parse directo con es-AR e invariant
        var ar = new CultureInfo("es-AR");
        if (decimal.TryParse(s, NumberStyles.Number, ar, out result)) return true;
        if (decimal.TryParse(s, NumberStyles.Number, CultureInfo.InvariantCulture, out result)) return true;

        return false;
    }
}
