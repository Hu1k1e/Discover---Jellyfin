using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using Jellyfin.Plugin.UpcomingMovies.Services;
using MediaBrowser.Controller.Entities.Movies;
using MediaBrowser.Controller.Library;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.UpcomingMovies.Services;

/// <summary>
/// Handles Jellyfin UserDataSaved events to keep user taste profiles up to date.
/// Registered directly in Plugin.cs constructor -- no DI interface needed.
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
    /// Called by IUserDataManager.UserDataSaved whenever a user plays or marks a movie as watched.
    /// </summary>
    public void OnUserDataSaved(object? sender, UserDataSaveEventArgs e)
    {
        // Only process if the item was marked as played (covers both playback-finish and manual mark)
        if (!e.UserData.Played)
            return;

        // Only movies, not episodes/music/etc.
        if (e.Item is not Movie movie)
            return;

        // Need a TMDB ID to be useful
        if (!movie.ProviderIds.TryGetValue("Tmdb", out var tmdbIdStr) ||
            !int.TryParse(tmdbIdStr, out var tmdbId) || tmdbId <= 0)
            return;

        var userId  = e.UserId.ToString("N");

        // Map Jellyfin genre names → TMDB genre IDs (BaseItem.Genres is string[], always available)
        var genreIds = (movie.Genres ?? Array.Empty<string>())
            .Select(g => UserProfileService.JellyfinNameToTmdbGenreId.TryGetValue(g, out var id) ? id : 0)
            .Where(id => id > 0)
            .ToList();

        // Always fetch director/actor from TMDB credits API (avoids Jellyfin.Data.Enums dependency)
        // Language defaults to "en" -- improved in a future phase if needed
        Task.Run(() => FetchCreditsAndUpdateAsync(userId, tmdbId, genreIds));
    }

    private async Task FetchCreditsAndUpdateAsync(string userId, int tmdbId, List<int> genreIds)
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

        _profileService.UpdateWithWatch(userId, tmdbId, genreIds, "en", directors, actors);
    }
}
