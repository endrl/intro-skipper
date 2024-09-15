using System.Net.Mime;
using System.Text;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.MediaAnalyzer.Controllers;

/// <summary>
/// Troubleshooting controller.
/// </summary>
[Authorize(Policy = "RequiresElevation")]
[ApiController]
[Produces(MediaTypeNames.Application.Json)]
[Route("JellyfinPluginIntroSkipSupport")]
public class TroubleshootingController : ControllerBase
{
    // private readonly IApplicationHost _applicationHost;
    private readonly ILogger<TroubleshootingController> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="TroubleshootingController"/> class.
    /// </summary>
    /// <param name="logger">Logger.</param>
    public TroubleshootingController(
        ILogger<TroubleshootingController> logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// Gets a Markdown formatted support bundle.
    /// </summary>
    /// <response code="200">Support bundle created.</response>
    /// <returns>Support bundle.</returns>
    [HttpGet("SupportBundle")]
    [Produces(MediaTypeNames.Text.Plain)]
    public ActionResult<string> GetSupportBundle()
    {
        var bundle = new StringBuilder();

        // bundle.Append("* Jellyfin version: ");
        // bundle.Append(_applicationHost.ApplicationVersionString);
        // bundle.Append('\n');

        var version = Plugin.Instance!.Version.ToString(3);

        bundle.Append("* Plugin version: ");
        bundle.Append(version);
        bundle.Append('\n');

        bundle.Append("* Warnings: `");
        bundle.Append(WarningManager.GetWarnings());
        bundle.Append("`\n");

        bundle.Append(FFmpegWrapper.GetChromaprintLogs());

        return bundle.ToString();
    }
}
