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
            double watchPercentage = 1.0;
            if (movie.RunTimeTicks > 0)
            {
                if (e.UserData.PlaybackPositionTicks == 0)
                    watchPercentage = 1.0;
                else
                    watchPercentage = (double)e.UserData.PlaybackPositionTicks / movie.RunTimeTicks;
            }
            
            // Full watch signal — strongest taste indicator, triggers decay of old weights
            Task.Run(() => FetchDetailsAndUpdateAsync(userId, tmdbId, genreIds, watchPercentage, isWatchlist: false));
        }
        else if (e.UserData.Likes == true)
        {
            // Watchlist signal — user bookmarked this movie (our /Rating?Likes=true call)
            Task.Run(() => FetchDetailsAndUpdateAsync(userId, tmdbId, genreIds, 1.0, isWatchlist: true));
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
    private async Task FetchDetailsAndUpdateAsync(string userId, int tmdbId, List<int> genreIds, double watchPercentage, bool isWatchlist)
    {
        var directors = new List<int>();
        var actors    = new List<int>();
        var keywords  = new List<int>();
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

                // ── Fetch 3: Keywords for micro-genres ──────────────────────────
                try
                {
                    var keywordsUrl = $"{TmdbBaseUrl}/movie/{tmdbId}/keywords?api_key={apiKey}";
                    var keywordsRes = await client.GetAsync(keywordsUrl).ConfigureAwait(false);
                    if (keywordsRes.IsSuccessStatusCode)
                    {
                        var keywordsJson = await keywordsRes.Content.ReadAsStringAsync().ConfigureAwait(false);
                        using var keywordsDoc = JsonDocument.Parse(keywordsJson);

                        if (keywordsDoc.RootElement.TryGetProperty("keywords", out var kwArr))
                        {
                            foreach (var kw in kwArr.EnumerateArray())
                            {
                                if (kw.TryGetProperty("id", out var idEl) && idEl.TryGetInt32(out var kwId))
                                {
                                    keywords.Add(kwId);
                                }
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "[UpcomingMovies] TMDB keywords fetch failed for {TmdbId}", tmdbId);
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
            _profileService.UpdateWithWatchlist(userId, tmdbId, genreIds, language, directors, actors, keywords);
        }
        else
        {
            _profileService.UpdateWithWatch(userId, tmdbId, genreIds, language, directors, actors, keywords, watchPercentage);
        }
    }
}
