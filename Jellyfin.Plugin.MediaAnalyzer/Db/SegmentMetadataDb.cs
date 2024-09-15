using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Jellyfin.Data.Enums;
using Jellyfin.Extensions;
using Microsoft.EntityFrameworkCore;

namespace Jellyfin.Plugin.MediaAnalyzer;

/// <summary>
/// Database api for segment metadata.
/// </summary>
public class SegmentMetadataDb
{
    private string _pluginDbPath;

    /// <summary>
    /// Initializes a new instance of the <see cref="SegmentMetadataDb"/> class.
    /// </summary>
    /// <param name="pluginDbPath">Plugin db path.</param>
    public SegmentMetadataDb(string pluginDbPath)
    {
        _pluginDbPath = pluginDbPath;
    }

    /// <summary>
    /// Create or update a segment.
    /// </summary>
    /// <param name="seg">Segment.</param>
    public async void SaveSegment(SegmentMetadata seg)
    {
        using var db = GetPluginDb();
        await CreateOrUpdate(db, seg).ConfigureAwait(false);
        await db.SaveChangesAsync().ConfigureAwait(false);
    }

    /// <summary>
    /// Create prevent analyze segments from QueuedMedia.
    /// </summary>
    /// <param name="media">Queued Media.</param>
    /// <param name="mode">Mode.</param>
    /// <returns>A <see cref="Task"/> representing the asynchronous operation.</returns>
    public async Task CreatePreventAnalyzeSegments(IReadOnlyCollection<QueuedMedia> media, MediaSegmentType mode)
    {
        using var db = GetPluginDb();

        foreach (var seg in media)
        {
            var newseg = new SegmentMetadata
            {
                Name = seg.GetFullName(),
                Type = mode,
                PreventAnalyzing = true,
                ItemId = seg.ItemId,
            };

            await CreateOrUpdate(db, newseg).ConfigureAwait(false);
        }

        await db.SaveChangesAsync().ConfigureAwait(false);
    }

    /// <summary>
    /// Get metadata for segmentId.
    /// </summary>
    /// <param name="segmentId">Media ItemId.</param>
    /// <returns>A <see cref="Task"/> representing the asynchronous operation.</returns>
    public async Task<SegmentMetadata> GetSegment(Guid segmentId)
    {
        using var db = GetPluginDb();
        return await db.SegmentMetadata.AsNoTracking().FirstAsync(s => s.SegmentId == segmentId).ConfigureAwait(false);
    }

    /// <summary>
    /// Get metadata for itemId and type. We also store metadata for media that have no segmentId in jellyfin.
    /// </summary>
    /// <param name="itemId">Media ItemId.</param>
    /// <param name="type">Segment Type.</param>
    /// <param name="analyzer">Optional: type of ananlyzer.</param>
    /// <returns>A <see cref="Task"/> representing the asynchronous operation.</returns>
    public async Task<IEnumerable<SegmentMetadata>> GetSegments(Guid itemId, MediaSegmentType type, AnalyzerType? analyzer)
    {
        using var db = GetPluginDb();
        var query = db.SegmentMetadata.Where(s => s.ItemId == itemId && s.Type == type);
        if (analyzer is not null)
        {
            query = query.Where(s => s.AnalyzerType == analyzer);
        }

        return await query.AsNoTracking().ToListAsync().ConfigureAwait(false);
    }

    /// <summary>
    /// Check if itemId and type should be prevented to analyze. AnalyzerType of these segments is NotSet.
    /// </summary>
    /// <param name="itemId">Media ItemId.</param>
    /// <param name="type">Segment Type.</param>
    /// <returns>A <see cref="Task"/> representing the asynchronous operation.</returns>
    public async Task<bool> PreventAnalyze(Guid itemId, MediaSegmentType type)
    {
        using var db = GetPluginDb();
        var seg = await db.SegmentMetadata.FirstAsync(s => s.ItemId == itemId && s.Type == type && s.AnalyzerType == AnalyzerType.NotSet).ConfigureAwait(false);
        // we may have multiple metadata for the same type+itemId. Search in all of them
        return seg is not null && seg.PreventAnalyzing;
    }

    /// <summary>
    /// Delete all metadata for itemId with prevent analyze set to true. Without itemId deletes them all.
    /// </summary>
    /// <param name="itemId">Media ItemId.</param>
    /// <returns>A <see cref="Task"/> representing the asynchronous operation.</returns>
    public async Task DeletePreventAnalyzeSegments(Guid? itemId)
    {
        using var db = GetPluginDb();
        var query = db.SegmentMetadata.Where(s => s.PreventAnalyzing);

        if (!itemId.IsNullOrEmpty())
        {
            query = db.SegmentMetadata.Where(s => s.ItemId == itemId);
        }

        await query.ExecuteDeleteAsync().ConfigureAwait(false);
    }

    /// <summary>
    /// Delete all metadata for itemId and optional type.
    /// </summary>
    /// <param name="itemId">Media ItemId.</param>
    /// <param name="type">Segment Type.</param>
    /// <returns>A <see cref="Task"/> representing the asynchronous operation.</returns>
    public async Task DeleteSegments(Guid itemId, MediaSegmentType? type)
    {
        using var db = GetPluginDb();
        var query = db.SegmentMetadata.Where(s => s.ItemId == itemId);

        if (type is not null)
        {
            query = query.Where(s => s.Type == type);
        }

        await query.ExecuteDeleteAsync().ConfigureAwait(false);
    }

    /// <summary>
    /// Create or update a segment.
    /// </summary>
    /// <param name="db">Database.</param>
    /// <param name="seg">SegmentMetadata.</param>
    /// <returns>Task.</returns>
    private async Task CreateOrUpdate(MediaAnalyzerDbContext db, SegmentMetadata seg)
    {
        var found = await db.SegmentMetadata.FirstAsync(s => s.Id.Equals(seg.Id)).ConfigureAwait(false);

        if (found is not null)
        {
            found.Name = seg.Name;
            found.SegmentId = seg.SegmentId;
            found.Type = seg.Type;
            found.ItemId = seg.ItemId;
            found.PreventAnalyzing = seg.PreventAnalyzing;
            found.AnalyzerType = seg.AnalyzerType;
            found.AnalyzerNote = seg.AnalyzerNote;
        }
        else
        {
            db.SegmentMetadata.Add(seg);
        }
    }

    /// <summary>
    /// Get context of plugin database.
    /// </summary>
    /// <returns>Instance of db.</returns>
    public MediaAnalyzerDbContext GetPluginDb()
    {
        return new MediaAnalyzerDbContext(_pluginDbPath);
    }
}
