using System;
using System.Net.Http;
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
public class TmdbController : ControllerBase
{
    private const string TmdbBaseUrl = "https://api.themoviedb.org/3";
    private readonly ILogger<TmdbController> _logger;
    private readonly IHttpClientFactory _httpClientFactory;

    /// <summary>
    /// Initializes a new instance of the <see cref="TmdbController"/> class.
    /// </summary>
    /// <param name="logger">Logger instance.</param>
    /// <param name="httpClientFactory">HTTP client factory from DI.</param>
    public TmdbController(ILogger<TmdbController> logger, IHttpClientFactory httpClientFactory)
    {
        _logger = logger;
        _httpClientFactory = httpClientFactory;
    }

    /// <summary>
    /// Proxies a request to TMDB's /movie/upcoming endpoint.
    /// </summary>
    [HttpGet("upcoming")]
    [Authorize]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status502BadGateway)]
    public async Task<IActionResult> GetUpcoming([FromQuery] int page = 1)
    {
        try
        {
            var apiKey = Plugin.Instance?.Configuration?.TmdbApiKey;
            if (string.IsNullOrWhiteSpace(apiKey))
            {
                _logger.LogWarning("[UpcomingMovies] TMDB API key is not configured.");
                return BadRequest(new { error = "TMDB API key is not configured. Please set it in the plugin settings." });
            }

            var client = _httpClientFactory.CreateClient();
            var url = $"{TmdbBaseUrl}/movie/upcoming?api_key={apiKey}&language=en-US&page={page}";
            var response = await client.GetAsync(url).ConfigureAwait(false);

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("[UpcomingMovies] TMDB /upcoming returned {StatusCode}", response.StatusCode);
                return StatusCode((int)response.StatusCode, new { error = "TMDB API returned an error." });
            }

            var json = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
            return Content(json, "application/json");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[UpcomingMovies] Unhandled exception in GetUpcoming");
            return StatusCode(StatusCodes.Status500InternalServerError, new { error = ex.Message });
        }
    }

    /// <summary>
    /// Proxies a request to TMDB's /discover/movie endpoint for personalized recommendations.
    /// </summary>
    [HttpGet("recommendations")]
    [Authorize]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status502BadGateway)]
    public async Task<IActionResult> GetRecommendations([FromQuery] string genreIds = "", [FromQuery] int page = 1)
    {
        try
        {
            var apiKey = Plugin.Instance?.Configuration?.TmdbApiKey;
            if (string.IsNullOrWhiteSpace(apiKey))
            {
                _logger.LogWarning("[UpcomingMovies] TMDB API key is not configured.");
                return BadRequest(new { error = "TMDB API key is not configured. Please set it in the plugin settings." });
            }

            var client = _httpClientFactory.CreateClient();
            var genreFilter = string.IsNullOrWhiteSpace(genreIds) ? string.Empty : $"&with_genres={genreIds}";
            var url = $"{TmdbBaseUrl}/discover/movie?api_key={apiKey}&language=en-US&sort_by=vote_average.desc&vote_count.gte=200&page={page}{genreFilter}";
            var response = await client.GetAsync(url).ConfigureAwait(false);

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("[UpcomingMovies] TMDB /discover returned {StatusCode}", response.StatusCode);
                return StatusCode((int)response.StatusCode, new { error = "TMDB API returned an error." });
            }

            var json = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
            return Content(json, "application/json");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[UpcomingMovies] Unhandled exception in GetRecommendations");
            return StatusCode(StatusCodes.Status500InternalServerError, new { error = ex.Message });
        }
    }

    /// <summary>
    /// Returns public-safe plugin configuration (no secrets exposed).
    /// </summary>
    [HttpGet("config")]
    [Authorize]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public IActionResult GetPublicConfig()
    {
        try
        {
            var config = Plugin.Instance?.Configuration;
            return Ok(new
            {
                streamBaseUrl = config?.StreamBaseUrl ?? string.Empty,
                navPlacement = config?.NavPlacement.ToString() ?? "Sidebar",
                showUpcoming = config?.ShowUpcomingSection ?? true,
                showRecommendations = config?.ShowRecommendationsSection ?? true,
                showWatchlist = config?.ShowWatchlistSection ?? true,
                tmdbConfigured = !string.IsNullOrWhiteSpace(config?.TmdbApiKey)
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[UpcomingMovies] Error in GetPublicConfig");
            return StatusCode(StatusCodes.Status500InternalServerError, new { error = ex.Message });
        }
    }
}
