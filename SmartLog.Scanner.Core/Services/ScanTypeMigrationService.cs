using SmartLog.Scanner.Core.Infrastructure;

namespace SmartLog.Scanner.Core.Services;

/// <summary>
/// US0089: One-time startup migration that collapses legacy per-camera ScanType preferences
/// ("MultiCamera.{i}.ScanType") into a single device-level key ("Scanner.DefaultScanType").
///
/// Migration rules:
/// - If all cameras had the same value, that value is kept.
/// - If values were mixed, "ENTRY" is used (safe default) and a notify flag is set
///   so the UI can inform the admin on next open.
/// - Idempotent: the migration done flag prevents re-running on subsequent launches.
/// </summary>
public class ScanTypeMigrationService
{
    public const int MaxCameraSlots = 4;
    public const string MigrationDoneKey = "Migration.ScanTypeUnified.v1";
    public const string NotifyKey = "Migration.ScanTypeUnifiedNotify";

    private readonly IMigrationStore _store;

    public ScanTypeMigrationService(IMigrationStore store)
    {
        _store = store;
    }

    public void MigrateIfNeeded()
    {
        if (_store.GetBool(MigrationDoneKey, false))
            return;

        var perCameraValues = new List<string>();
        for (int i = 0; i < MaxCameraSlots; i++)
        {
            var key = $"MultiCamera.{i}.ScanType";
            if (_store.ContainsKey(key))
            {
                perCameraValues.Add(_store.GetString(key, "ENTRY"));
                _store.Remove(key);
            }
        }

        // Only write device-level key when there is something to migrate and the key is not already set
        if (perCameraValues.Count > 0 && !_store.ContainsKey(ConfigKeys.DefaultScanType))
        {
            var distinctValues = perCameraValues.Distinct().ToList();
            var unified = distinctValues.Count == 1 ? distinctValues[0] : "ENTRY";
            _store.SetString(ConfigKeys.DefaultScanType, unified);

            if (distinctValues.Count > 1)
                _store.SetBool(NotifyKey, true);
        }

        _store.SetBool(MigrationDoneKey, true);
    }

    /// <summary>
    /// Returns true when the migration found mixed per-camera values and the admin has
    /// not yet acknowledged the notification. Clears the flag on read.
    /// </summary>
    public bool ConsumeUnifiedNotify()
    {
        var notify = _store.GetBool(NotifyKey, false);
        if (notify)
            _store.Remove(NotifyKey);
        return notify;
    }
}
