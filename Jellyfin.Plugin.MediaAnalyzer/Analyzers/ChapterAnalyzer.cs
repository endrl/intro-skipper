namespace Jellyfin.Plugin.MediaAnalyzer;

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Data.Enums;
using MediaBrowser.Model.Entities;
using Microsoft.Extensions.Logging;

/// <summary>
/// Chapter name analyzer.
/// </summary>
public class ChapterAnalyzer : IMediaFileAnalyzer
{
    private ILogger<ChapterAnalyzer> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="ChapterAnalyzer"/> class.
    /// </summary>
    /// <param name="logger">Logger.</param>
    public ChapterAnalyzer(ILogger<ChapterAnalyzer> logger)
    {
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<(ReadOnlyCollection<QueuedMedia> NotAnalyzed, ReadOnlyDictionary<Guid, Segment> Analyzed, ReadOnlyDictionary<Guid, SegmentMetadata> SegmentMetadata)> AnalyzeMediaFilesAsync(
        ReadOnlyCollection<QueuedMedia> analysisQueue,
        MediaSegmentType mode,
        CancellationToken cancellationToken)
    {
        var skippableRanges = new Dictionary<Guid, Segment>();
        var metadata = new Dictionary<Guid, SegmentMetadata>();

        var expression = mode == MediaSegmentType.Intro ?
            Plugin.Instance!.Configuration.ChapterAnalyzerIntroductionPattern :
            Plugin.Instance!.Configuration.ChapterAnalyzerEndCreditsPattern;

        if (string.IsNullOrWhiteSpace(expression))
        {
            return (analysisQueue, skippableRanges.AsReadOnly(), metadata.AsReadOnly());
        }

        foreach (var episode in analysisQueue)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                break;
            }

            var meta = await Plugin.Instance!.GetMetadataDb().GetSegments(episode.ItemId, mode, AnalyzerType.ChapterAnalyzer);

            var skipRange = FindMatchingChapter(
                episode,
                new(Plugin.Instance!.GetChapters(episode.ItemId)),
                expression,
                mode);

            if (skipRange is null)
            {
                continue;
            }

            // protect against broken timestamps
            if (skipRange.Start >= skipRange.End)
            {
                continue;
            }

            skippableRanges.Add(episode.ItemId, skipRange);
            if (meta is null)
            {
                metadata[episode.ItemId] = new SegmentMetadata(episode, mode, AnalyzerType.ChapterAnalyzer);
            }
        }

        return (analysisQueue
            .Where(x => !skippableRanges.ContainsKey(x.ItemId))
            .ToList()
            .AsReadOnly(), skippableRanges.AsReadOnly(), metadata.AsReadOnly());
    }

    /// <summary>
    /// Searches a list of chapter names for one that matches the provided regular expression.
    /// Only public to allow for unit testing.
    /// </summary>
    /// <param name="episode">Episode.</param>
    /// <param name="chapters">Media item chapters.</param>
    /// <param name="expression">Regular expression pattern.</param>
    /// <param name="mode">Analysis mode.</param>
    /// <returns>Intro object containing skippable time range, or null if no chapter matched.</returns>
    public Segment? FindMatchingChapter(
        QueuedMedia episode,
        Collection<ChapterInfo> chapters,
        string expression,
        MediaSegmentType mode)
    {
        Segment? matchingChapter = null;

        var config = Plugin.Instance?.Configuration ?? new Configuration.PluginConfiguration();

        var minDuration = config.MinimumIntroDuration;
        int maxDuration = mode == MediaSegmentType.Intro ?
            config.MaximumIntroDuration :
            config.MaximumEpisodeCreditsDuration;

        if (mode == MediaSegmentType.Outro)
        {
            // Since the ending credits chapter may be the last chapter in the file, append a virtual
            // chapter at the very end of the file.
            chapters.Add(new()
            {
                StartPositionTicks = TimeSpan.FromSeconds(episode.Duration).Ticks
            });
        }

        // Check all chapters
        for (int i = 0; i < chapters.Count - 1; i++)
        {
            var current = chapters[i];
            var next = chapters[i + 1];

            if (string.IsNullOrWhiteSpace(current.Name))
            {
                continue;
            }

            var currentRange = new TimeRange(
                TimeSpan.FromTicks(current.StartPositionTicks).TotalSeconds,
                TimeSpan.FromTicks(next.StartPositionTicks).TotalSeconds);

            var baseMessage = string.Format(
                CultureInfo.InvariantCulture,
                "{0}: Chapter \"{1}\" ({2} - {3})",
                episode.Path,
                current.Name,
                currentRange.Start,
                currentRange.End);

            if (currentRange.Duration < minDuration || currentRange.Duration > maxDuration)
            {
                _logger.LogTrace("{Base}: ignoring (invalid duration)", baseMessage);
                continue;
            }

            // Regex.IsMatch() is used here in order to allow the runtime to cache the compiled regex
            // between function invocations.
            var match = Regex.IsMatch(
                current.Name,
                expression,
                RegexOptions.None,
                TimeSpan.FromSeconds(1));

            if (!match)
            {
                _logger.LogTrace("{Base}: ignoring (does not match regular expression)", baseMessage);
                continue;
            }

            matchingChapter = new(episode.ItemId, episode.IsEpisode(), currentRange);
            _logger.LogTrace("{Base}: okay", baseMessage);
            break;
        }

        return matchingChapter;
    }
}
