using UglyToad.PdfPig;
using UglyToad.PdfPig.Content;
using UglyToad.PdfPig.Core;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

public partial class PdfImportService
{
    private sealed record ParsedTxn(int Month, int Day, DateOnly? ExactDate, string Desc, decimal Amt);

    public string ExtractCsv(byte[] pdfBytes)
    {
        var debug = ExtractCsvDebug(pdfPages: null, pdfBytes);
        return debug.Csv;
    }

    public (string RawText, string Csv, string[] SkippedLines) ExtractCsvDebug(
        int[]? pdfPages,
        byte[] pdfBytes,
        decimal? dateColumnEnd = null,
        decimal? amountColumnStart = null,
        bool joinContinuations = true,
        string? rowRegex = null)
    {
        using var pdf = PdfDocument.Open(pdfBytes);

        var allText = new StringBuilder();
        var pageNumbers = (pdfPages ?? Enumerable.Range(1, pdf.NumberOfPages).ToArray())
            .Where(n => n >= 1 && n <= pdf.NumberOfPages)
            .ToArray();

        foreach (var pageNum in pageNumbers)
            allText.AppendLine(pdf.GetPage(pageNum).Text);

        var year = DetectYear(allText.ToString());

        if (!string.IsNullOrWhiteSpace(rowRegex))
            return ExtractWithCustomRegex(pageNumbers, pdf, year, rowRegex);

        if (dateColumnEnd.HasValue && amountColumnStart.HasValue)
            return ExtractPositional(pdf, pageNumbers, year,
                dateColumnEnd.Value, amountColumnStart.Value, joinContinuations);

        return ExtractFlattened(pageNumbers, pdf, year);
    }

    // ── Custom user-supplied regex mode ──────────────────────────────────
    // Each capture group becomes a column in the extracted CSV; the names are
    // whatever the user chose and get mapped (or ignored) via column mapping.

    private static (string RawText, string Csv, string[] SkippedLines) ExtractWithCustomRegex(
        int[] pageNumbers, PdfDocument pdf, int baseYear, string pattern)
    {
        Regex rx;
        try
        {
            rx = new Regex(pattern, RegexOptions.Compiled, TimeSpan.FromSeconds(2));
        }
        catch (ArgumentException ex)
        {
            throw new ArgumentException("Invalid row regex: " + ex.Message, nameof(pattern));
        }

        var groupNames = rx.GetGroupNames()
            .Where(n => n != "0") // group 0 is the whole match, not a capture group
            .OrderBy(n => int.TryParse(n, out var i) ? i : int.MaxValue)
            .ToArray();
        if (groupNames.Length == 0)
            throw new ArgumentException(
                "Row regex must contain at least one capture group. Example: " +
                "^(?<Date>\\d{1,2}/\\d{2})\\s?(?<TransId>\\S{17})\\s?(?<Description>.+?)\\s?(?<Amount>-?\\$[\\d,.]+)$");

        var rawLines = new StringBuilder();
        var skipped = new List<string>();
        var rows = new List<string[]>();
        var dates = new List<(int CellIdx, DateOnly? Exact, int Month, int Day)?>();

        foreach (var pageNum in pageNumbers)
        {
            foreach (var rowWords in GroupIntoRows(pdf.GetPage(pageNum).GetWords()))
            {
                rowWords.Sort((a, b) => WordCentreX(a).CompareTo(WordCentreX(b)));
                var lineText = string.Join(" ", rowWords.Select(w => w.Text)).Trim();
                if (lineText.Length == 0) continue;
                rawLines.AppendLine(lineText);

                var m = rx.Match(lineText);
                if (!m.Success)
                {
                    skipped.Add(lineText);
                    continue;
                }

                var cells = new string[groupNames.Length];
                (int, DateOnly?, int, int)? di = null;
                for (var gi = 0; gi < groupNames.Length; gi++)
                {
                    var val = m.Groups[groupNames[gi]].Value.Trim();
                    if (di == null && TryExtractDate(val, out var ex, out var mo, out var dy))
                        di = (gi, ex, mo, dy);
                    cells[gi] = NormalizeAmountCell(val);
                }

                rows.Add(cells);
                dates.Add(di);
            }
        }

        ResolveStatementYears(rows, dates, baseYear);

        var sb = new StringBuilder();
        sb.AppendLine(string.Join(",", groupNames.Select(CsvEscape)));
        foreach (var cells in rows)
            sb.AppendLine(string.Join(",", cells.Select(CsvEscape)));

        return (rawLines.ToString(), sb.ToString(), skipped.ToArray());
    }

