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
