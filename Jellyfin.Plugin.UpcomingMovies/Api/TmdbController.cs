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

    /// <summary>
    /// Initializes a new instance of the <see cref="TmdbController"/> class.
    /// UserProfileService is accessed via Plugin.ProfileService (static) rather than DI
    /// to avoid referencing Jellyfin.Common types not available in Jellyfin.Controller.
    /// </summary>
    public TmdbController(
        ILogger<TmdbController> logger,
        IHttpClientFactory httpClientFactory)
    {
        _logger = logger;
        _httpClientFactory = httpClientFactory;
    }

    // Convenience accessor — returns Plugin.ProfileService, or null for safe handling below
    private static UserProfileService? ProfileService => Plugin.ProfileService;

    /// <summary>
    /// Proxies a request to TMDB's /discover/movie endpoint filtered to upcoming releases.
    /// </summary>
    [HttpGet("upcoming")]
    [Authorize]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> GetUpcoming(
        [FromQuery] int    page         = 1,
        [FromQuery] string languages    = "en",
        [FromQuery] string genres       = "",
        [FromQuery] string releaseTypes = "1,2,3,4,5",
        [FromQuery] string dateFrom     = "",
        [FromQuery] string dateTo       = "")
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

            // Build date range
            var fromStr = string.IsNullOrWhiteSpace(dateFrom) ? DateTime.UtcNow.ToString("yyyy-MM-dd")            : dateFrom;
            var toStr   = string.IsNullOrWhiteSpace(dateTo)   ? DateTime.UtcNow.AddYears(1).ToString("yyyy-MM-dd") : dateTo;

            // Build release-type pipe string (TMDB accepts e.g. "1|2|3")
            var rtParts = (releaseTypes ?? "1,2,3,4,5").Split(',', StringSplitOptions.RemoveEmptyEntries)
                                                        .Select(s => s.Trim()).Where(s => !string.IsNullOrEmpty(s));
            var rtParam = string.Join("|", rtParts);
            if (string.IsNullOrEmpty(rtParam)) rtParam = "1|2|3|4|5";

            // Genre filter (pipe-OR)
            var genreParam = string.IsNullOrWhiteSpace(genres) ? string.Empty
                : "&with_genres=" + Uri.EscapeDataString(genres.Replace(",", "|"));

            // Multiple languages → parallel requests, merged + deduplicated
            var langCodes = (languages ?? "en").Split(',', StringSplitOptions.RemoveEmptyEntries)
                                                .Select(s => s.Trim()).Where(s => !string.IsNullOrEmpty(s)).ToList();
            if (langCodes.Count == 0) langCodes = new List<string> { "en" };

            var allMovies   = new List<JsonElement>();
            var seenIds     = new HashSet<int>();
            var lockObj     = new object();

            var langFetchTasks = langCodes.Select(async lang =>
            {
                try
                {
                    var url = $"{TmdbBaseUrl}/discover/movie?api_key={apiKey}"
                            + $"&language=en-US&page={page}"
                            + $"&primary_release_date.gte={fromStr}&primary_release_date.lte={toStr}"
                            + $"&sort_by=popularity.desc"
                            + $"&with_release_type={rtParam}"
                            + $"&with_original_language={lang}"
                            + genreParam;

                    var response = await client.GetAsync(url).ConfigureAwait(false);
                    if (!response.IsSuccessStatusCode) return;
                    var json = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                    using var doc = JsonDocument.Parse(json);
                    if (!doc.RootElement.TryGetProperty("results", out var results)) return;
                    lock (lockObj)
                    {
                        foreach (var m in results.EnumerateArray())
                        {
                            if (!m.TryGetProperty("id", out var idEl) || !idEl.TryGetInt32(out var id)) continue;
                            if (seenIds.Add(id)) allMovies.Add(m.Clone());
                        }
                    }
                }
                catch (Exception ex) { _logger.LogWarning(ex, "[UpcomingMovies] upcoming fetch for lang={Lang} failed", lang); }
            });

            await Task.WhenAll(langFetchTasks).ConfigureAwait(false);

            // Sort merged results by popularity descending
            var sorted = allMovies
                .Select(m => (m, pop: m.TryGetProperty("popularity", out var p) && p.TryGetDouble(out var pd) ? pd : 0.0))
                .OrderByDescending(x => x.pop)
                .Select(x => x.m)
                .ToList();

            var outputJson = JsonSerializer.Serialize(new
            {
                results      = sorted,
                total_results = sorted.Count,
                page,
                total_pages  = 1
            });
            return Content(outputJson, "application/json");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[UpcomingMovies] Unhandled exception in GetUpcoming");
            return StatusCode(StatusCodes.Status500InternalServerError, new { error = ex.Message });
        }
    }

    // ── Inline scoring helper ────────────────────────────────────────────────────────
    // Compresses raw accumulated weights onto a log10 curve so a user who watches
    // 10 animated movies only scores ~2× more than someone with 1 watch (not 10×).
    private static double NW(double rawWeight)
        => UserProfileService.NormalizedWeight(rawWeight);

    /// <summary>
    /// Phase 28 — Balanced, diverse recommendation engine.
    /// Loads the requesting user's server-side taste profile (built from watch history)
    /// and uses it to produce a personalized, scored candidate pool from TMDB.
    ///
    /// Scoring factors (applied to every candidate):
    ///   • Genre weights   — log-normalized per matching genre (prevents single-genre dominance)
    ///   • Director bonus  — movies from favourite directors have +25 source bonus
    ///   • Actor bonus     — movies with favourite actors have +20 source bonus
    ///   • Language weight — log-normalized preferred language multiplier (×6)
    ///   • Vote average    — quality signal (×7)
    ///   • Popularity      — capped at 100 (×0.6)
    ///   • Recency         — ≤2 years old +10, >10 years old −6
    ///
    /// Final 60 results assembled via 3-tier diversity slot allocation:
    ///   • 30 top-score slots (any genre)
    ///   • 20 secondary-genre slots (from genres outside top-1 weighted)
    ///   • 10 wildcard slots (high quality+popularity, genre-agnostic)
    /// </summary>
    [HttpGet("recommendations")]
    [Authorize]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> GetRecommendations(
        [FromQuery] string userId           = "",
        [FromQuery] int    page             = 1,
        [FromQuery] string filterLanguages  = "",
        [FromQuery] string filterGenres     = "",
        [FromQuery] string filterDateFrom   = "",
        [FromQuery] string filterDateTo     = "")
    {
        try
        {
            var apiKey = Plugin.Instance?.Configuration?.TmdbApiKey;
            if (string.IsNullOrWhiteSpace(apiKey))
            {
                return StatusCode(400, new { _needsSetup = true, error = "TMDB API key not configured." });
            }

            // ── Load user profile (ProfileService is null only before plugin fully initialises)
            var svc = ProfileService;
            var profile = svc is null || string.IsNullOrWhiteSpace(userId)
                ? new UserProfileData()
                : svc.GetProfile(userId);

            var watchedIds  = new HashSet<int>(profile.WatchedTmdbIds);
            var seedIds     = svc?.GetRecentSeedIds(profile, 8)   ?? new List<int>();
            var wlSeedIds   = svc?.GetWatchlistSeedIds(profile, 5) ?? new List<int>();
            var topGenres   = svc?.GetTopGenres(profile, 5)       ?? new List<int>();
            var topDirs     = svc?.GetTopDirectors(profile, 5)    ?? new List<int>();
            var topActors   = svc?.GetTopActors(profile, 5)       ?? new List<int>();
            bool hasProfile = profile.TotalWatched > 0 || wlSeedIds.Count > 0;

            var client = _httpClientFactory.CreateClient();

            // Thread-safe accumulation of candidates
            var lock_ = new object();
            var candidateSourceBonus = new Dictionary<int, double>();
            var candidateElements    = new Dictionary<int, JsonElement>();

            void AddCandidate(JsonElement movie, double sourceBonus, bool bypassLangFilter = false)
            {
                if (!movie.TryGetProperty("id", out var idProp) || !idProp.TryGetInt32(out var movieId)) return;
                if (watchedIds.Contains(movieId)) return;

                // Language allowlist: only surface languages the user cares about
                // en=Hollywood, hi=Hindi, ta=Tamil, ml=Malayalam, te=Telugu, ko=Korean, ja=Japanese/Anime
                // bypassLangFilter=true is set by Source 9 for the user's own top language
                if (!bypassLangFilter && movie.TryGetProperty("original_language", out var langEl))
                {
                    var lang = langEl.GetString();
                    if (!string.IsNullOrEmpty(lang) &&
                        lang != "en" && lang != "hi" && lang != "ta" &&
                        lang != "ml" && lang != "te" && lang != "ko" && lang != "ja")
                        return;
                }

                lock (lock_)
                {
                    if (!candidateElements.ContainsKey(movieId))
                        candidateElements[movieId] = movie.Clone(); // Clone so element outlives the 'using var doc' in each parallel task
                    if (!candidateSourceBonus.TryGetValue(movieId, out var existing) || sourceBonus > existing)
                        candidateSourceBonus[movieId] = sourceBonus;
                }
            }


            // ── Source 1: /recommendations from recent seed movies (+30) ──────────────────
            // Cycle through TMDB pages per backend page so each call returns a fresh pool.
            var tmdbPage1 = ((page - 1) % 3) + 1; // rotates 1→2→3→1→2→3
            var seedTasks = seedIds.Select(async seedId =>
            {
                try
                {
                    var url = $"{TmdbBaseUrl}/movie/{seedId}/recommendations?api_key={apiKey}&language=en-US&page={tmdbPage1}";
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
            var tmdbPage2 = ((page - 1) % 5) + 1; // rotates 1→2→3→4→5→1
            var simTasks = seedIds.Take(3).Select(async seedId =>
            {
                try
                {
                    var url = $"{TmdbBaseUrl}/movie/{seedId}/similar?api_key={apiKey}&language=en-US&page={tmdbPage2}";
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
            // Uses a different TMDB page each call so results vary between Discover More clicks.
            var tmdbPage3 = ((page - 1) % 8) + 1;
            var genreTask = Task.Run(async () =>
            {
                try
                {
                    var genreFilter = topGenres.Count > 0
                        ? "&with_genres=" + Uri.EscapeDataString(string.Join("|", topGenres))
                        : string.Empty;
                    var url = $"{TmdbBaseUrl}/discover/movie?api_key={apiKey}&language=en-US&sort_by=vote_average.desc&vote_count.gte=100&page={tmdbPage3}{genreFilter}";
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

            // ── Source 7: Popular + high-quality movies, always-on (+8) ─────────────────────
            // Guarantees a pool of broadly appealing movies for the wildcard diversity tier,
            // even for users with a well-established profile. Without this, the wildcard tier
            // had nothing to draw from once the trending source was suppressed.
            var popularTask = Task.Run(async () =>
            {
                try
                {
                    var url = $"{TmdbBaseUrl}/discover/movie?api_key={apiKey}&language=en-US&sort_by=popularity.desc&vote_average.gte=7.0&vote_count.gte=200&page={page}";
                    var res = await client.GetAsync(url).ConfigureAwait(false);
                    if (!res.IsSuccessStatusCode) return;
                    var json = await res.Content.ReadAsStringAsync().ConfigureAwait(false);
                    using var doc = JsonDocument.Parse(json);
                    if (doc.RootElement.TryGetProperty("results", out var results))
                        foreach (var m in results.EnumerateArray())
                            AddCandidate(m, 8.0);
                }
                catch (Exception ex) { _logger.LogWarning(ex, "[UpcomingMovies] popular source failed"); }
            });

            // ── Source 8: Watchlist seeds — /recommendations from watchlisted movies (+22) ────
            // Users who add movies to their watchlist express strong genre/director/actor intent.
            // We seed from those movies at +22 (between watched-seed /rec +30 and /similar +15)
            // because watchlist intent is confident but weaker than "actually watched and enjoyed".
            var wlSeedTasks = wlSeedIds.Select(async wlId =>
            {
                try
                {
                    var url = $"{TmdbBaseUrl}/movie/{wlId}/recommendations?api_key={apiKey}&language=en-US&page=1";
                    var res = await client.GetAsync(url).ConfigureAwait(false);
                    if (!res.IsSuccessStatusCode) return;
                    var json = await res.Content.ReadAsStringAsync().ConfigureAwait(false);
                    using var doc = JsonDocument.Parse(json);
                    if (doc.RootElement.TryGetProperty("results", out var results))
                        foreach (var m in results.EnumerateArray())
                            AddCandidate(m, 22.0);
                }
                catch (Exception ex) { _logger.LogWarning(ex, "[UpcomingMovies] wl-seed recommendations/{Id} failed", wlId); }
            });

            // ── Source 9: Language-affinity discover — top non-English languages (+18) ────
            // Discovers movies specifically in the user's most-watched non-English language(s).
            // This is the ONLY source that surfaces regional films (Malayalam, Hindi, Korean…)
            // because all other sources default to TMDB's en-US language preference.
            //
            // Activation: any non-English language weight >= 0.5 (triggered by even a single
            // watched or watchlisted film in that language).
            //
            // Up to 2 top non-English languages are fetched in parallel so bilingual users
            // (e.g., someone who watches both Malayalam and Hindi) get both covered.
            var topNonEnglishLangs = profile.LanguageWeights
                .Where(kv => kv.Key != "en" && kv.Value >= 0.5)
                .OrderByDescending(kv => kv.Value)
                .Take(2)
                .Select(kv => kv.Key)
                .ToList();

            var langTasks = topNonEnglishLangs.Select(async langCode =>
            {
                try
                {
                    var tmdbPageLang = ((page - 1) % 10) + 1;
                    var genreFilter = topGenres.Count > 0
                        ? "&with_genres=" + Uri.EscapeDataString(string.Join("|", topGenres.Take(3)))
                        : string.Empty;
                    // Fetch with genre filter first for precision
                    var url = $"{TmdbBaseUrl}/discover/movie?api_key={apiKey}&language=en-US"
                            + $"&with_original_language={langCode}"
                            + $"&sort_by=vote_average.desc&vote_count.gte=20&page={tmdbPageLang}{genreFilter}";
                    var res = await client.GetAsync(url).ConfigureAwait(false);
                    if (!res.IsSuccessStatusCode) return;
                    var json = await res.Content.ReadAsStringAsync().ConfigureAwait(false);
                    using var doc = JsonDocument.Parse(json);
                    var resultCount = 0;
                    if (doc.RootElement.TryGetProperty("results", out var results))
                    {
                        foreach (var m in results.EnumerateArray())
                        {
                            AddCandidate(m, 18.0, bypassLangFilter: true);
                            resultCount++;
                        }
                    }
                    // If genre filter returned <5 results, also fetch without genre restriction
                    // so the user always gets regional films even in niche genre combos
                    if (resultCount < 5)
                    {
                        var fallbackUrl = $"{TmdbBaseUrl}/discover/movie?api_key={apiKey}&language=en-US"
                                        + $"&with_original_language={langCode}"
                                        + $"&sort_by=vote_average.desc&vote_count.gte=20&page={tmdbPageLang}";
                        var fb = await client.GetAsync(fallbackUrl).ConfigureAwait(false);
                        if (fb.IsSuccessStatusCode)
                        {
                            var fbJson = await fb.Content.ReadAsStringAsync().ConfigureAwait(false);
                            using var fbDoc = JsonDocument.Parse(fbJson);
                            if (fbDoc.RootElement.TryGetProperty("results", out var fbResults))
                                foreach (var m in fbResults.EnumerateArray())
                                    AddCandidate(m, 15.0, bypassLangFilter: true);
                        }
                    }
                }
                catch (Exception ex) { _logger.LogWarning(ex, "[UpcomingMovies] lang-affinity ({Lang}) failed", langCode); }
            });

            // Run all sources in parallel for speed
            await Task.WhenAll(
                Task.WhenAll(seedTasks),
                Task.WhenAll(simTasks),
                Task.WhenAll(wlSeedTasks),
                Task.WhenAll(langTasks),
                genreTask,
                directorTask,
                actorTask,
                trendingTask,
                popularTask
            ).ConfigureAwait(false);


            // ── Scoring engine ──────────────────────────────────────────────────────────────
            // Genre weights are log-normalised via NW() to prevent a single repeated genre
            // from dominating all 60 output slots.
            var today = DateTime.UtcNow;

            // ── Recency snapshot ──────────────────────────────────────────────────────────
            // Compute time-decayed genre/language weights from the last 200 watch events.
            // Half-life = 90 days: a genre watched yesterday contributes 1.0, 90 days ago 0.5,
            // 180 days ago 0.25, 1 year ago ~0.08.
            // These run ALONGSIDE the main log-normalized weights as a gentle nudge:
            //   - recency can add at most +8 pts/genre and +5 pts/language to the score
            //   - vs. quality (vote_average×7 = up to 70 pts) and source bonus (up to 30 pts)
            // This means a recent genre preference can shift rank by ~1-3 positions, not dominate.
            var (recentGenre, recentLang) = ProfileService?.GetRecentInterestWeights(profile)
                ?? (new System.Collections.Generic.Dictionary<int, double>(),
                    new System.Collections.Generic.Dictionary<string, double>());

            // Max raw recency score across genres (used to normalise the bonus)
            double maxGenreRecency = recentGenre.Count > 0 ? recentGenre.Values.Max() : 1.0;
            double maxLangRecency  = recentLang.Count  > 0 ? recentLang.Values.Max()  : 1.0;

            // Determine the single highest-weight genre so we can spread secondary genres
            var topGenreId = profile.GenreWeights.Count > 0
                ? profile.GenreWeights.OrderByDescending(kv => kv.Value).First().Key
                : -1;

            var allScored = candidateElements.Select(kv =>
            {
                var movieId = kv.Key;
                var m = kv.Value;

                // Start with source bonus (director/actor/seed sourcing)
                double score = candidateSourceBonus.GetValueOrDefault(movieId);

                // Genre match — log-normalised weight × 2.0 per matching genre
                // This means a user who watched 10 animated movies only scores ~2× more
                // for genre than someone who watched 1, preventing total genre dominance.
                // Recency bonus: adds up to +8 pts for a genre watched very recently.
                bool hasTopGenre = false;
                if (m.TryGetProperty("genre_ids", out var genreArr))
                {
                    foreach (var g in genreArr.EnumerateArray())
                    {
                        if (g.TryGetInt32(out var gid))
                        {
                            score += NW(profile.GenreWeights.GetValueOrDefault(gid)) * 2.0;
                            // Recency nudge: 0→8 pts based on how recently this genre was watched
                            // Normalised so the top recent genre always contributes 8 pts max
                            if (recentGenre.TryGetValue(gid, out var gr))
                                score += (gr / maxGenreRecency) * 8.0;
                            if (gid == topGenreId) hasTopGenre = true;
                        }
                    }
                }

                // Language affinity — log-normalised × 6.0
                // Recency bonus: up to +5 pts if the user has watched this language very recently
                if (m.TryGetProperty("original_language", out var langProp))
                {
                    var lang = langProp.GetString() ?? "en";
                    score += NW(profile.LanguageWeights.GetValueOrDefault(lang)) * 6.0;
                    if (recentLang.TryGetValue(lang, out var lr))
                        score += (lr / maxLangRecency) * 5.0;
                }

                // Vote average (0–10 → 0–70 pts) — core quality signal
                double va = 0;
                if (m.TryGetProperty("vote_average", out var vaProp) && vaProp.TryGetDouble(out va))
                    score += va * 7.0;

                // Popularity (capped at 100 → max 60 pts)
                double pop = 0;
                if (m.TryGetProperty("popularity", out var popProp) && popProp.TryGetDouble(out pop))
                    score += Math.Min(pop, 100) * 0.6;

                // Recency bonus/penalty (gentle nudge only — classics still surface via quality)
                if (m.TryGetProperty("release_date", out var rdProp) &&
                    DateTime.TryParse(rdProp.GetString(), out var releaseDate))
                {
                    var yearsOld = (today - releaseDate).TotalDays / 365.25;
                    if (yearsOld <= 2) score += 10;
                    else if (yearsOld > 10) score -= 6;
                }

                return (movieId, score, element: m, hasTopGenre, va, pop);
            })
            .OrderByDescending(x => x.score)
            .ToList();

            // ── Post-filters (applied after scoring so profile weights still rank results) ──
            // filterLanguages: comma-sep ISO codes  e.g. "hi,ml" → only show those languages
            // filterGenres:    comma-sep TMDB genre IDs e.g. "28,18" → movie must share ≥1 genre
            // filterDateFrom/To: yyyy-MM-dd → filter by release_date range
            var fLangSet = (filterLanguages ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries)
                            .Select(s => s.Trim().ToLowerInvariant()).Where(s => !string.IsNullOrEmpty(s)).ToHashSet();
            var fGenreSet = (filterGenres ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries)
                            .Select(s => s.Trim()).Where(s => int.TryParse(s, out _))
                            .Select(s => int.Parse(s)).ToHashSet();
            DateTime? fDateFrom = null, fDateTo = null;
            if (!string.IsNullOrWhiteSpace(filterDateFrom) && DateTime.TryParse(filterDateFrom, out var fdf)) fDateFrom = fdf;
            if (!string.IsNullOrWhiteSpace(filterDateTo)   && DateTime.TryParse(filterDateTo,   out var fdt)) fDateTo   = fdt;

            if (fLangSet.Count > 0 || fGenreSet.Count > 0 || fDateFrom.HasValue || fDateTo.HasValue)
            {
                allScored = allScored.Where(x =>
                {
                    var el = x.element;
                    // Language filter
                    if (fLangSet.Count > 0)
                    {
                        var lang = el.TryGetProperty("original_language", out var lp) ? lp.GetString() ?? "" : "";
                        if (!fLangSet.Contains(lang.ToLowerInvariant())) return false;
                    }
                    // Genre filter (at least one matching)
                    if (fGenreSet.Count > 0)
                    {
                        if (!el.TryGetProperty("genre_ids", out var gArr)) return false;
                        var hasMatch = gArr.EnumerateArray().Any(g => g.TryGetInt32(out var gid) && fGenreSet.Contains(gid));
                        if (!hasMatch) return false;
                    }
                    // Date filters
                    if (fDateFrom.HasValue || fDateTo.HasValue)
                    {
                        if (!el.TryGetProperty("release_date", out var rdp) ||
                            !DateTime.TryParse(rdp.GetString(), out var rd)) return false;
                        if (fDateFrom.HasValue && rd < fDateFrom.Value) return false;
                        if (fDateTo.HasValue   && rd > fDateTo.Value)   return false;
                    }
                    return true;
                }).ToList();
            }

            // ── 3-tier diversity slot allocation ───────────────────────────────────────────
            // Tier 1: 30 top-scored picks (any genre)  — represents the user's core taste
            // Tier 2: 20 secondary-genre picks          — genres the user watches but isn't obsessed with
            // Tier 3: 10 wildcard picks                 — high quality/popular regardless of genre
            //
            // This guarantees that even if the user watches 20 animated movies, only ~30 of
            // 60 slots favour that heavily; the remaining 30 come from other areas.

            var usedIds   = new HashSet<int>();
            var tier1     = new List<JsonElement>();
            var tier2     = new List<JsonElement>();
            var tier3     = new List<JsonElement>();

            // Tier 1 — top 30 by pure score
            foreach (var x in allScored)
            {
                if (tier1.Count >= 30) break;
                if (usedIds.Add(x.movieId))
                    tier1.Add(x.element);
            }

            // Tier 2 — scored movies that DON'T exclusively belong to the #1 genre
            // (i.e. they have some profile affinity outside the dominant genre)
            foreach (var x in allScored)
            {
                if (tier2.Count >= 20) break;
                if (!usedIds.Contains(x.movieId) && !x.hasTopGenre)
                {
                    usedIds.Add(x.movieId);
                    tier2.Add(x.element);
                }
            }
            // If tier 2 is still short (user only watches one genre), backfill from remaining scored
            foreach (var x in allScored)
            {
                if (tier2.Count >= 20) break;
                if (!usedIds.Contains(x.movieId))
                {
                    usedIds.Add(x.movieId);
                    tier2.Add(x.element);
                }
            }

            // Tier 3 — wildcard: high quality (va ≥ 7.0) + popular (pop ≥ 40) regardless of genre
            foreach (var x in allScored
                .Where(x => x.va >= 7.0 && x.pop >= 40)
                .OrderByDescending(x => x.va * 0.6 + x.pop * 0.4))
            {
                if (tier3.Count >= 10) break;
                if (!usedIds.Contains(x.movieId))
                {
                    usedIds.Add(x.movieId);
                    tier3.Add(x.element);
                }
            }
            // Backfill tier 3 from any remaining if quality filter left it short
            foreach (var x in allScored)
            {
                if (tier3.Count >= 10) break;
                if (!usedIds.Contains(x.movieId))
                {
                    usedIds.Add(x.movieId);
                    tier3.Add(x.element);
                }
            }

            // Interleave tiers so the grid looks varied: T1, T2, T3, T1, T2, T3 …
            var diversified = new List<JsonElement>(60);
            int i1 = 0, i2 = 0, i3 = 0;
            while (diversified.Count < 60)
            {
                bool added = false;
                if (i1 < tier1.Count) { diversified.Add(tier1[i1++]); added = true; }
                if (i2 < tier2.Count) { diversified.Add(tier2[i2++]); added = true; }
                if (i3 < tier3.Count) { diversified.Add(tier3[i3++]); added = true; }
                if (!added) break; // no more candidates at all
            }

            // ── Output: Paginate 20 items per backend page from the diversified pool ───────────
            // Each call to GetRecommendations(page=N) fetches fresh TMDB data and produces
            // a freshly scored pool of ~80 diversified candidates. We return 20 per page so
            // the frontend can call page=1,2,3... and reliably get new cards each time.
            const int pageSize = 20;
            var skip = (page - 1) * pageSize;
            // If skip >= pool size, just return what we have from the end
            if (skip >= diversified.Count) skip = Math.Max(0, diversified.Count - pageSize);
            var pageResults = diversified.Skip(skip).Take(pageSize).ToList();
            var totalPages  = Math.Max(1, (int)Math.Ceiling(diversified.Count / (double)pageSize));
            // Surface the real total so frontend knows when to stop
            // For practical purposes, treat as 50 pages (sources vary per page call so we never truly run out)
            const int virtualTotalPages = 50;

            var finalJson = JsonSerializer.Serialize(new
            {
                results       = pageResults,
                total_results = diversified.Count,
                page,
                total_pages   = virtualTotalPages
            });

            _logger.LogInformation(
                "[UpcomingMovies] Recommendations for user {UserId}: {Count} diversified " +
                "(tier1={T1}, tier2={T2}, tier3={T3}, watched={Watched}, genres={Genres}, dirs={Dirs}, actors={Actors})",
                userId, diversified.Count, tier1.Count, tier2.Count, tier3.Count,
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

        var svc2 = ProfileService;
        if (svc2 is null)
            return Ok(new { userId, totalWatched = 0, message = "Profile service not yet initialised" });

        var profile = svc2.GetProfile(userId);
        return Ok(new
        {
            userId = profile.UserId,
            lastUpdated = profile.LastUpdated,
            totalWatched = profile.TotalWatched,
            topGenres = svc2.GetTopGenres(profile, 10)
                .Select(id => new { id, name = UserProfileService.TmdbGenreIdToName.GetValueOrDefault(id, id.ToString()), weight = profile.GenreWeights.GetValueOrDefault(id) }),
            topDirectors = svc2.GetTopDirectors(profile, 5)
                .Select(id => new { id, weight = profile.DirectorWeights.GetValueOrDefault(id) }),
            topActors = svc2.GetTopActors(profile, 5)
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
    /// <summary>
    /// Returns IMDB, Rotten Tomatoes, and TMDB ratings for a movie.
    /// TMDB rating is always returned (from TMDB API).
    /// IMDB and RT ratings are returned only when OmdbApiKey is configured.
    /// </summary>
    [HttpGet("ratings")]
    [Authorize]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<IActionResult> GetMovieRatings([FromQuery] int tmdbId = 0)
    {
        try
        {
            var apiKey = Plugin.Instance?.Configuration?.TmdbApiKey;
            if (string.IsNullOrWhiteSpace(apiKey) || tmdbId <= 0)
                return BadRequest(new { error = "TMDB API key not configured or invalid tmdbId." });

            var client = _httpClientFactory.CreateClient();

            // Fetch full TMDB details to get imdb_id and vote_average
            var tmdbUrl = $"{TmdbBaseUrl}/movie/{tmdbId}?api_key={apiKey}";
            var tmdbResp = await client.GetAsync(tmdbUrl).ConfigureAwait(false);

            if (!tmdbResp.IsSuccessStatusCode)
                return StatusCode((int)tmdbResp.StatusCode, new { error = "TMDB API error" });

            var tmdbJson = await tmdbResp.Content.ReadAsStringAsync().ConfigureAwait(false);
            using var tmdbDoc = JsonDocument.Parse(tmdbJson);
            var root = tmdbDoc.RootElement;

            var imdbId     = root.TryGetProperty("imdb_id",       out var imdbEl) ? imdbEl.GetString()                                 : null;
            var tmdbRating = root.TryGetProperty("vote_average",   out var vaEl)  && vaEl.TryGetDouble(out var va)  ? (double?)va       : null;
            var voteCount  = root.TryGetProperty("vote_count",     out var vcEl)  && vcEl.TryGetInt32(out var vc)   ? (int?)vc          : null;

            // Try OMDB for IMDB + Rotten Tomatoes ratings
            string? imdbRating = null;
            string? rtScore    = null;

            var omdbKey = Plugin.Instance?.Configuration?.OmdbApiKey;
            if (!string.IsNullOrWhiteSpace(omdbKey) && !string.IsNullOrWhiteSpace(imdbId))
            {
                try
                {
                    var omdbUrl  = $"https://www.omdbapi.com/?i={imdbId}&apikey={omdbKey}";
                    var omdbResp = await client.GetAsync(omdbUrl).ConfigureAwait(false);

                    if (omdbResp.IsSuccessStatusCode)
                    {
                        var omdbJson = await omdbResp.Content.ReadAsStringAsync().ConfigureAwait(false);
                        using var omdbDoc = JsonDocument.Parse(omdbJson);
                        var omdb = omdbDoc.RootElement;

                        // OMDB returns "N/A" when rating not available
                        if (omdb.TryGetProperty("imdbRating", out var imdbR))
                        {
                            var v = imdbR.GetString();
                            if (!string.IsNullOrEmpty(v) && v != "N/A") imdbRating = v;
                        }

                        if (omdb.TryGetProperty("Ratings", out var ratings))
                        {
                            foreach (var r in ratings.EnumerateArray())
                            {
                                var source = r.TryGetProperty("Source", out var sEl) ? sEl.GetString() : null;
                                var value  = r.TryGetProperty("Value",  out var vEl) ? vEl.GetString() : null;
                                if (source?.Contains("Rotten Tomatoes", StringComparison.OrdinalIgnoreCase) == true
                                    && !string.IsNullOrEmpty(value) && value != "N/A")
                                    rtScore = value;
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "[UpcomingMovies] OMDB ratings fetch failed for imdbId={ImdbId}", imdbId);
                }
            }

            return Ok(new { tmdbRating, voteCount, imdbId, imdbRating, rtScore });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[UpcomingMovies] Unhandled exception in GetMovieRatings");
            return StatusCode(StatusCodes.Status500InternalServerError, new { error = ex.Message });
        }
    }

    // ── Watchlist Fulfillment ───────────────────────────────────────────────────
    // Called by Jellyseerr webhook (or manually) when a requested movie becomes
    // available in Jellyfin.  Adds the movie to the watchlists of all users who
    // had requested it via the Request button on the Discover page.
    //
    // Jellyseerr webhook setup:
    //   URL:  https://your-jellyfin/UpcomingMovies/tmdb/watchlist/fulfill
    //   Body: { "tmdbId": 12345, "jellyfinItemId": "abc123..." }
    //   Send on: media.available
    /// <summary>
    /// Fulfils pending watchlist entries for a movie that has just become available.
    /// Intended to be called by a Jellyseerr "media.available" webhook.
    /// </summary>
    [HttpPost("watchlist/fulfill")]
    [Authorize]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> FulfillWatchlist(
        [FromQuery] int tmdbId,
        [FromQuery] string jellyfinItemId = "")
    {
        if (tmdbId <= 0)
            return BadRequest(new { error = "tmdbId is required" });
        if (string.IsNullOrWhiteSpace(jellyfinItemId))
            return BadRequest(new { error = "jellyfinItemId is required" });

        var consumer = Plugin.WatchlistConsumer;
        if (consumer is null)
            return StatusCode(503, new { error = "WatchlistConsumer not yet initialised" });

        await consumer.FulfillAsync(tmdbId, jellyfinItemId).ConfigureAwait(false);
        return Ok(new { message = $"Watchlist fulfilled for tmdbId={tmdbId}" });
    }

    // ── Profile Debug Endpoints ─────────────────────────────────────────────────
    // GET /UpcomingMovies/tmdb/profile?userId=<jellyfinUserId>
    //   Returns the full scored taste profile for a user in readable JSON.
    //   Use this to verify language weights (LanguageWeights), genre weights,
    //   top directors/actors, and recent watch history.
    //
    // GET /UpcomingMovies/tmdb/profile/all
    //   Lists all user IDs that have a stored profile file.

    /// <summary>
    /// Returns the full taste profile for a Jellyfin user in human-readable JSON.
    /// Useful for debugging recommendation quality and verifying language repair.
    /// URL: GET /UpcomingMovies/tmdb/profile?userId={jellyfinUserId}
    /// </summary>
    [HttpGet("profile")]
    [AllowAnonymous]   // taste-profile data only — no API keys; safe to read without Jellyfin cookie
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public IActionResult GetUserProfile([FromQuery] string userId = "")
    {
        if (string.IsNullOrWhiteSpace(userId))
            return BadRequest(new { error = "userId query param is required" });

        var svc = ProfileService;
        if (svc is null)
            return StatusCode(503, new { error = "ProfileService not initialised" });

        var profile = svc.GetProfile(userId);

        // Convert genre IDs to readable names for display
        var genresByName = profile.GenreWeights
            .OrderByDescending(kv => kv.Value)
            .ToDictionary(
                kv => UserProfileService.TmdbGenreIdToName.TryGetValue(kv.Key, out var name)
                      ? $"{name} (id={kv.Key})" : $"genre_{kv.Key}",
                kv => Math.Round(kv.Value, 2));

        var languagesSorted = profile.LanguageWeights
            .OrderByDescending(kv => kv.Value)
            .ToDictionary(kv => kv.Key, kv => Math.Round(kv.Value, 2));

        var directorsSorted = profile.DirectorWeights
            .OrderByDescending(kv => kv.Value)
            .Take(10)
            .ToDictionary(kv => $"person_{kv.Key}", kv => Math.Round(kv.Value, 2));

        var actorsSorted = profile.ActorWeights
            .OrderByDescending(kv => kv.Value)
            .Take(10)
            .ToDictionary(kv => $"person_{kv.Key}", kv => Math.Round(kv.Value, 2));

        return Ok(new
        {
            userId,
            totalWatched      = profile.TotalWatched,
            watchedTmdbIds    = profile.WatchedTmdbIds.TakeLast(20).ToList(),
            languageWeights   = languagesSorted,
            genreWeights      = genresByName,
            topDirectors      = directorsSorted,
            topActors         = actorsSorted,
            recentWatches     = profile.RecentWatches?.Take(10).Select(w => new {
                w.TmdbId, w.Language, watchedAt = w.WatchedAt.ToString("yyyy-MM-dd")
            }),
            watchlistTmdbIds  = profile.WatchlistTmdbIds?.Take(20).ToList()
        });
    }

    /// <summary>
    /// Lists all Jellyfin user IDs that have a stored recommendation profile.
    /// URL: GET /UpcomingMovies/tmdb/profile/all
    /// </summary>
    [HttpGet("profile/all")]
    [AllowAnonymous]   // lists userId files only
    [ProducesResponseType(StatusCodes.Status200OK)]
    public IActionResult GetAllProfileUsers()
    {
        var svc = ProfileService;
        if (svc is null)
            return StatusCode(503, new { error = "ProfileService not initialised" });

        var users = svc.GetAllProfileUserIds();
        return Ok(new { count = users.Count, userIds = users });
    }
}


