namespace SmartLog.Scanner.Core.Exceptions;

/// <summary>
/// Exception thrown when MAUI SecureStorage is unavailable or fails
/// (e.g., Keychain locked on macOS, DPAPI access denied on Windows)
/// </summary>
public class SecureStorageUnavailableException : Exception
{
    /// <summary>
    /// Platform where the failure occurred (macOS or Windows)
    /// </summary>
    public string Platform { get; }

    /// <summary>
    /// Operation that was attempted (e.g., "SetApiKey", "GetHmacSecret")
    /// </summary>
    public string Operation { get; }

    public SecureStorageUnavailableException(string platform, string operation, string message)
        : base(message)
    {
        Platform = platform;
        Operation = operation;
    }

    public SecureStorageUnavailableException(string platform, string operation, string message, Exception innerException)
        : base(message, innerException)
    {
        Platform = platform;
        Operation = operation;
    }
}
