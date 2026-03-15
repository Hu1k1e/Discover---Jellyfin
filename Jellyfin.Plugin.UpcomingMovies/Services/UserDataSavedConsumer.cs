using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using Jellyfin.Plugin.UpcomingMovies.Services;
using MediaBrowser.Controller.Entities.Movies;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Plugins;
using MediaBrowser.Model.Entities;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.UpcomingMovies.Services;

/// <summary>
/// Listens for Jellyfin UserDataSaved events and updates the user's taste profile
/// automatically whenever a movie is played or marked as watched.
/// Uses IServerEntryPoint so it runs for the lifetime of the server.
/// </summary>
public class UserDataSavedConsumer : IServerEntryPoint
{
    private readonly IUserDataManager _userDataManager;
    private readonly ILibraryManager _libraryManager;
    private readonly UserProfileService _profileService;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<UserDataSavedConsumer> _logger;

    private const string TmdbBaseUrl = "https://api.themoviedb.org/3";

    public UserDataSavedConsumer(
        IUserDataManager userDataManager,
        ILibraryManager libraryManager,
        UserProfileService profileService,
        IHttpClientFactory httpClientFactory,
        ILogger<UserDataSavedConsumer> logger)
    {
        _userDataManager = userDataManager;
        _libraryManager = libraryManager;
        _profileService = profileService;
        _httpClientFactory = httpClientFactory;
        _logger = logger;
    }

    public Task RunAsync()
    {
        _userDataManager.UserDataSaved += OnUserDataSaved;
        _logger.LogInformation("[UpcomingMovies] UserDataSaved listener registered — profiles will auto-update on movie watch.");
        return Task.CompletedTask;
    }

    private void OnUserDataSaved(object? sender, UserDataSaveEventArgs e)
    {
        // Only care if user actually played or marked-watched
        if (e.SaveReason != UserDataSaveReason.PlaybackFinished &&
            e.SaveReason != UserDataSaveReason.TogglePlayed)
            return;

        if (!e.UserData.Played)
            return;

        // Only movies, not episodes/music/etc.
        if (e.Item is not Movie movie)
            return;

        // Need a TMDB ID to be useful
        if (!movie.ProviderIds.TryGetValue("Tmdb", out var tmdbIdStr) ||
            !int.TryParse(tmdbIdStr, out var tmdbId) || tmdbId <= 0)
            return;

        var userId = e.UserId.ToString("N");

        // Map Jellyfin genre names → TMDB genre IDs
        var genreIds = (movie.Genres ?? Array.Empty<string>())
            .Select(g => UserProfileService.JellyfinNameToTmdbGenreId.TryGetValue(g, out var id) ? id : 0)
            .Where(id => id > 0)
            .ToList();

        var language = movie.OriginalLanguage ?? "en";

        // Get director/actor TMDB person IDs from Jellyfin metadata
        var directors = new List<int>();
        var actors = new List<int>();

        try
        {
            var people = _libraryManager.GetPeople(new InternalPeopleQuery { ItemId = movie.Id });
            foreach (var person in people)
            {
                if (!person.ProviderIds.TryGetValue("Tmdb", out var personTmdbStr) ||
                    !int.TryParse(personTmdbStr, out var personId) || personId <= 0)
                    continue;

                if (person.Type == PersonKind.Director)
                    directors.Add(personId);
                else if (person.Type == PersonKind.Actor && actors.Count < 10)
                    actors.Add(personId);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[UpcomingMovies] Could not load people for item {ItemId}", movie.Id);
        }

        // If Jellyfin metadata didn't have TMDB person IDs, fetch from TMDB in the background
        if (directors.Count == 0 && actors.Count == 0)
        {
            Task.Run(() => FetchCreditsAndUpdateAsync(userId, tmdbId, genreIds, language));
        }
        else
        {
            _profileService.UpdateWithWatch(userId, tmdbId, genreIds, language, directors, actors);
        }
    }

    /// <summary>
    /// Fallback: fetches credits from TMDB and then updates the profile.
    /// Called in a background task when Jellyfin metadata has no TMDB person IDs.
    /// </summary>
    private async Task FetchCreditsAndUpdateAsync(
        string userId, int tmdbId, List<int> genreIds, string language)
    {
        var directors = new List<int>();
        var actors = new List<int>();

        try
        {
            var apiKey = Plugin.Instance?.Configuration?.TmdbApiKey;
            if (!string.IsNullOrWhiteSpace(apiKey))
            {
                var client = _httpClientFactory.CreateClient();
                var url = $"{TmdbBaseUrl}/movie/{tmdbId}/credits?api_key={apiKey}";
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

                    // Top-billed actors from cast (first 5)
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
            _logger.LogWarning(ex, "[UpcomingMovies] TMDB credits fetch failed for movie {TmdbId}", tmdbId);
        }

        _profileService.UpdateWithWatch(userId, tmdbId, genreIds, language, directors, actors);
    }

    public void Dispose()
    {
        _userDataManager.UserDataSaved -= OnUserDataSaved;
        GC.SuppressFinalize(this);
    }
}