    // Cells that look like currency ($, parentheses or detached minus) are
    // rewritten as plain signed numbers so the import amount parser accepts
    // them. Plain numbers/dates/text pass through untouched.
    private static string NormalizeAmountCell(string value)
    {
        var t = value.Trim();
        var currencyish = t.Contains('$') || t.Contains('(') || t.Contains(')')
            || t.StartsWith("-") || t.EndsWith("-");
        return currencyish && TryParseCapturedAmount(t, out var amt)
            ? amt.ToString("F2", CultureInfo.InvariantCulture)
            : t;
    }

    // Year-less MM/DD dates: if the rows mix December and January it is a
    // year-end statement — January gets the newer calendar year, December the
    // year before. Otherwise every row anchors to the detected/base year.
    private static void ResolveStatementYears(
        List<string[]> rows,
        List<(int CellIdx, DateOnly? Exact, int Month, int Day)?> dates,
        int baseYear)
    {
        var yearlessMonths = dates.Where(d => d != null).Select(d => d.Value.Month).ToHashSet();
        int? janYear = null;
        if (yearlessMonths.Contains(12) && yearlessMonths.Contains(1))
            janYear = Math.Max(baseYear, DateTime.UtcNow.Year);

        for (var i = 0; i < rows.Count; i++)
        {
            var di = dates[i];
            if (di == null) continue;

            DateOnly date;
            if (di.Value.Exact.HasValue)
                date = di.Value.Exact.Value;
            else if (janYear.HasValue)
                date = new DateOnly(di.Value.Month == 1 ? janYear.Value : janYear.Value - 1, di.Value.Month, di.Value.Day);
            else
                date = new DateOnly(baseYear, di.Value.Month, di.Value.Day);

            rows[i][di.Value.CellIdx] = date.ToString("yyyy-MM-dd");
        }
    }

    private static bool TryParseCapturedAmount(string raw, out decimal value)
    {
        value = 0;
        var t = raw.Trim();
        if (t.Length == 0) return false;

        var neg = false;
        if (t.StartsWith("(") && t.EndsWith(")")) { neg = true; t = t[1..^1].Trim(); }
        else if (t.StartsWith("-")) { neg = true; t = t[1..].Trim(); }
        else if (t.EndsWith("-")) { neg = true; t = t[..^1].Trim(); }
        else if (t.StartsWith("+")) { t = t[1..].Trim(); }

        var digits = new string(t.Where(c => char.IsDigit(c) || c == '.' || c == ',').ToArray())
            .Replace(",", "");
        if (digits.Length == 0 ||
            !decimal.TryParse(digits, NumberStyles.Float, CultureInfo.InvariantCulture, out var v))
            return false;

        value = neg ? -v : v;
        return true;
    }

    private (string RawText, string Csv, string[] SkippedLines) ExtractPositional(
        PdfDocument pdf,
        int[] pageNumbers,
        int year,
        decimal dateColumnEnd,
        decimal amountColumnStart,
        bool joinContinuations)
    {
        var rawLines = new StringBuilder();
        var skipped = new List<string>();
        var txns = new List<ParsedTxn>();

        foreach (var pageNum in pageNumbers)
        {
            var page = pdf.GetPage(pageNum);
            var dateEndX = page.Width * (double)dateColumnEnd;
            var amtStartX = page.Width * (double)amountColumnStart;

            foreach (var row in GroupIntoRows(page.GetWords()))
            {
                row.Sort((a, b) => WordCentreX(a).CompareTo(WordCentreX(b)));
                var lineText = string.Join(" ", row.Select(w => w.Text));
                rawLines.AppendLine(lineText);

                var dateWords = new List<string>();
                var descWords = new List<string>();
                var amtTokens = new List<string>();

                foreach (var w in row)
                {
                    var cx = WordCentreX(w);
                    if (cx < dateEndX) dateWords.Add(w.Text);
                    else if (cx >= amtStartX) amtTokens.Add(w.Text);
                    else descWords.Add(w.Text);
                }

                var hasDate = TryExtractDate(string.Join(" ", dateWords), out var exactDate, out var month, out var day);

                var hasAmt = false;
                decimal amt = 0;
                foreach (var tok in amtTokens)
                {
                    if (TryParseAmountToken(tok, out amt)) { hasAmt = true; break; }
                }

                var desc = string.Join(" ", descWords).Trim();

                if (hasDate && hasAmt && desc.Length > 0)
                {
                    txns.Add(new ParsedTxn(month, day, exactDate, CleanDescription(desc), amt));
                }
                else if (joinContinuations && !hasDate && !hasAmt && desc.Length > 0 && txns.Count > 0)
                {
                    var prev = txns[^1];
                    txns[^1] = prev with { Desc = prev.Desc + " " + desc };
                }
                else if (lineText.Length > 3)
                {
                    skipped.Add(lineText);
                }
            }
        }

        return (rawLines.ToString(), BuildCsv(txns, year), skipped.ToArray());
    }

