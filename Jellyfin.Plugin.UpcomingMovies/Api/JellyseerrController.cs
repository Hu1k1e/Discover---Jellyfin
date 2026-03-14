using System;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.UpcomingMovies.Api;

/// <summary>
/// Full request body sent from the Discover page when the user submits the Request modal.
/// </summary>
public class JellyseerrRequestBody
{
    /// <summary>Gets or sets the TMDB movie ID to request.</summary>
    [JsonPropertyName("tmdbId")]
    public int TmdbId { get; set; }

    /// <summary>Gets or sets the media type (movie or tv).</summary>
    [JsonPropertyName("mediaType")]
    public string MediaType { get; set; } = "movie";

    /// <summary>Gets or sets the Radarr server ID selected by the user.</summary>
    [JsonPropertyName("serverId")]
    public int? ServerId { get; set; }

    /// <summary>Gets or sets the quality profile ID selected by the user.</summary>
    [JsonPropertyName("profileId")]
    public int? ProfileId { get; set; }

    /// <summary>Gets or sets the root folder path selected by the user.</summary>
    [JsonPropertyName("rootFolder")]
    public string? RootFolder { get; set; }
}

/// <summary>
/// API controller that acts as a secure server-side proxy for Jellyseerr API requests.
/// The Jellyseerr API key never leaves the server.
/// </summary>
[ApiController]
[Route("UpcomingMovies/jellyseerr")]
[Authorize]
public class JellyseerrController : ControllerBase
{
    private readonly ILogger<JellyseerrController> _logger;
    private readonly IHttpClientFactory _httpClientFactory;

    /// <summary>
    /// Initializes a new instance of the <see cref="JellyseerrController"/> class.
    /// </summary>
    /// <param name="logger">Logger instance.</param>
    /// <param name="httpClientFactory">HTTP client factory from DI.</param>
    public JellyseerrController(ILogger<JellyseerrController> logger, IHttpClientFactory httpClientFactory)
    {
        _logger = logger;
        _httpClientFactory = httpClientFactory;
    }

    /// <summary>
    /// Returns the configured Radarr instances from Jellyseerr, including quality profiles and root folders.
    /// Used by the Request modal to populate dropdowns.
    /// </summary>
    [HttpGet("radarr")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status502BadGateway)]
    public async Task<IActionResult> GetRadarrInstances()
    {
        try
        {
            var config = Plugin.Instance?.Configuration;
            if (config is null || string.IsNullOrWhiteSpace(config.JellyseerrUrl) || string.IsNullOrWhiteSpace(config.JellyseerrApiKey))
            {
                return BadRequest(new { error = "Jellyseerr URL or API key is not configured." });
            }

            var baseUrl = config.JellyseerrUrl.TrimEnd('/');
            var client = _httpClientFactory.CreateClient();
            using var request = new HttpRequestMessage(HttpMethod.Get, $"{baseUrl}/api/v1/settings/radarr");
            request.Headers.Add("X-Api-Key", config.JellyseerrApiKey);

            var response = await client.SendAsync(request).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                var body = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                _logger.LogWarning("[UpcomingMovies] Jellyseerr /radarr returned {StatusCode}: {Body}", response.StatusCode, body);
                return StatusCode((int)response.StatusCode, new { error = $"Jellyseerr error: {body}" });
            }

            var json = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
            return Content(json, "application/json");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[UpcomingMovies] Error in GetRadarrInstances");
            return StatusCode(StatusCodes.Status502BadGateway, new { error = "Could not reach Jellyseerr. Check the URL in plugin settings." });
        }
    }

    /// <summary>
    /// Proxies a media request to the configured Jellyseerr instance.
    /// Supports full advanced options: serverId, profileId, rootFolder.
    /// </summary>
    /// <param name="body">Request body containing TMDB ID, media type, and optional advanced options.</param>
    /// <returns>Jellyseerr API response.</returns>
    [HttpPost("request")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status502BadGateway)]
    public async Task<IActionResult> SubmitRequest([FromBody] JellyseerrRequestBody body)
    {
        try
        {
            var config = Plugin.Instance?.Configuration;
            if (config is null || string.IsNullOrWhiteSpace(config.JellyseerrUrl) || string.IsNullOrWhiteSpace(config.JellyseerrApiKey))
            {
                return BadRequest(new { error = "Jellyseerr URL or API key is not configured. Please set them in the plugin settings." });
            }

            if (body.TmdbId <= 0)
            {
                return BadRequest(new { error = "Invalid TMDB ID." });
            }

            var baseUrl = config.JellyseerrUrl.TrimEnd('/');
            var endpoint = $"{baseUrl}/api/v1/request";

            // Build the Jellyseerr request payload -- include advanced options if provided
            var payloadObj = new
            {
                mediaType = body.MediaType,
                mediaId = body.TmdbId,
                is4k = false,
                serverId = body.ServerId,
                profileId = body.ProfileId,
                rootFolder = body.RootFolder
            };

            var payload = JsonSerializer.Serialize(payloadObj);
            _logger.LogInformation("[UpcomingMovies] Submitting Jellyseerr request: {Payload}", payload);

            var client = _httpClientFactory.CreateClient();
            using var request = new HttpRequestMessage(HttpMethod.Post, endpoint);
            request.Headers.Add("X-Api-Key", config.JellyseerrApiKey);
            request.Content = new StringContent(payload, Encoding.UTF8, "application/json");

            var response = await client.SendAsync(request).ConfigureAwait(false);

            if (!response.IsSuccessStatusCode)
            {
                var errorBody = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                _logger.LogWarning("[UpcomingMovies] Jellyseerr returned {StatusCode}: {Body}", response.StatusCode, errorBody);
                return StatusCode((int)response.StatusCode, new { error = $"Jellyseerr returned an error: {errorBody}" });
            }

            var json = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
            return Content(json, "application/json");
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(ex, "[UpcomingMovies] Failed to reach Jellyseerr at {Url}", Plugin.Instance?.Configuration?.JellyseerrUrl);
            return StatusCode(StatusCodes.Status502BadGateway, new { error = "Could not reach Jellyseerr. Check that the URL is correct and the server is reachable." });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[UpcomingMovies] Unhandled exception in SubmitRequest");
            return StatusCode(StatusCodes.Status500InternalServerError, new { error = ex.Message });
        }
    }
}
