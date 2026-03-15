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
            Task.Run(() => FetchCreditsAndUpdateAsync(userId, tmdbId, genreIds, isWatchlist: false));
        }
        else if (e.UserData.Likes == true)
        {
            // Watchlist signal — user bookmarked this movie (our /Rating?Likes=true call)
            Task.Run(() => FetchCreditsAndUpdateAsync(userId, tmdbId, genreIds, isWatchlist: true));
        }
    }

    private async Task FetchCreditsAndUpdateAsync(string userId, int tmdbId, List<int> genreIds, bool isWatchlist)
    {
        var directors = new List<int>();
        var actors    = new List<int>();

        try
        {
            var apiKey = Plugin.Instance?.Configuration?.TmdbApiKey;
            if (!string.IsNullOrWhiteSpace(apiKey))
            {
                var client   = _httpClientFactory.CreateClient();
                var url      = $"{TmdbBaseUrl}/movie/{tmdbId}/credits?api_key={apiKey}";
                var response = await client.GetAsync(url).ConfigureAwait(false);

                if (response.IsSuccessStatusCode)
                {
                    var json = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                    using var doc = JsonDocument.Parse(json);

                    // Directors from crew
                    if (doc.RootElement.TryGetProperty("crew", out var crew))
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
                    if (doc.RootElement.TryGetProperty("cast", out var cast))
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
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[UpcomingMovies] TMDB credits fetch failed for {TmdbId}", tmdbId);
        }

        if (isWatchlist)
        {
            _profileService.UpdateWithWatchlist(userId, tmdbId, genreIds, "en", directors, actors);
        }
        else
        {
            _profileService.UpdateWithWatch(userId, tmdbId, genreIds, "en", directors, actors);
        }
    }
}
