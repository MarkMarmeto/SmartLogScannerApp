using SmartLog.Scanner.Core.Infrastructure;
using SmartLog.Scanner.Core.Services;

namespace SmartLog.Scanner.Tests.Services;

/// <summary>
/// Unit tests for ScanTypeMigrationService (US0089).
/// Covers: uniform per-camera values, mixed per-camera values, idempotency,
/// device-level key already set, and ConsumeUnifiedNotify behaviour.
/// </summary>
public class ScanTypeMigrationServiceTests
{
    // ── In-memory test double ──────────────────────────────────────────────────

    private sealed class InMemoryStore : IMigrationStore
    {
        private readonly Dictionary<string, object> _data = new(StringComparer.Ordinal);

        public bool ContainsKey(string key) => _data.ContainsKey(key);
        public string GetString(string key, string def) => _data.TryGetValue(key, out var v) ? (string)v : def;
        public bool GetBool(string key, bool def) => _data.TryGetValue(key, out var v) ? (bool)v : def;
        public void SetString(string key, string value) => _data[key] = value;
        public void SetBool(string key, bool value) => _data[key] = value;
        public void Remove(string key) => _data.Remove(key);

        public void SetPerCameraScanType(int index, string scanType)
            => _data[$"MultiCamera.{index}.ScanType"] = scanType;

        public bool HasPerCameraScanType(int index)
            => _data.ContainsKey($"MultiCamera.{index}.ScanType");
    }

    private static (ScanTypeMigrationService svc, InMemoryStore store) Build()
    {
        var store = new InMemoryStore();
        return (new ScanTypeMigrationService(store), store);
    }

    // ── Tests ─────────────────────────────────────────────────────────────────

    [Fact]
    public void MigrateIfNeeded_NoPriorPrefs_SetsDefaultEntry()
    {
        var (svc, store) = Build();

        svc.MigrateIfNeeded();

        // When no per-camera keys exist and no device key is set, device key is not
        // written (no source to migrate from; the real PreferencesService default of
        // "ENTRY" handles it at read time).
        Assert.False(store.ContainsKey(ConfigKeys.DefaultScanType));
        Assert.True(store.GetBool(ScanTypeMigrationService.MigrationDoneKey, false));
    }

    [Fact]
    public void MigrateIfNeeded_UniformExit_SetsExitAsDeviceKey()
    {
        var (svc, store) = Build();
        store.SetPerCameraScanType(0, "EXIT");
        store.SetPerCameraScanType(1, "EXIT");
        store.SetPerCameraScanType(2, "EXIT");

        svc.MigrateIfNeeded();

        Assert.Equal("EXIT", store.GetString(ConfigKeys.DefaultScanType, "ENTRY"));
        Assert.False(store.ContainsKey(ScanTypeMigrationService.NotifyKey));
    }

    [Fact]
    public void MigrateIfNeeded_MixedValues_SetsEntryAndSetsNotify()
    {
        var (svc, store) = Build();
        store.SetPerCameraScanType(0, "ENTRY");
        store.SetPerCameraScanType(1, "EXIT");

        svc.MigrateIfNeeded();

        Assert.Equal("ENTRY", store.GetString(ConfigKeys.DefaultScanType, "EXIT"));
        Assert.True(store.GetBool(ScanTypeMigrationService.NotifyKey, false));
    }

    [Fact]
    public void MigrateIfNeeded_RemovesPerCameraKeys()
    {
        var (svc, store) = Build();
        store.SetPerCameraScanType(0, "ENTRY");
        store.SetPerCameraScanType(3, "EXIT");

        svc.MigrateIfNeeded();

        Assert.False(store.HasPerCameraScanType(0));
        Assert.False(store.HasPerCameraScanType(3));
    }

    [Fact]
    public void MigrateIfNeeded_Idempotent_DoesNotOverwriteOnSecondCall()
    {
        var (svc, store) = Build();
        store.SetPerCameraScanType(0, "EXIT");
        store.SetPerCameraScanType(1, "EXIT");

        svc.MigrateIfNeeded();
        // Change the device key manually to simulate admin override
        store.SetString(ConfigKeys.DefaultScanType, "ENTRY");

        svc.MigrateIfNeeded();  // second call — should be a no-op

        Assert.Equal("ENTRY", store.GetString(ConfigKeys.DefaultScanType, "EXIT"));
    }

    [Fact]
    public void MigrateIfNeeded_DeviceKeyAlreadySet_DoesNotOverwrite()
    {
        var (svc, store) = Build();
        store.SetString(ConfigKeys.DefaultScanType, "EXIT");
        store.SetPerCameraScanType(0, "ENTRY");

        svc.MigrateIfNeeded();

        Assert.Equal("EXIT", store.GetString(ConfigKeys.DefaultScanType, "ENTRY"));
    }

    [Fact]
    public void ConsumeUnifiedNotify_WhenSet_ReturnsTrueAndClears()
    {
        var (svc, store) = Build();
        store.SetPerCameraScanType(0, "ENTRY");
        store.SetPerCameraScanType(1, "EXIT");
        svc.MigrateIfNeeded();

        var first = svc.ConsumeUnifiedNotify();
        var second = svc.ConsumeUnifiedNotify();

        Assert.True(first);
        Assert.False(second);
        Assert.False(store.ContainsKey(ScanTypeMigrationService.NotifyKey));
    }

    [Fact]
    public void ConsumeUnifiedNotify_WhenNotSet_ReturnsFalse()
    {
        var (svc, _) = Build();

        Assert.False(svc.ConsumeUnifiedNotify());
    }
}
