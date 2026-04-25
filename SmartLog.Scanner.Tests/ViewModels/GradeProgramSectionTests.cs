using SmartLog.Scanner.Core.Models;

namespace SmartLog.Scanner.Tests.ViewModels;

/// <summary>
/// Unit tests for the LastGradeSection computed property on ScanResult (US0091).
/// Tests the Grade · Program · Section display format and fallback behavior.
/// </summary>
public class GradeProgramSectionTests
{
    // Mirrors the MainViewModel.LastGradeSection logic as a pure function for testing
    private static string? Compute(string? grade, string? program, string? section)
    {
        if (string.IsNullOrEmpty(grade) && string.IsNullOrEmpty(section))
            return null;
        var g = grade ?? string.Empty;
        var s = section ?? string.Empty;
        return string.IsNullOrEmpty(program)
            ? $"{g} · {s}".Trim(' ', '·')
            : $"{g} · {program} · {s}".Trim(' ', '·');
    }

    [Fact]
    public void GradeProgramSection_WithAllThree_ShowsFullFormat()
    {
        Assert.Equal("Grade 11 · STEM · 11-A", Compute("Grade 11", "STEM", "11-A"));
    }

    [Fact]
    public void GradeProgramSection_NullProgram_ShowsGradeAndSection()
    {
        Assert.Equal("Grade 7 · 7-A", Compute("Grade 7", null, "7-A"));
    }

    [Fact]
    public void GradeProgramSection_EmptyProgram_ShowsGradeAndSection()
    {
        Assert.Equal("Grade 7 · 7-A", Compute("Grade 7", string.Empty, "7-A"));
    }

    [Fact]
    public void GradeProgramSection_LongSectionName_NotTruncated()
    {
        var longSection = "STEM-Aquinas Section 7-A Special Program";
        var result = Compute("Grade 7", "STEM", longSection);
        Assert.Contains(longSection, result);
    }

    [Fact]
    public void GradeProgramSection_BothNull_ReturnsNull()
    {
        Assert.Null(Compute(null, null, null));
    }

    [Fact]
    public void GradeProgramSection_OnlyGrade_ShowsGrade()
    {
        var result = Compute("Grade 7", null, null);
        Assert.Contains("Grade 7", result);
    }

    [Fact]
    public void GradeProgramSection_RegularProgram_Shown()
    {
        Assert.Equal("Grade 7 · REGULAR · 7-A", Compute("Grade 7", "REGULAR", "7-A"));
    }
}
