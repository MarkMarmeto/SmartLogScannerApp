using Moq;
using SmartLog.Scanner.Core.Services;
using SmartLog.Scanner.Core.ViewModels;
using SmartLog.Scanner.Core.Models;
using Microsoft.Extensions.Logging;
using Xunit;

namespace SmartLog.Scanner.Tests.ViewModels;

/// <summary>
/// US0004: Unit tests for SetupViewModel.
/// Tests validation rules, save orchestration, and error handling.
/// </summary>
public class SetupViewModelTests
{
	private readonly Mock<ISecureConfigService> _mockSecureConfig;
	private readonly Mock<IPreferencesService> _mockPreferences;
	private readonly Mock<INavigationService> _mockNavigation;
	private readonly Mock<IConnectionTestService> _mockConnectionTest;
	private readonly Mock<IDeviceDetectionService> _mockDeviceDetection;
	private readonly Mock<ILogger<SetupViewModel>> _mockLogger;
	private readonly SetupViewModel _viewModel;

	public SetupViewModelTests()
	{
		_mockSecureConfig = new Mock<ISecureConfigService>();
		_mockPreferences = new Mock<IPreferencesService>();
		_mockNavigation = new Mock<INavigationService>();
		_mockConnectionTest = new Mock<IConnectionTestService>();
		_mockDeviceDetection = new Mock<IDeviceDetectionService>();
		_mockLogger = new Mock<ILogger<SetupViewModel>>();
		_viewModel = new SetupViewModel(
			_mockSecureConfig.Object,
			_mockPreferences.Object,
			_mockNavigation.Object,
			_mockConnectionTest.Object,
			_mockDeviceDetection.Object,
			_mockLogger.Object);
	}

	#region Validation Tests

	[Fact]
	public void ValidateAll_EmptyServerUrl_SetsServerUrlError()
	{
		// Arrange
		_viewModel.ServerUrl = string.Empty;
		_viewModel.ApiKey = "test-key";
		_viewModel.HmacSecret = "test-secret";

		// Act
		var result = InvokeValidateAll();

		// Assert
		Assert.False(result);
		Assert.Equal("This field is required", _viewModel.ServerUrlError);
	}

	[Fact]
	public void ValidateAll_InvalidServerUrl_MissingScheme_SetsServerUrlError()
	{
		// Arrange
		_viewModel.ServerUrl = "192.168.1.100:8443";
		_viewModel.ApiKey = "test-key";
		_viewModel.HmacSecret = "test-secret";

		// Act
		var result = InvokeValidateAll();

		// Assert
		Assert.False(result);
		Assert.Contains("valid URL", _viewModel.ServerUrlError);
	}

	[Fact]
	public void ValidateAll_InvalidServerUrl_NotAUrl_SetsServerUrlError()
	{
		// Arrange
		_viewModel.ServerUrl = "not-a-url";
		_viewModel.ApiKey = "test-key";
		_viewModel.HmacSecret = "test-secret";

		// Act
		var result = InvokeValidateAll();

		// Assert
		Assert.False(result);
		Assert.Contains("valid URL", _viewModel.ServerUrlError);
	}

	[Fact]
	public void ValidateAll_ValidHttpsUrl_ClearsServerUrlError()
	{
		// Arrange
		_viewModel.ServerUrl = "https://192.168.1.100:8443";
		_viewModel.ApiKey = "test-key";
		_viewModel.HmacSecret = "test-secret";

		// Act
		var result = InvokeValidateAll();

		// Assert
		Assert.True(result);
		Assert.Null(_viewModel.ServerUrlError);
	}

	[Fact]
	public void ValidateAll_ValidHttpUrl_ClearsServerUrlError()
	{
		// Arrange
		_viewModel.ServerUrl = "http://192.168.1.100:8080";
		_viewModel.ApiKey = "test-key";
		_viewModel.HmacSecret = "test-secret";

		// Act
		var result = InvokeValidateAll();

		// Assert
		Assert.True(result);
		Assert.Null(_viewModel.ServerUrlError);
	}

