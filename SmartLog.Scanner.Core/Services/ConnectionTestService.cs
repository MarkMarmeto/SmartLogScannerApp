using System.Net;
using System.Net.Sockets;
using System.Security.Authentication;
using Microsoft.Extensions.Logging;
using SmartLog.Scanner.Core.Models;

namespace SmartLog.Scanner.Core.Services;

/// <summary>
/// US0005: Service for testing connectivity to SmartLog API server.
/// Sends GET /api/v1/health/details with X-API-Key header to validate server URL and API key.
/// </summary>
public class ConnectionTestService : IConnectionTestService
{
	private readonly IHttpClientFactory _httpClientFactory;
	private readonly ILogger<ConnectionTestService> _logger;

	public ConnectionTestService(
		IHttpClientFactory httpClientFactory,
		ILogger<ConnectionTestService> logger)
	{
		_httpClientFactory = httpClientFactory;
		_logger = logger;
	}

	public async Task<ConnectionTestResultDto> TestConnectionAsync(string serverUrl, string apiKey, bool acceptSelfSignedCerts = false)
	{
		if (string.IsNullOrWhiteSpace(serverUrl))
			throw new ArgumentException("Server URL cannot be empty", nameof(serverUrl));

		if (string.IsNullOrWhiteSpace(apiKey))
			throw new ArgumentException("API key cannot be empty", nameof(apiKey));

		// When acceptSelfSignedCerts is enabled, create a temporary HttpClient
		// that bypasses cert validation for this one-off connection test.
		// Otherwise, use the factory-configured client with standard validation.
		HttpClient client;
		HttpClientHandler? tempHandler = null;

		if (acceptSelfSignedCerts)
		{
			tempHandler = new HttpClientHandler
			{
				ServerCertificateCustomValidationCallback = (message, cert, chain, errors) => true
			};
			client = new HttpClient(tempHandler) { Timeout = TimeSpan.FromSeconds(10) };
		}
		else
		{
			client = _httpClientFactory.CreateClient("SmartLogApi");
		}

		try
		{
			var requestUri = new Uri(new Uri(serverUrl), "/api/v1/health/details");

			var request = new HttpRequestMessage(HttpMethod.Get, requestUri);
			request.Headers.Add("X-API-Key", apiKey);

			var response = await client.SendAsync(request);

			if (response.StatusCode == HttpStatusCode.OK)
			{
				return new ConnectionTestResultDto(
					ConnectionTestResult.Success,
					"Connection successful");
			}
			else if (response.StatusCode == HttpStatusCode.Unauthorized)
			{
				return new ConnectionTestResultDto(
					ConnectionTestResult.AuthError,
					"Invalid API key. Please verify your API key from the admin panel.");
			}
			else
			{
				return new ConnectionTestResultDto(
					ConnectionTestResult.UnexpectedError,
					$"Server returned {(int)response.StatusCode}. Check server logs.",
					response.StatusCode.ToString());
			}
		}
		catch (TaskCanceledException)
		{
			return new ConnectionTestResultDto(
				ConnectionTestResult.Timeout,
				"Connection timed out. Check network connectivity.");
		}
		catch (HttpRequestException ex) when (ex.InnerException is SocketException socketEx)
		{
			if (socketEx.SocketErrorCode == SocketError.ConnectionRefused)
			{
				return new ConnectionTestResultDto(
					ConnectionTestResult.ConnectionRefused,
					"Cannot reach server. Check the server URL and ensure the server is running.");
			}
			else if (socketEx.SocketErrorCode == SocketError.HostNotFound ||
			         socketEx.SocketErrorCode == SocketError.TryAgain ||
			         socketEx.SocketErrorCode == SocketError.NoData)
			{
				return new ConnectionTestResultDto(
					ConnectionTestResult.DnsFailure,
					"Server not found. Check the URL format.");
			}
			else
			{
				return new ConnectionTestResultDto(
					ConnectionTestResult.UnexpectedError,
					$"Network error: {socketEx.SocketErrorCode}",
					socketEx.Message);
			}
		}
		catch (HttpRequestException ex) when (ex.InnerException is AuthenticationException)
		{
			return new ConnectionTestResultDto(
				ConnectionTestResult.TlsError,
				"TLS certificate error. Enable self-signed certificate support.");
		}
		catch (Exception ex)
		{
			_logger.LogError(ex, "Unexpected error during connection test");
			return new ConnectionTestResultDto(
				ConnectionTestResult.UnexpectedError,
				"Unexpected error occurred. Check logs.",
				ex.Message);
		}
		finally
		{
			// Dispose temporary resources (don't dispose factory-created clients)
			if (tempHandler != null)
			{
				client.Dispose();
				tempHandler.Dispose();
			}
		}
	}
}
