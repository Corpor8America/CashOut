using Microsoft.VisualStudio.TestTools.UnitTesting;
using UglyToad.PdfPig.Content;
using UglyToad.PdfPig.Core;
using UglyToad.PdfPig.Fonts.Standard14Fonts;
using UglyToad.PdfPig.Writer;

namespace CashOut.Tests;

[TestClass]
public class PdfImportServiceTests
{
    private readonly PdfImportService _svc = new();

    // ── Positional mode: column boundaries ────────────────────────────────

    [TestMethod]
    public void ExtractCsvDebug_PositionalMode_KeepsFullMultiWordDescription()
    {
        var pdf = BuildStatementPdf(
            ("Statement Period 01/01/2026 - 01/31/2026", 40, 760),
            ("01/15", 40, 700),
            ("FOOD", 120, 700),
            ("LION", 200, 700),
            ("#0040", 280, 700),
            ("CARY", 360, 700),
            ("NC", 420, 700),
            ("$56.78", 470, 700));

        var (_, csv, _) = _svc.ExtractCsvDebug(null, pdf,
            dateColumnEnd: 0.12m, amountColumnStart: 0.75m);

        var lines = csv.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        Assert.AreEqual(2, lines.Length, $"Expected header + 1 transaction, got: {csv}");
        Assert.AreEqual("2026-01-15,FOOD LION #0040 CARY NC,56.78", lines[1]);
    }

    [TestMethod]
    public void ExtractCsvDebug_PositionalMode_JoinsWrappedContinuationRows()
    {
        var pdf = BuildStatementPdf(
            ("Statement Period 01/01/2026 - 01/31/2026", 40, 760),
            ("01/15", 40, 700),
            ("DEBIT CARD PURCHASE", 120, 700),
            ("$56.78", 470, 700),
            ("LION #0040 CARY NC", 120, 685));

        var (_, csv, skipped) = _svc.ExtractCsvDebug(null, pdf,
            dateColumnEnd: 0.12m, amountColumnStart: 0.75m);

        var lines = csv.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        Assert.AreEqual(2, lines.Length, $"Expected header + 1 transaction, got: {csv}");
        Assert.AreEqual(
            "2026-01-15,DEBIT CARD PURCHASE LION #0040 CARY NC,56.78",
            lines[1]);
        CollectionAssert.DoesNotContain(skipped, "LION #0040 CARY NC");
    }

    [TestMethod]
    public void ExtractCsvDebug_PositionalMode_JoinDisabled_SkipsContinuationRows()
    {
        var pdf = BuildStatementPdf(
            ("01/15", 40, 700),
            ("DEBIT CARD PURCHASE", 120, 700),
            ("$56.78", 470, 700),
            ("LION #0040 CARY NC", 120, 685));

        var (_, csv, skipped) = _svc.ExtractCsvDebug(null, pdf,
            dateColumnEnd: 0.12m, amountColumnStart: 0.75m, joinContinuations: false);

        var lines = csv.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        Assert.AreEqual(2, lines.Length);
        Assert.AreEqual("2026-01-15,DEBIT CARD PURCHASE,56.78", lines[1]);
        Assert.IsTrue(skipped.Contains("LION #0040 CARY NC"));
    }

    [TestMethod]
    public void ExtractCsvDebug_PositionalMode_ParsesNegativeAndParenthesizedAmounts()
    {
        var pdf = BuildStatementPdf(
            ("01/10", 40, 700),
            ("PAYCHECK DIRECT DEP", 120, 700),
            ("-1500.00", 470, 700),
            ("01/12", 40, 680),
            ("GROCERY STORE REFUND", 120, 680),
            ("(23.45)", 470, 680));

        var (_, csv, _) = _svc.ExtractCsvDebug(null, pdf,
            dateColumnEnd: 0.12m, amountColumnStart: 0.75m);

        var lines = csv.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        Assert.IsTrue(lines.Contains("2026-01-10,PAYCHECK DIRECT DEP,-1500.00"), csv);
        Assert.IsTrue(lines.Contains("2026-01-12,GROCERY STORE REFUND,-23.45"), csv);
    }

    [TestMethod]
    public void ExtractCsvDebug_PositionalMode_IgnoresBalanceColumnAfterAmount()
    {
        var pdf = BuildStatementPdf(
            ("01/15", 40, 700),
            ("COFFEE SHOP", 120, 700),
            ("$5.25", 470, 700),
            ("1234.56", 530, 700));

        var (_, csv, _) = _svc.ExtractCsvDebug(null, pdf,
            dateColumnEnd: 0.12m, amountColumnStart: 0.75m);

        var lines = csv.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        Assert.AreEqual(2, lines.Length);
        Assert.AreEqual("2026-01-15,COFFEE SHOP,5.25", lines[1]);
    }

    // ── Flattened fallback ────────────────────────────────────────────────

