using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace AudioTranscriber.Data;

public class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options)
    {
    }

    public DbSet<TranscriptionJob> Jobs => Set<TranscriptionJob>();

    public DbSet<TranscriptSegment> Segments => Set<TranscriptSegment>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        // SQLite does not store DateTimeKind, so mark dates as UTC when they are read back.
        // Without this the API returns times without the "Z" suffix and clients treat them as local time.
        var utcConverter = new ValueConverter<DateTime, DateTime>(
            v => v,
            v => DateTime.SpecifyKind(v, DateTimeKind.Utc));

        var nullableUtcConverter = new ValueConverter<DateTime?, DateTime?>(
            v => v,
            v => v.HasValue ? DateTime.SpecifyKind(v.Value, DateTimeKind.Utc) : v);

        modelBuilder.Entity<TranscriptionJob>(entity =>
        {
            entity.HasKey(j => j.Id);
            entity.Property(j => j.CreatedAt).HasConversion(utcConverter);
            entity.Property(j => j.StartedAt).HasConversion(nullableUtcConverter);
            entity.Property(j => j.CompletedAt).HasConversion(nullableUtcConverter);
            entity.Property(j => j.Status).HasConversion<string>().HasMaxLength(20);
            entity.Property(j => j.OriginalFileName).HasMaxLength(255);
            entity.Property(j => j.StoredFilePath).HasMaxLength(500);
            entity.HasIndex(j => j.Status);

            entity.HasMany(j => j.Segments)
                .WithOne(s => s.Job)
                .HasForeignKey(s => s.JobId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<TranscriptSegment>(entity =>
        {
            entity.HasKey(s => s.Id);
            entity.HasIndex(s => new { s.JobId, s.Index });
        });
    }
}
