using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using MediaBrowser.Controller.Library;
using MediaBrowser.Model.Tasks;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.MediaAnalyzer;

/// <summary>
/// Analyze all television episodes for introduction sequences.
/// </summary>
public class AnalyzeMedia : IScheduledTask
{
    private readonly ILoggerFactory _loggerFactory;

    private readonly ILibraryManager _libraryManager;

    /// <summary>
    /// Initializes a new instance of the <see cref="AnalyzeMedia"/> class.
    /// </summary>
    /// <param name="loggerFactory">Logger factory.</param>
    /// <param name="libraryManager">Library manager.</param>
    public AnalyzeMedia(
        ILoggerFactory loggerFactory,
        ILibraryManager libraryManager)
    {
        _loggerFactory = loggerFactory;
        _libraryManager = libraryManager;
    }

    /// <summary>
    /// Gets the task name.
    /// </summary>
    public string Name => "Analyze Media";

    /// <summary>
    /// Gets the task category.
    /// </summary>
    public string Category => "Media Analyzer";

    /// <summary>
    /// Gets the task description.
    /// </summary>
    public string Description => "Analyzes the audio of all television episodes to find introduction and credits sequences.";

    /// <summary>
    /// Gets the task key.
    /// </summary>
    public string Key => "JFPMediaAnalyzerAnalyzeMedia";

    /// <summary>
    /// Analyze all episodes in the queue. Only one instance of this task should be run at a time.
    /// </summary>
    /// <param name="progress">Task progress.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Task.</returns>
    public Task ExecuteAsync(IProgress<double> progress, CancellationToken cancellationToken)
    {
        if (_libraryManager is null)
        {
            throw new InvalidOperationException("Library manager was null");
        }

        if (Plugin.Instance!.AnalysisRunning)
        {
            return Task.CompletedTask;
        }
        else
        {
            Plugin.Instance!.AnalysisRunning = true;
        }

        // load blacklist
        Plugin.Instance!.GetBlacklistFromDb();

        // intro
        var introBaseAnalyzer = new BaseItemAnalyzerTask(
            AnalysisMode.Introduction,
            _loggerFactory.CreateLogger<AnalyzeMedia>(),
            _loggerFactory,
            _libraryManager);

        introBaseAnalyzer.AnalyzeItems(progress, cancellationToken);

        // reset progress
        progress.Report(0);

        // outro
        var outroBaseAnalyzer = new BaseItemAnalyzerTask(
            AnalysisMode.Credits,
            _loggerFactory.CreateLogger<AnalyzeMedia>(),
            _loggerFactory,
            _libraryManager);

        outroBaseAnalyzer.AnalyzeItems(progress, cancellationToken);

        // save blacklist to db
        Plugin.Instance!.SaveBlacklist();

        // reset blacklist
        Plugin.Instance!.Blacklist.Clear();

        Plugin.Instance!.AnalysisRunning = false;

        return Task.CompletedTask;
    }

    /// <summary>
    /// Get task triggers.
    /// </summary>
    /// <returns>Task triggers.</returns>
    public IEnumerable<TaskTriggerInfo> GetDefaultTriggers()
    {
        return Array.Empty<TaskTriggerInfo>();
    }
}
