namespace Jellyfin.Plugin.MediaAnalyzer;

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Numerics;
using System.Threading;
using Microsoft.Extensions.Logging;

/// <summary>
/// Chromaprint audio analyzer.
/// </summary>
public class ChromaprintAnalyzer : IMediaFileAnalyzer
{
    /// <summary>
    /// Seconds of audio in one fingerprint point.
    /// This value is defined by the Chromaprint library and should not be changed.
    /// </summary>
    private const double SamplesToSeconds = 0.128;

    private int minimumIntroDuration;

    private int maximumDifferences;

    private int invertedIndexShift;

    private double maximumTimeSkip;

    private double silenceDetectionMinimumDuration;

    private ILogger<ChromaprintAnalyzer> _logger;

    private AnalysisMode _analysisMode;

    /// <summary>
    /// Initializes a new instance of the <see cref="ChromaprintAnalyzer"/> class.
    /// </summary>
    /// <param name="logger">Logger.</param>
    public ChromaprintAnalyzer(ILogger<ChromaprintAnalyzer> logger)
    {
        var config = Plugin.Instance?.Configuration ?? new Configuration.PluginConfiguration();
        maximumDifferences = config.MaximumFingerprintPointDifferences;
        invertedIndexShift = config.InvertedIndexShift;
        maximumTimeSkip = config.MaximumTimeSkip;
        silenceDetectionMinimumDuration = config.SilenceDetectionMinimumDuration;
        minimumIntroDuration = config.MinimumIntroDuration;

        _logger = logger;
    }

    /// <inheritdoc />
    public ReadOnlyCollection<QueuedMedia> AnalyzeMediaFiles(
        ReadOnlyCollection<QueuedMedia> analysisQueue,
        AnalysisMode mode,
        CancellationToken cancellationToken)
    {
        // All segments for this season.
        var seasonSegments = new Dictionary<Guid, Segment>();

        // Cache of all fingerprints for this season.
        var fingerprintCache = new Dictionary<Guid, uint[]>();

        // Episode analysis queue based on not analyzed episodes
        var episodeAnalysisQueue = analysisQueue.ToList().Where(m => !m.IsAnalyzed).ToList();

        // Episodes that were analyzed and do not have an introduction.
        var episodesWithoutSegments = new List<QueuedMedia>();

        this._analysisMode = mode;

        // we need at least two episodes, it's possible to use an already analzyed one as reference
        if (episodeAnalysisQueue.Count == 1 && analysisQueue.Count > 1)
        {
            var item = analysisQueue.Where(i => !episodeAnalysisQueue.Contains(i)).FirstOrDefault();

            if (item is not null)
            {
                episodeAnalysisQueue.Add(item);
            }
        }

        // If we have just one episode, we can abort and flag the episode to skip blacklisting
        if (episodeAnalysisQueue.Count == 1)
        {
            var item = episodeAnalysisQueue.First();
            _logger.LogInformation("Found just one episode for {Series}: S{Season}. Skipping as we need at least two.", item.SeriesName, item.SeasonNumber);

            item.SkipPreventAnalyzing = true;
            episodesWithoutSegments.Add(item);
            return episodesWithoutSegments.AsReadOnly();
        }

        // Compute fingerprints for all episodes in the season
        foreach (var episode in episodeAnalysisQueue)
        {
            try
            {
                fingerprintCache[episode.ItemId] = FFmpegWrapper.Fingerprint(episode, mode);

                if (cancellationToken.IsCancellationRequested)
                {
                    return analysisQueue;
                }
            }
            catch (FingerprintException ex)
            {
                _logger.LogDebug("Caught fingerprint error: {Ex}", ex);
                WarningManager.SetFlag(PluginWarning.InvalidChromaprintFingerprint);

                // Fallback to an empty fingerprint on any error
                fingerprintCache[episode.ItemId] = Array.Empty<uint>();
            }
        }

        // While there are still episodes in the queue
        while (episodeAnalysisQueue.Count > 0)
        {
            // Pop the first episode from the queue
            var currentEpisode = episodeAnalysisQueue[0];
            episodeAnalysisQueue.RemoveAt(0);

            // Search through all remaining episodes.
            foreach (var remainingEpisode in episodeAnalysisQueue)
            {
                // Compare the current episode to all remaining episodes in the queue.
                var (currentIntro, remainingIntro) = CompareEpisodes(
                    currentEpisode.ItemId,
                    fingerprintCache[currentEpisode.ItemId],
                    remainingEpisode.ItemId,
                    fingerprintCache[remainingEpisode.ItemId]);

                // Ignore this comparison result if:
                // - one of the intros isn't valid, or
                // - the introduction exceeds the configured limit
                if (
                    !remainingIntro.Valid ||
                    remainingIntro.Duration > Plugin.Instance!.Configuration.MaximumIntroDuration)
                {
                    continue;
                }

                /* Since the Fingerprint() function returns an array of Chromaprint points without time
                 * information, the times reported from the index search function start from 0.
                 *
                 * While this is desired behavior for detecting introductions, it breaks credit
                 * detection, as the audio we're analyzing was extracted from some point into the file.
                 *
                 * To fix this, add the starting time of the fingerprint to the reported time range.
                 */
                if (this._analysisMode == AnalysisMode.Credits)
                {
                    currentIntro.Start += currentEpisode.CreditsFingerprintStart;
                    currentIntro.End += currentEpisode.CreditsFingerprintStart;
                    remainingIntro.Start += remainingEpisode.CreditsFingerprintStart;
                    remainingIntro.End += remainingEpisode.CreditsFingerprintStart;
                }

                // Only save the discovered intro if it is:
                // - the first intro discovered for this episode
                // - longer than the previously discovered intro
                if (
                    !seasonSegments.TryGetValue(currentIntro.ItemId, out var savedCurrentIntro) ||
                    currentIntro.Duration > savedCurrentIntro.Duration)
                {
                    seasonSegments[currentIntro.ItemId] = currentIntro;
                }

                if (
                    !seasonSegments.TryGetValue(remainingIntro.ItemId, out var savedRemainingIntro) ||
                    remainingIntro.Duration > savedRemainingIntro.Duration)
                {
                    seasonSegments[remainingIntro.ItemId] = remainingIntro;
                }

                break;
            }

            // If no intro is found at this point, the popped episode is not reinserted into the queue.
            episodesWithoutSegments.Add(currentEpisode);
        }

        // If cancellation was requested, report that no episodes were analyzed.
        if (cancellationToken.IsCancellationRequested)
        {
            return analysisQueue;
        }

        if (this._analysisMode == AnalysisMode.Introduction)
        {
            // Adjust all introduction end times so that they end at silence.
            seasonSegments = AdjustIntroEndTimes(analysisQueue, seasonSegments);
        }

        if (seasonSegments.Count != 0)
        {
            _logger.LogInformation("Save {Count} segments", seasonSegments.Count);
            Plugin.Instance!.UpdateTimestamps(seasonSegments, this._analysisMode);
        }
        else
        {
            _logger.LogDebug("No segments found");
        }

        return episodesWithoutSegments.AsReadOnly();
    }

