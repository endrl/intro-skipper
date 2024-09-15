using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Net.Mime;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Data.Enums;
using MediaBrowser.Controller;
using MediaBrowser.Controller.Library;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.MediaAnalyzer.Controllers;

/// <summary>
/// PluginEdl controller.
/// </summary>
[Authorize(Policy = "RequiresElevation")]
[ApiController]
[Produces(MediaTypeNames.Application.Json)]
[Route("PluginMediaAnalyzer")]
public class MediaAnalyzerController : ControllerBase
{
    private readonly ILoggerFactory _loggerFactory;
    private readonly ILibraryManager _libraryManager;
    private readonly IMediaSegmentManager _mediaSegmentManager;

    /// <summary>
    /// Initializes a new instance of the <see cref="MediaAnalyzerController"/> class.
    /// </summary>
    /// <param name="loggerFactory">Logger factory.</param>
    /// <param name="libraryManager">LibraryManager.</param>
    /// <param name="mediaSegmentManager">MediaSegmentsManager.</param>
    public MediaAnalyzerController(
        ILoggerFactory loggerFactory,
        ILibraryManager libraryManager,
        IMediaSegmentManager mediaSegmentManager)
    {
        _loggerFactory = loggerFactory;
        _libraryManager = libraryManager;
        _mediaSegmentManager = mediaSegmentManager;
    }

    /// <summary>
    /// Plugin meta endpoint.
    /// </summary>
    /// <returns>The version info.</returns>
    [HttpGet]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public JsonResult GetPluginMetadata()
    {
        var json = new
        {
            version = Plugin.Instance!.Version.ToString(3),
        };

        return new JsonResult(json);
    }

    /// <summary>
    /// Run analyzer based on itemIds and params and returns the segments + metadata.
    /// </summary>
    /// <param name="itemIds">ItemIds.</param>
    /// <param name="analyzerTypes">Analyzers to use.</param>
    /// <param name="mode">Segment Type to search for.</param>
    /// <returns>The found segments.</returns>
    [HttpGet("Analyzers")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<JsonResult> AnalyzeIds(
        [FromQuery, Required] Guid[] itemIds,
        [FromQuery, Required] AnalyzerType[] analyzerTypes,
        [FromQuery, Required] MediaSegmentType mode)
    {
        var queueManager = new QueueManager(_loggerFactory.CreateLogger<QueueManager>(), _libraryManager, MediaSegmentType.Intro);

        var errors = new JsonArray();
        var analyzedItems = new Dictionary<Guid, Segment>();
        var metadatas = new Dictionary<Guid, SegmentMetadata>();
        var jsonObject = new JsonObject();

        // get ItemIds
        var mediaItems = queueManager.GetMediaItemsById(itemIds);
        // setup analyzers
        foreach (var (key, media) in mediaItems)
        {
            var items = media.AsReadOnly();
            var totalItems = mediaItems.Count;
            var first = items[0];

            var analyzers = new Collection<IMediaFileAnalyzer>();

            if (analyzerTypes.Contains(AnalyzerType.ChapterAnalyzer))
            {
                analyzers.Add(new ChapterAnalyzer(_loggerFactory.CreateLogger<ChapterAnalyzer>()));
            }

            // Movies don't use chromparint analyzer
            if (first.IsEpisode() && analyzerTypes.Contains(AnalyzerType.ChromaprintAnalyzer))
            {
                if (items.Count == 1)
                {
                    errors.Add($"Chromaprint needs at least two media files to compare, one provided: {first.GetFullName}");
                }
                else
                {
                    analyzers.Add(new ChromaprintAnalyzer(_loggerFactory.CreateLogger<ChromaprintAnalyzer>()));
                }
            }

            if (mode == MediaSegmentType.Outro && analyzerTypes.Contains(AnalyzerType.BlackFrameAnalyzer))
            {
                analyzers.Add(new BlackFrameAnalyzer(_loggerFactory.CreateLogger<BlackFrameAnalyzer>()));
            }

            // Use each analyzer to find skippable ranges in all media files, removing successfully
            // analyzed items from the queue.
            foreach (var analyzer in analyzers)
            {
                var cancellationToken = default(CancellationToken);
                var (notAnalyzed, analyzed, metadata) = await analyzer.AnalyzeMediaFilesAsync(items, mode, cancellationToken);

                var atype = analyzer is BlackFrameAnalyzer ? "BlackFrameAnalyzer" : analyzer is ChromaprintAnalyzer ? "ChromaprintAnalyzer" : analyzer is ChapterAnalyzer ? "ChapterAnalyzer" : throw new NotImplementedException("Unknown Analyzer type");
                jsonObject.Add(atype, BuildAnalyzerOutput(analyzed, metadata));
            }
        }

        jsonObject.Add("Errors", errors);

        return new JsonResult(jsonObject);
    }

    /// <summary>
    /// Fingerprint the provided episode and returns the uncompressed fingerprint data points.
    /// </summary>
    /// <param name="id">Episode id.</param>
    /// <param name="mode">Type Intro or Outro.</param>
    /// <returns>Read only collection of fingerprint points.</returns>
    [HttpGet("Chromaprint/{Id}")]
    public ActionResult<uint[]> GetMediaFingerprint(
        [FromRoute, Required] Guid id,
        [FromQuery, Required] MediaSegmentType mode)
    {
        var queueManager = new QueueManager(_loggerFactory.CreateLogger<QueueManager>(), _libraryManager, mode);
        var queuedMedia = queueManager.GetMediaItemsById([id]);

        // Search through all queued episodes to find the requested id
        foreach (var season in queuedMedia)
        {
            foreach (var needle in season.Value)
            {
                if (needle.ItemId == id)
                {
                    return FFmpegWrapper.Fingerprint(needle, mode);
                }
            }
        }

        return NotFound();
    }

    private static JsonObject BuildAnalyzerOutput(ReadOnlyDictionary<Guid, Segment> segments, ReadOnlyDictionary<Guid, SegmentMetadata> metadatas)
    {
        var itemsObject = new JsonObject();
        Dictionary<Guid, SegmentMetadata> metadataLocal = metadatas.ToDictionary();

        foreach (var item in segments)
        {
            if (metadatas.TryGetValue(item.Key, out var metadata))
            {
                metadataLocal.Remove(item.Key);

                var json = new
                {
                    Segment = item.Value,
                    Metadata = metadata
                };
                itemsObject.Add(item.Key.ToString(), json.ToString());
            }
        }

        // we may have more metadata
        foreach (var item in metadataLocal)
        {
            var json = new
            {
                Metadata = item.Value
            };
            itemsObject.Add(item.Key.ToString(), json.ToString());
        }

        return itemsObject;
    }
}