    [TestMethod]
    public void ExtractCsvDebug_WithoutBoundaries_UsesLegacyFlattenedPipeline()
    {
        var pdf = BuildStatementPdf(
            ("01/15", 40, 700),
            ("DEBIT CARD PURCHASE FOOD LION #0040", 120, 700),
            ("$56.78", 470, 700));

        var (_, csv, _) = _svc.ExtractCsvDebug(null, pdf);

        Assert.IsTrue(csv.Contains("DEBIT CARD PURCHASE FOOD LION #0040"), csv);
        Assert.IsTrue(csv.Contains("56.78"), csv);
    }

    // ── Custom user-supplied row regex ────────────────────────────────────

    [TestMethod]
    public void ExtractCsvDebug_CustomRegexMode_CaptureGroupsBecomeColumns()
    {
        const string rx = @"^(?<date>\d{1,2}/\d{2})\s?(?<ref>\S{17})\s?(?<name>.+?)\s?(?<amount>-?\$[\d,.]+)$";

        var pdf = BuildStatementPdf(
            ("Statement Period", 40, 780), ("11/25/2025 - 12/24/2025", 200, 780),
            ("01/15", 40, 760), ("F389400CZ00CHGDDA", 120, 760),
            ("AUTOMATIC PAYMENT - THANK YOU", 220, 760), ("-$2,840.40", 470, 760),
            ("General Purchases and Other Debits", 40, 740), ("$1,154.66", 470, 740),
            ("12/22", 40, 720), ("2400097B5W8W47MHN", 120, 720),
            ("LOS TRES MAGUEYES- CAR CARY NC", 220, 720), ("$30.91", 470, 720),
            ("12/23", 40, 700), ("2412254B6H7KYSNLN", 120, 700),
            ("AMOCO#2999200HH 255 APEX NC", 220, 700), ("$25.58", 470, 700),
            ("12/23", 40, 680), ("2442733B5LYT3VBPGM", 120, 680),
            ("MCDONALD'S F17721 APEX NC", 220, 680), ("$7.45", 470, 680));

        var (_, csv, skipped) = _svc.ExtractCsvDebug(null, pdf, rowRegex: rx);

        var lines = csv.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        Assert.AreEqual(5, lines.Length, $"Got: {csv}");

        // Every named group becomes a column, in declaration order
        Assert.AreEqual("date,ref,name,amount", lines[0], csv);

        // Payment is listed FIRST but belongs to January of the newer year;
        // the December expense rows below it are the year before
        Assert.IsTrue(lines.Contains("2026-01-15,F389400CZ00CHGDDA,AUTOMATIC PAYMENT - THANK YOU,-2840.40"), csv);

        Assert.IsTrue(lines.Any(l => l.StartsWith("2025-12-22,") && l.Contains("MAGUEYES") && l.EndsWith(",30.91")), csv);
        Assert.IsTrue(lines.Any(l => l.StartsWith("2025-12-23,") && l.Contains("AMOCO#2999200HH") && l.EndsWith(",25.58")), csv);
        Assert.IsTrue(lines.Any(l => l.StartsWith("2025-12-23,") && l.Contains("MCDONALD'S") && l.EndsWith(",7.45")), csv);

        // Rows that never match are skipped
        Assert.IsTrue(skipped.Any(s => s.StartsWith("General Purchases") && s.Contains("1,154.66")));
    }

    [TestMethod]
    public void ExtractCsvDebug_CustomRegexMode_OverridesColumnBoundaries()
    {
        const string rx = @"^(?<date>\d{1,2}/\d{2})(?<name>.+?)\$(?<amount>[\d,.]+)";

        var pdf = BuildStatementPdf(
            ("Statement Period 01/01/2026 - 01/31/2026", 40, 760),
            ("01/15COFFEE SHOP$5.25", 40, 700),
            ("1234.56", 530, 700));

        var (_, csv, _) = _svc.ExtractCsvDebug(null, pdf,
            dateColumnEnd: 0.12m, amountColumnStart: 0.75m, rowRegex: rx);

        var lines = csv.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        Assert.AreEqual(2, lines.Length, $"Expected header + 1 transaction, got: {csv}");
        Assert.AreEqual("2026-01-15,COFFEE SHOP,5.25", lines[1]);
    }

    [TestMethod]
    public void ExtractCsvDebug_CustomRegex_WithoutCaptureGroups_ThrowsHelpfulError()
    {
        var pdf = BuildStatementPdf(("12/22SOMESTORE$5.00", 40, 700));

        var ex = Assert.ThrowsExactly<ArgumentException>(
            () => _svc.ExtractCsvDebug(null, pdf, rowRegex: @"^\d+$"));

        StringAssert.Contains(ex.Message, "capture group");
    }

    [TestMethod]
    public void ExtractCsvDebug_CustomRegex_InvalidSyntax_ThrowsWithReason()
    {
        var pdf = BuildStatementPdf(("12/22SOMESTORE$5.00", 40, 700));

        var ex = Assert.ThrowsExactly<ArgumentException>(
            () => _svc.ExtractCsvDebug(null, pdf, rowRegex: "^(?<date>[unclosed"));

        StringAssert.Contains(ex.Message, "Invalid row regex");
    }

