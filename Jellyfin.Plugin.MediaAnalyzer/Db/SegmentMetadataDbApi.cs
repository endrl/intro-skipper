using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Jellyfin.Data.Enums;
using MediaBrowser.Controller.Library;
using Microsoft.EntityFrameworkCore;

namespace Jellyfin.Plugin.MediaAnalyzer;

/// <summary>
/// Database api for segment metadata.
/// </summary>
public sealed class SegmentMetadataDbApi : IDisposable
{
    private Plugin _plugin;

    private ILibraryManager _libraryManager;

    /// <summary>
    /// Initializes a new instance of the <see cref="SegmentMetadataDbApi"/> class.
    /// </summary>
    /// <param name="plugin">Plugin instance.</param>
    /// <param name="libraryManager">LibraryManager.</param>
    public SegmentMetadataDbApi(Plugin plugin, ILibraryManager libraryManager)
    {
        _plugin = plugin;
        _libraryManager = libraryManager;
        _libraryManager.ItemRemoved += LibraryManagerItemRemoved;
    }

    /// <summary>
    /// Create or update a segment.
    /// </summary>
    /// <param name="seg">Segment.</param>
    public async void SaveSegment(SegmentMetadata seg)
    {
        using var db = _plugin.GetPluginDb();
        await CreateOrUpdate(db, seg).ConfigureAwait(false);
        await db.SaveChangesAsync().ConfigureAwait(false);
    }

    /// <summary>
    /// Get metadata for segmentId.
    /// </summary>
    /// <param name="segmentId">Media ItemId.</param>
    /// <returns>A <see cref="Task"/> representing the asynchronous operation.</returns>
    public async Task<SegmentMetadata> GetSegment(Guid segmentId)
    {
        using var db = _plugin.GetPluginDb();
        return await db.SegmentMetadata.AsNoTracking().FirstAsync(s => s.SegmentId == segmentId).ConfigureAwait(false);
    }

    /// <summary>
    /// Get metadata for itemId and type. We also store metadata for media that have no segmentId in jellyfin.
    /// </summary>
    /// <param name="itemId">Media ItemId.</param>
    /// <param name="type">Segment Type.</param>
    /// <returns>A <see cref="Task"/> representing the asynchronous operation.</returns>
    public async Task<IEnumerable<SegmentMetadata>> GetSegments(Guid itemId, MediaSegmentType type)
    {
        using var db = _plugin.GetPluginDb();
        var query = db.SegmentMetadata.Where(s => s.ItemId == itemId && s.Type == type);
        return await query.AsNoTracking().ToListAsync().ConfigureAwait(false);
    }

    /// <summary>
    /// Check if itemId and type should be prevented to analyze. Just the first segment is used.
    /// </summary>
    /// <param name="itemId">Media ItemId.</param>
    /// <param name="type">Segment Type.</param>
    /// <returns>A <see cref="Task"/> representing the asynchronous operation.</returns>
    public async Task<bool> PreventAnalyze(Guid itemId, MediaSegmentType type)
    {
        using var db = _plugin.GetPluginDb();
        var seg = await db.SegmentMetadata.FirstAsync(s => s.ItemId == itemId && s.Type == type).ConfigureAwait(false);
        return seg is not null && seg.PreventAnalyzing;
    }

    /// <summary>
    /// Delete all metadata for itemId and optional type.
    /// </summary>
    /// <param name="itemId">Media ItemId.</param>
    /// <param name="type">Segment Type.</param>
    /// <returns>A <see cref="Task"/> representing the asynchronous operation.</returns>
    public async Task DeleteSegments(Guid itemId, MediaSegmentType? type)
    {
        using var db = _plugin.GetPluginDb();
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
    /// Delete all segments when itemid is deleted from library.
    /// </summary>
    /// <param name="sender">The sending entity.</param>
    /// <param name="itemChangeEventArgs">The <see cref="ItemChangeEventArgs"/>.</param>
    private async void LibraryManagerItemRemoved(object? sender, ItemChangeEventArgs itemChangeEventArgs)
    {
        await DeleteSegments(itemChangeEventArgs.Item.Id, null).ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        _libraryManager.ItemRemoved -= LibraryManagerItemRemoved;
    }
}
