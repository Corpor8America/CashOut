using UglyToad.PdfPig;
using UglyToad.PdfPig.Content;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

public partial class PdfImportService
{
    public string ExtractCsv(byte[] pdfBytes)
    {
        using var pdf = PdfDocument.Open(pdfBytes);
        var sb = new StringBuilder();

        var allText = new StringBuilder();
        foreach (var page in pdf.GetPages())
            allText.AppendLine(page.Text);

        var text = allText.ToString();
        var year = DetectYear(text);
        var lines = text.Split('\n', StringSplitOptions.RemoveEmptyEntries);

        sb.AppendLine("Date,Description,Amount");

        foreach (var line in lines)
        {
            var trimmed = line.Trim();
            if (string.IsNullOrWhiteSpace(trimmed)) continue;

            if (TryParseTransaction(trimmed, year, out var date, out var description, out var amount))
                sb.AppendLine(CsvEscape(date) + "," + CsvEscape(description) + "," + amount.ToString("F2", CultureInfo.InvariantCulture));
        }

        return sb.ToString();
    }

    private static int DetectYear(string text)
    {
        var yearMatch = YearPattern().Match(text);
        if (yearMatch.Success)
        {
            var y = int.Parse(yearMatch.Groups[1].Value);
            if (y >= 2020 && y <= 2030) return y;
        }
        return DateTime.UtcNow.Year;
    }

    private static bool TryParseTransaction(string line, int year, out string date, out string description, out decimal amount)
    {
        date = "";
        description = "";
        amount = 0;

        var trimmed = line.Trim();
        if (trimmed.Length < 10) return false;

        var amtMatch = AmountEndPattern().Match(trimmed);
        if (!amtMatch.Success) return false;

        var amtStr = amtMatch.Groups[1].Value;
        var negative = amtMatch.Groups[2].Success;
        if (!decimal.TryParse(amtStr.Replace(",", ""), NumberStyles.Any, CultureInfo.InvariantCulture, out var rawAmt))
            return false;
        amount = negative ? -rawAmt : rawAmt;

        var dateDescPortion = trimmed[..amtMatch.Index].TrimEnd();
        if (dateDescPortion.Length == 0) return false;

        var dateMatch = DateStartPattern().Match(dateDescPortion);
        if (!dateMatch.Success) return false;

        var rawDate = dateMatch.Groups[1].Value;
        description = dateDescPortion[dateMatch.Length..].Trim();
        if (string.IsNullOrWhiteSpace(description)) return false;

        if (DateOnly.TryParse(rawDate, out var d))
        {
            date = d.ToString("yyyy-MM-dd");
            return true;
        }

        if (MDSlashPattern().Match(rawDate).Success)
        {
            var parts = rawDate.Split('/');
            if (int.TryParse(parts[0], out var m) && int.TryParse(parts[1], out var day) && m >= 1 && m <= 12 && day >= 1 && day <= 31)
            {
                date = new DateOnly(year, m, day).ToString("yyyy-MM-dd");
                return true;
            }
        }

        return false;
    }

    private static string CsvEscape(string s)
    {
        if (s.Contains(',') || s.Contains('"') || s.Contains('\n') || s.Contains('\r'))
            return "\"" + s.Replace("\"", "\"\"") + "\"";
        return s;
    }

    [GeneratedRegex(@"Statement Period|Account Summary|(?:20|30)\d{2}|\b(?:202[0-9]|2030)\b")]
    private static partial Regex YearPattern();

    [GeneratedRegex(@"\s+(-?\$?[\d,]+\.\d{2})\s*$")]
    private static partial Regex AmountEndPattern();

    [GeneratedRegex(@"^\s*(-?\$[\d,]+\.\d{2}|\(\$[\d,]+\.\d{2}\)|[\d,]+\.[\d]{2}-)\s*$")]
    private static partial Regex AmountOnlyPattern();

    [GeneratedRegex(@"^\s*(\d{1,2}/\d{1,2}(?:/\d{4})?|[A-Z][a-z]{2}\s+\d{1,2},?\s+\d{4}|\d{4}-\d{2}-\d{2}|\d{1,2}\s+[A-Z][a-z]{2}\s+\d{4})")]
    private static partial Regex DateStartPattern();

    [GeneratedRegex(@"^\d{1,2}/\d{1,2}(?:/\d{4})?$")]
    private static partial Regex MDSlashPattern();
}
