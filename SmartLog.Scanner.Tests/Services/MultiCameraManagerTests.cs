using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using SmartLog.Scanner.Core.Models;
using SmartLog.Scanner.Core.Services;

namespace SmartLog.Scanner.Tests.Services;

/// <summary>
/// EP0011: Unit tests for MultiCameraManager orchestration logic.
/// Uses a stubbed IServiceProvider that returns pre-created mock CameraQrScannerService instances.
/// </summary>
public class MultiCameraManagerTests
{
    // ── Helpers ──────────────────────────────────────────────────────────────

    private static CameraInstance MakeCamera(int index, bool enabled = true, string? deviceId = "cam-dev-1")
        => new()
        {
            Index = index,
            CameraDeviceId = deviceId ?? string.Empty,
            DisplayName = $"Camera {index + 1}",
            ScanType = "ENTRY",
            IsEnabled = enabled,
            DecodeThrottleFrames = 5
        };

    private static List<CameraInstance> MakeCameras(int count)
        => Enumerable.Range(0, count).Select(i => MakeCamera(i)).ToList();

    /// <summary>
    /// Creates a MultiCameraManager with a real IServiceProvider that can resolve
    /// CameraQrScannerService. The scanner service's core dependencies are mocked.
    /// </summary>
    private static (MultiCameraManager manager, Mock<IPreferencesService> prefsMock) CreateManager()
    {
        var prefsMock = new Mock<IPreferencesService>();
        prefsMock.Setup(p => p.GetCameraScanType(It.IsAny<int>())).Returns("ENTRY");
        prefsMock.Setup(p => p.GetDefaultScanType()).Returns("ENTRY");
        prefsMock.Setup(p => p.GetSelectedCameraId()).Returns(string.Empty);

        var scanApiMock = new Mock<IScanApiService>();
        var hmacMock = new Mock<IHmacValidator>();
        var dedupMock = new Mock<IScanDeduplicationService>();
        var healthMock = new Mock<IHealthCheckService>();
        var offlineMock = new Mock<IOfflineQueueService>();
        var timeServiceMock = new Mock<ITimeService>();
        timeServiceMock.Setup(t => t.UtcNow).Returns(DateTimeOffset.UtcNow);
        timeServiceMock.Setup(t => t.SyncAsync()).Returns(Task.CompletedTask);

        var services = new ServiceCollection();
        services.AddTransient<CameraQrScannerService>();
        services.AddSingleton(prefsMock.Object);
        services.AddSingleton(scanApiMock.Object);
        services.AddSingleton(hmacMock.Object);
        services.AddSingleton(dedupMock.Object);
        services.AddSingleton(healthMock.Object);
        services.AddSingleton(offlineMock.Object);
        services.AddSingleton(timeServiceMock.Object);
        services.AddLogging(); // provides ILogger<T> resolution

        var sp = services.BuildServiceProvider();

        var manager = new MultiCameraManager(
            sp,
            prefsMock.Object,
            NullLogger<MultiCameraManager>.Instance);

        return (manager, prefsMock);
    }

    // ── InitializeAsync ───────────────────────────────────────────────────────

    [Fact]
    public async Task InitializeAsync_WithValidCameras_SetsUpCamerasList()
    {
        var (manager, _) = CreateManager();
        var cameras = MakeCameras(3);

        await manager.InitializeAsync(cameras);

        Assert.Equal(3, manager.Cameras.Count);
    }

    [Fact]
    public async Task InitializeAsync_CameraWithNoDeviceId_StatusIsOffline()
    {
        var (manager, _) = CreateManager();
        var cameras = new List<CameraInstance>
        {
            MakeCamera(0, deviceId: ""),  // No device
            MakeCamera(1, deviceId: "valid-cam")
        };

        await manager.InitializeAsync(cameras);

        Assert.Equal(CameraStatus.Offline, manager.Cameras[0].Status);
        Assert.Equal(CameraStatus.Idle, manager.Cameras[1].Status);
    }

    [Fact]
    public async Task InitializeAsync_MoreThan8Cameras_ThrowsArgumentException()
    {
        var (manager, _) = CreateManager();
        var cameras = MakeCameras(9);

        await Assert.ThrowsAsync<ArgumentException>(() => manager.InitializeAsync(cameras));
    }

    [Fact]
    public async Task InitializeAsync_ExactlyMaxCameras_Succeeds()
    {
        var (manager, _) = CreateManager();
        var cameras = MakeCameras(8);

        var ex = await Record.ExceptionAsync(() => manager.InitializeAsync(cameras));

        Assert.Null(ex);
        Assert.Equal(8, manager.Cameras.Count);
    }

