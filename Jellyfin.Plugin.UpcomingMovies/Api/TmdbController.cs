using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using Jellyfin.Plugin.UpcomingMovies.Model;
using Jellyfin.Plugin.UpcomingMovies.Services;
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
    private readonly UserProfileService _profileService;

    /// <summary>
    /// Initializes a new instance of the <see cref="TmdbController"/> class.
    /// </summary>
    public TmdbController(
        ILogger<TmdbController> logger,
        IHttpClientFactory httpClientFactory,
        UserProfileService profileService)
    {
        _logger = logger;
        _httpClientFactory = httpClientFactory;
        _profileService = profileService;
    }

    /// <summary>
    /// Proxies a request to TMDB's /discover/movie endpoint filtered to upcoming releases.
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

            // Request future movies up to 1 year in advance
            var todayStr   = DateTime.UtcNow.ToString("yyyy-MM-dd");
            var oneYearStr = DateTime.UtcNow.AddYears(1).ToString("yyyy-MM-dd");
            var url = $"{TmdbBaseUrl}/discover/movie?api_key={apiKey}&language=en-US&page={page}&primary_release_date.gte={todayStr}&primary_release_date.lte={oneYearStr}&sort_by=popularity.desc&with_release_type=2|3&with_original_language=en&region=US";

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
    /// Phase 21 — Intelligent recommendation engine.
    /// Loads the requesting user's server-side taste profile (built from watch history)
    /// and uses it to produce a personalized, scored candidate pool from TMDB.
    ///
    /// Scoring factors (applied to every candidate):
    ///   • Genre weights   — how much the user watches each genre (1.5× per genre match)
    ///   • Director bonus  — movies from favourite directors have +50 source bonus
    ///   • Actor bonus     — movies with favourite actors have +40 source bonus
    ///   • Language weight — preferred language multiplier (2.0×)
    ///   • Vote average    — quality signal (×5)
    ///   • Popularity      — capped at 100 (×0.3)
    ///   • Recency         — ≤2 years old +20, >10 years old −10
    /// </summary>
    [HttpGet("recommendations")]
    [Authorize]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> GetRecommendations(
        [FromQuery] string userId = "",
        [FromQuery] int page = 1)
    {
        try
        {
            var apiKey = Plugin.Instance?.Configuration?.TmdbApiKey;
            if (string.IsNullOrWhiteSpace(apiKey))
            {
                return StatusCode(400, new { _needsSetup = true, error = "TMDB API key not configured." });
            }

            // ── Load user profile ──────────────────────────────────────────────────────────
            var profile = string.IsNullOrWhiteSpace(userId)
                ? new UserProfileData()
                : _profileService.GetProfile(userId);

            var watchedIds = new HashSet<int>(profile.WatchedTmdbIds);
            var seedIds    = _profileService.GetRecentSeedIds(profile, 8);
            var topGenres  = _profileService.GetTopGenres(profile, 5);
            var topDirs    = _profileService.GetTopDirectors(profile, 5);
            var topActors  = _profileService.GetTopActors(profile, 5);
            bool hasProfile = profile.TotalWatched > 0;

            var client = _httpClientFactory.CreateClient();

            // Thread-safe accumulation of candidates
            var lock_ = new object();
            var candidateSourceBonus = new Dictionary<int, double>();
            var candidateElements    = new Dictionary<int, JsonElement>();

            void AddCandidate(JsonElement movie, double sourceBonus)
            {
                if (!movie.TryGetProperty("id", out var idProp) || !idProp.TryGetInt32(out var movieId)) return;
                if (watchedIds.Contains(movieId)) return;
                lock (lock_)
                {
                    if (!candidateElements.ContainsKey(movieId))
                        candidateElements[movieId] = movie;
                    if (!candidateSourceBonus.TryGetValue(movieId, out var existing) || sourceBonus > existing)
                        candidateSourceBonus[movieId] = sourceBonus;
                }
            }

            // ── Source 1: /recommendations from recent seed movies (+30) ──────────────────
            var seedTasks = seedIds.Select(async seedId =>
            {
                try
                {
                    var url = $"{TmdbBaseUrl}/movie/{seedId}/recommendations?api_key={apiKey}&language=en-US&page=1";
                    var res = await client.GetAsync(url).ConfigureAwait(false);
                    if (!res.IsSuccessStatusCode) return;
                    var json = await res.Content.ReadAsStringAsync().ConfigureAwait(false);
                    using var doc = JsonDocument.Parse(json);
                    if (doc.RootElement.TryGetProperty("results", out var results))
                        foreach (var m in results.EnumerateArray())
                            AddCandidate(m, 30.0);
                }
                catch (Exception ex) { _logger.LogWarning(ex, "[UpcomingMovies] recommendations/{Id} failed", seedId); }
            });

            // ── Source 2: /similar for top 3 seeds (+15) ──────────────────────────────────
            var simTasks = seedIds.Take(3).Select(async seedId =>
            {
                try
                {
                    var url = $"{TmdbBaseUrl}/movie/{seedId}/similar?api_key={apiKey}&language=en-US&page=1";
                    var res = await client.GetAsync(url).ConfigureAwait(false);
                    if (!res.IsSuccessStatusCode) return;
                    var json = await res.Content.ReadAsStringAsync().ConfigureAwait(false);
                    using var doc = JsonDocument.Parse(json);
                    if (doc.RootElement.TryGetProperty("results", out var results))
                        foreach (var m in results.EnumerateArray())
                            AddCandidate(m, 15.0);
                }
                catch (Exception ex) { _logger.LogWarning(ex, "[UpcomingMovies] similar/{Id} failed", seedId); }
            });

            // ── Source 3: Genre-weighted discover (+0 — genre scoring applied inline) ────
            var genreTask = Task.Run(async () =>
            {
                try
                {
                    var genreFilter = topGenres.Count > 0
                        ? "&with_genres=" + Uri.EscapeDataString(string.Join("|", topGenres))
                        : string.Empty;
                    var url = $"{TmdbBaseUrl}/discover/movie?api_key={apiKey}&language=en-US&sort_by=vote_average.desc&vote_count.gte=100&page={page}{genreFilter}";
                    var res = await client.GetAsync(url).ConfigureAwait(false);
                    if (!res.IsSuccessStatusCode) return;
                    var json = await res.Content.ReadAsStringAsync().ConfigureAwait(false);
                    using var doc = JsonDocument.Parse(json);
                    if (doc.RootElement.TryGetProperty("results", out var results))
                        foreach (var m in results.EnumerateArray())
                            AddCandidate(m, 0.0);
                }
                catch (Exception ex) { _logger.LogWarning(ex, "[UpcomingMovies] genre discover failed"); }
            });

            // ── Source 4: Director-based discover (+25) ──────────────────────────────────
            var directorTask = Task.Run(async () =>
            {
                if (topDirs.Count == 0) return;
                try
                {
                    var pf = Uri.EscapeDataString(string.Join("|", topDirs));
                    var url = $"{TmdbBaseUrl}/discover/movie?api_key={apiKey}&language=en-US&sort_by=vote_average.desc&vote_count.gte=50&with_people={pf}&page=1";
                    var res = await client.GetAsync(url).ConfigureAwait(false);
                    if (!res.IsSuccessStatusCode) return;
                    var json = await res.Content.ReadAsStringAsync().ConfigureAwait(false);
                    using var doc = JsonDocument.Parse(json);
                    if (doc.RootElement.TryGetProperty("results", out var results))
                        foreach (var m in results.EnumerateArray())
                            AddCandidate(m, 25.0);
                }
                catch (Exception ex) { _logger.LogWarning(ex, "[UpcomingMovies] director discover failed"); }
            });

            // ── Source 5: Actor-based discover (+20) ────────────────────────────────────
            var actorTask = Task.Run(async () =>
            {
                if (topActors.Count == 0) return;
                try
                {
                    var pf = Uri.EscapeDataString(string.Join("|", topActors));
                    var url = $"{TmdbBaseUrl}/discover/movie?api_key={apiKey}&language=en-US&sort_by=vote_average.desc&vote_count.gte=50&with_people={pf}&page=1";
                    var res = await client.GetAsync(url).ConfigureAwait(false);
                    if (!res.IsSuccessStatusCode) return;
                    var json = await res.Content.ReadAsStringAsync().ConfigureAwait(false);
                    using var doc = JsonDocument.Parse(json);
                    if (doc.RootElement.TryGetProperty("results", out var results))
                        foreach (var m in results.EnumerateArray())
                            AddCandidate(m, 20.0);
                }
                catch (Exception ex) { _logger.LogWarning(ex, "[UpcomingMovies] actor discover failed"); }
            });

            // ── Source 6: Trending fallback for new users with no watch history (+5) ──────
            var trendingTask = Task.Run(async () =>
            {
                if (hasProfile) return;
                try
                {
                    var url = $"{TmdbBaseUrl}/trending/movie/week?api_key={apiKey}&language=en-US";
                    var res = await client.GetAsync(url).ConfigureAwait(false);
                    if (!res.IsSuccessStatusCode) return;
                    var json = await res.Content.ReadAsStringAsync().ConfigureAwait(false);
                    using var doc = JsonDocument.Parse(json);
                    if (doc.RootElement.TryGetProperty("results", out var results))
                        foreach (var m in results.EnumerateArray())
                            AddCandidate(m, 5.0);
                }
                catch (Exception ex) { _logger.LogWarning(ex, "[UpcomingMovies] trending fallback failed"); }
            });

            // Run all sources in parallel for speed
            await Task.WhenAll(
                Task.WhenAll(seedTasks),
                Task.WhenAll(simTasks),
                genreTask,
                directorTask,
                actorTask,
                trendingTask
            ).ConfigureAwait(false);

            // ── Scoring engine ─────────────────────────────────────────────────────────────
            var today = DateTime.UtcNow;

            var scored = candidateElements.Select(kv =>
            {
                var movieId = kv.Key;
                var m = kv.Value;

                // Start with source bonus (director/actor/seed sourcing)
                double score = candidateSourceBonus.GetValueOrDefault(movieId);

                // Genre match (× 2.0 — meaningful but lets other factors contribute too)
                if (m.TryGetProperty("genre_ids", out var genreArr))
                {
                    foreach (var g in genreArr.EnumerateArray())
                    {
                        if (g.TryGetInt32(out var gid))
                            score += profile.GenreWeights.GetValueOrDefault(gid) * 2.0;
                    }
                }

                // Language affinity (2× multiplier)
                if (m.TryGetProperty("original_language", out var langProp))
                {
                    var lang = langProp.GetString() ?? "en";
                    score += profile.LanguageWeights.GetValueOrDefault(lang) * 2.0;
                }

                // Vote average (0–10 → 0–60 pts) — strong quality signal
                if (m.TryGetProperty("vote_average", out var vaProp) && vaProp.TryGetDouble(out var va))
                    score += va * 6.0;

                // Popularity (capped at 100 → max 50 pts) — reflects cultural relevance
                if (m.TryGetProperty("popularity", out var popProp) && popProp.TryGetDouble(out var pop))
                    score += Math.Min(pop, 100) * 0.5;

                // Recency bonus/penalty (gentle — recent is preferred but old classics still show)
                if (m.TryGetProperty("release_date", out var rdProp) &&
                    DateTime.TryParse(rdProp.GetString(), out var releaseDate))
                {
                    var yearsOld = (today - releaseDate).TotalDays / 365.25;
                    if (yearsOld <= 2) score += 12;
                    else if (yearsOld > 10) score -= 8;
                }

                return (movieId, score, element: m);
            })
            .OrderByDescending(x => x.score)
            .Take(60)
            .Select(x => x.element)
            .ToList();

            var finalJson = JsonSerializer.Serialize(new
            {
                results = scored,
                total_results = scored.Count,
                page,
                total_pages = 500
            });

            _logger.LogInformation(
                "[UpcomingMovies] Recommendations for user {UserId}: {Count} scored " +
                "(watched={Watched}, genres={Genres}, dirs={Dirs}, actors={Actors})",
                userId, scored.Count,
                profile.TotalWatched,
                profile.GenreWeights.Count,
                profile.DirectorWeights.Count,
                profile.ActorWeights.Count);

            return Content(finalJson, "application/json");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[UpcomingMovies] Unhandled exception in GetRecommendations");
            return StatusCode(StatusCodes.Status500InternalServerError, new { error = ex.Message });
        }
    }

    /// <summary>
    /// Returns the current user's profile summary (for debugging / admin view).
    /// </summary>
    [HttpGet("profile")]
    [Authorize]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public IActionResult GetProfile([FromQuery] string userId = "")
    {
        if (string.IsNullOrWhiteSpace(userId))
            return BadRequest(new { error = "userId is required" });

        var profile = _profileService.GetProfile(userId);
        return Ok(new
        {
            userId = profile.UserId,
            lastUpdated = profile.LastUpdated,
            totalWatched = profile.TotalWatched,
            topGenres = _profileService.GetTopGenres(profile, 10)
                .Select(id => new { id, name = UserProfileService.TmdbGenreIdToName.GetValueOrDefault(id, id.ToString()), weight = profile.GenreWeights.GetValueOrDefault(id) }),
            topDirectors = _profileService.GetTopDirectors(profile, 5)
                .Select(id => new { id, weight = profile.DirectorWeights.GetValueOrDefault(id) }),
            topActors = _profileService.GetTopActors(profile, 5)
                .Select(id => new { id, weight = profile.ActorWeights.GetValueOrDefault(id) }),
            topLanguages = profile.LanguageWeights
                .OrderByDescending(kv => kv.Value).Take(5)
                .Select(kv => new { language = kv.Key, weight = kv.Value }),
            recentWatches = profile.RecentWatches.Take(10)
                .Select(w => new { w.TmdbId, watchedAt = w.WatchedAt, language = w.Language })
        });
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
                streamBaseUrl           = config?.StreamBaseUrl ?? string.Empty,
                navPlacement            = config?.NavPlacement.ToString() ?? "Sidebar",
                showUpcoming            = config?.ShowUpcomingSection ?? true,
                showRecommendations     = config?.ShowRecommendationsSection ?? true,
                tmdbConfigured          = !string.IsNullOrWhiteSpace(config?.TmdbApiKey),
                jellyseerrConfigured    = !string.IsNullOrWhiteSpace(config?.JellyseerrApiKey)
                                       && !string.IsNullOrWhiteSpace(config?.JellyseerrUrl)
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[UpcomingMovies] Error in GetPublicConfig");
            return StatusCode(StatusCodes.Status500InternalServerError, new { error = ex.Message });
        }
    }
}
