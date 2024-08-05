using System;
using Jellyfin.Data.Enums;
using Microsoft.EntityFrameworkCore;

namespace Jellyfin.Plugin.MediaAnalyzer;

/// <summary>
/// A segment that is blacklisted for future analysis runs.
/// This happens, when a media has been analyzed but no segment was returned.
/// </summary>
public class BlacklistSegment
{
    /// <summary>
    /// Initializes a new instance of the <see cref="BlacklistSegment"/> class.
    /// </summary>
    /// <param name="intro">intro.</param>
    /// <param name="mode">mode.</param>
    public BlacklistSegment(Segment intro, AnalysisMode mode)
    {
        ItemId = intro.ItemId;
        Type = mode == AnalysisMode.Introduction ? MediaSegmentType.Intro : MediaSegmentType.Outro;
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="BlacklistSegment"/> class.
    /// </summary>
    public BlacklistSegment()
    {
    }

    /// <summary>
    /// Gets or sets the segment name used for better log messages.
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the item ID of db.
    /// </summary>
    public Guid ItemId { get; set; }

    /// <summary>
    /// Gets or sets the segment type.
    /// </summary>
    public MediaSegmentType Type { get; set; }

    /// <inheritdoc/>
    public override bool Equals(object? obj)
    {
        return (obj as BlacklistSegment)?.Type == this.Type && (obj as BlacklistSegment)?.ItemId == this.ItemId;
    }

    /// <inheritdoc/>
    public override int GetHashCode()
    {
        return Type.GetHashCode() + ItemId.GetHashCode();
    }
}
