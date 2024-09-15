using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using Jellyfin.Data.Enums;
using MediaBrowser.Controller;
using MediaBrowser.Model.MediaSegments;

namespace Jellyfin.Plugin.MediaAnalyzer;

/// <summary>
/// Small abstraction over MediaSegmentsManager.
/// </summary>
public class MediaSegmentsDb
{
    private readonly IMediaSegmentManager _segmentsManager;

    /// <summary>
    /// Initializes a new instance of the <see cref="MediaSegmentsDb"/> class.
    /// </summary>
    /// <param name="segmentsManager">MediaSegmentsManager.</param>
    public MediaSegmentsDb(IMediaSegmentManager segmentsManager)
    {
        _segmentsManager = segmentsManager;
    }

    /// <summary>
    /// Test if we can find segments.
    /// </summary>
    /// <param name="itemId">ItemId.</param>
    /// <param name="type">Mode.</param>
    /// <returns>A <see cref="Task{TResult}"/> representing the result of the asynchronous operation.</returns>
    public async Task<bool> HasSegments(Guid itemId, MediaSegmentType type)
    {
        var list = await _segmentsManager.GetSegmentsAsync(itemId, [type]).ConfigureAwait(false);
        return list.Any();
    }

    /// <summary>
    /// Create new media segment together with metadata. Can also handle additional metadata without segment.
    /// </summary>
    /// <param name="segments">segments to add.</param>
    /// <param name="metadata">Metadata for a segment.</param>
    /// <param name="mode">Mode.</param>
    /// <returns>A <see cref="Task{TResult}"/> representing the result of the asynchronous operation.</returns>
    public async Task CreateMediaSegments(ReadOnlyDictionary<Guid, Segment> segments, ReadOnlyDictionary<Guid, SegmentMetadata> metadata, MediaSegmentType mode)
    {
        var metaDBb = Plugin.Instance!.GetMetadataDb();
        Dictionary<Guid, SegmentMetadata> metadataLocal = metadata.ToDictionary();

        if (metaDBb == null)
        {
            throw new InvalidOperationException("Meta database was null");
        }

        foreach (var (key, seg) in segments)
        {
            var newGuid = Guid.NewGuid();

            var newSeg = new MediaSegmentDto()
            {
                Id = newGuid,
                ItemId = seg.ItemId,
                Type = mode,
                StartTicks = Utils.SToTicks(seg.Start),
                EndTicks = Utils.SToTicks(seg.End),
            };

            await _segmentsManager.CreateSegmentAsync(newSeg, Plugin.Instance!.Name).ConfigureAwait(false);

            if (metadata.TryGetValue(key, out var meta))
            {
                metadataLocal.Remove(key);

                meta.SegmentId = newGuid;
                metaDBb.SaveSegment(meta);
            }
        }

        // we may have more metadata
        foreach (var meta in metadataLocal)
        {
            metaDBb.SaveSegment(meta.Value);
        }
    }

    /// <summary>
    /// Get segments from db by mode and id.
    /// </summary>
    /// <param name="itemId">Item Id.</param>
    /// <param name="mode">Mode of analysis.</param>
    /// <returns>Dictionary of guid,segments.</returns>
    public async Task<Dictionary<Guid, Segment>> GetMediaSegmentsByIdAsync(Guid itemId, MediaSegmentType mode)
    {
        var segments = await _segmentsManager.GetSegmentsAsync(itemId, [mode]).ConfigureAwait(false);

        var intros = new Dictionary<Guid, Segment>();

        foreach (var item in segments)
        {
            intros.TryAdd(item.ItemId, new Segment()
            {
                ItemId = item.ItemId,
                Start = Utils.TicksToS(item.StartTicks),
                End = Utils.TicksToS(item.EndTicks),
            });
        }

        return intros;
    }
}
