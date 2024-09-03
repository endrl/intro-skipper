using System;

namespace Jellyfin.Plugin.MediaAnalyzer;

/// <summary>
/// Media queued for analysis.
/// </summary>
public class QueuedMedia
{
    /// <summary>
    /// Gets or sets the Series name.
    /// </summary>
    public string SeriesName { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the season number.
    /// </summary>
    public int SeasonNumber { get; set; }

    /// <summary>
    /// Gets or sets the media id.
    /// </summary>
    public Guid ItemId { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether this media is an episode, part of a tv show.
    /// </summary>
    public bool IsEpisode { get; set; } = true;

    /// <summary>
    /// Gets or sets a value indicating whether this media has been already analyzed.
    /// </summary>
    public bool IsAnalyzed { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether this media should be skipped for blacklisting.
    /// This will happen when a Season has just one episode, which can't be Chromaprint compared analyzed but maybe at a later run.
    /// </summary>
    public bool SkipPreventAnalyzing { get; set; }

    /// <summary>
    /// Gets or sets the full path to episode.
    /// </summary>
    public string Path { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the name of the media, episode or movie with source name/quality.
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the timestamp (in seconds) to stop searching for an introduction at.
    /// </summary>
    public int IntroFingerprintEnd { get; set; }

    /// <summary>
    /// Gets or sets the timestamp (in seconds) to start looking for end credits at.
    /// </summary>
    public int CreditsFingerprintStart { get; set; }

    /// <summary>
    /// Gets or sets the total duration of this media file (in seconds).
    /// </summary>
    public int Duration { get; set; }

    /// <inheritdoc/>
    public override bool Equals(object? obj)
    {
        return (obj as QueuedMedia)?.ItemId == this.ItemId;
    }

    /// <inheritdoc/>
    public override int GetHashCode()
    {
        return ItemId.GetHashCode();
    }
}