	[Fact]
	public void ValidateAll_EmptyApiKey_SetsApiKeyError()
	{
		// Arrange
		_viewModel.ServerUrl = "https://192.168.1.100:8443";
		_viewModel.ApiKey = string.Empty;
		_viewModel.HmacSecret = "test-secret";

		// Act
		var result = InvokeValidateAll();

		// Assert
		Assert.False(result);
		Assert.Equal("This field is required", _viewModel.ApiKeyError);
	}

	[Fact]
	public void ValidateAll_EmptyHmacSecret_PassesValidation()
	{
		// Arrange - HMAC Secret is optional in v1.0
		_viewModel.ServerUrl = "https://192.168.1.100:8443";
		_viewModel.ApiKey = "test-key";
		_viewModel.HmacSecret = string.Empty;

		// Act
		var result = InvokeValidateAll();

		// Assert - Should pass validation since HMAC is optional
		Assert.True(result);
		Assert.Null(_viewModel.HmacSecretError);
	}

	[Fact]
	public void ValidateAll_AllFieldsValid_ReturnsTrue_ClearsAllErrors()
	{
		// Arrange
		_viewModel.ServerUrl = "https://192.168.1.100:8443";
		_viewModel.ApiKey = "test-key";
		_viewModel.HmacSecret = "test-secret";

		// Act
		var result = InvokeValidateAll();

		// Assert
		Assert.True(result);
		Assert.Null(_viewModel.ServerUrlError);
		Assert.Null(_viewModel.ApiKeyError);
		Assert.Null(_viewModel.HmacSecretError);
	}

	[Fact]
	public void ValidateAll_AnyFieldInvalid_ReturnsFalse()
	{
		// Arrange
		_viewModel.ServerUrl = "https://192.168.1.100:8443";
		_viewModel.ApiKey = string.Empty; // Invalid
		_viewModel.HmacSecret = "test-secret";

		// Act
		var result = InvokeValidateAll();

		// Assert
		Assert.False(result);
	}

	#endregion

	#region Save Logic Tests

	[Fact]
	public async Task SaveAsync_InvalidInputs_DoesNotCallServices_StaysOnPage()
	{
		// Arrange
		_viewModel.ServerUrl = string.Empty; // Invalid
		_viewModel.ApiKey = "test-key";
		_viewModel.HmacSecret = "test-secret";

		// Act
		await _viewModel.SaveCommand.ExecuteAsync(null);

		// Assert
		_mockSecureConfig.Verify(s => s.SetApiKeyAsync(It.IsAny<string>()), Times.Never);
		_mockSecureConfig.Verify(s => s.SetHmacSecretAsync(It.IsAny<string>()), Times.Never);
		_mockPreferences.Verify(p => p.SetServerBaseUrl(It.IsAny<string>()), Times.Never);
		_mockNavigation.Verify(n => n.GoToAsync(It.IsAny<string>()), Times.Never);
	}

	[Fact]
	public async Task SaveAsync_ValidInputs_CallsAllServices_InCorrectOrder()
	{
		// Arrange
		_viewModel.ServerUrl = "https://192.168.1.100:8443";
		_viewModel.ApiKey = "test-key";
		_viewModel.HmacSecret = "test-secret";
		_viewModel.SelectedScanType = "ENTRY";

		var callOrder = new List<string>();
		_mockSecureConfig.Setup(s => s.SetApiKeyAsync(It.IsAny<string>()))
			.Callback(() => callOrder.Add("SetApiKey"))
			.Returns(Task.CompletedTask);
		_mockSecureConfig.Setup(s => s.SetHmacSecretAsync(It.IsAny<string>()))
			.Callback(() => callOrder.Add("SetHmacSecret"))
			.Returns(Task.CompletedTask);
		_mockPreferences.Setup(p => p.SetServerBaseUrl(It.IsAny<string>()))
			.Callback(() => callOrder.Add("SetServerBaseUrl"));
		_mockPreferences.Setup(p => p.SetSetupCompleted(It.IsAny<bool>()))
			.Callback(() => callOrder.Add("SetSetupCompleted"));

		// Act
		await _viewModel.SaveCommand.ExecuteAsync(null);

		// Assert - SecureStorage first, then preferences, then navigation
		Assert.Contains("SetApiKey", callOrder);
		Assert.Contains("SetHmacSecret", callOrder);
		Assert.Contains("SetServerBaseUrl", callOrder);
		Assert.Contains("SetSetupCompleted", callOrder);
		Assert.True(callOrder.IndexOf("SetApiKey") < callOrder.IndexOf("SetServerBaseUrl"));
		_mockNavigation.Verify(n => n.GoToAsync("//main"), Times.Once);
	}

