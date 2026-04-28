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

    // ── US0124: Student detail fields (populated during ShowFlash, cleared on reset) ─────────
    [ObservableProperty] private string? _lastStudentId;
    [ObservableProperty] private string? _lastLrn;
    [ObservableProperty] private string? _lastGrade;
    [ObservableProperty] private string? _lastSection;
    [ObservableProperty] private string? _lastProgram;
    [ObservableProperty] private string? _lastScanTime;
    [ObservableProperty] private bool _isVisitorScan;

    public string? LastGradeSection
    {
        get
        {
            if (string.IsNullOrEmpty(LastGrade) && string.IsNullOrEmpty(LastSection))
                return null;
            var grade = LastGrade ?? string.Empty;
            var section = LastSection ?? string.Empty;
            return string.IsNullOrEmpty(LastProgram)
                ? $"{grade} · {section}".Trim(' ', '·')
                : $"{grade} · {LastProgram} · {section}".Trim(' ', '·');
        }
    }

    partial void OnLastGradeChanged(string? value) => OnPropertyChanged(nameof(LastGradeSection));
    partial void OnLastSectionChanged(string? value) => OnPropertyChanged(nameof(LastGradeSection));
    partial void OnLastProgramChanged(string? value) => OnPropertyChanged(nameof(LastGradeSection));

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

    // ── US0126: Bottom device strip ──────────────────────────────────────────

    /// <summary>
    /// Bottom strip status text — line 2 of the USB device strip.
    /// During flash: the friendly scan message. During health warning: the locked US0123 wording.
    /// Otherwise: "Ready to Scan".
    /// </summary>
    public string BottomStripStatusText => ShowFlash
        ? (LastScanMessage ?? "Scan complete")
        : (IsHealthWarning ? "⚠ No recent scans (1m+)" : "Ready to Scan");

    /// <summary>
    /// Bottom strip background colour.
    /// Default indigo (USB identity). Amber when 60s health warning fires. FlashColor during a scan flash.
    /// </summary>
    public Color BottomStripColor => ShowFlash
        ? FlashColor
        : (IsHealthWarning ? Color.FromArgb("#FF9800") : Color.FromArgb("#6A4C93"));

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

    partial void OnShowFlashChanged(bool value)
    {
        OnPropertyChanged(nameof(DisplayColor));
        OnPropertyChanged(nameof(BottomStripStatusText));
        OnPropertyChanged(nameof(BottomStripColor));
    }

    partial void OnIsHealthWarningChanged(bool value)
    {
        OnPropertyChanged(nameof(StatusText));
        OnPropertyChanged(nameof(DisplayColor));
        OnPropertyChanged(nameof(BottomStripStatusText));
        OnPropertyChanged(nameof(BottomStripColor));
    }

    partial void OnIsListeningChanged(bool value) =>
        OnPropertyChanged(nameof(StatusText));

    partial void OnLastScanStatusChanged(ScanStatus? value)
    {
        OnPropertyChanged(nameof(FlashColor));
        OnPropertyChanged(nameof(FlashIcon));
        OnPropertyChanged(nameof(DisplayColor));
        OnPropertyChanged(nameof(BottomStripColor));
    }

    partial void OnLastScanMessageChanged(string? value) =>
        OnPropertyChanged(nameof(BottomStripStatusText));

    partial void OnScanTypeChanged(string value) =>
        OnPropertyChanged(nameof(ScanTypeBadgeColor));
}
