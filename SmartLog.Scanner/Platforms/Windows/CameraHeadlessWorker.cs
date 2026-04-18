using SmartLog.Scanner.Core.Services;

namespace SmartLog.Scanner.Platforms.Windows;

/// <summary>
/// EP0011: Headless Windows camera worker.
/// Thin wrapper around WindowsCameraScanner (which already has no UI dependencies).
/// </summary>
public sealed class CameraHeadlessWorker : ICameraWorker
{
    private readonly WindowsCameraScanner _scanner;

    public event EventHandler<string>? QrCodeDetected;
    public event EventHandler<string>? ErrorOccurred;
    public bool IsRunning => _scanner.IsScanning;

    public CameraHeadlessWorker()
    {
        _scanner = new WindowsCameraScanner();
        _scanner.QrCodeDetected += (_, payload) => QrCodeDetected?.Invoke(this, payload);
    }

    public Task StartAsync(string? deviceId = null)
        => _scanner.StartAsync(deviceId);

    public async Task StopAsync()
    {
        if (_scanner.IsScanning)
            await _scanner.StopAsync();
    }

    public ValueTask DisposeAsync()
    {
        _scanner.Dispose();
        return ValueTask.CompletedTask;
    }
}
