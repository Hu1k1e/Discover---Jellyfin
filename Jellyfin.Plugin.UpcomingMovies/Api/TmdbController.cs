using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
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
    /// Returns only movies whose release_date is today or in the future.
    /// </summary>
    [HttpGet("upcoming")]
    [Authorize]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
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
            
            // Phase 9: Request future movies up to 1 year in advance
            var todayStr = DateTime.UtcNow.ToString("yyyy-MM-dd");
            var oneYearStr = DateTime.UtcNow.AddYears(1).ToString("yyyy-MM-dd");
            var url = $"{TmdbBaseUrl}/discover/movie?api_key={apiKey}&language=en-US&page={page}&primary_release_date.gte={todayStr}&primary_release_date.lte={oneYearStr}&sort_by=primary_release_date.asc&with_release_type=2|3";
            
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
    /// Intelligent recommendation engine.
    /// Accepts TMDB IDs of the user's watched/favorited movies and genre preference weights.
    /// Fetches per-movie recommendations + genre-based discover results, then merges and deduplicates.
    /// </summary>
    [HttpGet("recommendations")]
    [Authorize]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> GetRecommendations(
        [FromQuery] string tmdbIds = "",
        [FromQuery] string genreIds = "",
        [FromQuery] int page = 1)
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
            var allResults = new List<JsonElement>();
            var seenIds = new HashSet<int>();

            // Seed IDs to exclude from recommendations (already watched/favorited by user)
            var excludeIds = new HashSet<int>();
            if (!string.IsNullOrWhiteSpace(tmdbIds))
            {
                foreach (var part in tmdbIds.Split(',', StringSplitOptions.RemoveEmptyEntries))
                {
                    if (int.TryParse(part.Trim(), out var id)) excludeIds.Add(id);
                }
            }

            // 1. Per-movie recommendations from watched/favorited titles (up to 5 seed movies)
            var seedIds = excludeIds.Take(5).ToList();
            foreach (var seedId in seedIds)
            {
                try
                {
                    var recUrl = $"{TmdbBaseUrl}/movie/{seedId}/recommendations?api_key={apiKey}&language=en-US&page=1";
                    var recRes = await client.GetAsync(recUrl).ConfigureAwait(false);
                    if (recRes.IsSuccessStatusCode)
                    {
                        var recJson = await recRes.Content.ReadAsStringAsync().ConfigureAwait(false);
                        var recDoc = JsonDocument.Parse(recJson);
                        if (recDoc.RootElement.TryGetProperty("results", out var recResults))
                        {
                            foreach (var movie in recResults.EnumerateArray())
                            {
                                if (movie.TryGetProperty("id", out var idProp) && idProp.TryGetInt32(out var movieId))
                                {
                                    if (!excludeIds.Contains(movieId) && seenIds.Add(movieId))
                                    {
                                        allResults.Add(movie);
                                    }
                                }
                            }
                        }
                    }
                }
                catch (Exception seedEx)
                {
                    _logger.LogWarning(seedEx, "[UpcomingMovies] Per-movie recommendations failed for TMDB ID {Id}", seedId);
                }
            }

            // 2. Genre-based discover as supplementary source
            var genreFilter = string.IsNullOrWhiteSpace(genreIds) ? string.Empty : $"&with_genres={genreIds}";
            var discoverUrl = $"{TmdbBaseUrl}/discover/movie?api_key={apiKey}&language=en-US&sort_by=vote_average.desc&vote_count.gte=200&page={page}{genreFilter}";
            var discoverRes = await client.GetAsync(discoverUrl).ConfigureAwait(false);

            if (discoverRes.IsSuccessStatusCode)
            {
                var discoverJson = await discoverRes.Content.ReadAsStringAsync().ConfigureAwait(false);
                var discoverDoc = JsonDocument.Parse(discoverJson);
                if (discoverDoc.RootElement.TryGetProperty("results", out var discoverResults))
                {
                    foreach (var movie in discoverResults.EnumerateArray())
                    {
                        if (movie.TryGetProperty("id", out var idProp) && idProp.TryGetInt32(out var movieId))
                        {
                            if (!excludeIds.Contains(movieId) && seenIds.Add(movieId))
                            {
                                allResults.Add(movie);
                            }
                        }
                    }
                }
            }

            // 3. Fallback to trending if no results
            if (allResults.Count == 0)
            {
                var trendUrl = $"{TmdbBaseUrl}/trending/movie/week?api_key={apiKey}&language=en-US";
                var trendRes = await client.GetAsync(trendUrl).ConfigureAwait(false);
                if (trendRes.IsSuccessStatusCode)
                {
                    var trendJson = await trendRes.Content.ReadAsStringAsync().ConfigureAwait(false);
                    var trendDoc = JsonDocument.Parse(trendJson);
                    if (trendDoc.RootElement.TryGetProperty("results", out var trendResults))
                    {
                        foreach (var movie in trendResults.EnumerateArray())
                        {
                            if (movie.TryGetProperty("id", out var idProp) && idProp.TryGetInt32(out var movieId))
                            {
                                if (!excludeIds.Contains(movieId) && seenIds.Add(movieId))
                                {
                                    allResults.Add(movie);
                                }
                            }
                        }
                    }
                }
            }

            // Sort by vote_average desc and take top 30
            var sorted = allResults
                .OrderByDescending(m =>
                {
                    if (m.TryGetProperty("vote_average", out var va) && va.TryGetDouble(out var d)) return d;
                    return 0.0;
                })
                .Take(30)
                .ToList();

            var finalJson = JsonSerializer.Serialize(new
            {
                results = sorted,
                total_results = sorted.Count,
                page = page
            });

            return Content(finalJson, "application/json");
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
                tmdbConfigured = !string.IsNullOrWhiteSpace(config?.TmdbApiKey),
                jellyseerrConfigured = !string.IsNullOrWhiteSpace(config?.JellyseerrApiKey) && !string.IsNullOrWhiteSpace(config?.JellyseerrUrl)
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[UpcomingMovies] Error in GetPublicConfig");
            return StatusCode(StatusCodes.Status500InternalServerError, new { error = ex.Message });
        }
    }
}
