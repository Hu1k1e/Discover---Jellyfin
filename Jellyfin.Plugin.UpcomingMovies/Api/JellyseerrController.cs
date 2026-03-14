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
/// Request body sent from the Discover page when the user clicks "Request on Jellyseerr".
/// </summary>
public class JellyseerrRequestBody
{
    /// <summary>Gets or sets the TMDB movie ID to request.</summary>
    [JsonPropertyName("tmdbId")]
    public int TmdbId { get; set; }

    /// <summary>Gets or sets the media type (movie or tv).</summary>
    [JsonPropertyName("mediaType")]
    public string MediaType { get; set; } = "movie";
}

/// <summary>
/// API controller that acts as a secure server-side proxy for Jellyseerr API requests.
/// The Jellyseerr API key never leaves the server.
/// </summary>
[ApiController]
[Route("UpcomingMovies/jellyseerr")]
[Authorize(Policy = "DefaultAuthorization")]
public class JellyseerrController : ControllerBase
{
    private static readonly HttpClient _httpClient = new HttpClient();
    private readonly ILogger<JellyseerrController> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="JellyseerrController"/> class.
    /// </summary>
    /// <param name="logger">Logger instance.</param>
    public JellyseerrController(ILogger<JellyseerrController> logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// Proxies a media request to the configured Jellyseerr instance.
    /// </summary>
    /// <param name="body">Request body containing TMDB ID and media type.</param>
    /// <returns>Jellyseerr API response.</returns>
    [HttpPost("request")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status502BadGateway)]
    public async Task<IActionResult> SubmitRequest([FromBody] JellyseerrRequestBody body)
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

        try
        {
            var baseUrl = config.JellyseerrUrl.TrimEnd('/');
            var endpoint = $"{baseUrl}/api/v1/request";

            var payload = JsonSerializer.Serialize(new
            {
                mediaType = body.MediaType,
                mediaId = body.TmdbId,
                is4k = false
            });

            using var request = new HttpRequestMessage(HttpMethod.Post, endpoint);
            request.Headers.Add("X-Api-Key", config.JellyseerrApiKey);
            request.Content = new StringContent(payload, Encoding.UTF8, "application/json");

            var response = await _httpClient.SendAsync(request).ConfigureAwait(false);

            if (!response.IsSuccessStatusCode)
            {
                var errorBody = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                _logger.LogWarning("Jellyseerr returned {StatusCode}: {Body}", response.StatusCode, errorBody);
                return StatusCode((int)response.StatusCode, new { error = $"Jellyseerr returned an error: {errorBody}" });
            }

            var json = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
            return Content(json, "application/json");
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(ex, "Failed to reach Jellyseerr at {Url}", config.JellyseerrUrl);
            return StatusCode(StatusCodes.Status502BadGateway, new { error = "Could not reach Jellyseerr. Check that the URL is correct and the server is reachable." });
        }
    }
}
