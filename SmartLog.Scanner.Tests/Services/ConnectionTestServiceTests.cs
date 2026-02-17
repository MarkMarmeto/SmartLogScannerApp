using System.Net;
using System.Net.Sockets;
using System.Security.Authentication;
using Microsoft.Extensions.Logging;
using Moq;
using Moq.Protected;
using SmartLog.Scanner.Core.Models;
using SmartLog.Scanner.Core.Services;
using Xunit;

namespace SmartLog.Scanner.Tests.Services;

/// <summary>
/// US0005: Unit tests for ConnectionTestService.
/// Tests all HTTP failure modes using mock HttpMessageHandler.
/// </summary>
public class ConnectionTestServiceTests
{
	private readonly Mock<IHttpClientFactory> _mockHttpClientFactory;
	private readonly Mock<ILogger<ConnectionTestService>> _mockLogger;
	private readonly Mock<HttpMessageHandler> _mockHttpMessageHandler;
	private readonly ConnectionTestService _service;

	public ConnectionTestServiceTests()
	{
		_mockHttpClientFactory = new Mock<IHttpClientFactory>();
		_mockLogger = new Mock<ILogger<ConnectionTestService>>();
		_mockHttpMessageHandler = new Mock<HttpMessageHandler>();

		// Setup HttpClient with mock handler
		var httpClient = new HttpClient(_mockHttpMessageHandler.Object)
		{
			BaseAddress = new Uri("https://test.local")
		};

		_mockHttpClientFactory
			.Setup(f => f.CreateClient("SmartLogApi"))
			.Returns(httpClient);

		_service = new ConnectionTestService(_mockHttpClientFactory.Object, _mockLogger.Object);
	}

	[Fact]
	public async Task TestConnectionAsync_Http200_ReturnsSuccess()
	{
		// Arrange
		_mockHttpMessageHandler
			.Protected()
			.Setup<Task<HttpResponseMessage>>(
				"SendAsync",
				ItExpr.IsAny<HttpRequestMessage>(),
				ItExpr.IsAny<CancellationToken>())
			.ReturnsAsync(new HttpResponseMessage
			{
				StatusCode = HttpStatusCode.OK,
				Content = new StringContent("{\"status\":\"healthy\"}")
			});

		// Act
		var result = await _service.TestConnectionAsync("https://192.168.1.100:8443", "test-key");

		// Assert
		Assert.Equal(ConnectionTestResult.Success, result.Status);
		Assert.Equal("Connection successful", result.Message);
	}

	[Fact]
	public async Task TestConnectionAsync_Http401_ReturnsAuthError()
	{
		// Arrange
		_mockHttpMessageHandler
			.Protected()
			.Setup<Task<HttpResponseMessage>>(
				"SendAsync",
				ItExpr.IsAny<HttpRequestMessage>(),
				ItExpr.IsAny<CancellationToken>())
			.ReturnsAsync(new HttpResponseMessage
			{
				StatusCode = HttpStatusCode.Unauthorized
			});

		// Act
		var result = await _service.TestConnectionAsync("https://192.168.1.100:8443", "bad-key");

		// Assert
		Assert.Equal(ConnectionTestResult.AuthError, result.Status);
		Assert.Contains("Invalid API key", result.Message);
	}

	[Fact]
	public async Task TestConnectionAsync_ConnectionRefused_ReturnsConnectionRefused()
	{
		// Arrange
		var socketException = new SocketException((int)SocketError.ConnectionRefused);
		var httpRequestException = new HttpRequestException("Connection refused", socketException);

		_mockHttpMessageHandler
			.Protected()
			.Setup<Task<HttpResponseMessage>>(
				"SendAsync",
				ItExpr.IsAny<HttpRequestMessage>(),
				ItExpr.IsAny<CancellationToken>())
			.ThrowsAsync(httpRequestException);

		// Act
		var result = await _service.TestConnectionAsync("https://192.168.1.100:8443", "test-key");

		// Assert
		Assert.Equal(ConnectionTestResult.ConnectionRefused, result.Status);
		Assert.Contains("Cannot reach server", result.Message);
	}

