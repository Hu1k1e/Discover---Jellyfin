using System;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.UpcomingMovies.Api;

/// <summary>
/// API controller that acts as a secure server-side proxy for TMDB API requests.
/// The TMDB API key never leaves the server.
/// </summary>
[ApiController]
[Route("UpcomingMovies/tmdb")]
[Authorize(Policy = "DefaultAuthorization")]
public class TmdbController : ControllerBase
{
    private const string TmdbBaseUrl = "https://api.themoviedb.org/3";
    private static readonly HttpClient _httpClient = new HttpClient();
    private readonly ILogger<TmdbController> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="TmdbController"/> class.
    /// </summary>
    /// <param name="logger">Logger instance.</param>
    public TmdbController(ILogger<TmdbController> logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// Proxies a request to TMDB's /movie/upcoming endpoint.
    /// Returns upcoming movie listings for the Discover page.
    /// </summary>
    /// <param name="page">Page number for pagination (default 1).</param>
    /// <returns>TMDB upcoming movies JSON response.</returns>
    [HttpGet("upcoming")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status502BadGateway)]
    public async Task<IActionResult> GetUpcoming([FromQuery] int page = 1)
    {
        var apiKey = Plugin.Instance?.Configuration.TmdbApiKey;
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            return BadRequest(new { error = "TMDB API key is not configured. Please set it in the plugin settings." });
        }

        try
        {
            var url = $"{TmdbBaseUrl}/movie/upcoming?api_key={apiKey}&language=en-US&page={page}";
            var response = await _httpClient.GetAsync(url).ConfigureAwait(false);

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("TMDB /upcoming returned {StatusCode}", response.StatusCode);
                return StatusCode((int)response.StatusCode, new { error = "TMDB API returned an error." });
            }

            var json = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
            return Content(json, "application/json");
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(ex, "Failed to reach TMDB API for /upcoming");
            return StatusCode(StatusCodes.Status502BadGateway, new { error = "Could not reach TMDB API." });
        }
    }

    /// <summary>
    /// Proxies a request to TMDB's /movie/top_rated endpoint as a lightweight
    /// fallback for "recommendations" when personalised data is unavailable.
    /// The frontend enriches this with watch history genre filtering via the Jellyfin API.
    /// </summary>
    /// <param name="genreIds">Comma-separated list of TMDB genre IDs to filter by.</param>
    /// <param name="page">Page number for pagination (default 1).</param>
    /// <returns>TMDB discover movies JSON response.</returns>
    [HttpGet("recommendations")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status502BadGateway)]
    public async Task<IActionResult> GetRecommendations([FromQuery] string genreIds = "", [FromQuery] int page = 1)
    {
        var apiKey = Plugin.Instance?.Configuration.TmdbApiKey;
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            return BadRequest(new { error = "TMDB API key is not configured. Please set it in the plugin settings." });
        }

        try
        {
            var genreFilter = string.IsNullOrWhiteSpace(genreIds) ? string.Empty : $"&with_genres={genreIds}";
            var url = $"{TmdbBaseUrl}/discover/movie?api_key={apiKey}&language=en-US&sort_by=vote_average.desc&vote_count.gte=200&page={page}{genreFilter}";
            var response = await _httpClient.GetAsync(url).ConfigureAwait(false);

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("TMDB /discover returned {StatusCode}", response.StatusCode);
                return StatusCode((int)response.StatusCode, new { error = "TMDB API returned an error." });
            }

            var json = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
            return Content(json, "application/json");
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(ex, "Failed to reach TMDB API for /discover");
            return StatusCode(StatusCodes.Status502BadGateway, new { error = "Could not reach TMDB API." });
        }
    }

    /// <summary>
    /// Returns the current plugin configuration visible to authenticated clients.
    /// Only exposes non-sensitive display settings (no API keys).
    /// </summary>
    /// <returns>Public plugin configuration subset.</returns>
    [HttpGet("config")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public IActionResult GetPublicConfig()
    {
        var config = Plugin.Instance?.Configuration;
        if (config is null)
        {
            return Ok(new
            {
                streamBaseUrl = string.Empty,
                navPlacement = "Sidebar",
                showUpcoming = true,
                showRecommendations = true,
                showWatchlist = true
            });
        }

        return Ok(new
        {
            streamBaseUrl = config.StreamBaseUrl,
            navPlacement = config.NavPlacement.ToString(),
            showUpcoming = config.ShowUpcomingSection,
            showRecommendations = config.ShowRecommendationsSection,
            showWatchlist = config.ShowWatchlistSection
        });
    }
}

