using System;
using System.Collections.Generic;
using System.IO;
using Jellyfin.Plugin.MediaAnalyzer.Configuration;
using MediaBrowser.Common.Configuration;
using MediaBrowser.Common.Plugins;
using MediaBrowser.Controller;
using MediaBrowser.Controller.Configuration;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Persistence;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.Plugins;
using MediaBrowser.Model.Serialization;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.MediaAnalyzer;

/// <summary>
/// TV Show Intro Skip plugin. Uses audio analysis to find common sequences of audio shared between episodes.
/// </summary>
public class Plugin : BasePlugin<PluginConfiguration>, IHasWebPages
{
    private IXmlSerializer _xmlSerializer;
    private ILibraryManager _libraryManager;
    private IItemRepository _itemRepository;
    private IMediaSegmentManager _mediaSegmentsManager;
    private ILogger<Plugin> _logger;
    private string _pluginCachePath;
    private string _pluginDbPath;
    private SegmentMetadataDb _segmentMetadataDb;
    private MediaSegmentsDb _mediasegmentsDb;

    /// <summary>
    /// Initializes a new instance of the <see cref="Plugin"/> class.
    /// </summary>
    /// <param name="applicationPaths">Instance of the <see cref="IApplicationPaths"/> interface.</param>
    /// <param name="xmlSerializer">Instance of the <see cref="IXmlSerializer"/> interface.</param>
    /// <param name="serverConfiguration">Server configuration manager.</param>
    /// <param name="libraryManager">Library manager.</param>
    /// <param name="itemRepository">Item repository.</param>
    /// <param name="mediaSegmentsManager">Segments manager.</param>
    /// <param name="logger">Logger.</param>
    public Plugin(
        IApplicationPaths applicationPaths,
        IXmlSerializer xmlSerializer,
        IServerConfigurationManager serverConfiguration,
        ILibraryManager libraryManager,
        IItemRepository itemRepository,
        IMediaSegmentManager mediaSegmentsManager,
        ILogger<Plugin> logger)
        : base(applicationPaths, xmlSerializer)
    {
        Instance = this;

        _xmlSerializer = xmlSerializer;
        _libraryManager = libraryManager;
        _itemRepository = itemRepository;
        _mediaSegmentsManager = mediaSegmentsManager;
        _logger = logger;

        FFmpegPath = serverConfiguration.GetEncodingOptions().EncoderAppPathDisplay;

        _pluginCachePath = Path.Join(applicationPaths.CachePath, "JFPMediaAnalyzer");
        _pluginDbPath = Path.Join(applicationPaths.PluginConfigurationsPath, "mediaanalyzer.db");

        FingerprintCachePath = Path.Join(_pluginCachePath, "chromaprints");

        // Create the base & cache directories (if needed).
        if (!Directory.Exists(FingerprintCachePath))
        {
            Directory.CreateDirectory(FingerprintCachePath);
        }

        // Create and migrate db
        using (var context = new MediaAnalyzerDbContext(this._pluginDbPath))
        {
            context.ApplyMigrations();
        }

        // init db interfaces
        _segmentMetadataDb = new SegmentMetadataDb(this._pluginDbPath);
        _mediasegmentsDb = new MediaSegmentsDb(_mediaSegmentsManager);

        ConfigurationChanged += OnConfigurationChanged;
    }

    /// <summary>
    /// Gets or sets a value indicating whether analysis is running.
    /// </summary>
    public bool AnalysisRunning { get; set; } = false;

    /// <summary>
    /// Gets the directory to cache fingerprints in.
    /// </summary>
    public string FingerprintCachePath { get; private set; }

    /// <summary>
    /// Gets the full path to FFmpeg.
    /// </summary>
    public string FFmpegPath { get; private set; }

    /// <inheritdoc />
    public override string Name => "Media Analyzer";

    /// <inheritdoc />
    public override Guid Id => Guid.Parse("80885677-DACB-461B-AC97-EE7E971288AA");

    /// <summary>
    /// Gets the plugin instance.
    /// </summary>
    public static Plugin? Instance { get; private set; }

    /// <inheritdoc />
    public IEnumerable<PluginPageInfo> GetPages()
    {
        return new[]
        {
            new PluginPageInfo
            {
                Name = this.Name,
                EmbeddedResourcePath = GetType().Namespace + ".Configuration.configPage.html"
            },
            new PluginPageInfo
            {
                Name = "visualizer.js",
                EmbeddedResourcePath = GetType().Namespace + ".Configuration.visualizer.js"
            }
        };
    }

    internal BaseItem? GetItem(Guid id)
    {
        return _libraryManager.GetItemById(id);
    }

    /// <summary>
    /// Gets the full path for an item.
    /// </summary>
    /// <param name="id">Item id.</param>
    /// <returns>Full path to item.</returns>
    internal string GetItemPath(Guid id)
    {
        var baseItem = GetItem(id);
        if (baseItem is not null)
        {
            return baseItem.Path;
        }
        else
        {
            return string.Empty;
        }
    }

    /// <summary>
    /// Gets all chapters for this item.
    /// </summary>
    /// <param name="id">Item id.</param>
    /// <returns>List of chapters.</returns>
    internal List<ChapterInfo> GetChapters(Guid id)
    {
        return _itemRepository.GetChapters(GetItem(id));
    }

    /// <summary>
    /// Get metadata db.
    /// </summary>
    /// <returns>Instance of db.</returns>
    public SegmentMetadataDb GetMetadataDb()
    {
        return this._segmentMetadataDb;
    }

    /// <summary>
    /// Get segments db.
    /// </summary>
    /// <returns>Instance of db.</returns>
    public MediaSegmentsDb GetMediaSegmentsDb()
    {
        return this._mediasegmentsDb;
    }

    private void OnConfigurationChanged(object? sender, BasePluginConfiguration e)
    {
        if (this.Configuration.ResetBlacklist == true)
        {
            _ = this.GetMetadataDb().DeletePreventAnalyzeSegments(null);
            this.Configuration.ResetBlacklist = false;
            this.SaveConfiguration(this.Configuration);
        }
    }

    /// <summary>
    /// Called just before the plugin is uninstalled from the server.
    /// </summary>
    public override void OnUninstalling()
    {
        // Delete cache data
        if (Directory.Exists(_pluginCachePath))
        {
            Directory.Delete(_pluginCachePath, true);
        }
    }
}