    /// <summary>
    /// Analyze two episodes to find an introduction sequence shared between them.
    /// </summary>
    /// <param name="lhsId">First episode id.</param>
    /// <param name="lhsPoints">First episode fingerprint points.</param>
    /// <param name="rhsId">Second episode id.</param>
    /// <param name="rhsPoints">Second episode fingerprint points.</param>
    /// <returns>Intros for the first and second episodes.</returns>
    public (Segment Lhs, Segment Rhs) CompareEpisodes(
        Guid lhsId,
        uint[] lhsPoints,
        Guid rhsId,
        uint[] rhsPoints)
    {
        // Creates an inverted fingerprint point index for both episodes.
        // For every point which is a 100% match, search for an introduction at that point.
        var (lhsRanges, rhsRanges) = SearchInvertedIndex(lhsId, lhsPoints, rhsId, rhsPoints);

        if (lhsRanges.Count > 0)
        {
            _logger.LogTrace("Index search successful");

            return GetLongestTimeRange(lhsId, lhsRanges, rhsId, rhsRanges);
        }

        _logger.LogTrace(
            "Unable to find a shared introduction sequence between {LHS} and {RHS}",
            lhsId,
            rhsId);

        return (new Segment(lhsId), new Segment(rhsId));
    }

    /// <summary>
    /// Locates the longest range of similar audio and returns an Intro class for each range.
    /// </summary>
    /// <param name="lhsId">First episode id.</param>
    /// <param name="lhsRanges">First episode shared timecodes.</param>
    /// <param name="rhsId">Second episode id.</param>
    /// <param name="rhsRanges">Second episode shared timecodes.</param>
    /// <returns>Intros for the first and second episodes.</returns>
    private (Segment Lhs, Segment Rhs) GetLongestTimeRange(
        Guid lhsId,
        List<TimeRange> lhsRanges,
        Guid rhsId,
        List<TimeRange> rhsRanges)
    {
        // Store the longest time range as the introduction.
        lhsRanges.Sort();
        rhsRanges.Sort();

        var lhsIntro = lhsRanges[0];
        var rhsIntro = rhsRanges[0];

        // If the intro starts early in the episode, move it to the beginning.
        if (lhsIntro.Start <= 5)
        {
            lhsIntro.Start = 0;
        }

        if (rhsIntro.Start <= 5)
        {
            rhsIntro.Start = 0;
        }

        // Create Intro classes for each time range.
        return (new Segment(lhsId, lhsIntro), new Segment(rhsId, rhsIntro));
    }

