using System.Linq;
using Jellyfin.Plugin.PosterOverlays.Overlay;
using MediaBrowser.Common.Api;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace Jellyfin.Plugin.PosterOverlays.Api;

/// <summary>
/// Endpoints used by the plugin settings page.
/// </summary>
[ApiController]
[Authorize(Policy = Policies.RequiresElevation)]
[Route("PosterOverlays")]
public class PosterOverlaysController : ControllerBase
{
    private readonly OverlayService _service;

    /// <summary>
    /// Initializes a new instance of the <see cref="PosterOverlaysController"/> class.
    /// </summary>
    /// <param name="service">Overlay service.</param>
    public PosterOverlaysController(OverlayService service)
    {
        _service = service;
    }

    /// <summary>
    /// Lists available templates and where custom templates go.
    /// </summary>
    /// <returns>Templates and the custom template folder.</returns>
    [HttpGet("Templates")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public ActionResult<object> GetTemplates()
    {
        var templates = _service.LoadTemplates().Select(t => new
        {
            t.Name,
            t.Source,
            Description = t.Root.TryGetProperty("description", out var d) && d.ValueKind == System.Text.Json.JsonValueKind.String ? d.GetString() : null,
        });
        return Ok(new { Templates = templates, CustomFolder = OverlayService.TemplatesPath });
    }
}
