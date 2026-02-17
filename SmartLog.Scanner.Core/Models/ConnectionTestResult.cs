namespace SmartLog.Scanner.Core.Models;

/// <summary>
/// US0005: Result status of connection test to SmartLog API server.
/// Maps HTTP failure modes to user-friendly error messages.
/// </summary>
public enum ConnectionTestResult
{
	/// <summary>No test has been run yet</summary>
	None,

	/// <summary>HTTP 200: Connection successful, API key valid</summary>
	Success,

	/// <summary>HTTP 401: Invalid API key</summary>
	AuthError,

	/// <summary>SocketException: Connection refused (server not listening)</summary>
	ConnectionRefused,

	/// <summary>TaskCanceledException: Request timed out (10 seconds)</summary>
	Timeout,

	/// <summary>SocketException: DNS name resolution failure</summary>
	DnsFailure,

	/// <summary>AuthenticationException: TLS certificate error</summary>
	TlsError,

	/// <summary>Unexpected HTTP error (5xx, network error, etc.)</summary>
	UnexpectedError
}

/// <summary>
/// US0005: Result of a connection test including status and user-facing message.
/// </summary>
public record ConnectionTestResultDto(
	ConnectionTestResult Status,
	string Message,
	string? Details = null
);
