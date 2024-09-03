namespace Jellyfin.Plugin.MediaAnalyzer;

using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MediaBrowser.Controller.Library;
using Microsoft.Extensions.Logging;

/// <summary>
/// Common code shared by all media item analyzer tasks.
/// </summary>
public class BaseItemAnalyzerTask
{
    private readonly AnalysisMode _analysisMode;

    private readonly ILogger _logger;

    private readonly ILoggerFactory _loggerFactory;

    private readonly ILibraryManager _libraryManager;

    /// <summary>
    /// Initializes a new instance of the <see cref="BaseItemAnalyzerTask"/> class.
    /// </summary>
    /// <param name="mode">Analysis mode.</param>
    /// <param name="logger">Task logger.</param>
    /// <param name="loggerFactory">Logger factory.</param>
    /// <param name="libraryManager">Library manager.</param>
    public BaseItemAnalyzerTask(
        AnalysisMode mode,
        ILogger logger,
        ILoggerFactory loggerFactory,
        ILibraryManager libraryManager)
    {
        _analysisMode = mode;
        _logger = logger;
        _loggerFactory = loggerFactory;
        _libraryManager = libraryManager;
    }

    /// <summary>
    /// Analyze all media items on the server.
    /// </summary>
    /// <param name="progress">Progress.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public void AnalyzeItems(
        IProgress<double> progress,
        CancellationToken cancellationToken)
    {
        var queueManager = new QueueManager(
            _loggerFactory.CreateLogger<QueueManager>(),
            _libraryManager,
            _analysisMode);

        var queue = queueManager.GetMediaItems();

        var totalQueued = 0;
        foreach (var kvp in queue)
        {
            totalQueued += kvp.Value.Count;
        }

        if (totalQueued == 0)
        {
            return;
        }

        var totalProcessed = 0;
        var options = new ParallelOptions()
        {
            MaxDegreeOfParallelism = Plugin.Instance!.Configuration.MaxParallelism
        };

        Parallel.ForEach(queue, options, (season) =>
        {
            // Since the first run of the task can run for multiple hours, ensure that none
            // of the current media items were deleted from Jellyfin since the task was started.
            var verifiedItems = queueManager.VerifyQueue(
                season.Value.AsReadOnly(),
                this._analysisMode);

            var notBlacklistedItems = queueManager.FilterWithBlacklist(verifiedItems, this._analysisMode);

            var (episodes, unanalyzed) = queueManager.FilterWithSegments(notBlacklistedItems, this._analysisMode);

            if (episodes.Count == 0)
            {
                return;
            }

            var first = episodes[0];

            if (!unanalyzed)
            {
                if (first.IsEpisode)
                {
                    _logger.LogDebug(
                        "All episodes in {Name} season {Season} have already been analyzed for {AnalyzeType}",
                        first.SeriesName,
                        first.SeasonNumber,
                        this._analysisMode);
                }
                else
                {
                    _logger.LogDebug(
                        "Movie {Name} have already been analyzed for {AnalyzeType}",
                        first.Name,
                        this._analysisMode);
                }

                return;
            }

            try
            {
                if (cancellationToken.IsCancellationRequested)
                {
                    return;
                }

                var analyzed = AnalyzeItems(episodes, cancellationToken);
                Interlocked.Add(ref totalProcessed, analyzed);
            }
            catch (FingerprintException ex)
            {
                if (first.IsEpisode)
                {
                    _logger.LogWarning(
                        "Unable to analyze {Series} season {Season}: unable to fingerprint: {Ex}",
                        first.SeriesName,
                        first.SeasonNumber,
                        ex);
                }
                else
                {
                    _logger.LogDebug(
                        "Unable to analyze Movie {Name}: unable to fingerprint: {Ex}",
                        first.Name,
                        ex);
                }
            }

            progress.Report((totalProcessed * 100) / totalQueued);
        });
    }

    /// <summary>
    /// Analyze a group of media items for skippable segments.
    /// </summary>
    /// <param name="items">Media items to analyze.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Number of items that were successfully analyzed.</returns>
    private int AnalyzeItems(
        ReadOnlyCollection<QueuedMedia> items,
        CancellationToken cancellationToken)
    {
        var totalItems = items.Count;
        var first = items[0];

        if (first.IsEpisode)
        {
            // Only analyze specials (season 0) if the user has opted in.
            if (first.SeasonNumber == 0 && !Plugin.Instance!.Configuration.AnalyzeSeasonZero)
            {
                return 0;
            }

            _logger.LogInformation(
                "Analyzing {Count} files for {Type} from {Name} season {Season}",
                items.Count,
                this._analysisMode,
                first.SeriesName,
                first.SeasonNumber);
        }
        else
        {
            // we ignore movies intro run
            if (this._analysisMode == AnalysisMode.Credits)
            {
                _logger.LogInformation("Analyzing Movie (Outro): {Name}", first.Name);
            }
        }

        var analyzers = new Collection<IMediaFileAnalyzer>();

        analyzers.Add(new ChapterAnalyzer(_loggerFactory.CreateLogger<ChapterAnalyzer>()));

        // Movies don't use chromparint analyzer
        if (first.IsEpisode)
        {
            analyzers.Add(new ChromaprintAnalyzer(_loggerFactory.CreateLogger<ChromaprintAnalyzer>()));
        }

        if (this._analysisMode == AnalysisMode.Credits)
        {
            analyzers.Add(new BlackFrameAnalyzer(_loggerFactory.CreateLogger<BlackFrameAnalyzer>()));
        }

        // Use each analyzer to find skippable ranges in all media files, removing successfully
        // analyzed items from the queue.
        foreach (var analyzer in analyzers)
        {
            items = analyzer.AnalyzeMediaFiles(items, this._analysisMode, cancellationToken);
        }

        // Unanalyzed items should be blacklisted
        var blacklisted = items.Where(i => !i.SkipPreventAnalyzing).ToList();

        if (blacklisted.Count > 0 && Plugin.Instance!.Configuration.EnableBlacklist)
        {
            Plugin.Instance!.UpdateBlacklist(blacklisted, this._analysisMode);
        }

        return totalItems;
    }
}
