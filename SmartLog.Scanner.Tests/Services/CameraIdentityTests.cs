using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using SmartLog.Scanner.Core.Data;
using SmartLog.Scanner.Core.Models;
using SmartLog.Scanner.Core.Services;

namespace SmartLog.Scanner.Tests.Services;

/// <summary>
/// PL0013/US0090 (AC3, AC6): Camera identity propagation through scan pipeline.
/// AC3: ScanCompleted carries CameraIndex and CameraName.
/// AC6: OfflineQueueService persists CameraIndex and CameraName.
/// </summary>
public class CameraIdentityTests : IDisposable
{
    // ── In-memory SQLite (shared connection so all contexts see the same data) ──

    private readonly SqliteConnection _connection;
    private readonly DbContextOptions<ScannerDbContext> _dbOptions;

    public CameraIdentityTests()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();
        _dbOptions = new DbContextOptionsBuilder<ScannerDbContext>()
            .UseSqlite(_connection)
            .Options;

        using var ctx = new ScannerDbContext(_dbOptions);
        ctx.Database.EnsureCreated();
    }

    public void Dispose()
    {
        _connection.Close();
        _connection.Dispose();
    }

    private IDbContextFactory<ScannerDbContext> Factory() => new TestDbContextFactory(_dbOptions);

    private sealed class TestDbContextFactory(DbContextOptions<ScannerDbContext> options)
        : IDbContextFactory<ScannerDbContext>
    {
        public ScannerDbContext CreateDbContext() => new(options);
        public Task<ScannerDbContext> CreateDbContextAsync(CancellationToken ct = default)
            => Task.FromResult(new ScannerDbContext(options));
    }

    // ── AC3: CameraQrScannerService propagates camera fields ──────────────────

    private readonly Mock<IHmacValidator> _hmacMock = new();
    private readonly Mock<IScanApiService> _scanApiMock = new();
    private readonly Mock<IHealthCheckService> _healthMock = new();
    private readonly Mock<IOfflineQueueService> _offlineMock = new();
    private readonly Mock<IPreferencesService> _prefsMock = new();
    private readonly Mock<IScanDeduplicationService> _dedupMock = new();
    private readonly Mock<ITimeService> _timeMock = new();

    private CameraQrScannerService BuildCameraService()
    {
        _prefsMock.Setup(p => p.GetDefaultScanType()).Returns("ENTRY");
        _timeMock.Setup(t => t.UtcNow).Returns(DateTimeOffset.UtcNow);
        _dedupMock
            .Setup(d => d.CheckAndRecord(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string?>()))
            .Returns(new DeduplicationResult(DeduplicationAction.Proceed, TimeSpan.Zero, null));
        _scanApiMock
            .Setup(s => s.SubmitScanAsync(
                It.IsAny<string>(), It.IsAny<DateTimeOffset>(), It.IsAny<string>(),
                It.IsAny<int?>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ScanResult { Status = ScanStatus.Accepted });

        return new CameraQrScannerService(
            _hmacMock.Object,
            _scanApiMock.Object,
            _healthMock.Object,
            _offlineMock.Object,
            _prefsMock.Object,
            _dedupMock.Object,
            _timeMock.Object,
            NullLogger<CameraQrScannerService>.Instance);
    }

    [Fact]
    public async Task ScanCompleted_IncludesCameraIndex_WhenSet()
    {
        var service = BuildCameraService();
        service.SetCameraIndex(2);
        service.SetCameraName("Side Gate");
        await service.StartAsync();

        _hmacMock.Setup(h => h.ValidateAsync(It.IsAny<string>()))
            .ReturnsAsync(HmacValidationResult.Success("STU001", "ts"));

        ScanResult? captured = null;
        service.ScanCompleted += (_, r) => captured = r;

        await service.ProcessQrCodeAsync("SMARTLOG:STU001:ts:hmac");

        Assert.NotNull(captured);
        Assert.Equal(2, captured.CameraIndex);
        Assert.Equal("Side Gate", captured.CameraName);
    }

    [Fact]
    public async Task ScanCompleted_CameraFieldsAreNull_WhenNotSet()
    {
        var service = BuildCameraService();
        await service.StartAsync();

        _hmacMock.Setup(h => h.ValidateAsync(It.IsAny<string>()))
            .ReturnsAsync(HmacValidationResult.Success("STU002", "ts"));

        ScanResult? captured = null;
        service.ScanCompleted += (_, r) => captured = r;

        await service.ProcessQrCodeAsync("SMARTLOG:STU002:ts:hmac");

        Assert.NotNull(captured);
        Assert.Null(captured.CameraIndex);
        Assert.Null(captured.CameraName);
    }

    [Fact]
    public async Task ScanCompleted_ReflectsUpdatedCameraName_AfterSetCameraName()
    {
        var service = BuildCameraService();
        service.SetCameraIndex(0);
        service.SetCameraName("Old Name");
        await service.StartAsync();

        service.SetCameraName("New Name");

        _hmacMock.Setup(h => h.ValidateAsync(It.IsAny<string>()))
            .ReturnsAsync(HmacValidationResult.Success("STU003", "ts"));

        ScanResult? captured = null;
        service.ScanCompleted += (_, r) => captured = r;

        await service.ProcessQrCodeAsync("SMARTLOG:STU003:ts:hmac");

        Assert.Equal("New Name", captured?.CameraName);
    }

    // ── AC6: OfflineQueueService persists camera fields ───────────────────────

    [Fact]
    public async Task EnqueueScanAsync_PersistsCameraIndex_AndCameraName()
    {
        var service = new OfflineQueueService(Factory(), NullLogger<OfflineQueueService>.Instance);

        await service.EnqueueScanAsync(
            "SMARTLOG:S001:ts:hmac",
            DateTimeOffset.UtcNow,
            "ENTRY",
            cameraIndex: 1,
            cameraName: "Gate B");

        var pending = await service.GetPendingScansAsync();
        Assert.Single(pending);
        Assert.Equal(1, pending[0].CameraIndex);
        Assert.Equal("Gate B", pending[0].CameraName);
    }

    [Fact]
    public async Task EnqueueScanAsync_AllowsNullCameraFields_ForUsbScans()
    {
        var service = new OfflineQueueService(Factory(), NullLogger<OfflineQueueService>.Instance);

        await service.EnqueueScanAsync("SMARTLOG:S002:ts:hmac", DateTimeOffset.UtcNow, "EXIT");

        var pending = await service.GetPendingScansAsync();
        Assert.Single(pending);
        Assert.Null(pending[0].CameraIndex);
        Assert.Null(pending[0].CameraName);
    }

    [Fact]
    public async Task EnqueueScanAsync_PreservesDistinctCameraFieldsPerEntry()
    {
        var service = new OfflineQueueService(Factory(), NullLogger<OfflineQueueService>.Instance);

        await service.EnqueueScanAsync("SMARTLOG:S003:ts1:hmac", DateTimeOffset.UtcNow, "ENTRY", 0, "Front Door");
        await service.EnqueueScanAsync("SMARTLOG:S004:ts2:hmac", DateTimeOffset.UtcNow, "ENTRY", 2, "Side Exit");

        var pending = await service.GetPendingScansAsync();
        Assert.Equal(2, pending.Count);

        var front = pending.Single(p => p.StudentId == "S003");
        var side = pending.Single(p => p.StudentId == "S004");

        Assert.Equal(0, front.CameraIndex);
        Assert.Equal("Front Door", front.CameraName);
        Assert.Equal(2, side.CameraIndex);
        Assert.Equal("Side Exit", side.CameraName);
    }

    // ── ScanLogEntry.CameraDisplay computed property ─────────────────────────

    [Theory]
    [InlineData(0, "Gate A", "Camera 1 — Gate A")]
    [InlineData(1, "Side Exit", "Camera 2 — Side Exit")]
    [InlineData(0, null, "Camera 1")]
    [InlineData(0, "", "Camera 1")]
    public void CameraDisplay_FormatsCorrectly(int index, string? name, string expected)
    {
        var entry = new ScanLogEntry { CameraIndex = index, CameraName = name };
        Assert.Equal(expected, entry.CameraDisplay);
    }

    [Fact]
    public void CameraDisplay_IsNull_WhenCameraIndexIsNull()
    {
        var entry = new ScanLogEntry { CameraIndex = null, CameraName = "Ignored" };
        Assert.Null(entry.CameraDisplay);
    }
}
