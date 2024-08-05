namespace Jellyfin.Plugin.MediaAnalyzer;

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Linq;
using Jellyfin.Data.Enums;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.Movies;
using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Controller.Library;
using MediaBrowser.Model.Dto;
using MediaBrowser.Model.Entities;
using Microsoft.Extensions.Logging;

/// <summary>
/// Manages enqueuing library items for analysis.
/// </summary>
public class QueueManager
{
    private readonly AnalysisMode _analysisMode;
    private ILibraryManager _libraryManager;
    private ILogger<QueueManager> _logger;
    private double analysisPercent;
    private List<string> selectedLibraries;
    private Dictionary<string, List<int>> skippedTvShows;
    private List<string> skippedMovies;
    private Dictionary<Guid, List<QueuedMedia>> _queuedEpisodes;

    /// <summary>
    /// Initializes a new instance of the <see cref="QueueManager"/> class.
    /// </summary>
    /// <param name="logger">Logger.</param>
    /// <param name="libraryManager">Library manager.</param>
    /// <param name="mode">Analysis mode.</param>
    public QueueManager(ILogger<QueueManager> logger, ILibraryManager libraryManager, AnalysisMode mode)
    {
        _logger = logger;
        _libraryManager = libraryManager;
        _analysisMode = mode;

        selectedLibraries = new();
        _queuedEpisodes = new();
        skippedTvShows = new();
        skippedMovies = new();
    }

    /// <summary>
    /// Gets all media items on the server.
    /// </summary>
    /// <returns>Queued media items.</returns>
    public ReadOnlyDictionary<Guid, List<QueuedMedia>> GetMediaItems()
    {
        // Assert that ffmpeg with chromaprint is installed
        if (!FFmpegWrapper.CheckFFmpegVersion())
        {
            throw new FingerprintException(
                "ffmpeg with chromaprint is not installed on this system - episodes will not be analyzed. If Jellyfin is running natively, install jellyfin-ffmpeg5. If Jellyfin is running in a container, upgrade it to the latest version of 10.8.0.");
        }

        Plugin.Instance!.TotalQueued = 0;

        LoadAnalysisSettings();

        // For all selected libraries, enqueue all contained episodes.
        foreach (var folder in _libraryManager.GetVirtualFolders())
        {
            // If libraries have been selected for analysis, ensure this library was selected.
            if (selectedLibraries.Count > 0 && !selectedLibraries.Contains(folder.Name))
            {
                _logger.LogDebug("Not analyzing library \"{Name}\": not selected by user", folder.Name);
                continue;
            }

            _logger.LogInformation(
                "Running enqueue of items in library {Name} ({ItemId})",
                folder.Name,
                folder.ItemId);

            try
            {
                QueueLibraryContents(folder.ItemId);
            }
            catch (Exception ex)
            {
                _logger.LogError("Failed to enqueue items from library {Name}: {Exception}", folder.Name, ex);
            }
        }

        Plugin.Instance!.QueuedMediaItems.Clear();
        foreach (var kvp in _queuedEpisodes)
        {
            Plugin.Instance!.QueuedMediaItems[kvp.Key] = kvp.Value;
        }

        return new(_queuedEpisodes);
    }

    /// <summary>
    /// Loads the list of libraries which have been selected for analysis and the minimum intro duration.
    /// Settings which have been modified from the defaults are logged.
    /// </summary>
    private void LoadAnalysisSettings()
    {
        var config = Plugin.Instance!.Configuration;

        // Store the analysis percent
        analysisPercent = Convert.ToDouble(config.AnalysisPercent) / 100;

        // Get the list of library names which have been selected for analysis, ignoring whitespace and empty entries.
        selectedLibraries = config.SelectedLibraries
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToList();

        // Get the list movie names which should be skipped.
        skippedMovies = config.SkippedMovies
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToList();

        // Get the list of tvshow names and seasons which should be skipped for analysis.
        var show = config.SkippedTvShows
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToList();

        foreach (var s in show)
        {
            if (s.Contains(';', System.StringComparison.InvariantCulture))
            {
                var rseasons = s.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                var seasons = rseasons.Skip(1).ToArray();
                var name = rseasons.ElementAt(0);
                var seasonNumbers = new List<int>();

                foreach (var season in seasons)
                {
                    var nr = season.Substring(1);

                    try
                    {
                        seasonNumbers.Add(int.Parse(nr, CultureInfo.InvariantCulture));
                    }
                    catch (FormatException)
                    {
                        _logger.LogError("Skipping TV Shows: Failed to parse season number '{Nr}' for tv show: {Name}. Fix your config!", nr, name);
                    }
                }

                skippedTvShows.Add(name, seasonNumbers);
            }
            else
            {
                skippedTvShows.Add(s, new List<int>());
            }
        }

        // If any libraries have been selected for analysis, log their names.
        if (selectedLibraries.Count > 0)
        {
            _logger.LogInformation("Limiting analysis to the following libraries: {Selected}", selectedLibraries);
        }
        else
        {
            _logger.LogDebug("Not limiting analysis by library name");
        }

        // If analysis settings have been changed from the default, log the modified settings.
        if (config.AnalysisLengthLimit != 15 || config.AnalysisPercent != 30 || config.MinimumIntroDuration != 15)
        {
            _logger.LogInformation(
                "Analysis settings have been changed to: {Percent}%/{Minutes}m and a minimum of {Minimum}s",
                config.AnalysisPercent,
                config.AnalysisLengthLimit,
                config.MinimumIntroDuration);
        }
    }