	[Fact]
	public async Task SaveAsync_ValidInputs_SetsSetupCompletedTrue()
	{
		// Arrange
		_viewModel.ServerUrl = "https://192.168.1.100:8443";
		_viewModel.ApiKey = "test-key";
		_viewModel.HmacSecret = "test-secret";

		// Act
		await _viewModel.SaveCommand.ExecuteAsync(null);

		// Assert - SetupCompleted is saved via Preferences
		_mockPreferences.Verify(p => p.SetSetupCompleted(true), Times.Once);
	}

	[Fact]
	public async Task SaveAsync_ValidInputs_NavigatesToMainPage()
	{
		// Arrange
		_viewModel.ServerUrl = "https://192.168.1.100:8443";
		_viewModel.ApiKey = "test-key";
		_viewModel.HmacSecret = "test-secret";

		// Act
		await _viewModel.SaveCommand.ExecuteAsync(null);

		// Assert
		_mockNavigation.Verify(n => n.GoToAsync("//main"), Times.Once);
	}

	[Fact]
	public async Task SaveAsync_ServiceThrowsException_ShowsErrorBanner_DoesNotSetSetupCompleted()
	{
		// Arrange
		_viewModel.ServerUrl = "https://192.168.1.100:8443";
		_viewModel.ApiKey = "test-key";
		_viewModel.HmacSecret = "test-secret";

		_mockSecureConfig.Setup(s => s.SetApiKeyAsync(It.IsAny<string>()))
			.ThrowsAsync(new Exception("SecureStorage failed"));

		// Act
		await _viewModel.SaveCommand.ExecuteAsync(null);

		// Assert
		Assert.NotNull(_viewModel.SaveError);
		Assert.Contains("SecureStorage", _viewModel.SaveError);
		_mockPreferences.Verify(p => p.SetSetupCompleted(It.IsAny<bool>()), Times.Never);
		_mockNavigation.Verify(n => n.GoToAsync(It.IsAny<string>()), Times.Never);
	}

	[Fact]
	public async Task SaveAsync_ServiceThrowsException_PreservesEnteredValues()
	{
		// Arrange
		const string originalUrl = "https://192.168.1.100:8443";
		const string originalKey = "test-key";
		const string originalSecret = "test-secret";

		_viewModel.ServerUrl = originalUrl;
		_viewModel.ApiKey = originalKey;
		_viewModel.HmacSecret = originalSecret;

		_mockSecureConfig.Setup(s => s.SetApiKeyAsync(It.IsAny<string>()))
			.ThrowsAsync(new Exception("SecureStorage failed"));

		// Act
		await _viewModel.SaveCommand.ExecuteAsync(null);

		// Assert
		Assert.Equal(originalUrl, _viewModel.ServerUrl);
		Assert.Equal(originalKey, _viewModel.ApiKey);
		Assert.Equal(originalSecret, _viewModel.HmacSecret);
	}