    private static IEnumerable<List<Word>> GroupIntoRows(IEnumerable<Word> words)
    {
        var ordered = words.Where(w => !string.IsNullOrWhiteSpace(w.Text))
            .OrderByDescending(w => w.BoundingBox.Top)
            .ToList();

        var rows = new List<List<Word>>();
        foreach (var w in ordered)
        {
            List<Word>? target = null;
            foreach (var r in rows)
            {
                if (r.Any(x => VerticalOverlapRatio(x.BoundingBox, w.BoundingBox) > 0.35))
                {
                    target = r;
                    break;
                }
            }

            if (target == null) rows.Add(new List<Word> { w });
            else target.Add(w);
        }

        return rows;
    }

    private static double VerticalOverlapRatio(PdfRectangle a, PdfRectangle b)
    {
        var top = Math.Min(a.Top, b.Top);
        var bottom = Math.Max(a.Bottom, b.Bottom);
        if (top <= bottom) return 0;
        var minH = Math.Min(a.Height, b.Height);
        return minH <= 0 ? 0 : (top - bottom) / minH;
    }

    private (string RawText, string Csv, string[] SkippedLines) ExtractFlattened(
        int[] pageNumbers, PdfDocument pdf, int year)
    {
        var allText = new StringBuilder();
        foreach (var pageNum in pageNumbers)
            allText.AppendLine(pdf.GetPage(pageNum).Text);

        var text = PreProcessPdfText(allText.ToString());
        var lines = text.Split('\n', StringSplitOptions.RemoveEmptyEntries);

        var skipped = new List<string>();
        var txns = new List<ParsedTxn>();

        foreach (var line in lines)
        {
            var trimmed = line.Trim();
            if (string.IsNullOrWhiteSpace(trimmed)) continue;

            if (TryParseTransaction(trimmed, out var txn))
                txns.Add(txn);
            else if (trimmed.Length > 3)
                skipped.Add(trimmed);
        }

        return (text, BuildCsv(txns, year), skipped.ToArray());
    }

    private static double WordCentreX(Word w) => (w.BoundingBox.Left + w.BoundingBox.Right) / 2;

    private static bool TryParseAmountToken(string token, out decimal value)
    {
        value = 0;
        var t = token.Trim();
        if (t.Length < 4) return false;

        var neg = false;
        if (t.StartsWith("(") && t.EndsWith(")")) { neg = true; t = t[1..^1].Trim(); }
        if (t.EndsWith("-")) { neg = true; t = t[..^1].Trim(); }
        if (t.StartsWith("-")) { neg = true; t = t[1..].Trim(); }

        if (!AmountTokenPattern().IsMatch(t)) return false;

        t = t.Replace("$", "").Replace(",", "");
        if (!decimal.TryParse(t, NumberStyles.Any, CultureInfo.InvariantCulture, out var v))
            return false;

        value = neg ? -v : v;
        return true;
    }

    private static string PreProcessPdfText(string text)
    {
        // Insert \n before MM/DD dates when preceded by a digit (PdfPig concatenates fields without spaces)
        text = PdfDatePattern().Replace(text, "\n$0");
        // Insert \n after amounts when followed by a letter or digit
        text = PdfAmountSepPattern().Replace(text, "$1\n");
        return text;
    }

    private static int DetectYear(string text)
    {
        var yearMatch = YearPattern().Match(text);
        if (yearMatch.Success)
        {
            var raw = yearMatch.Groups[1].Success ? yearMatch.Groups[1].Value : yearMatch.Groups[2].Value;
            if (int.TryParse(raw, out var y) && y >= 2020 && y <= 2030) return y;
        }
        return DateTime.UtcNow.Year;
    }

    private static bool TryParseTransaction(string line, out ParsedTxn txn)
    {
        txn = null!;

        var trimmed = line.Trim();
        if (trimmed.Length < 5) return false;

        var amtMatch = AmountEndPattern().Match(trimmed);
        if (!amtMatch.Success) return false;

        var amtStr = amtMatch.Groups[1].Value;
        if (!decimal.TryParse(amtStr.Replace("$", "").Replace(",", ""), NumberStyles.Any, CultureInfo.InvariantCulture, out var amount))
            return false;

        var dateDescPortion = trimmed[..amtMatch.Index].TrimEnd();
        if (dateDescPortion.Length == 0) return false;

        if (!TryExtractDate(dateDescPortion, out var exactDate, out var month, out var day)) return false;

        var dateMatch = DateStartPattern().Match(dateDescPortion);
        var description = dateDescPortion[dateMatch.Length..].Trim();
        if (string.IsNullOrWhiteSpace(description)) return false;

        txn = new ParsedTxn(month, day, exactDate, CleanDescription(description), amount);
        return true;
    }

