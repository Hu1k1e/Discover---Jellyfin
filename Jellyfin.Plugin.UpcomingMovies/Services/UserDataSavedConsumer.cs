using System;
using System.Collections.Concurrent;
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
    /// Handles ONLY the watchlist signal (Likes=true).
    ///
    /// NOTE: Watch signals (Played=true) are intentionally NOT handled here.
    /// They are handled by <see cref="PlaybackStoppedConsumer.OnPlaybackStopped"/> instead,
    /// because <c>PlaybackStopped</c> provides the real live <c>PositionTicks</c> BEFORE
    /// Jellyfin resets them to 0 upon completion. This is the only reliable way to compute
    /// the true watch percentage.
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

        // Watchlist signal ONLY — full watch events are handled by PlaybackStoppedConsumer.
        //
        // UserDataSaved fires for EVERY save of a movie's user data (playback progress, "played" toggles,
        // watchlist sync tasks that re-send the same rating, ...). A movie that is already on the watchlist
        // keeps Likes == true on all of those saves, so reacting to "Likes == true" alone re-applied the
        // watchlist weight (and re-fetched TMDB data) over and over, inflating the taste profile.
        // Only the transition "not on the watchlist -> on the watchlist" counts, and removing a movie
        // (Likes no longer true) makes it eligible again.
        var known = KnownWatchlist(userId);
        if (e.UserData.Likes == true)
        {
            // Add returns false when the movie is already tracked: not a new watchlist add -> ignore
            bool isNew;
            lock (known)
            {
                isNew = known.Add(tmdbId);
            }

            if (!isNew) return;

            // Watchlist signal — user bookmarked this movie (our /Rating?Likes=true call)
            Task.Run(() => FetchDetailsAndUpdateAsync(userId, tmdbId, genreIds, 1.0, isWatchlist: true));
        }
        else if (e.SaveReason.ToString() == "UpdateUserRating")
        {
            // A real rating/watchlist toggle with Likes no longer true: forget the movie so a later re-add counts again.
            // (Other save reasons such as playback progress are ignored without touching any file.)
            bool wasTracked;
            lock (known)
            {
                wasTracked = known.Remove(tmdbId);
            }

            if (wasTracked)
            {
                Task.Run(() => _profileService.RemoveFromWatchlist(userId, tmdbId));
            }
        }
    }

    // Per-user set of TMDB ids already counted as watchlist signals. Loaded once per user from the stored
    // profile (WatchlistTmdbIds) and then kept in memory, so the very frequent UserDataSaved events never read files.
    private readonly ConcurrentDictionary<string, HashSet<int>> _watchlistKnown = new(StringComparer.OrdinalIgnoreCase);

    private HashSet<int> KnownWatchlist(string userId)
        => _watchlistKnown.GetOrAdd(userId, id => new HashSet<int>(_profileService.GetProfile(id).WatchlistTmdbIds));

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
                var client = Jellyfin.Plugin.UpcomingMovies.Services.TmdbHttp.CreateClient();

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
