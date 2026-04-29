using Moq;
using SmartLog.Scanner.Core.Services;
using SmartLog.Scanner.Core.ViewModels;
using SmartLog.Scanner.Core.Models;
using Microsoft.Extensions.Logging;
using Xunit;

namespace SmartLog.Scanner.Tests.ViewModels;

/// <summary>
/// US0127: Unit tests for auto-slot creation in SetupViewModel.LoadCamerasAsync().
/// </summary>
public class SetupViewModelAutoDetectTests
{
    private readonly Mock<ISecureConfigService> _mockSecureConfig = new();
    private readonly Mock<IPreferencesService> _mockPreferences = new();
    private readonly Mock<INavigationService> _mockNavigation = new();
    private readonly Mock<IConnectionTestService> _mockConnectionTest = new();
    private readonly Mock<IDeviceDetectionService> _mockDeviceDetection = new();
    private readonly Mock<ILogger<SetupViewModel>> _mockLogger = new();
    private readonly Mock<ICameraEnumerationService> _mockCameraEnum = new();

    private SetupViewModel BuildVm() => new(
        _mockSecureConfig.Object,
        _mockPreferences.Object,
        _mockNavigation.Object,
        _mockConnectionTest.Object,
        _mockDeviceDetection.Object,
        _mockLogger.Object,
        _mockCameraEnum.Object);

    private static CameraDeviceInfo Cam(string id, string name) => new(id, name);

    [Fact]
    public async Task LoadCamerasAsync_TwoCamerasDetected_CreatesTwoSlots()
    {
        var cameras = new List<CameraDeviceInfo> { Cam("id1", "FaceTime HD Camera"), Cam("id2", "Logitech C920") };
        _mockCameraEnum.Setup(s => s.GetAvailableCamerasAsync()).ReturnsAsync(cameras);

        var vm = BuildVm();
        await vm.InitializeAsync();

        Assert.Equal(2, vm.CameraSlots.Count);
    }

    [Fact]
    public async Task LoadCamerasAsync_SlotsAssignedInDetectionOrder()
    {
        var cameras = new List<CameraDeviceInfo> { Cam("id1", "FaceTime HD Camera"), Cam("id2", "Logitech C920") };
        _mockCameraEnum.Setup(s => s.GetAvailableCamerasAsync()).ReturnsAsync(cameras);

        var vm = BuildVm();
        await vm.InitializeAsync();

        Assert.Equal(cameras[0], vm.CameraSlots[0].SelectedDevice);
        Assert.Equal(cameras[1], vm.CameraSlots[1].SelectedDevice);
    }

    [Fact]
    public async Task LoadCamerasAsync_AutoNameApplied_WhenSavedNameBlank()
    {
        var cameras = new List<CameraDeviceInfo> { Cam("id1", "FaceTime HD Camera") };
        _mockCameraEnum.Setup(s => s.GetAvailableCamerasAsync()).ReturnsAsync(cameras);
        _mockPreferences.Setup(p => p.GetCameraName(0)).Returns(string.Empty);

        var vm = BuildVm();
        await vm.InitializeAsync();

        Assert.Equal("Camera 1 – FaceTime HD Camera", vm.CameraSlots[0].DisplayName);
    }

    [Fact]
    public async Task LoadCamerasAsync_SavedNameRestored_WhenNotBlank()
    {
        var cameras = new List<CameraDeviceInfo> { Cam("id1", "FaceTime HD Camera") };
        _mockCameraEnum.Setup(s => s.GetAvailableCamerasAsync()).ReturnsAsync(cameras);
        _mockPreferences.Setup(p => p.GetCameraName(0)).Returns("Entrance Cam 1");

        var vm = BuildVm();
        await vm.InitializeAsync();

        Assert.Equal("Entrance Cam 1", vm.CameraSlots[0].DisplayName);
    }

    [Fact]
    public async Task LoadCamerasAsync_WhitespaceSavedName_FallsBackToAutoName()
    {
        var cameras = new List<CameraDeviceInfo> { Cam("id1", "FaceTime HD Camera") };
        _mockCameraEnum.Setup(s => s.GetAvailableCamerasAsync()).ReturnsAsync(cameras);
        _mockPreferences.Setup(p => p.GetCameraName(0)).Returns("   ");

        var vm = BuildVm();
        await vm.InitializeAsync();

        Assert.Equal("Camera 1 – FaceTime HD Camera", vm.CameraSlots[0].DisplayName);
    }

    [Fact]
    public async Task LoadCamerasAsync_FourCamerasDetected_CapsAtThree()
    {
        var cameras = Enumerable.Range(1, 4).Select(i => Cam($"id{i}", $"Cam {i}")).ToList();
        _mockCameraEnum.Setup(s => s.GetAvailableCamerasAsync()).ReturnsAsync(cameras);

        var vm = BuildVm();
        await vm.InitializeAsync();

        Assert.Equal(3, vm.CameraSlots.Count);
    }

    [Fact]
    public async Task LoadCamerasAsync_NoCamerasDetected_EmptySlotsNoException()
    {
        _mockCameraEnum.Setup(s => s.GetAvailableCamerasAsync()).ReturnsAsync(new List<CameraDeviceInfo>());

        var vm = BuildVm();
        var ex = await Record.ExceptionAsync(() => vm.InitializeAsync());

        Assert.Null(ex);
        Assert.Empty(vm.CameraSlots);
    }

    [Fact]
    public async Task SaveMultiCameraConfig_PersistsCameraCount()
    {
        var cameras = new List<CameraDeviceInfo> { Cam("id1", "FaceTime HD Camera") };
        _mockCameraEnum.Setup(s => s.GetAvailableCamerasAsync()).ReturnsAsync(cameras);

        _mockSecureConfig.Setup(s => s.SetApiKeyAsync(It.IsAny<string>())).Returns(Task.CompletedTask);
        _mockSecureConfig.Setup(s => s.SetHmacSecretAsync(It.IsAny<string>())).Returns(Task.CompletedTask);
        _mockNavigation.Setup(n => n.GoToAsync(It.IsAny<string>())).Returns(Task.CompletedTask);
        _mockDeviceDetection.Setup(d => d.DetectDevicesAsync()).ReturnsAsync(ScanningMethod.Camera);
        _mockDeviceDetection.Setup(d => d.GetDetectionSummary()).Returns("Camera detected.");

        var vm = BuildVm();
        vm.ServerUrl = "http://192.168.1.1:8080";
        vm.ApiKey = "test-key";
        await vm.InitializeAsync();

        await InvokeSaveAsync(vm);

        _mockPreferences.Verify(p => p.SetCameraCount(1), Times.Once);
    }

    private static async Task InvokeSaveAsync(SetupViewModel vm)
    {
        var method = typeof(SetupViewModel).GetMethod("SaveAsync",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        await (Task)method!.Invoke(vm, null)!;
    }
}
