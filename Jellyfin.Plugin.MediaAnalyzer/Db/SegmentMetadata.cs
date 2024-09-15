using System;
using Jellyfin.Data.Enums;

namespace Jellyfin.Plugin.MediaAnalyzer;

/// <summary>
/// Metadata for MediaSegments. Metadata is also created for non segments (e.g. analyze blocking).
/// </summary>
public class SegmentMetadata
{
    /// <summary>
    /// Initializes a new instance of the <see cref="SegmentMetadata"/> class.
    /// </summary>
    /// <param name="media">Media item.</param>
    /// <param name="mode">The mode it ran.</param>
    /// <param name="analyzer">Analyzer who created it.</param>
    public SegmentMetadata(QueuedMedia media, MediaSegmentType mode, AnalyzerType analyzer)
    {
        ItemId = media.ItemId;
        Type = mode;
        AnalyzerType = analyzer;
        Name = media.GetFullName();
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="SegmentMetadata"/> class.
    /// </summary>
    public SegmentMetadata()
    {
    }

    /// <summary>
    /// Gets or sets the Id. Database generated.
    /// </summary>
    public Guid Id { get; set; }

    /// <summary>
    /// Gets or sets the "full" name for the Media (Series + Season + Episode or Movie + Source).
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the segment Id of Jellyfin.
    /// </summary>
    public Guid SegmentId { get; set; }

    /// <summary>
    /// Gets or sets the item ID of Jellyfin.
    /// </summary>
    public Guid ItemId { get; set; }

    /// <summary>
    /// Gets or sets the segment type.
    /// </summary>
    public MediaSegmentType Type { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether the segment is blocked for future analysis.
    /// </summary>
    public bool PreventAnalyzing { get; set; }

    /// <summary>
    /// Gets or sets which analyzer created this segment.
    /// </summary>
    public AnalyzerType AnalyzerType { get; set; } = AnalyzerType.NotSet;

    /// <summary>
    /// Gets or sets the analyzer note. Data that an analyzer might provide as additional info.
    /// </summary>
    public string AnalyzerNote { get; set; } = string.Empty;
}
