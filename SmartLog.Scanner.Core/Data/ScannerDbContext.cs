using Microsoft.EntityFrameworkCore;
using SmartLog.Scanner.Core.Models;

namespace SmartLog.Scanner.Core.Data;

/// <summary>
/// US0014: EF Core DbContext for SmartLog Scanner offline queue.
/// </summary>
public class ScannerDbContext : DbContext
{
    public DbSet<QueuedScan> QueuedScans { get; set; } = null!;
    public DbSet<ScanLogEntry> ScanLogs { get; set; } = null!;

    public ScannerDbContext(DbContextOptions<ScannerDbContext> options)
        : base(options)
    {
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<QueuedScan>(entity =>
        {
            entity.ToTable("QueuedScans");

            entity.HasKey(e => e.Id);

            entity.Property(e => e.QrPayload)
                .IsRequired()
                .HasMaxLength(500);

            entity.Property(e => e.StudentId)
                .IsRequired()
                .HasMaxLength(100);

            entity.Property(e => e.ScannedAt)
                .IsRequired()
                .HasMaxLength(50);

            entity.Property(e => e.ScanType)
                .IsRequired()
                .HasMaxLength(10);

            entity.Property(e => e.CreatedAt)
                .IsRequired()
                .HasMaxLength(50);

            entity.Property(e => e.SyncStatus)
                .IsRequired()
                .HasMaxLength(20)
                .HasDefaultValue("PENDING");

            entity.Property(e => e.SyncAttempts)
                .HasDefaultValue(0);

            entity.Property(e => e.LastSyncError)
                .HasMaxLength(1000);

            entity.Property(e => e.ServerScanId)
                .HasMaxLength(100);

            entity.Property(e => e.LastAttemptAt)
                .HasMaxLength(50);

            // Index for efficient pending scan queries
            entity.HasIndex(e => e.SyncStatus);

            // Index for efficient timestamp ordering
            entity.HasIndex(e => e.CreatedAt);

            // Composite index for efficient offline queue deduplication queries
            entity.HasIndex(e => new { e.StudentId, e.ScanType, e.SyncStatus });
        });

        modelBuilder.Entity<ScanLogEntry>(entity =>
        {
            entity.ToTable("ScanLogs");

            entity.HasKey(e => e.Id);

            entity.Property(e => e.Timestamp)
                .IsRequired();

            entity.Property(e => e.RawPayload)
                .IsRequired()
                .HasMaxLength(500);

            entity.Property(e => e.StudentId)
                .HasMaxLength(100);

            entity.Property(e => e.StudentName)
                .HasMaxLength(200);

            entity.Property(e => e.ScanType)
                .IsRequired()
                .HasMaxLength(10);

            entity.Property(e => e.Status)
                .IsRequired();

            entity.Property(e => e.Message)
                .HasMaxLength(500);

            entity.Property(e => e.ScanId)
                .HasMaxLength(100);

            entity.Property(e => e.GradeSection)
                .HasMaxLength(50);

            entity.Property(e => e.ErrorDetails)
                .HasMaxLength(1000);

            entity.Property(e => e.ScanMethod)
                .HasMaxLength(20);

            // Index for efficient timestamp queries
            entity.HasIndex(e => e.Timestamp);

            // Index for efficient status queries
            entity.HasIndex(e => e.Status);

            // Index for efficient student search
            entity.HasIndex(e => e.StudentId);
        });
    }
}
