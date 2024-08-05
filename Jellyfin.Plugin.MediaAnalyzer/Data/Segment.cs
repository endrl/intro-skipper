using System;
using System.Text.Json.Serialization;

namespace Jellyfin.Plugin.MediaAnalyzer;

/// <summary>
/// Result of fingerprinting and analyzing two episodes in a season.
/// All times are measured in seconds relative to the beginning of the media file.
/// </summary>
public class Segment
{
    /// <summary>
    /// Initializes a new instance of the <see cref="Segment"/> class.
    /// </summary>
    /// <param name="episode">Episode.</param>
    /// <param name="isEpisode">is episode.</param>
    /// <param name="intro">Introduction time range.</param>
    public Segment(Guid episode, bool isEpisode, TimeRange intro)
    {
        ItemId = episode;
        Start = intro.Start;
        End = intro.End;
        IsEpisode = isEpisode;
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="Segment"/> class.
    /// </summary>
    /// <param name="episode">Episode.</param>
    /// <param name="intro">Introduction time range.</param>
    public Segment(Guid episode, TimeRange intro)
    {
        ItemId = episode;
        Start = intro.Start;
        End = intro.End;
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="Segment"/> class.
    /// </summary>
    /// <param name="episode">Episode.</param>
    public Segment(Guid episode)
    {
        ItemId = episode;
        Start = 0;
        End = 0;
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="Segment"/> class.
    /// </summary>
    /// <param name="intro">intro.</param>
    public Segment(Segment intro)
    {
        ItemId = intro.ItemId;
        Start = intro.Start;
        End = intro.End;
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="Segment"/> class.
    /// </summary>
    public Segment()
    {
    }

    /// <summary>
    /// Gets or sets the item ID of db.
    /// </summary>
    public Guid ItemId { get; set; }

    /// <summary>
    /// Gets a value indicating whether this introduction is valid or not.
    /// Invalid results must not be returned through the API.
    /// </summary>
    public bool Valid => End > 0;

    /// <summary>
    /// Gets the duration of this intro.
    /// </summary>
    [JsonIgnore]
    public double Duration => End - Start;

    /// <summary>
    /// Gets or sets the segment sequence start time.
    /// </summary>
    public double Start { get; set; }

    /// <summary>
    /// Gets or sets the segment sequence end time.
    /// </summary>
    public double End { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether this is an episode (not a movie).
    /// </summary>
    public bool IsEpisode { get; set; } = true;
}