    /// <summary>
    /// Search for a shared introduction sequence using inverted indexes.
    /// </summary>
    /// <param name="lhsId">LHS ID.</param>
    /// <param name="lhsPoints">Left episode fingerprint points.</param>
    /// <param name="rhsId">RHS ID.</param>
    /// <param name="rhsPoints">Right episode fingerprint points.</param>
    /// <returns>List of shared TimeRanges between the left and right episodes.</returns>
    private (List<TimeRange> Lhs, List<TimeRange> Rhs) SearchInvertedIndex(
        Guid lhsId,
        uint[] lhsPoints,
        Guid rhsId,
        uint[] rhsPoints)
    {
        var lhsRanges = new List<TimeRange>();
        var rhsRanges = new List<TimeRange>();

        // Generate inverted indexes for the left and right episodes.
        var lhsIndex = FFmpegWrapper.CreateInvertedIndex(lhsId, lhsPoints);
        var rhsIndex = FFmpegWrapper.CreateInvertedIndex(rhsId, rhsPoints);
        var indexShifts = new HashSet<int>();

        // For all audio points in the left episode, check if the right episode has a point which matches exactly.
        // If an exact match is found, calculate the shift that must be used to align the points.
        foreach (var kvp in lhsIndex)
        {
            var originalPoint = kvp.Key;

            for (var i = -1 * invertedIndexShift; i <= invertedIndexShift; i++)
            {
                var modifiedPoint = (uint)(originalPoint + i);

                if (rhsIndex.TryGetValue(modifiedPoint, out int modifiedPointValue))
                {
                    var lhsFirst = (int)lhsIndex[originalPoint];
                    var rhsFirst = modifiedPointValue;
                    indexShifts.Add(rhsFirst - lhsFirst);
                }
            }
        }

        // Use all discovered shifts to compare the episodes.
        foreach (var shift in indexShifts)
        {
            var (lhsIndexContiguous, rhsIndexContiguous) = FindContiguous(lhsPoints, rhsPoints, shift);
            if (lhsIndexContiguous.End > 0 && rhsIndexContiguous.End > 0)
            {
                lhsRanges.Add(lhsIndexContiguous);
                rhsRanges.Add(rhsIndexContiguous);
            }
        }

        return (lhsRanges, rhsRanges);
    }

