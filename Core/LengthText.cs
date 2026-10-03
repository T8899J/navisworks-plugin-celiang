using System;
using System.Globalization;
using System.Text.RegularExpressions;

namespace JiePinPai.TrayMeasurement.Core
{
    public static class LengthText
    {
        public static double? ParseMetres(string text)
        {
            // Bare numbers and ambiguous separators are intentionally not guessed.
            Match match = Regex.Match(text ?? "", @"^\s*([+]?(?:\d+(?:\.\d*)?|\.\d+))\s*(mm|cm|m|ft|in|毫米|厘米|米|英尺|英寸)\s*$", RegexOptions.IgnoreCase);
            double value;
            if (!match.Success || !double.TryParse(match.Groups[1].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out value) || !Vec.IsFinite(value) || value <= 0) return null;
            string unit = match.Groups[2].Value.ToLowerInvariant();
            double scale = unit == "mm" || unit == "毫米" ? .001 : unit == "cm" || unit == "厘米" ? .01 : unit == "ft" || unit == "英尺" ? .3048 : unit == "in" || unit == "英寸" ? .0254 : 1;
            return value * scale;
        }
    }
}
