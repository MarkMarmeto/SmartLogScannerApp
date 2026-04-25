namespace SmartLog.Scanner.Tests.ViewModels;

/// <summary>
/// Unit tests for the US0092 clock format strings used in MainViewModel.UpdateClock().
/// Tests verify format patterns without requiring the MAUI runtime.
/// </summary>
public class ClockFormatTests
{
    [Fact]
    public void CurrentTime_Format_IsHHmm_24Hour()
    {
        var noon = new DateTime(2026, 4, 24, 14, 32, 0);
        Assert.Equal("14:32", noon.ToString("HH:mm"));
    }

    [Fact]
    public void CurrentTime_Format_ZeroPadsHour()
    {
        var earlyMorning = new DateTime(2026, 4, 24, 7, 5, 0);
        Assert.Equal("07:05", earlyMorning.ToString("HH:mm"));
    }

    [Fact]
    public void CurrentDate_Format_MatchesExpectedPattern()
    {
        // Fri, 24 Apr 2026
        var friday = new DateTime(2026, 4, 24);
        Assert.Equal("Fri, 24 Apr 2026", friday.ToString("ddd, dd MMM yyyy"));
    }

    [Fact]
    public void CurrentDate_Format_LeadingZeroOnSingleDigitDay()
    {
        var firstOfMonth = new DateTime(2026, 4, 1);
        Assert.Equal("Wed, 01 Apr 2026", firstOfMonth.ToString("ddd, dd MMM yyyy"));
    }
}