    /// <summary>
    /// Finds the longest contiguous region of similar audio between two fingerprints using the provided shift amount.
    /// </summary>
    /// <param name="lhs">First fingerprint to compare.</param>
    /// <param name="rhs">Second fingerprint to compare.</param>
    /// <param name="shiftAmount">Amount to shift one fingerprint by.</param>
    private (TimeRange Lhs, TimeRange Rhs) FindContiguous(
        uint[] lhs,
        uint[] rhs,
        int shiftAmount)
    {
        var leftOffset = 0;
        var rightOffset = 0;

        // Calculate the offsets for the left and right hand sides.
        if (shiftAmount < 0)
        {
            leftOffset -= shiftAmount;
        }
        else
        {
            rightOffset += shiftAmount;
        }

        // Store similar times for both LHS and RHS.
        var lhsTimes = new List<double>();
        var rhsTimes = new List<double>();
        var upperLimit = Math.Min(lhs.Length, rhs.Length) - Math.Abs(shiftAmount);

        // XOR all elements in LHS and RHS, using the shift amount from above.
        for (var i = 0; i < upperLimit; i++)
        {
            // XOR both samples at the current position.
            var lhsPosition = i + leftOffset;
            var rhsPosition = i + rightOffset;
            var diff = lhs[lhsPosition] ^ rhs[rhsPosition];

            // If the difference between the samples is small, flag both times as similar.
            if (CountBits(diff) > maximumDifferences)
            {
                continue;
            }

            var lhsTime = lhsPosition * SamplesToSeconds;
            var rhsTime = rhsPosition * SamplesToSeconds;

            lhsTimes.Add(lhsTime);
            rhsTimes.Add(rhsTime);
        }

        // Ensure the last timestamp is checked
        lhsTimes.Add(double.MaxValue);
        rhsTimes.Add(double.MaxValue);

        // Now that both fingerprints have been compared at this shift, see if there's a contiguous time range.
        var lContiguous = TimeRangeHelpers.FindContiguous(lhsTimes.ToArray(), maximumTimeSkip);
        if (lContiguous is null || lContiguous.Duration < minimumIntroDuration)
        {
            return (new TimeRange(), new TimeRange());
        }

        // Since LHS had a contiguous time range, RHS must have one also.
        var rContiguous = TimeRangeHelpers.FindContiguous(rhsTimes.ToArray(), maximumTimeSkip)!;

        if (this._analysisMode == AnalysisMode.Introduction)
        {
            // Tweak the end timestamps just a bit to ensure as little content as possible is skipped over.
            // TODO: remove this
            if (lContiguous.Duration >= 90)
            {
                lContiguous.End -= 2 * maximumTimeSkip;
                rContiguous.End -= 2 * maximumTimeSkip;
            }
            else if (lContiguous.Duration >= 30)
            {
                lContiguous.End -= maximumTimeSkip;
                rContiguous.End -= maximumTimeSkip;
            }
        }

        return (lContiguous, rContiguous);
    }

    /// <summary>
    /// Adjusts the end timestamps of all intros so that they end at silence.
    /// </summary>
    /// <param name="episodes">QueuedEpisodes to adjust.</param>
    /// <param name="originalIntros">Original introductions.</param>
    private Dictionary<Guid, Segment> AdjustIntroEndTimes(
        ReadOnlyCollection<QueuedMedia> episodes,
        Dictionary<Guid, Segment> originalIntros)
    {
        // The minimum duration of audio that must be silent before adjusting the intro's end.
        var minimumSilence = Plugin.Instance!.Configuration.SilenceDetectionMinimumDuration;

        Dictionary<Guid, Segment> modifiedIntros = new();

        // For all episodes
        foreach (var episode in episodes)
        {
            _logger.LogTrace(
                "Adjusting introduction end time for {Name} ({Id})",
                episode.Name,
                episode.ItemId);

            // If no intro was found for this episode, skip it.
            if (!originalIntros.TryGetValue(episode.ItemId, out var originalIntro))
            {
                _logger.LogTrace("{Name} does not have an intro", episode.Name);
                continue;
            }

            // Only adjust the end timestamp of the intro
            var originalIntroEnd = new TimeRange(originalIntro.End - 15, originalIntro.End);

            _logger.LogTrace(
                "{Name} original intro: {Start} - {End}",
                episode.Name,
                originalIntro.Start,
                originalIntro.End);

            // Detect silence in the media file up to the end of the intro.
            var silence = FFmpegWrapper.DetectSilence(episode, (int)originalIntro.End + 2);

            // For all periods of silence
            foreach (var currentRange in silence)
            {
                _logger.LogTrace(
                    "{Name} silence: {Start} - {End}",
                    episode.Name,
                    currentRange.Start,
                    currentRange.End);

                // Ignore any silence that:
                // * doesn't intersect the ending of the intro, or
                // * is shorter than the user defined minimum duration, or
                // * starts before the introduction does
                if (
                    !originalIntroEnd.Intersects(currentRange) ||
                    currentRange.Duration < silenceDetectionMinimumDuration ||
                    currentRange.Start < originalIntro.Start)
                {
                    continue;
                }

                // Adjust the end timestamp of the intro to match the start of the silence region.
                originalIntro.End = currentRange.Start;
                break;
            }

            _logger.LogTrace(
                "{Name} adjusted intro: {Start} - {End}",
                episode.Name,
                originalIntro.Start,
                originalIntro.End);

            // Add the (potentially) modified intro back.
            modifiedIntros[episode.ItemId] = originalIntro;
        }

        return modifiedIntros;
    }

    /// <summary>
    /// Count the number of bits that are set in the provided number.
    /// </summary>
    /// <param name="number">Number to count bits in.</param>
    /// <returns>Number of bits that are equal to 1.</returns>
    public int CountBits(uint number)
    {
        return BitOperations.PopCount(number);
    }
}
