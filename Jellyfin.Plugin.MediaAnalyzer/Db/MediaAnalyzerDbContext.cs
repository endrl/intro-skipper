using System;
using Microsoft.EntityFrameworkCore;

namespace Jellyfin.Plugin.MediaAnalyzer;

/// <summary>
/// Plugin database.
/// </summary>
public class MediaAnalyzerDbContext : DbContext
{
    private string dbPath;

    /// <summary>
    /// Initializes a new instance of the <see cref="MediaAnalyzerDbContext"/> class.
    /// </summary>
    /// <param name="path">Path to db.</param>
    public MediaAnalyzerDbContext(string path)
    {
        dbPath = path;
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="MediaAnalyzerDbContext"/> class.
    /// </summary>
    /// <param name="options">The options.</param>
    public MediaAnalyzerDbContext(DbContextOptions options) : base(options)
    {
        var folder = Environment.SpecialFolder.LocalApplicationData;
        var path = Environment.GetFolderPath(folder);
        dbPath = System.IO.Path.Join(path, "mediaanalyzer.db");
    }

    /// <summary>
    /// Gets the <see cref="DbSet{TEntity}"/> containing the blacklisted segments.
    /// </summary>
    public DbSet<SegmentMetadata> SegmentMetadata => Set<SegmentMetadata>();

    /// <inheritdoc/>
    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
        => optionsBuilder.UseSqlite($"Data Source={dbPath}");

    /// <inheritdoc/>
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<SegmentMetadata>()
            .HasKey(s => s.Id);

        modelBuilder.Entity<SegmentMetadata>()
            .Property(s => s.Id)
            .ValueGeneratedOnAdd();

        modelBuilder.Entity<SegmentMetadata>()
            .HasIndex(s => s.ItemId);
    }

    /// <summary>
    /// Apply migrations. Needs to be called before any actions are executed.
    /// </summary>
    public void ApplyMigrations()
    {
        this.Database.Migrate();
    }
}
