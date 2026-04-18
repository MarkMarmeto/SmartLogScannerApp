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
        CameraStatus.Scanning => "● Scanning",
        CameraStatus.Error    => $"⚠ {ErrorMessage ?? "Error"}",
        CameraStatus.Offline  => "⊘ Offline",
        CameraStatus.NoSignal => "? No Signal",
        _                     => "○ Idle"
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
        CameraStatus.Scanning => Color.FromArgb("#4CAF50"),
        CameraStatus.Error    => Color.FromArgb("#F44336"),
        CameraStatus.Offline  => Color.FromArgb("#9E9E9E"),
        CameraStatus.NoSignal => Color.FromArgb("#FF9800"),
        _                     => Color.FromArgb("#E0E0E0")
    });

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
    }

    partial void OnErrorMessageChanged(string? value) =>
        OnPropertyChanged(nameof(StatusText));

    partial void OnScanTypeChanged(string value) =>
        OnPropertyChanged(nameof(ScanTypeBadgeColor));

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
