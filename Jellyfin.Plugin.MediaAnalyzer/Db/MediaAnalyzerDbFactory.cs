using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Jellyfin.Plugin.MediaAnalyzer;

/// <summary>
/// Plugin database factory.
/// </summary>
public class MediaAnalyzerDbFactory : IDesignTimeDbContextFactory<MediaAnalyzerDbContext>
{
    /// <inheritdoc/>
    public MediaAnalyzerDbContext CreateDbContext(string[] args)
    {
        var optionsBuilder = new DbContextOptionsBuilder<MediaAnalyzerDbContext>();
        optionsBuilder.UseSqlite("Data Source=jfpmediaanalyzer.db");

        return new MediaAnalyzerDbContext(optionsBuilder.Options);
    }
}
