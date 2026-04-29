using Microsoft.Maui.Graphics;
using SmartLog.Scanner.Core.Models;
using SmartLog.Scanner.Core.ViewModels;

namespace SmartLog.Scanner.Tests.ViewModels;

public class UsbScannerSlotStateTests
{
    // ── Initial state ────────────────────────────────────────────────────────

    [Fact]
    public void Initial_State_Is_Not_Listening_Not_Warning_Not_Visible()
    {
        var slot = new UsbScannerSlotState();
        Assert.False(slot.IsListening);
        Assert.False(slot.IsHealthWarning);
        Assert.False(slot.IsVisible);
        Assert.False(slot.ShowFlash);
    }

    // ── StartListening ───────────────────────────────────────────────────────

    [Fact]
    public void StartListening_Sets_IsListening_True_And_Clears_Warning()
    {
        var slot = new UsbScannerSlotState();
        slot.IsHealthWarning = true;
        slot.StartListening();
        Assert.True(slot.IsListening);
        Assert.False(slot.IsHealthWarning);
        Assert.Null(slot.LastScanAt);
    }

    // ── Tick / warning heuristic ─────────────────────────────────────────────

    [Fact]
    public void Tick_Before_First_Scan_Does_Not_Set_Warning()
    {
        var slot = new UsbScannerSlotState();
        slot.StartListening();
        slot.Tick();
        Assert.False(slot.IsHealthWarning);
    }

    [Fact]
    public void Tick_Within_60_Seconds_Of_Last_Scan_Does_Not_Set_Warning()
    {
        var slot = new UsbScannerSlotState();
        slot.StartListening();
        slot.LastScanAt = DateTimeOffset.UtcNow.AddSeconds(-30);
        slot.Tick();
        Assert.False(slot.IsHealthWarning);
    }

    [Fact]
    public void Tick_After_60_Seconds_Sets_Warning()
    {
        var slot = new UsbScannerSlotState();
        slot.StartListening();
        slot.LastScanAt = DateTimeOffset.UtcNow.AddSeconds(-61);
        slot.Tick();
        Assert.True(slot.IsHealthWarning);
    }

    [Fact]
    public void Tick_Clears_Warning_When_LastScanAt_Refreshed_Within_Threshold()
    {
        var slot = new UsbScannerSlotState();
        slot.StartListening();
        slot.LastScanAt = DateTimeOffset.UtcNow.AddSeconds(-61);
        slot.Tick();
        Assert.True(slot.IsHealthWarning);

        slot.LastScanAt = DateTimeOffset.UtcNow;
        slot.Tick();
        Assert.False(slot.IsHealthWarning);
    }

    [Fact]
    public void Tick_While_Not_Listening_Does_Nothing()
    {
        var slot = new UsbScannerSlotState();
        slot.LastScanAt = DateTimeOffset.UtcNow.AddSeconds(-90);
        slot.Tick();
        Assert.False(slot.IsHealthWarning);
    }

    // ── StopListening ────────────────────────────────────────────────────────

    [Fact]
    public void StopListening_Clears_Listening_Warning_And_Flash()
    {
        var slot = new UsbScannerSlotState();
        slot.StartListening();
        slot.IsHealthWarning = true;
        slot.ShowFlash = true;
        slot.StopListening();
        Assert.False(slot.IsListening);
        Assert.False(slot.IsHealthWarning);
        Assert.False(slot.ShowFlash);
    }

    // ── StatusText locked wording ────────────────────────────────────────────

    [Fact]
    public void StatusText_When_Warning_Returns_Locked_Wording()
    {
        var slot = new UsbScannerSlotState();
        slot.StartListening();
        slot.LastScanAt = DateTimeOffset.UtcNow.AddSeconds(-90);
        slot.Tick();
        Assert.Equal("⚠ No recent scans (1m+)", slot.StatusText);
    }

    [Fact]
    public void StatusText_When_Listening_No_Warning_Returns_Listening()
    {
        var slot = new UsbScannerSlotState();
        slot.StartListening();
        Assert.Equal("● Listening", slot.StatusText);
    }

    [Fact]
    public void StatusText_When_Not_Listening_Returns_Idle()
    {
        var slot = new UsbScannerSlotState();
        Assert.Equal("○ Idle", slot.StatusText);
    }

    // ── DisplayColor ─────────────────────────────────────────────────────────

