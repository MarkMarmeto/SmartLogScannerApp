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
}
