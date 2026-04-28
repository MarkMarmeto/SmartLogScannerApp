using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SmartLog.Scanner.Core.Models;

namespace SmartLog.Scanner.ViewModels;

/// <summary>
/// EP0011 (US0068/US0070): Observable state for a single camera status card.
/// Bound to each item in the MainPage camera status grid (CollectionView).
/// No native view dependencies — pure MAUI/XAML bindable state.
/// </summary>
public partial class CameraSlotState : ObservableObject
{
    private readonly Func<int, Task>? _restartCallback;

    public int Index { get; }

    [ObservableProperty] private string _displayName;
    [ObservableProperty] private string _scanType = "ENTRY";
    [ObservableProperty] private CameraStatus _status = CameraStatus.Idle;
    [ObservableProperty] private string? _errorMessage;

    /// <summary>True while the scan flash animation is showing for this camera card.</summary>
    [ObservableProperty] private bool _showFlash;

    /// <summary>Student name displayed briefly after a successful scan.</summary>
    [ObservableProperty] private string? _flashStudentName;

    /// <summary>Status of the last scan (drives flash color/icon).</summary>
    [ObservableProperty] private ScanStatus? _lastScanStatus;

    /// <summary>Friendly status message shown briefly under the camera name (e.g. "Already scanned").</summary>
    [ObservableProperty] private string? _lastScanMessage;

    // ── US0124: Student detail fields (populated during ShowFlash, cleared on reset) ─────────

    /// <summary>Student number / ID. Visitor scans show "Visitor Pass #N" via FlashStudentName instead.</summary>
    [ObservableProperty] private string? _lastStudentId;

    /// <summary>Learner Reference Number (12-digit DepEd ID).</summary>
    [ObservableProperty] private string? _lastLrn;

    /// <summary>Grade level (e.g., "Grade 11").</summary>
    [ObservableProperty] private string? _lastGrade;

    /// <summary>Section (e.g., "Section A").</summary>
    [ObservableProperty] private string? _lastSection;

    /// <summary>Program / strand (e.g., "STEM"). Optional — omitted from LastGradeSection when null.</summary>
    [ObservableProperty] private string? _lastProgram;

    /// <summary>Local-time scan timestamp (HH:mm:ss) for the per-card bottom banner.</summary>
    [ObservableProperty] private string? _lastScanTime;

    /// <summary>True when the last scan was a visitor pass — drives header label and hides student-only rows.</summary>
    [ObservableProperty] private bool _isVisitorScan;

    /// <summary>Composed "Grade · Program · Section" string. Program omitted when null/empty.</summary>
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

    /// <summary>Formatted frame rate string. Updated by the 1s timer in MainViewModel.</summary>
    [ObservableProperty] private string _frameRateDisplay = "—";

    /// <summary>Physical device ID (informational — displayed in tooltip or card subtitle).</summary>
    [ObservableProperty] private string _cameraDeviceId = string.Empty;

    /// <summary>True when this camera slot is configured and should be shown in the grid.</summary>
    [ObservableProperty] private bool _isVisible = false;

    // ── Computed display properties ──────────────────────────────────────────

    /// <summary>Human-readable status line for the status card.</summary>
    public string StatusText => Status switch
    {
        CameraStatus.Error   => $"⚠ {ErrorMessage ?? "Error"}",
        CameraStatus.Offline => "⊘ Offline",
        _                    => "● Ready to Scan"
    };

    /// <summary>Color of the scan-type badge (teal = ENTRY, red = EXIT).</summary>
    public Color ScanTypeBadgeColor => ScanType == "EXIT"
        ? Color.FromArgb("#F44336")
        : Color.FromArgb("#4D9B91");

    /// <summary>True when status == Error or Offline — shows the Restart button.</summary>
    public bool CanRestart => Status is CameraStatus.Error or CameraStatus.Offline;

    /// <summary>Border color indicating camera health.</summary>
    public Brush StatusBrush => new SolidColorBrush(Status switch
    {
        CameraStatus.Error   => Color.FromArgb("#F44336"),
        CameraStatus.Offline => Color.FromArgb("#9E9E9E"),
        _                    => Color.FromArgb("#4CAF50")
    });

    /// <summary>Color matching the central student card palette for the most recent scan outcome.</summary>
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

    /// <summary>Brush wrapper for binding to Border.Stroke when ShowFlash is true.</summary>
    public Brush FlashBrush => new SolidColorBrush(FlashColor);

    /// <summary>Single-glyph status icon shown on the camera card during flash.</summary>
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

    /// <summary>Border stroke that swaps to the flash color while a scan is being shown.</summary>
    public Brush DisplayBrush => ShowFlash ? FlashBrush : StatusBrush;

    // ── US0126: Bottom device strip ──────────────────────────────────────────

    /// <summary>
    /// Bottom strip status text — line 2 of the device identity strip.
    /// Idle: "Ready to Scan". During flash: the friendly scan message ("✓ Juan Cruz — Accepted").
    /// </summary>
    public string BottomStripStatusText => ShowFlash
        ? (LastScanMessage ?? "Scan complete")
        : "Ready to Scan";

    /// <summary>
    /// Bottom strip background colour. Default green (camera identity).
    /// Shifts to FlashColor (status-coloured) during a 1-second flash, then reverts.
    /// </summary>
    public Color BottomStripColor => ShowFlash
        ? FlashColor
        : Color.FromArgb("#4CAF50");

    // Frame-rate measurement — incremented externally, read by 1s timer
    private int _frameCounter;
    public void IncrementFrameCount() => Interlocked.Increment(ref _frameCounter);

    public void UpdateFrameRate()
    {
        var count = Interlocked.Exchange(ref _frameCounter, 0);
        FrameRateDisplay = Status == CameraStatus.Scanning ? $"{count} fps" : "—";
    }

    // ── Property-changed notifications ───────────────────────────────────────

    partial void OnStatusChanged(CameraStatus value)
    {
        OnPropertyChanged(nameof(CanRestart));
        OnPropertyChanged(nameof(StatusBrush));
        OnPropertyChanged(nameof(StatusText));
        OnPropertyChanged(nameof(DisplayBrush));
    }

    partial void OnErrorMessageChanged(string? value) =>
        OnPropertyChanged(nameof(StatusText));

    partial void OnScanTypeChanged(string value) =>
        OnPropertyChanged(nameof(ScanTypeBadgeColor));

    partial void OnLastScanStatusChanged(ScanStatus? value)
    {
        OnPropertyChanged(nameof(FlashColor));
        OnPropertyChanged(nameof(FlashBrush));
        OnPropertyChanged(nameof(FlashIcon));
        OnPropertyChanged(nameof(DisplayBrush));
        OnPropertyChanged(nameof(BottomStripColor));
    }

    partial void OnShowFlashChanged(bool value)
    {
        OnPropertyChanged(nameof(DisplayBrush));
        OnPropertyChanged(nameof(BottomStripStatusText));
        OnPropertyChanged(nameof(BottomStripColor));
    }

    partial void OnLastScanMessageChanged(string? value) =>
        OnPropertyChanged(nameof(BottomStripStatusText));

    // ── Commands ──────────────────────────────────────────────────────────────

    [RelayCommand]
    private Task Restart() => _restartCallback?.Invoke(Index) ?? Task.CompletedTask;

    // ── Constructor ───────────────────────────────────────────────────────────

    public CameraSlotState(int index, string displayName = "", Func<int, Task>? restartCallback = null)
    {
        Index = index;
        _displayName = string.IsNullOrEmpty(displayName) ? $"Camera {index + 1}" : displayName;
        _restartCallback = restartCallback;
    }
}
