namespace Jellyfin.Plugin.MediaAnalyzer;

using System;
using System.Collections.ObjectModel;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Data.Enums;

/// <summary>
/// Media file analyzer interface.
/// </summary>
public interface IMediaFileAnalyzer
{
    /// <summary>
    /// Analyze media files for shared introductions or credits, returning all media files that were not analyzed, analyzed and optional metadata for both.
    /// </summary>
    /// <param name="analysisQueue">Collection of unanalyzed media files.</param>
    /// <param name="mode">Analysis mode.</param>
    /// <param name="cancellationToken">Cancellation token from scheduled task.</param>
    /// <returns>Collection of media files that were **unsuccessfully analyzed** and successfull.</returns>
    public Task<(ReadOnlyCollection<QueuedMedia> NotAnalyzed, ReadOnlyDictionary<Guid, Segment> Analyzed, ReadOnlyDictionary<Guid, SegmentMetadata> SegmentMetadata)> AnalyzeMediaFilesAsync(
        ReadOnlyCollection<QueuedMedia> analysisQueue,
        MediaSegmentType mode,
        CancellationToken cancellationToken);
}
