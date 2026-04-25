using Microsoft.Maui.Storage;

namespace SmartLog.Scanner.Core.Services;

/// <summary>
/// Production implementation of IMigrationStore backed by MAUI Preferences.Default.
/// </summary>
public class MauiMigrationStore : IMigrationStore
{
    public bool ContainsKey(string key) => Preferences.Default.ContainsKey(key);
    public string GetString(string key, string defaultValue) => Preferences.Default.Get(key, defaultValue);
    public bool GetBool(string key, bool defaultValue) => Preferences.Default.Get(key, defaultValue);
    public void SetString(string key, string value) => Preferences.Default.Set(key, value);
    public void SetBool(string key, bool value) => Preferences.Default.Set(key, value);
    public void Remove(string key) => Preferences.Default.Remove(key);
}
