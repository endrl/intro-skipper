using System;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Data.Enums;
using MediaBrowser.Controller.Entities.Movies;
using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Controller.Library;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.Tasks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.MediaAnalyzer;

/// <summary>
/// Act on changes of the jellyfin library.
/// </summary>
public sealed class LibraryChangedEntrypoint : IHostedService, IDisposable
{
    private readonly ILibraryManager _libraryManager;
    private readonly ITaskManager _taskManager;
    private readonly ILogger<LibraryChangedEntrypoint> _logger;
    private readonly ILoggerFactory _loggerFactory;
    private Timer _queueTimer;
    private bool _analyzeAgain;

    /// <summary>
    /// Initializes a new instance of the <see cref="LibraryChangedEntrypoint"/> class.
    /// </summary>
    /// <param name="libraryManager">Library manager.</param>
    /// <param name="taskManager">Task manager.</param>
    /// <param name="logger">Logger.</param>
    /// <param name="loggerFactory">Logger factory.</param>
    public LibraryChangedEntrypoint(
        ILibraryManager libraryManager,
        ITaskManager taskManager,
        ILogger<LibraryChangedEntrypoint> logger,
        ILoggerFactory loggerFactory)
    {
        _libraryManager = libraryManager;
        _taskManager = taskManager;
        _logger = logger;
        _loggerFactory = loggerFactory;

        _queueTimer = new Timer(
         OnQueueTimerCallback,
         null,
         Timeout.InfiniteTimeSpan,
         Timeout.InfiniteTimeSpan);
    }

    /// <inheritdoc/>
    public Task StartAsync(CancellationToken cancellationToken)
    {
        _libraryManager.ItemAdded += LibraryManagerItemAdded;
        _libraryManager.ItemUpdated += LibraryManagerItemUpdated;
        _libraryManager.ItemRemoved += LibraryManagerItemRemoved;
        FFmpegWrapper.Logger = _logger;

        return Task.CompletedTask;
    }

    /// <inheritdoc/>
    public Task StopAsync(CancellationToken cancellationToken)
    {
        _libraryManager.ItemAdded -= LibraryManagerItemAdded;
        _libraryManager.ItemUpdated -= LibraryManagerItemUpdated;
        _libraryManager.ItemRemoved -= LibraryManagerItemRemoved;
        return Task.CompletedTask;
    }

    /// <summary>
    /// Delete segments for itemid when library removed it.
    /// </summary>
    /// <param name="sender">The sending entity.</param>
    /// <param name="itemChangeEventArgs">The <see cref="ItemChangeEventArgs"/>.</param>
    private void LibraryManagerItemRemoved(object? sender, ItemChangeEventArgs itemChangeEventArgs)
    {
        if (itemChangeEventArgs.Item is not Movie and not Episode)
        {
            return;
        }

        if (itemChangeEventArgs.Item.LocationType == LocationType.Virtual)
        {
            return;
        }

        _ = Plugin.Instance!.GetMetadataDb().DeleteSegments(itemChangeEventArgs.Item.Id, null);
    }

    /// <summary>
    /// Library item was added.
    /// </summary>
    /// <param name="sender">The sending entity.</param>
    /// <param name="itemChangeEventArgs">The <see cref="ItemChangeEventArgs"/>.</param>
    private void LibraryManagerItemAdded(object? sender, ItemChangeEventArgs itemChangeEventArgs)
    {
        if (!Plugin.Instance!.Configuration.RunAfterAddOrUpdateEvent)
        {
            return;
        }

        // Don't do anything if it's not a supported media type
        if (itemChangeEventArgs.Item is not Movie and not Episode)
        {
            return;
        }

        if (itemChangeEventArgs.Item.LocationType == LocationType.Virtual)
        {
            return;
        }

        StartTimer();
    }

    /// <summary>
    /// TaskManager task ended.
    /// </summary>
    /// <param name="sender">The sending entity.</param>
    /// <param name="eventArgs">The <see cref="TaskCompletionEventArgs"/>.</param>
    private void TaskManagerTaskCompleted(object? sender, TaskCompletionEventArgs eventArgs)
    {
        var result = eventArgs.Result;

        if (!Plugin.Instance!.Configuration.RunAfterLibraryScan)
        {
            return;
        }

        if (result.Key != "RefreshLibrary")
        {
            return;
        }

        if (result.Status != TaskCompletionStatus.Completed)
        {
            return;
        }

        StartTimer();
    }

    /// <summary>
    /// Library item was updated.
    /// </summary>
    /// <param name="sender">The sending entity.</param>
    /// <param name="itemChangeEventArgs">The <see cref="ItemChangeEventArgs"/>.</param>
    private void LibraryManagerItemUpdated(object? sender, ItemChangeEventArgs itemChangeEventArgs)
    {
        if (!Plugin.Instance!.Configuration.RunAfterAddOrUpdateEvent)
        {
            return;
        }

        // Don't do anything if it's not a supported media type
        if (itemChangeEventArgs.Item is not Movie and not Episode)
        {
            return;
        }

        if (itemChangeEventArgs.Item.LocationType == LocationType.Virtual)
        {
            return;
        }

        StartTimer();
    }

    /// <summary>
    /// Start or restart timer to debounce analyzing.
    /// </summary>
    private void StartTimer()
    {
        if (Plugin.Instance!.AnalysisRunning)
        {
            _analyzeAgain = true;
        }
        else
        {
            _logger.LogInformation("Media Library changed, analyzis will start soon!");
            _queueTimer.Change(TimeSpan.FromMilliseconds(15000), Timeout.InfiniteTimeSpan);
        }
    }

    /// <summary>
    /// Wait for timer callback to be completed.
    /// </summary>
    private void OnQueueTimerCallback(object? state)
    {
        try
        {
            OnQueueTimerCallbackInternal();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in OnQueueTimerCallbackInternal");
        }
    }

    /// <summary>
    /// Wait for timer to be completed.
    /// </summary>
    private void OnQueueTimerCallbackInternal()
    {
        _logger.LogInformation("Timer elapsed - start analyzing");
        Plugin.Instance!.AnalysisRunning = true;
        var progress = new Progress<double>();
        var cancellationToken = new CancellationToken(false);

        // intro
        var introBaseAnalyzer = new BaseItemAnalyzerTask(
            MediaSegmentType.Intro,
            _loggerFactory.CreateLogger<AnalyzeMedia>(),
            _loggerFactory,
            _libraryManager);

        introBaseAnalyzer.AnalyzeItems(progress, cancellationToken);

        // outro
        var outroBaseAnalyzer = new BaseItemAnalyzerTask(
            MediaSegmentType.Outro,
            _loggerFactory.CreateLogger<AnalyzeMedia>(),
            _loggerFactory,
            _libraryManager);

        outroBaseAnalyzer.AnalyzeItems(progress, cancellationToken);

        Plugin.Instance!.AnalysisRunning = false;

        // we might need to analyze again
        if (_analyzeAgain)
        {
            _logger.LogInformation("Analyzing ended, but we need to analyze again!");
            _analyzeAgain = false;
            StartTimer();
        }
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        _queueTimer.Dispose();
    }
}