    [Fact]
    public void DisplayColor_Indigo_When_Listening_No_Warning_No_Flash()
    {
        var slot = new UsbScannerSlotState();
        slot.StartListening();
        Assert.Equal(Color.FromArgb("#6A4C93"), slot.DisplayColor);
    }

    [Fact]
    public void DisplayColor_Amber_When_Warning()
    {
        var slot = new UsbScannerSlotState();
        slot.StartListening();
        slot.LastScanAt = DateTimeOffset.UtcNow.AddSeconds(-90);
        slot.Tick();
        Assert.Equal(Color.FromArgb("#FF9800"), slot.DisplayColor);
    }

    [Fact]
    public void DisplayColor_Green_When_Flashing_Accepted()
    {
        var slot = new UsbScannerSlotState();
        slot.StartListening();
        slot.LastScanStatus = ScanStatus.Accepted;
        slot.ShowFlash = true;
        Assert.Equal(Color.FromArgb("#4CAF50"), slot.DisplayColor);
    }

    [Fact]
    public void DisplayColor_Red_When_Flashing_Rejected()
    {
        var slot = new UsbScannerSlotState();
        slot.StartListening();
        slot.LastScanStatus = ScanStatus.Rejected;
        slot.ShowFlash = true;
        Assert.Equal(Color.FromArgb("#F44336"), slot.DisplayColor);
    }

    // ── ScanTypeBadgeColor ────────────────────────────────────────────────────

    [Fact]
    public void ScanTypeBadgeColor_Teal_For_Entry()
    {
        var slot = new UsbScannerSlotState { ScanType = "ENTRY" };
        Assert.Equal(Color.FromArgb("#4D9B91"), slot.ScanTypeBadgeColor);
    }

    [Fact]
    public void ScanTypeBadgeColor_Red_For_Exit()
    {
        var slot = new UsbScannerSlotState { ScanType = "EXIT" };
        Assert.Equal(Color.FromArgb("#F44336"), slot.ScanTypeBadgeColor);
    }

    // ── FlashIcon ─────────────────────────────────────────────────────────────

    [Theory]
    [InlineData(ScanStatus.Accepted, "✓")]
    [InlineData(ScanStatus.Duplicate, "⚠")]
    [InlineData(ScanStatus.DebouncedLocally, "⚠")]
    [InlineData(ScanStatus.RateLimited, "⏱")]
    [InlineData(ScanStatus.Queued, "📥")]
    [InlineData(ScanStatus.Rejected, "✗")]
    [InlineData(ScanStatus.Error, "✗")]
    public void FlashIcon_Maps_Each_ScanStatus_To_Correct_Glyph(ScanStatus status, string expected)
    {
        var slot = new UsbScannerSlotState { LastScanStatus = status };
        Assert.Equal(expected, slot.FlashIcon);
    }

    // ── US0124: Student detail fields and LastGradeSection ────────────────────

    [Fact]
    public void LastGradeSection_With_All_Three_Fields_Joins_With_Middle_Dot()
    {
        var slot = new UsbScannerSlotState
        {
            LastGrade = "Grade 11",
            LastProgram = "STEM",
            LastSection = "A"
        };
        Assert.Equal("Grade 11 · STEM · A", slot.LastGradeSection);
    }

    [Fact]
    public void LastGradeSection_Without_Program_Omits_Middle_Segment()
    {
        var slot = new UsbScannerSlotState
        {
            LastGrade = "Grade 11",
            LastSection = "A"
        };
        Assert.Equal("Grade 11 · A", slot.LastGradeSection);
    }

    [Fact]
    public void LastGradeSection_With_Empty_Program_Treats_It_As_Null()
    {
        var slot = new UsbScannerSlotState
        {
            LastGrade = "Grade 12",
            LastProgram = string.Empty,
            LastSection = "B"
        };
        Assert.Equal("Grade 12 · B", slot.LastGradeSection);
    }

    [Fact]
    public void LastGradeSection_Returns_Null_When_Both_Grade_And_Section_Empty()
    {
        var slot = new UsbScannerSlotState();
        Assert.Null(slot.LastGradeSection);
    }