	[Fact]
	public async Task TestConnectionAsync_Timeout_ReturnsTimeout()
	{
		// Arrange
		_mockHttpMessageHandler
			.Protected()
			.Setup<Task<HttpResponseMessage>>(
				"SendAsync",
				ItExpr.IsAny<HttpRequestMessage>(),
				ItExpr.IsAny<CancellationToken>())
			.ThrowsAsync(new TaskCanceledException("Request timed out"));

		// Act
		var result = await _service.TestConnectionAsync("https://192.168.1.100:8443", "test-key");

		// Assert
		Assert.Equal(ConnectionTestResult.Timeout, result.Status);
		Assert.Contains("timed out", result.Message);
	}

	[Fact]
	public async Task TestConnectionAsync_DnsFailure_ReturnsDnsFailure()
	{
		// Arrange
		var socketException = new SocketException((int)SocketError.HostNotFound);
		var httpRequestException = new HttpRequestException("Name resolution failed", socketException);

		_mockHttpMessageHandler
			.Protected()
			.Setup<Task<HttpResponseMessage>>(
				"SendAsync",
				ItExpr.IsAny<HttpRequestMessage>(),
				ItExpr.IsAny<CancellationToken>())
			.ThrowsAsync(httpRequestException);

		// Act
		var result = await _service.TestConnectionAsync("https://nonexistent.local:8443", "test-key");

		// Assert
		Assert.Equal(ConnectionTestResult.DnsFailure, result.Status);
		Assert.Contains("Server not found", result.Message);
	}

	[Fact]
	public async Task TestConnectionAsync_TlsError_ReturnsTlsError()
	{
		// Arrange
		var authException = new AuthenticationException("TLS handshake failed");
		var httpRequestException = new HttpRequestException("TLS error", authException);

		_mockHttpMessageHandler
			.Protected()
			.Setup<Task<HttpResponseMessage>>(
				"SendAsync",
				ItExpr.IsAny<HttpRequestMessage>(),
				ItExpr.IsAny<CancellationToken>())
			.ThrowsAsync(httpRequestException);

		// Act
		var result = await _service.TestConnectionAsync("https://192.168.1.100:8443", "test-key");

		// Assert
		Assert.Equal(ConnectionTestResult.TlsError, result.Status);
		Assert.Contains("TLS certificate error", result.Message);
	}

	[Fact]
	public async Task TestConnectionAsync_Http500_ReturnsUnexpectedError()
	{
		// Arrange
		_mockHttpMessageHandler
			.Protected()
			.Setup<Task<HttpResponseMessage>>(
				"SendAsync",
				ItExpr.IsAny<HttpRequestMessage>(),
				ItExpr.IsAny<CancellationToken>())
			.ReturnsAsync(new HttpResponseMessage
			{
				StatusCode = HttpStatusCode.InternalServerError
			});

		// Act
		var result = await _service.TestConnectionAsync("https://192.168.1.100:8443", "test-key");

		// Assert
		Assert.Equal(ConnectionTestResult.UnexpectedError, result.Status);
		Assert.Contains("500", result.Message);
	}

	[Fact]
	public async Task TestConnectionAsync_InvalidUrl_ThrowsArgumentException()
	{
		// Act & Assert
		await Assert.ThrowsAsync<ArgumentException>(async () =>
			await _service.TestConnectionAsync("", "test-key"));
	}

	[Fact]
	public async Task TestConnectionAsync_EmptyApiKey_ThrowsArgumentException()
	{
		// Act & Assert
		await Assert.ThrowsAsync<ArgumentException>(async () =>
			await _service.TestConnectionAsync("https://192.168.1.100:8443", ""));
	}
}
