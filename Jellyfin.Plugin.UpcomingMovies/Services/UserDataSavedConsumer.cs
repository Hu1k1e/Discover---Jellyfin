using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using MediaBrowser.Controller.Entities.Movies;
using MediaBrowser.Controller.Library;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.UpcomingMovies.Services;

/// <summary>
/// Handles Jellyfin UserDataSaved events to keep user taste profiles up to date.
/// Fires on both Played=true (watch signal, 1× weight) and Likes=true (watchlist signal, 0.5× weight).
/// Registered directly in Plugin.cs constructor — no DI interface needed.
/// </summary>
public class UserDataSavedConsumer
{
    private readonly UserProfileService _profileService;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<UserDataSavedConsumer> _logger;

    private const string TmdbBaseUrl = "https://api.themoviedb.org/3";

    public UserDataSavedConsumer(
        UserProfileService profileService,
        IHttpClientFactory httpClientFactory,
        ILogger<UserDataSavedConsumer> logger)
    {
        _profileService = profileService;
        _httpClientFactory = httpClientFactory;
        _logger = logger;
    }

    /// <summary>
    /// Called by IUserDataManager.UserDataSaved whenever user data changes for a library item.
    /// Handles two signals:
    ///   - Played = true  → full watch signal (1× weight + exponential decay on all weights)
    ///   - Likes = true   → watchlist signal  (0.5× weight, additive only, no decay)
    /// </summary>
    public void OnUserDataSaved(object? sender, UserDataSaveEventArgs e)
    {
        // Only movies, not episodes/music/etc.
        if (e.Item is not Movie movie) return;

        // Need a TMDB ID to be useful
        if (!movie.ProviderIds.TryGetValue("Tmdb", out var tmdbIdStr) ||
            !int.TryParse(tmdbIdStr, out var tmdbId) || tmdbId <= 0)
            return;

        var userId = e.UserId.ToString("N");

        // Map Jellyfin genre names → TMDB genre IDs (BaseItem.Genres is string[], always available)
        var genreIds = (movie.Genres ?? Array.Empty<string>())
            .Select(g => UserProfileService.JellyfinNameToTmdbGenreId.TryGetValue(g, out var id) ? id : 0)
            .Where(id => id > 0)
            .ToList();

        if (e.UserData.Played)
        {
            // Full watch signal — strongest taste indicator, triggers decay of old weights
            Task.Run(() => FetchDetailsAndUpdateAsync(userId, tmdbId, genreIds, isWatchlist: false));
        }
        else if (e.UserData.Likes == true)
        {
            // Watchlist signal — user bookmarked this movie (our /Rating?Likes=true call)
            Task.Run(() => FetchDetailsAndUpdateAsync(userId, tmdbId, genreIds, isWatchlist: true));
        }
    }

