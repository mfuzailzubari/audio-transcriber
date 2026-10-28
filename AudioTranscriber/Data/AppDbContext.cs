using Microsoft.EntityFrameworkCore;

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
        modelBuilder.Entity<TranscriptionJob>(entity =>
        {
            entity.HasKey(j => j.Id);
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
