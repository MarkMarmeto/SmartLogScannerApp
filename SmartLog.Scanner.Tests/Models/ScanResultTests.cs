using SmartLog.Scanner.Core.Models;

namespace SmartLog.Scanner.Tests.Models;

/// <summary>
/// Tests for ScanResult visitor scan detection (US0076).
/// </summary>
public class ScanResultTests
{
    [Fact]
    public void IsVisitorScan_WithPassCode_ReturnsTrue()
    {
        var result = new ScanResult
        {
            PassCode = "VISITOR-005",
            PassNumber = 5,
            Status = ScanStatus.Accepted
        };

        Assert.True(result.IsVisitorScan);
    }

    [Fact]
    public void IsVisitorScan_WithoutPassCode_ReturnsFalse()
    {
        var result = new ScanResult
        {
            StudentName = "Juan Dela Cruz",
            StudentId = "2026-07-0001",
            Status = ScanStatus.Accepted
        };

        Assert.False(result.IsVisitorScan);
    }

    [Fact]
    public void IsVisitorScan_NullPassCode_ReturnsFalse()
    {
        var result = new ScanResult
        {
            PassCode = null,
            Status = ScanStatus.Accepted
        };

        Assert.False(result.IsVisitorScan);
    }

    [Fact]
    public void VisitorScan_PreservesPassFields()
    {
        var result = new ScanResult
        {
            PassCode = "VISITOR-010",
            PassNumber = 10,
            ScanType = "ENTRY",
            Status = ScanStatus.Accepted
        };

        Assert.Equal("VISITOR-010", result.PassCode);
        Assert.Equal(10, result.PassNumber);
        Assert.Equal("ENTRY", result.ScanType);
    }

    [Fact]
    public void StudentScan_PassFieldsAreNull()
    {
        var result = new ScanResult
        {
            StudentName = "Juan Dela Cruz",
            Grade = "7",
            Section = "Section A",
            Status = ScanStatus.Accepted
        };

        Assert.Null(result.PassCode);
        Assert.Null(result.PassNumber);
        Assert.False(result.IsVisitorScan);
    }
}
