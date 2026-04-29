using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using SmartLog.Scanner.Core.Models;
using SmartLog.Scanner.Core.Services;

namespace SmartLog.Scanner.Core.ViewModels;

/// <summary>
/// EP0011 (US0071): Represents a single camera configuration row on the Setup page.
/// </summary>
public partial class CameraSlotViewModel : ObservableObject
{
    private readonly ICameraEnumerationService? _cameraEnumeration;
    private readonly ILogger<CameraSlotViewModel> _logger;

    public int Index { get; }
    public int DisplayNumber => Index + 1;

    [ObservableProperty] private string _displayName;
    [ObservableProperty] private CameraDeviceInfo? _selectedDevice;
    [ObservableProperty] private string _scanType = "ENTRY";
    [ObservableProperty] private bool _isEnabled = true;
    [ObservableProperty] private bool _isConnected;
    [ObservableProperty] private bool _isTestRunning;
    [ObservableProperty] private string? _testResult;

    public ObservableCollection<CameraDeviceInfo> AvailableDevices { get; } = new();

    public CameraSlotViewModel(
        int index,
        ICameraEnumerationService? cameraEnumeration,
        ILogger<CameraSlotViewModel> logger)
    {
        Index = index;
        _displayName = $"Camera {index + 1}";
        _cameraEnumeration = cameraEnumeration;
        _logger = logger;
    }

    public void PopulateDevices(IEnumerable<CameraDeviceInfo> devices)
    {
        // Capture current selection before clearing — MAUI Picker resets SelectedItem
        // to null via TwoWay binding when ItemsSource is cleared, so we must re-apply.
        var currentId = SelectedDevice?.Id;
        AvailableDevices.Clear();
        foreach (var d in devices)
            AvailableDevices.Add(d);
        if (currentId != null)
            SelectedDevice = AvailableDevices.FirstOrDefault(d => d.Id == currentId);
    }

    /// <summary>
    /// macOS Catalyst: the native UIKit picker does not honour SelectedItem set during the
    /// initial render pass. Toggling the value after layout forces the picker to re-read it.
    /// </summary>
    public void ForceRefreshSelection()
    {
        if (_selectedDevice is null) return;
        var saved = _selectedDevice;
        _selectedDevice = null;
        OnPropertyChanged(nameof(SelectedDevice));
        _selectedDevice = saved;
        OnPropertyChanged(nameof(SelectedDevice));
    }

    /// <summary>
    /// Test command: briefly opens the camera to verify it works.
    /// </summary>
    [RelayCommand]
    private async Task TestCameraAsync()
    {
        if (_cameraEnumeration == null)
        {
            TestResult = "Camera testing not available on this platform.";
            return;
        }

        IsTestRunning = true;
        TestResult = null;

        try
        {
            var deviceId = SelectedDevice?.Id;
            if (string.IsNullOrWhiteSpace(deviceId))
            {
                TestResult = "No device selected.";
                IsConnected = false;
                return;
            }

            var success = await _cameraEnumeration.TestCameraAsync(deviceId);
            IsConnected = success;
            TestResult = success ? "Camera OK" : "Test failed — camera did not respond";
            _logger.LogInformation("Camera {Index} test: {Result}", Index, TestResult);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Camera {Index} test threw an exception", Index);
            IsConnected = false;
            TestResult = $"Test failed: {ex.Message}";
        }
        finally
        {
            IsTestRunning = false;
        }
    }
}
