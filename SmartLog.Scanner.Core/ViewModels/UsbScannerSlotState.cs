using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.Maui.Graphics;
using SmartLog.Scanner.Core.Models;

namespace SmartLog.Scanner.Core.ViewModels;

/// <summary>
/// EP0012 (US0123): Observable state for the USB scanner indicator card on MainPage.
/// Peer to CameraSlotState with USB-specific health semantics:
/// - No frame rate display (event-driven, not polled)
/// - No restart button (HID device — nothing to restart)
/// - 60s no-scan warning, fires only after the first scan of the session
///
/// Lives in Core so the test project can reference it without depending on the MAUI project.
/// Uses Color properties instead of Brush — MAUI XAML auto-converts Color → Brush for Border.Stroke.
/// </summary>
public partial class UsbScannerSlotState : ObservableObject
{
    private const int WarningThresholdSeconds = 60;

    [ObservableProperty] private string _displayName = "USB Scanner";
    [ObservableProperty] private string _scanType = "ENTRY";
    [ObservableProperty] private bool _isListening;
    [ObservableProperty] private DateTimeOffset? _lastScanAt;
    [ObservableProperty] private bool _showFlash;
    [ObservableProperty] private string? _flashStudentName;
    [ObservableProperty] private ScanStatus? _lastScanStatus;
    [ObservableProperty] private string? _lastScanMessage;
    [ObservableProperty] private bool _isVisible;
    [ObservableProperty] private bool _isHealthWarning;

    /// <summary>Locked wording per US0123 AC5.</summary>
    public string StatusText => IsHealthWarning
        ? "⚠ No recent scans (1m+)"
        : (IsListening ? "● Listening" : "○ Idle");

    /// <summary>Indigo (#6A4C93) normally, amber when warning, flash color during a scan flash.</summary>
    public Color DisplayColor => ShowFlash
        ? FlashColor
        : (IsHealthWarning ? Color.FromArgb("#FF9800") : Color.FromArgb("#6A4C93"));

    /// <summary>Scan-type badge color — same palette as camera cards.</summary>
    public Color ScanTypeBadgeColor => ScanType == "EXIT"
        ? Color.FromArgb("#F44336")
        : Color.FromArgb("#4D9B91");

    /// <summary>Flash result color — same palette as CameraSlotState.</summary>
    public Color FlashColor => LastScanStatus switch
    {
        ScanStatus.Accepted         => Color.FromArgb("#4CAF50"),
        ScanStatus.Duplicate        => Color.FromArgb("#FF9800"),
        ScanStatus.DebouncedLocally => Color.FromArgb("#FF9800"),
        ScanStatus.RateLimited      => Color.FromArgb("#FF9800"),
        ScanStatus.Queued           => Color.FromArgb("#4D9B91"),
        ScanStatus.Rejected         => Color.FromArgb("#F44336"),
        ScanStatus.Error            => Color.FromArgb("#F44336"),
        _                           => Color.FromArgb("#4CAF50")
    };

    /// <summary>Single-glyph result icon — same palette as CameraSlotState.</summary>
    public string FlashIcon => LastScanStatus switch
    {
        ScanStatus.Accepted         => "✓",
        ScanStatus.Duplicate        => "⚠",
        ScanStatus.DebouncedLocally => "⚠",
        ScanStatus.RateLimited      => "⏱",
        ScanStatus.Queued           => "📥",
        ScanStatus.Rejected         => "✗",
        ScanStatus.Error            => "✗",
        _                           => string.Empty
    };

    /// <summary>
    /// Called by the 1s timer in MainViewModel.
    /// Warning fires only after the first scan of the session (LastScanAt must be set).
    /// Before any scan: always "Listening", never warning (AC8).
    /// </summary>
    public void Tick()
    {
        if (!IsListening || !LastScanAt.HasValue) return;
        var ageSeconds = (DateTimeOffset.UtcNow - LastScanAt.Value).TotalSeconds;
        IsHealthWarning = ageSeconds >= WarningThresholdSeconds;
    }

    /// <summary>Called by MainViewModel when the USB pipeline starts.</summary>
    public void StartListening()
    {
        IsListening = true;
        IsHealthWarning = false;
        LastScanAt = null;
    }

    /// <summary>Called by MainViewModel when the USB pipeline stops.</summary>
    public void StopListening()
    {
        IsListening = false;
        IsHealthWarning = false;
        ShowFlash = false;
    }

    // ── Property-changed cascades ────────────────────────────────────────────

    partial void OnShowFlashChanged(bool value) =>
        OnPropertyChanged(nameof(DisplayColor));

    partial void OnIsHealthWarningChanged(bool value)
    {
        OnPropertyChanged(nameof(StatusText));
        OnPropertyChanged(nameof(DisplayColor));
    }

    partial void OnIsListeningChanged(bool value) =>
        OnPropertyChanged(nameof(StatusText));

    partial void OnLastScanStatusChanged(ScanStatus? value)
    {
        OnPropertyChanged(nameof(FlashColor));
        OnPropertyChanged(nameof(FlashIcon));
        OnPropertyChanged(nameof(DisplayColor));
    }

    partial void OnScanTypeChanged(string value) =>
        OnPropertyChanged(nameof(ScanTypeBadgeColor));
}
