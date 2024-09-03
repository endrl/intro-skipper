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
    /// <param name="intro">intro.</param>
    /// <param name="mode">mode.</param>
    public SegmentMetadata(Segment intro, AnalysisMode mode)
    {
        ItemId = intro.ItemId;
        Type = mode == AnalysisMode.Introduction ? MediaSegmentType.Intro : MediaSegmentType.Outro;
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
    /// Gets or sets the name for the Media (Episode or Movie+Source).
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the series name if applicable.
    /// </summary>
    public string SeriesName { get; set; } = string.Empty;

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
    public AnalyzerType? AnalyzerType { get; set; }

    /// <summary>
    /// Gets or sets the analyzer note. Data that an analyzer might provide as additional info.
    /// </summary>
    public string AnalyzerNote { get; set; } = string.Empty;
}