    private void QueueLibraryContents(string rawId)
    {
        _logger.LogDebug("Constructing anonymous internal query");

        var includes = new BaseItemKind[] { BaseItemKind.Episode };

        // When analyzing for credits also search for movies
        if (_analysisMode == AnalysisMode.Credits)
        {
            includes = includes.Concat(new BaseItemKind[] { BaseItemKind.Movie }).ToArray();
        }

        var query = new InternalItemsQuery()
        {
            // Order by series name, season, and then episode number so that status updates are logged in order
            ParentId = Guid.Parse(rawId),
            OrderBy = new[]
            {
                (ItemSortBy.SeriesSortName, SortOrder.Ascending),
                (ItemSortBy.ParentIndexNumber, SortOrder.Ascending),
                (ItemSortBy.IndexNumber, SortOrder.Ascending),
            },
            IncludeItemTypes = includes,
            Recursive = true,
            IsVirtualItem = false
        };

        _logger.LogDebug("Getting items");

        var items = _libraryManager.GetItemList(query, false);

        if (items is null)
        {
            _logger.LogError("Library query result is null");
            return;
        }

        // Queue all media on the server for fingerprinting.
        _logger.LogDebug("Iterating through library items");

        foreach (var item in items)
        {
            if (item is Episode episode)
            {
                if (SkipEpisode(episode))
                {
                    _logger.LogInformation("Skipping episode: '{EpisodeName}' of series: '{SeriesName} S{Season}'", episode.Name, episode.SeriesName, episode.AiredSeasonNumber);
                    continue;
                }

                QueueEpisode(episode);
            }
            else if (item is Movie movie)
            {
                if (skippedMovies.Contains(movie.Name))
                {
                    _logger.LogInformation("Skipping Movie: '{Name}'", movie.Name);
                    continue;
                }

                // Movie can have multiple MediaSources like 1080p and a 4k file, they have different ids
                foreach (MediaSourceInfo source in movie.GetMediaSources(false))
                {
                    _logger.LogInformation("Adding movie: '{Name}'", source.Name);
                    QueueMovie(source);
                }
            }
            else
            {
                _logger.LogDebug("Item {Name} is not an episode or movie", item.Name);
                continue;
            }
        }

        _logger.LogDebug("Queued {Count} media items", items.Count);
    }

    // Test if should skip the episode
    private bool SkipEpisode(Episode episode)
    {
        if (skippedTvShows.TryGetValue(episode.SeriesName, out var seasons))
        {
            return (episode.AiredSeasonNumber != null && seasons.Contains(episode.AiredSeasonNumber.GetValueOrDefault())) ? true : false;
        }

        return false;
    }

    private void QueueEpisode(Episode episode)
    {
        if (Plugin.Instance is null)
        {
            throw new InvalidOperationException("plugin instance was null");
        }

        if (string.IsNullOrEmpty(episode.Path))
        {
            _logger.LogWarning(
                "Not queuing episode \"{Name}\" from series \"{Series}\" ({Id}) as no path was provided by Jellyfin",
                episode.Name,
                episode.SeriesName,
                episode.Id);
            return;
        }

        if (episode.RunTimeTicks is null)
        {
            _logger.LogWarning(
                "Not queuing episode \"{Name}\" from series \"{Series}\" ({Id}) as no duration was provided by Jellyfin",
                episode.Name,
                episode.SeriesName,
                episode.Id);
            return;
        }

        // Limit analysis to the first X% of the episode and at most Y minutes.
        // X and Y default to 30% and 15 minutes.
        var duration = TimeSpan.FromTicks(episode.RunTimeTicks ?? 0).TotalSeconds;
        var fingerprintDuration = duration;

        if (fingerprintDuration >= 5 * 60)
        {
            fingerprintDuration *= analysisPercent;
        }

        fingerprintDuration = Math.Min(
            fingerprintDuration,
            60 * Plugin.Instance!.Configuration.AnalysisLengthLimit);

        // Allocate a new list for each new season
        _queuedEpisodes.TryAdd(episode.SeasonId, new List<QueuedMedia>());

        // Queue the episode for analysis
        var maxCreditsDuration = Plugin.Instance!.Configuration.MaximumEpisodeCreditsDuration;
        _queuedEpisodes[episode.SeasonId].Add(new QueuedMedia()
        {
            SeriesName = episode.SeriesName,
            SeasonNumber = episode.AiredSeasonNumber ?? 0,
            ItemId = episode.Id,
            Name = episode.Name,
            Path = episode.Path,
            Duration = Convert.ToInt32(duration),
            IntroFingerprintEnd = Convert.ToInt32(fingerprintDuration),
            CreditsFingerprintStart = Convert.ToInt32(duration - maxCreditsDuration),
        });

        Plugin.Instance!.TotalQueued++;
    }

