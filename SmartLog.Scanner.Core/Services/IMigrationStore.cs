namespace SmartLog.Scanner.Core.Services;

/// <summary>
/// Thin raw-preferences abstraction used by startup migration services.
/// Kept separate from IPreferencesService to expose ContainsKey/Remove without
/// polluting the main preferences interface.
/// </summary>
public interface IMigrationStore
{
    bool ContainsKey(string key);
    string GetString(string key, string defaultValue);
    bool GetBool(string key, bool defaultValue);
    void SetString(string key, string value);
    void SetBool(string key, bool value);
    void Remove(string key);
}