    [Fact]
    public void Setting_Grade_Or_Section_Or_Program_Raises_LastGradeSection_Notification()
    {
        var slot = new UsbScannerSlotState();
        var raised = new List<string>();
        slot.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName != null) raised.Add(e.PropertyName);
        };

        slot.LastGrade = "Grade 12";
        slot.LastProgram = "ABM";
        slot.LastSection = "B";

        Assert.Equal(3, raised.Count(n => n == nameof(UsbScannerSlotState.LastGradeSection)));
    }

    [Fact]
    public void IsVisitorScan_Defaults_False()
    {
        var slot = new UsbScannerSlotState();
        Assert.False(slot.IsVisitorScan);
    }

    [Fact]
    public void Student_Detail_Fields_Default_To_Null()
    {
        var slot = new UsbScannerSlotState();
        Assert.Null(slot.LastStudentId);
        Assert.Null(slot.LastLrn);
        Assert.Null(slot.LastGrade);
        Assert.Null(slot.LastSection);
        Assert.Null(slot.LastProgram);
        Assert.Null(slot.LastScanTime);
    }

    [Fact]
    public void Populating_All_Detail_Fields_Then_Clearing_All_Returns_To_Null()
    {
        var slot = new UsbScannerSlotState
        {
            LastStudentId = "STU12345",
            LastLrn = "123456789012",
            LastGrade = "Grade 11",
            LastProgram = "STEM",
            LastSection = "A",
            LastScanTime = "14:32:05",
            IsVisitorScan = false
        };

        Assert.Equal("STU12345", slot.LastStudentId);
        Assert.Equal("Grade 11 · STEM · A", slot.LastGradeSection);

        slot.LastStudentId = null;
        slot.LastLrn = null;
        slot.LastGrade = null;
        slot.LastSection = null;
        slot.LastProgram = null;
        slot.LastScanTime = null;

        Assert.Null(slot.LastStudentId);
        Assert.Null(slot.LastGradeSection);
    }

    // ── US0126: Bottom strip computed properties ─────────────────────────────

    [Fact]
    public void BottomStripStatusText_When_Idle_Reads_Ready_To_Scan()
    {
        var slot = new UsbScannerSlotState();
        slot.StartListening();
        Assert.Equal("Ready to Scan", slot.BottomStripStatusText);
    }

    [Fact]
    public void BottomStripStatusText_When_Flash_Reads_LastScanMessage()
    {
        var slot = new UsbScannerSlotState
        {
            LastScanMessage = "✓ Juan Cruz — Accepted",
            ShowFlash = true
        };
        Assert.Equal("✓ Juan Cruz — Accepted", slot.BottomStripStatusText);
    }

    [Fact]
    public void BottomStripStatusText_When_Health_Warning_Reads_Locked_Wording()
    {
        var slot = new UsbScannerSlotState();
        slot.StartListening();
        slot.IsHealthWarning = true;
        Assert.Equal("⚠ No recent scans (1m+)", slot.BottomStripStatusText);
    }

    [Fact]
    public void BottomStripStatusText_Flash_Beats_Health_Warning()
    {
        var slot = new UsbScannerSlotState
        {
            LastScanMessage = "✓ Accepted",
            IsHealthWarning = true,
            ShowFlash = true
        };
        Assert.Equal("✓ Accepted", slot.BottomStripStatusText);
    }

    [Fact]
    public void BottomStripColor_When_Idle_Is_Indigo()
    {
        var slot = new UsbScannerSlotState();
        slot.StartListening();
        Assert.Equal(Color.FromArgb("#6A4C93"), slot.BottomStripColor);
    }

    [Fact]
    public void BottomStripColor_When_Health_Warning_Is_Amber()
    {
        var slot = new UsbScannerSlotState();
        slot.StartListening();
        slot.IsHealthWarning = true;
        Assert.Equal(Color.FromArgb("#FF9800"), slot.BottomStripColor);
    }

    [Fact]
    public void BottomStripColor_When_Flash_Accepted_Is_Green()
    {
        var slot = new UsbScannerSlotState
        {
            LastScanStatus = ScanStatus.Accepted,
            ShowFlash = true
        };
        Assert.Equal(Color.FromArgb("#4CAF50"), slot.BottomStripColor);
    }

    [Fact]
    public void BottomStripColor_Reverts_To_Indigo_When_ShowFlash_Returns_False()
    {
        var slot = new UsbScannerSlotState();
        slot.StartListening();
        slot.LastScanStatus = ScanStatus.Accepted;
        slot.ShowFlash = true;
        Assert.Equal(Color.FromArgb("#4CAF50"), slot.BottomStripColor);

        slot.ShowFlash = false;
        Assert.Equal(Color.FromArgb("#6A4C93"), slot.BottomStripColor);
    }
}