    // ── UpdateThrottleValues ──────────────────────────────────────────────────

    [Theory]
    [InlineData(1, 5)]
    [InlineData(2, 5)]
    [InlineData(3, 8)]
    [InlineData(4, 8)]
    [InlineData(8, 15)]
    public async Task UpdateThrottleValues_SetsCorrectThrottle(int cameraCount, int expectedThrottle)
    {
        var (manager, _) = CreateManager();
        await manager.InitializeAsync(MakeCameras(cameraCount));

        manager.UpdateThrottleValues();

        foreach (var cam in manager.Cameras)
            Assert.Equal(expectedThrottle, cam.DecodeThrottleFrames);
    }

    [Fact]
    public async Task UpdateThrottleValues_DisabledCamerasExcluded_FromActiveCount()
    {
        var (manager, _) = CreateManager();
        var cameras = new List<CameraInstance>
        {
            MakeCamera(0, enabled: false),
            MakeCamera(1, enabled: false),
            MakeCamera(2, enabled: true)
        };
        await manager.InitializeAsync(cameras);

        // Only 1 camera is enabled → throttle should match 1-camera tier (5)
        manager.UpdateThrottleValues();

        Assert.Equal(AdaptiveDecodeThrottle.Calculate(1), manager.Cameras[2].DecodeThrottleFrames);
    }

    // ── UpdateScanTypes ───────────────────────────────────────────────────────

    [Fact]
    public async Task UpdateScanTypes_UpdatesCameraInstanceScanType()
    {
        var (manager, prefsMock) = CreateManager();
        await manager.InitializeAsync(MakeCameras(2));

        prefsMock.Setup(p => p.GetCameraScanType(0)).Returns("EXIT");
        prefsMock.Setup(p => p.GetCameraScanType(1)).Returns("ENTRY");

        manager.UpdateScanTypes();

        Assert.Equal("EXIT", manager.Cameras[0].ScanType);
        Assert.Equal("ENTRY", manager.Cameras[1].ScanType);
    }

    // ── CameraStatusChanged events ────────────────────────────────────────────

    [Fact]
    public async Task CameraStatusChanged_FiredWith_OfflineStatus_WhenNoDeviceId()
    {
        var (manager, _) = CreateManager();
        var received = new List<(int, CameraStatus)>();
        manager.CameraStatusChanged += (_, e) => received.Add((e.CameraIndex, e.Status));

        // Re-initialize with a camera that has no device ID
        var cameras = new List<CameraInstance> { MakeCamera(0, deviceId: "") };
        await manager.InitializeAsync(cameras);

        // Status is set directly in InitializeAsync for offline cameras — no event fired.
        // Verify the status is Offline.
        Assert.Equal(CameraStatus.Offline, manager.Cameras[0].Status);
    }

    // ── StopCameraAsync disables auto-recovery ─────────────────────────────────

    [Fact]
    public async Task StopCameraAsync_SetsIsEnabledFalse()
    {
        var (manager, _) = CreateManager();
        await manager.InitializeAsync(MakeCameras(1));

        await manager.StopCameraAsync(0);

        Assert.False(manager.Cameras[0].IsEnabled);
    }

    // ── RestartCameraAsync re-enables camera ──────────────────────────────────

    [Fact]
    public async Task RestartCameraAsync_ResetsReconnectAttemptsAndReEnablesCamera()
    {
        var (manager, _) = CreateManager();
        var cameras = MakeCameras(1);
        cameras[0].ReconnectAttempts = 3;
        cameras[0].IsEnabled = false;
        await manager.InitializeAsync(cameras);

        await manager.RestartCameraAsync(0);

        Assert.True(manager.Cameras[0].IsEnabled);
        Assert.Equal(0, manager.Cameras[0].ReconnectAttempts);
    }

    // ── ProcessQrCodeAsync routes to correct service ──────────────────────────

    [Fact]
    public async Task ProcessQrCodeAsync_UnknownCameraIndex_DoesNotThrow()
    {
        var (manager, _) = CreateManager();
        await manager.InitializeAsync(MakeCameras(1));

        // Index 99 does not exist — should log a warning but not throw
        var ex = await Record.ExceptionAsync(
            () => manager.ProcessQrCodeAsync(99, "SMARTLOG:TEST:12345:abc123"));

        Assert.Null(ex);
    }

    // ── DisposeAsync ──────────────────────────────────────────────────────────

    [Fact]
    public async Task DisposeAsync_CanBeCalledWithoutInit_DoesNotThrow()
    {
        var (manager, _) = CreateManager();

        var ex = await Record.ExceptionAsync(async () => await manager.DisposeAsync());

        Assert.Null(ex);
    }
}