	[Fact]
	public async Task SaveAsync_SetsIsSavingTrue_DuringSave_ThenFalse()
	{
		// Arrange
		_viewModel.ServerUrl = "https://192.168.1.100:8443";
		_viewModel.ApiKey = "test-key";
		_viewModel.HmacSecret = "test-secret";

		bool isSavingDuringSave = false;
		_mockSecureConfig.Setup(s => s.SetApiKeyAsync(It.IsAny<string>()))
			.Callback(() => isSavingDuringSave = _viewModel.IsSaving)
			.Returns(Task.CompletedTask);

		// Act
		Assert.False(_viewModel.IsSaving); // Before
		await _viewModel.SaveCommand.ExecuteAsync(null);

		// Assert
		Assert.True(isSavingDuringSave); // During
		Assert.False(_viewModel.IsSaving); // After
	}

	[Fact]
	public async Task SaveAsync_EmptyHmacSecret_SkipsHmacStorage()
	{
		// Arrange - HMAC Secret is optional in v1.0
		_viewModel.ServerUrl = "https://192.168.1.100:8443";
		_viewModel.ApiKey = "test-key";
		_viewModel.HmacSecret = string.Empty; // Not provided
		_viewModel.SelectedScanType = "ENTRY";

		// Act
		await _viewModel.SaveCommand.ExecuteAsync(null);

		// Assert - Should NOT call SetHmacSecretAsync when empty
		_mockSecureConfig.Verify(s => s.SetApiKeyAsync("test-key"), Times.Once);
		_mockSecureConfig.Verify(s => s.SetHmacSecretAsync(It.IsAny<string>()), Times.Never);
		_mockPreferences.Verify(p => p.SetSetupCompleted(true), Times.Once);
		_mockNavigation.Verify(n => n.GoToAsync("//main"), Times.Once);
	}

	#endregion

	#region Test Connection Tests

	[Fact]
	public async Task TestConnectionAsync_SuccessResult_SetsIsConnectionValidTrue()
	{
		// Arrange
		_viewModel.ServerUrl = "https://192.168.1.100:8443";
		_viewModel.ApiKey = "test-key";

		_mockConnectionTest
			.Setup(s => s.TestConnectionAsync(It.IsAny<string>(), It.IsAny<string>()))
			.ReturnsAsync(new ConnectionTestResultDto(
				ConnectionTestResult.Success,
				"Connection successful"));

		// Act
		await _viewModel.TestConnectionCommand.ExecuteAsync(null);

		// Assert
		Assert.True(_viewModel.IsConnectionValid);
	}

	[Fact]
	public async Task TestConnectionAsync_SuccessResult_DisplaysSuccessMessage()
	{
		// Arrange
		_viewModel.ServerUrl = "https://192.168.1.100:8443";
		_viewModel.ApiKey = "test-key";

		_mockConnectionTest
			.Setup(s => s.TestConnectionAsync(It.IsAny<string>(), It.IsAny<string>()))
			.ReturnsAsync(new ConnectionTestResultDto(
				ConnectionTestResult.Success,
				"Connection successful"));

		// Act
		await _viewModel.TestConnectionCommand.ExecuteAsync(null);

		// Assert
		Assert.Equal(ConnectionTestResult.Success, _viewModel.TestResult);
		Assert.Equal("Connection successful", _viewModel.TestResultMessage);
	}

	[Fact]
	public async Task TestConnectionAsync_AuthErrorResult_SetsIsConnectionValidFalse()
	{
		// Arrange
		_viewModel.ServerUrl = "https://192.168.1.100:8443";
		_viewModel.ApiKey = "bad-key";

		_mockConnectionTest
			.Setup(s => s.TestConnectionAsync(It.IsAny<string>(), It.IsAny<string>()))
			.ReturnsAsync(new ConnectionTestResultDto(
				ConnectionTestResult.AuthError,
				"Invalid API key"));

		// Act
		await _viewModel.TestConnectionCommand.ExecuteAsync(null);

		// Assert
		Assert.False(_viewModel.IsConnectionValid);
	}