    private static bool TryExtractDate(string text, out DateOnly? exactDate, out int month, out int day)
    {
        exactDate = null;
        month = 0;
        day = 0;

        var dateMatch = DateStartPattern().Match(text);
        if (!dateMatch.Success) return false;

        var rawDate = dateMatch.Groups[1].Value;

        // M/D (and M/D/YYYY) first — DateOnly.TryParse would silently accept a
        // bare "12/21" as December 21 of the current year.
        var md = MDSlashPattern().Match(rawDate);
        if (md.Success)
        {
            var parts = rawDate.Split('/');
            if (!int.TryParse(parts[0], out var m) || !int.TryParse(parts[1], out var dd) ||
                m < 1 || m > 12 || dd < 1 || dd > 31)
                return false;

            month = m;
            day = dd;

            if (parts.Length == 3 && int.TryParse(parts[2], out var y) && y is >= 2000 and <= 2100)
                exactDate = new DateOnly(y, m, dd);

            return true;
        }

        if (DateOnly.TryParse(rawDate, out var parsed))
        {
            exactDate = parsed;
            month = parsed.Month;
            day = parsed.Day;
            return true;
        }

        return false;
    }

    private static string BuildCsv(List<ParsedTxn> txns, int baseYear)
    {
        // Year-end statements list both December and January without years.
        // January belongs to the newer calendar year, December to the year
        // before it — regardless of the order rows appear in.
        var yearlessMonths = txns.Where(t => !t.ExactDate.HasValue).Select(t => t.Month).ToHashSet();
        int? janYear = null;
        if (yearlessMonths.Contains(12) && yearlessMonths.Contains(1))
            janYear = Math.Max(baseYear, DateTime.UtcNow.Year);

        var resolved = new List<(DateOnly Date, string Desc, decimal Amt)>();

        foreach (var t in txns)
        {
            DateOnly date;
            if (t.ExactDate.HasValue)
                date = t.ExactDate.Value;
            else if (janYear.HasValue)
                date = new DateOnly(t.Month == 1 ? janYear.Value : janYear.Value - 1, t.Month, t.Day);
            else
                date = new DateOnly(baseYear, t.Month, t.Day);

            resolved.Add((date, t.Desc, t.Amt));
        }

        var sb = new StringBuilder();
        sb.AppendLine("Date,Description,Amount");

        foreach (var r in resolved)
            sb.AppendLine(CsvEscape(r.Date.ToString("yyyy-MM-dd")) + "," + CsvEscape(r.Desc) + "," + r.Amt.ToString("F2", CultureInfo.InvariantCulture));

        return sb.ToString();
    }

    private static string CsvEscape(string s)
    {
        if (s.Contains(',') || s.Contains('"') || s.Contains('\n') || s.Contains('\r'))
            return "\"" + s.Replace("\"", "\"\"") + "\"";
        return s;
    }

    private static string CleanDescription(string desc)
    {
        return PdfRefNumberPattern().Replace(desc, "").Trim();
    }

    [GeneratedRegex(@"(?:Statement Period|Account Summary)\D*?((?:20|30)\d{2})|\b((?:202[0-9]|2030))\b")]
    private static partial Regex YearPattern();

    [GeneratedRegex(@"\s*(-?\$[\d,]+\.\d{2})\s*$")]
    private static partial Regex AmountEndPattern();

    [GeneratedRegex(@"^\s*(-?\$[\d,]+\.\d{2}|\(\$[\d,]+\.\d{2}\)|[\d,]+\.[\d]{2}-)\s*$")]
    private static partial Regex AmountOnlyPattern();

    [GeneratedRegex(@"^\s*(\d{1,2}/\d{1,2}(?:/\d{4})?|[A-Z][a-z]{2}\s+\d{1,2},?\s+\d{4}|\d{4}-\d{2}-\d{2}|\d{1,2}\s+[A-Z][a-z]{2}\s+\d{4})")]
    private static partial Regex DateStartPattern();

    [GeneratedRegex(@"^\d{1,2}/\d{1,2}(?:/\d{4})?$")]
    private static partial Regex MDSlashPattern();

    [GeneratedRegex(@"(?<=\d)\d{1,2}/\d{1,2}")]
    private static partial Regex PdfDatePattern();

    [GeneratedRegex(@"(-?\$[\d,]+\.\d{2})(?=[a-zA-Z0-9])")]
    private static partial Regex PdfAmountSepPattern();

    [GeneratedRegex(@"^[A-Z0-9]{10,}")]
    private static partial Regex PdfRefNumberPattern();

    [GeneratedRegex(@"^-?\$?(?:\d{1,3}(?:,\d{3})+|\d+)\.\d{2}$")]
    private static partial Regex AmountTokenPattern();
}