    private void QueueMovie(MediaSourceInfo movie)
    {
        if (Plugin.Instance is null)
        {
            throw new InvalidOperationException("plugin instance was null");
        }

        if (string.IsNullOrEmpty(movie.Path))
        {
            _logger.LogWarning(
                "Not queuing movie \"{Name}\" ({Id}) as no path was provided by Jellyfin",
                movie.Name,
                movie.Id);
            return;
        }

        if (movie.RunTimeTicks is null)
        {
            _logger.LogWarning(
                "Not queuing Movie \"{Name}\" ({Id}) as no duration was provided by Jellyfin",
                movie.Name,
                movie.Id);
            return;
        }

        // Limit analysis to the first X% of the episode and at most Y minutes.
        // X and Y default to 30% and 15 minutes.
        var duration = TimeSpan.FromTicks(movie.RunTimeTicks ?? 0).TotalSeconds;
        var fingerprintDuration = duration;

        if (fingerprintDuration >= 5 * 60)
        {
            fingerprintDuration *= analysisPercent;
        }

        fingerprintDuration = Math.Min(
            fingerprintDuration,
            60 * Plugin.Instance!.Configuration.AnalysisLengthLimit);

        // Allocate a new list for each movie
        _queuedEpisodes.TryAdd(Guid.Parse(movie.Id), new List<QueuedMedia>());

        // Queue the movie for analysis
        var maxCreditsDuration = Plugin.Instance!.Configuration.MaximumMovieCreditsDuration;
        _queuedEpisodes[Guid.Parse(movie.Id)].Add(new QueuedMedia()
        {
            SeriesName = movie.Name,
            SeasonNumber = 0,
            ItemId = Guid.Parse(movie.Id),
            Name = movie.Name,
            Path = movie.Path,
            Duration = Convert.ToInt32(duration),
            IntroFingerprintEnd = Convert.ToInt32(fingerprintDuration),
            CreditsFingerprintStart = Convert.ToInt32(duration - maxCreditsDuration),
            IsEpisode = false,
        });

        Plugin.Instance!.TotalQueued++;
    }

    /// <summary>
    /// Verify that a collection of queued media items still exist in Jellyfin and in storage.
    /// This is done to ensure that we don't analyze items that were deleted between the call to GetMediaItems() and popping them from the queue.
    /// </summary>
    /// <param name="candidates">Queued media items.</param>
    /// <param name="mode">Analysis mode.</param>
    /// <returns>Media items that have been verified to exist in Jellyfin and in storage.</returns>
    public (ReadOnlyCollection<QueuedMedia> VerifiedItems, bool AnyUnanalyzed)
        VerifyQueue(ReadOnlyCollection<QueuedMedia> candidates, AnalysisMode mode)
    {
        var unanalyzed = false;
        var verified = new List<QueuedMedia>();
        var blacklisted = Plugin.Instance!.Blacklist;

        foreach (var candidate in candidates)
        {
            try
            {
                var path = Plugin.Instance!.GetItemPath(candidate.ItemId);

                if (File.Exists(path))
                {
                    var timestamps = Plugin.Instance!.GetMediaSegmentsById(candidate.ItemId, mode);

                    if (!timestamps.ContainsKey(candidate.ItemId) && !blacklisted.Any(s => s.ItemId == candidate.ItemId && s.Type == (mode == AnalysisMode.Introduction ? MediaSegmentType.Intro : MediaSegmentType.Outro)))
                    {
                        unanalyzed = true;
                    }
                    else
                    {
                        candidate.IsAnalyzed = true;
                    }

                    verified.Add(candidate);
                }
            }
            catch (Exception ex)
            {
                _logger.LogDebug(
                    "Skipping {Mode} analysis of {Name} ({Id}): {Exception}",
                    mode,
                    candidate.Name,
                    candidate.ItemId,
                    ex);
            }
        }

        return (verified.AsReadOnly(), unanalyzed);
    }
}