	[Fact]
	public async Task TestConnectionAsync_AuthErrorResult_DisplaysAuthErrorMessage()
	{
		// Arrange
		_viewModel.ServerUrl = "https://192.168.1.100:8443";
		_viewModel.ApiKey = "bad-key";

		_mockConnectionTest
			.Setup(s => s.TestConnectionAsync(It.IsAny<string>(), It.IsAny<string>()))
			.ReturnsAsync(new ConnectionTestResultDto(
				ConnectionTestResult.AuthError,
				"Invalid API key"));

		// Act
		await _viewModel.TestConnectionCommand.ExecuteAsync(null);

		// Assert
		Assert.Equal(ConnectionTestResult.AuthError, _viewModel.TestResult);
		Assert.Contains("Invalid API key", _viewModel.TestResultMessage);
	}

	[Fact]
	public async Task TestConnectionAsync_SetsIsTestingConnectionTrue_DuringTest_ThenFalse()
	{
		// Arrange
		_viewModel.ServerUrl = "https://192.168.1.100:8443";
		_viewModel.ApiKey = "test-key";

		bool isTestingDuringTest = false;
		_mockConnectionTest
			.Setup(s => s.TestConnectionAsync(It.IsAny<string>(), It.IsAny<string>()))
			.Callback(() => isTestingDuringTest = _viewModel.IsTestingConnection)
			.ReturnsAsync(new ConnectionTestResultDto(
				ConnectionTestResult.Success,
				"Connection successful"));

		// Act
		Assert.False(_viewModel.IsTestingConnection); // Before
		await _viewModel.TestConnectionCommand.ExecuteAsync(null);

		// Assert
		Assert.True(isTestingDuringTest); // During
		Assert.False(_viewModel.IsTestingConnection); // After
	}

	[Fact]
	public void TestConnectionCommand_EmptyUrl_CannotExecute()
	{
		// Arrange
		_viewModel.ServerUrl = string.Empty;
		_viewModel.ApiKey = "test-key";

		// Act & Assert
		Assert.False(_viewModel.TestConnectionCommand.CanExecute(null));
	}

	[Fact]
	public void TestConnectionCommand_EmptyApiKey_CannotExecute()
	{
		// Arrange
		_viewModel.ServerUrl = "https://192.168.1.100:8443";
		_viewModel.ApiKey = string.Empty;

		// Act & Assert
		Assert.False(_viewModel.TestConnectionCommand.CanExecute(null));
	}

	[Fact]
	public void OnServerUrlChanged_ClearsTestResult()
	{
		// Arrange
		_viewModel.ServerUrl = "https://192.168.1.100:8443";
		_viewModel.ApiKey = "test-key";
		_viewModel.TestResult = ConnectionTestResult.Success;
		_viewModel.TestResultMessage = "Connection successful";
		_viewModel.IsConnectionValid = true;

		// Act
		_viewModel.ServerUrl = "https://192.168.1.101:8443"; // Change URL

		// Assert
		Assert.Equal(ConnectionTestResult.None, _viewModel.TestResult);
		Assert.Null(_viewModel.TestResultMessage);
		Assert.False(_viewModel.IsConnectionValid);
	}

	[Fact]
	public void OnApiKeyChanged_ClearsTestResult()
	{
		// Arrange
		_viewModel.ServerUrl = "https://192.168.1.100:8443";
		_viewModel.ApiKey = "test-key";
		_viewModel.TestResult = ConnectionTestResult.Success;
		_viewModel.TestResultMessage = "Connection successful";
		_viewModel.IsConnectionValid = true;

		// Act
		_viewModel.ApiKey = "new-key"; // Change API key

		// Assert
		Assert.Equal(ConnectionTestResult.None, _viewModel.TestResult);
		Assert.Null(_viewModel.TestResultMessage);
		Assert.False(_viewModel.IsConnectionValid);
	}

	#endregion

	#region Helper Methods

	/// <summary>
	/// Use reflection to invoke private ValidateAll method for testing.
	/// </summary>
	private bool InvokeValidateAll()
	{
		var method = typeof(SetupViewModel).GetMethod("ValidateAll",
			System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
		return (bool)method!.Invoke(_viewModel, null)!;
	}

	#endregion
}
