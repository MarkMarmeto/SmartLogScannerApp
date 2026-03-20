using SmartLog.Scanner.Core.Models;

namespace SmartLog.Scanner.Core.Services;

/// <summary>
/// US0005: Service for testing connectivity to SmartLog API server.
/// Validates server URL and API key by sending GET /api/v1/health/details.
/// </summary>
public interface IConnectionTestService
{
	/// <summary>
	/// Tests connection to the SmartLog API server.
	/// </summary>
	/// <param name="serverUrl">Base URL (e.g., "https://192.168.1.100:8443")</param>
	/// <param name="apiKey">API key to test (sent in X-API-Key header)</param>
	/// <param name="acceptSelfSignedCerts">Whether to accept self-signed TLS certificates during this test</param>
	/// <returns>Result with status and user-facing message</returns>
	Task<ConnectionTestResultDto> TestConnectionAsync(string serverUrl, string apiKey, bool acceptSelfSignedCerts = false);
}