    /// <summary>
    /// Fetches TMDB movie details (for original_language) and credits (directors/actors),
    /// then updates the user profile with the appropriate signal weight.
    ///
    /// IMPORTANT: We MUST fetch the actual original_language from TMDB rather than defaulting
    /// to "en". A user who watches Malayalam movies should accumulate LanguageWeights["ml"],
    /// not LanguageWeights["en"], so that the recommendation engine can surface regional content.
    /// </summary>
    private async Task FetchDetailsAndUpdateAsync(string userId, int tmdbId, List<int> genreIds, bool isWatchlist)
    {
        var directors = new List<int>();
        var actors    = new List<int>();
        var language  = "en"; // fallback only — overwritten by TMDB response below

        try
        {
            var apiKey = Plugin.Instance?.Configuration?.TmdbApiKey;
            if (!string.IsNullOrWhiteSpace(apiKey))
            {
                var client = _httpClientFactory.CreateClient();

                // ── Fetch 1: Movie details for original_language ──────────────────────────
                // This is the CRITICAL call that makes language weighting work correctly.
                // Without it, all movies default to "en" regardless of their actual language.
                try
                {
                    var detailsUrl = $"{TmdbBaseUrl}/movie/{tmdbId}?api_key={apiKey}&language=en-US";
                    var detailsRes = await client.GetAsync(detailsUrl).ConfigureAwait(false);
                    if (detailsRes.IsSuccessStatusCode)
                    {
                        var detailsJson = await detailsRes.Content.ReadAsStringAsync().ConfigureAwait(false);
                        using var detailsDoc = JsonDocument.Parse(detailsJson);
                        if (detailsDoc.RootElement.TryGetProperty("original_language", out var langEl))
                        {
                            var fetchedLang = langEl.GetString();
                            if (!string.IsNullOrWhiteSpace(fetchedLang))
                                language = fetchedLang;
                        }
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "[UpcomingMovies] TMDB details fetch failed for {TmdbId}", tmdbId);
                }

                // ── Fetch 2: Credits for directors and top actors ──────────────────────────
                try
                {
                    var creditsUrl = $"{TmdbBaseUrl}/movie/{tmdbId}/credits?api_key={apiKey}";
                    var creditsRes = await client.GetAsync(creditsUrl).ConfigureAwait(false);
                    if (creditsRes.IsSuccessStatusCode)
                    {
                        var creditsJson = await creditsRes.Content.ReadAsStringAsync().ConfigureAwait(false);
                        using var creditsDoc = JsonDocument.Parse(creditsJson);

                        // Directors from crew
                        if (creditsDoc.RootElement.TryGetProperty("crew", out var crew))
                        {
                            foreach (var member in crew.EnumerateArray())
                            {
                                if (member.TryGetProperty("job", out var job) &&
                                    job.GetString()?.Equals("Director", StringComparison.OrdinalIgnoreCase) == true &&
                                    member.TryGetProperty("id", out var idEl) &&
                                    idEl.TryGetInt32(out var personId))
                                {
                                    directors.Add(personId);
                                }
                            }
                        }

                        // Top-billed actors (first 5)
                        if (creditsDoc.RootElement.TryGetProperty("cast", out var cast))
                        {
                            foreach (var member in cast.EnumerateArray().Take(5))
                            {
                                if (member.TryGetProperty("id", out var idEl) &&
                                    idEl.TryGetInt32(out var personId))
                                {
                                    actors.Add(personId);
                                }
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "[UpcomingMovies] TMDB credits fetch failed for {TmdbId}", tmdbId);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[UpcomingMovies] Profile update fetch failed for {TmdbId}", tmdbId);
        }

        _logger.LogInformation(
            "[UpcomingMovies] Profile update: user={UserId} tmdb={TmdbId} lang={Lang} watchlist={WL}",
            userId, tmdbId, language, isWatchlist);

        if (isWatchlist)
        {
            _profileService.UpdateWithWatchlist(userId, tmdbId, genreIds, language, directors, actors);
        }
        else
        {
            _profileService.UpdateWithWatch(userId, tmdbId, genreIds, language, directors, actors);
        }

        // ── Lazy profile language repair ──────────────────────────────────────────
        // Profiles built before Phase 35 (v1.0.51) have all LanguageWeights incorrectly set to "en"
        // because the consumer was hardcoded. This repair detects a corrupted profile (non-English
        // weight < 5.0 = less than ~1 non-English watch equivalent) and rebuilds LanguageWeights
        // from the actual TMDB data for the user's 30 most recent watched movies.
        // The repair runs in the background; it will stop running once the profile is healthy.
        _ = Task.Run(() => RepairLanguageWeightsIfNeededAsync(userId));
    }

    /// <summary>
    /// Detects and repairs corrupted LanguageWeights caused by pre-Phase-35 code that
    /// hardcoded all languages to "en". Fetches up to 30 recent WatchedTmdbIds from TMDB
    /// in parallel (max 5 concurrent) to discover actual original_language values, then
    /// adds the missing weights to the profile.
    ///
    /// Only runs when the non-English weight total is &lt; 5.0 AND the user has watched
    /// at least 3 movies — covering the case where someone has watched many Hindi/Malayalam
    /// films but zero non-English weight accumulated.
    /// </summary>
    private async Task RepairLanguageWeightsIfNeededAsync(string userId)
    {
        try
        {
            var apiKey = Plugin.Instance?.Configuration?.TmdbApiKey;
            if (string.IsNullOrWhiteSpace(apiKey)) return;

            var profile = _profileService.GetProfile(userId);

            // Guard: only repair if profile looks suspicious
            var nonEnWeight = profile.LanguageWeights
                .Where(kv => kv.Key != "en")
                .Sum(kv => kv.Value);
            if (nonEnWeight >= 5.0) return;           // already healthy
            if (profile.WatchedTmdbIds.Count < 3) return; // too few watches to matter

            _logger.LogInformation(
                "[UpcomingMovies] Profile language repair starting for user {UserId} " +
                "(non-English weight={W:F1}, watched={Count})",
                userId, nonEnWeight, profile.WatchedTmdbIds.Count);

            // Fetch last 30 watched movies from TMDB to get actual languages
            var recentIds = profile.WatchedTmdbIds.TakeLast(30).ToList();
            var langCounts = new Dictionary<string, int>();
            var semaphore = new System.Threading.SemaphoreSlim(5, 5); // max 5 parallel TMDB calls
            var client = _httpClientFactory.CreateClient();

            var tasks = recentIds.Select(async id =>
            {
                await semaphore.WaitAsync().ConfigureAwait(false);
                try
                {
                    var res = await client.GetAsync(
                        $"{TmdbBaseUrl}/movie/{id}?api_key={apiKey}&language=en-US")
                        .ConfigureAwait(false);
                    if (!res.IsSuccessStatusCode) return;
                    var json = await res.Content.ReadAsStringAsync().ConfigureAwait(false);
                    using var doc = JsonDocument.Parse(json);
                    if (doc.RootElement.TryGetProperty("original_language", out var langEl))
                    {
                        var lang = langEl.GetString();
                        if (!string.IsNullOrWhiteSpace(lang))
                            lock (langCounts) { langCounts[lang] = langCounts.GetValueOrDefault(lang) + 1; }
                    }
                }
                catch { /* ignore per-movie failures */ }
                finally { semaphore.Release(); }
            });

            await Task.WhenAll(tasks).ConfigureAwait(false);

            // Re-load profile (may have changed during the await) and patch language weights
            profile = _profileService.GetProfile(userId);
            bool changed = false;

            foreach (var (lang, count) in langCounts)
            {
                if (lang == "en") continue;
                // Only add weight if this language is currently underrepresented in the profile.
                // Weight = count × BaseWatchWeight × 0.4 (conservative — avoids overshooting decay).
                var addWeight = count * 5.0 * 0.4;
                var current   = profile.LanguageWeights.GetValueOrDefault(lang);
                if (addWeight > current)
                {
                    profile.LanguageWeights[lang] = addWeight;
                    changed = true;
                    _logger.LogInformation(
                        "[UpcomingMovies] Repaired language weight for user={UserId}: {Lang} = {W:F1} (from {Count} historical movies)",
                        userId, lang, addWeight, count);
                }
            }

            if (changed)
            {
                _profileService.SaveProfile(profile);
                _logger.LogInformation("[UpcomingMovies] Profile language repair complete for user {UserId}", userId);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[UpcomingMovies] Profile language repair failed for user {UserId}", userId);
        }
    }
}