    // ── Year inference across the Jan 1 boundary ──────────────────────────

    [TestMethod]
    public void ExtractCsvDebug_StatementCrossesYearBoundary_RollsJanuaryForward()
    {
        var pdf = BuildStatementPdf(
            ("Statement Period 12/21/2025 - 01/21/2026", 40, 760),
            ("12/21", 40, 700),
            ("FOOD LION #0040 CARY NC", 120, 700),
            ("$56.78", 470, 700),
            ("01/05", 40, 680),
            ("KOHLS NEW YORK NY", 120, 680),
            ("$30.00", 470, 680));

        var (_, csv, _) = _svc.ExtractCsvDebug(null, pdf,
            dateColumnEnd: 0.12m, amountColumnStart: 0.75m);

        var lines = csv.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        Assert.AreEqual(3, lines.Length, csv);
        Assert.AreEqual("2025-12-21,FOOD LION #0040 CARY NC,56.78", lines[1]);
        Assert.AreEqual("2026-01-05,KOHLS NEW YORK NY,30.00", lines[2]);
    }

    [TestMethod]
    public void ExtractCsvDebug_NoExplicitYear_FutureDatesShiftBackOneYear()
    {
        // A 12/21 - 01/21 statement with no year anywhere. Whichever year the
        // parser anchors to, one side would land in the future; both rows must
        // resolve so December precedes January by exactly one year.
        var pdf = BuildStatementPdf(
            ("12/21", 40, 700),
            ("FOOD LION #0040 CARY NC", 120, 700),
            ("$56.78", 470, 700),
            ("01/05", 40, 680),
            ("KOHLS NEW YORK NY", 120, 680),
            ("$30.00", 470, 680));

        var (_, csv, _) = _svc.ExtractCsvDebug(null, pdf,
            dateColumnEnd: 0.12m, amountColumnStart: 0.75m);

        var lines = csv.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        Assert.AreEqual(3, lines.Length, csv);

        var decDate = DateOnly.Parse(lines[1].Split(',')[0]);
        var janDate = DateOnly.Parse(lines[2].Split(',')[0]);

        Assert.AreEqual(12, decDate.Month);
        Assert.AreEqual(21, decDate.Day);
        Assert.AreEqual(1, janDate.Month);
        Assert.AreEqual(5, janDate.Day);
        Assert.AreEqual(decDate.Year + 1, janDate.Year,
            "December row must be the year before the January row");
        Assert.IsFalse(janDate > DateOnly.FromDateTime(DateTime.UtcNow).AddDays(1),
            "resolved dates must not sit in the future");
    }

    [TestMethod]
    public void ExtractCsvDebug_MidYearStatement_DoesNotShiftDates()
    {
        var pdf = BuildStatementPdf(
            ("03/01", 40, 700),
            ("COFFEE SHOP", 120, 700),
            ("$5.25", 470, 700),
            ("03/15", 40, 680),
            ("GROCERY STORE", 120, 680),
            ("$42.10", 470, 680));

        var (_, csv, _) = _svc.ExtractCsvDebug(null, pdf,
            dateColumnEnd: 0.12m, amountColumnStart: 0.75m);

        var lines = csv.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        Assert.AreEqual(3, lines.Length, csv);
        Assert.IsTrue(lines[1].StartsWith($"{DateTime.UtcNow.Year}-03-01,"), lines[1]);
        Assert.IsTrue(lines[2].StartsWith($"{DateTime.UtcNow.Year}-03-15,"), lines[2]);
    }

    [TestMethod]
    public void ExtractCsvDebug_NovDecStatement_NoWrap_KeepsCurrentYear()
    {
        var pdf = BuildStatementPdf(
            ("11/21", 40, 700),
            ("GROCERY STORE", 120, 700),
            ("$42.10", 470, 700),
            ("12/05", 40, 680),
            ("COFFEE SHOP", 120, 680),
            ("$5.25", 470, 680));

        var (_, csv, _) = _svc.ExtractCsvDebug(null, pdf,
            dateColumnEnd: 0.12m, amountColumnStart: 0.75m);

        var lines = csv.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        Assert.AreEqual(3, lines.Length, csv);
        Assert.IsTrue(lines[1].StartsWith($"{DateTime.UtcNow.Year}-11-21,"), lines[1]);
        Assert.IsTrue(lines[2].StartsWith($"{DateTime.UtcNow.Year}-12-05,"), lines[2]);
    }

    // ── Helpers ───────────────────────────────────────────────────────────

    private static byte[] BuildStatementPdf(params (string Text, double X, double Y)[] items)
    {
        var builder = new PdfDocumentBuilder();
        var page = builder.AddPage(PageSize.A4);
        var font = builder.AddStandard14Font(Standard14Font.Helvetica);

        foreach (var (text, x, y) in items)
            page.AddText(text, 10, new PdfPoint(x, y), font);

        return builder.Build();
    }
}
