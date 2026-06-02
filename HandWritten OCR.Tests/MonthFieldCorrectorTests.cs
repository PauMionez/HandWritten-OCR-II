using HandWritten_OCR.Helpers;
using Xunit;

namespace HandWritten_OCR.Tests;

/// <summary>
/// Tests for the date-field-scoped fuzzy month correction.
/// </summary>
public class MonthFieldCorrectorTests
{
    // ── Field scoping ─────────────────────────────────────────────────────────

    [Theory]
    [InlineData("When Born", true)]
    [InlineData("When Registered", true)]
    [InlineData("Date of Birth", true)]
    [InlineData("Birth Month", true)]
    [InlineData("Name", false)]
    [InlineData("Street and No.", false)]
    [InlineData("Where Born", false)]  // birthplace, not a date — vetoed by "where"
    [InlineData("Place of Business", false)]
    [InlineData(null, false)]
    [InlineData("", false)]
    public void IsDateField_DetectsDateColumns(string? column, bool expected)
    {
        Assert.Equal(expected, MonthFieldCorrector.IsDateField(column));
    }

    // ── Non-date fields are never altered ─────────────────────────────────────

    [Fact]
    public void Apply_OnNonDateField_LeavesTextUnchanged()
    {
        // "Sept" looks like a month but Name must not be snapped.
        Assert.Equal("Sefre", MonthFieldCorrector.Apply("Sefre", "Name"));
    }

    // ── Fuzzy snapping on date fields ─────────────────────────────────────────

    [Theory]
    [InlineData("Septr 12 1899", "Sept 12 1899")]
    [InlineData("Octr 3 1901", "Oct 3 1901")]
    [InlineData("Febr 9 1885", "Feb 9 1885")]
    [InlineData("Jany 27 1877", "Jany 27 1877")]   // already correct → unchanged
    public void Apply_OnDateField_SnapsLeadingWord(string input, string expected)
    {
        Assert.Equal(expected, MonthFieldCorrector.Apply(input, "When Born"));
    }

    // Cursive "Sept" misreads must snap back even though their edit distance to
    // "sept" is too large for the generic fuzzy tolerance — the cursive shapes
    // are listed as accepted forms so the whole family resolves to "Sept".
    [Theory]
    [InlineData("Sefre 12 1899", "Sept 12 1899")]
    [InlineData("Sefe 3 1901", "Sept 3 1901")]
    [InlineData("Seffre 9 1885", "Sept 9 1885")]   // one off "sefre" → still within tolerance
    [InlineData("Sefte 5 1890", "Sept 5 1890")]
    public void Apply_OnDateField_SnapsCursiveSeptMisreads(string input, string expected)
    {
        Assert.Equal(expected, MonthFieldCorrector.Apply(input, "When Born"));
    }

    // Cursive capital "J" reads as "f" — Jany/June/July become fany/fune/fuly.
    [Theory]
    [InlineData("Fany 27 1877", "Jany 27 1877")]
    [InlineData("Fanny 4 1880", "Jany 4 1880")]    // distance 2 from "jany" — needs the explicit form
    [InlineData("Fanuary 1 1882", "Jany 1 1882")]
    [InlineData("Fune 15 1890", "June 15 1890")]
    [InlineData("Fuly 8 1893", "July 8 1893")]
    public void Apply_OnDateField_SnapsCursiveJMonthMisreads(string input, string expected)
    {
        Assert.Equal(expected, MonthFieldCorrector.Apply(input, "When Born"));
    }

    [Fact]
    public void Apply_PreservesTrailingDigitsAndPunctuation()
    {
        Assert.Equal("Sept .", MonthFieldCorrector.Apply("Sept .", "When Registered"));
    }

    // ── Words too far from any month are left alone ───────────────────────────

    [Fact]
    public void Apply_OnUnrelatedWord_LeavesTextUnchanged()
    {
        Assert.Equal("Holden 27", MonthFieldCorrector.Apply("Holden 27", "When Born"));
    }

    [Fact]
    public void Apply_OnEmptyOrLeadingDigits_ReturnsInput()
    {
        Assert.Equal("", MonthFieldCorrector.Apply("", "When Born"));
        Assert.Equal("12 1899", MonthFieldCorrector.Apply("12 1899", "When Born"));
    }
}
