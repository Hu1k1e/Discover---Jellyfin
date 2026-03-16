using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.UpcomingMovies.Model;
using MediaBrowser.Controller.Entities.Movies;
using MediaBrowser.Controller.Library;
using MediaBrowser.Model.Tasks;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.UpcomingMovies.Services;

/// <summary>
/// Bulk sync task that rebuilds User Profiles from their native Jellyfin watch history and watchlists.
/// This solves the problem where movies watched before the plugin was installed are missing 
/// from the profile.
/// </summary>
public class SyncProfilesTask : IScheduledTask
{
    private readonly IUserManager _userManager;
    private readonly ILibraryManager _libraryManager;
    private readonly IUserDataManager _userDataManager;
    private readonly UserProfileService _profileService;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<SyncProfilesTask> _logger;

    private const string TmdbBaseUrl = "https://api.themoviedb.org/3";

    // Cache to prevent hitting TMDB multiple times for the same movie across different users
    private readonly Dictionary<int, MovieDetailsCache> _tmdbCache = new();

    public string Name => "Upcoming Movies - Sync User Profiles";
    public string Key => "Jellyfin.Plugin.UpcomingMovies.SyncProfiles";
    public string Description => "Reads native Jellyfin watch history and watchlists for all users and rebuilds their Upcoming Movies profiles. Run this once after you install the plugin.";
    public string Category => "Upcoming Movies";

    public SyncProfilesTask(
        IUserManager userManager,
        ILibraryManager libraryManager,
        IUserDataManager userDataManager,
        UserProfileService profileService,
        IHttpClientFactory httpClientFactory,
        ILogger<SyncProfilesTask> logger)
    {
        _userManager = userManager;
        _libraryManager = libraryManager;
        _userDataManager = userDataManager;
        _profileService = profileService;
        _httpClientFactory = httpClientFactory;
        _logger = logger;
    }

    public async Task ExecuteAsync(IProgress<double> progress, CancellationToken cancellationToken)
    {
        _logger.LogInformation("[UpcomingMovies] Starting Bulk Profile Sync Task...");
        _tmdbCache.Clear();

        var users = _userManager.Users.ToList();
        var allMovies = _libraryManager.GetItemList(new InternalItemsQuery
        {
            IncludeItemTypes = new[] { "Movie" }
        }).OfType<Movie>().ToList();

        _logger.LogInformation("[UpcomingMovies] Found {UserCount} users and {MovieCount} total movies.", users.Count, allMovies.Count);

        int userIndex = 0;
        foreach (var user in users)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var userIdStr = user.Id.ToString("N");
            _logger.LogInformation("[UpcomingMovies] Processing user {UserName} ({UserId})", user.Name, userIdStr);

            // Recreate a fresh profile
            var profile = new UserProfileData { UserId = userIdStr };

            // We need to process watches chronologically to apply exponential decay correctly
            // But Jellyfin's user data doesn't reliably store the exact watch date across all clients unless we query playback history.
            // For a bulk import, we'll assign them all without decay, THEN apply decay if there's a lot, or just treat 
            // the whole library as the "base" taste profile.
            // Actually, to keep it simple and match UserDataSavedConsumer: we'll just process them.

            var userMovies = new List<HistoricalEvent>();

            foreach (var movie in allMovies)
            {
                var userData = _userDataManager.GetUserData(user, movie);
                bool played = userData.Played;
                bool liked = userData.Likes == true;

                if (!played && !liked) continue;

                if (!movie.ProviderIds.TryGetValue("Tmdb", out var tmdbIdStr) || !int.TryParse(tmdbIdStr, out var tmdbId) || tmdbId <= 0)
                    continue;

                userMovies.Add(new HistoricalEvent
                {
                    Movie = movie,
                    TmdbId = tmdbId,
                    Played = played,
                    Liked = liked,
                    // Note: UserData doesn't always have a strict 'date watched' that's easy to pull without IUserDataRepository.
                    // We'll use LastPlayedDate if available
                    Date = userData.LastPlayedDate ?? DateTime.UtcNow.AddYears(-1)
                });
            }

            // Order by date oldest to newest so exponential decay applies chronologically
            userMovies = userMovies.OrderBy(x => x.Date).ToList();

            int movieIndex = 0;
            foreach (var evt in userMovies)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var details = await GetCachedTmdbDetailsAsync(evt.TmdbId, cancellationToken);
                
                var genreIds = (evt.Movie.Genres ?? Array.Empty<string>())
                    .Select(g => UserProfileService.JellyfinNameToTmdbGenreId.TryGetValue(g, out var id) ? id : 0)
                    .Where(id => id > 0)
                    .ToList();

                if (evt.Played)
                {
                    ApplyHistoricalWatch(profile, evt.TmdbId, genreIds, details.Language, details.Directors, details.Actors, evt.Date);
                }
                else if (evt.Liked)
                {
                    ApplyHistoricalWatchlist(profile, evt.TmdbId, genreIds, details.Language, details.Directors, details.Actors);
                }

                movieIndex++;
                if (movieIndex % 25 == 0)
                {
                    _logger.LogInformation("[UpcomingMovies] User {UserName}: Processed {MIndex}/{Total} movies...", user.Name, movieIndex, userMovies.Count);
                }
            }

            _profileService.SaveProfile(profile);
            
            userIndex++;
            progress.Report((double)userIndex / users.Count * 100);
        }

        _logger.LogInformation("[UpcomingMovies] Bulk Profile Sync completed successfully.");
    }

    private class HistoricalEvent
    {
        public Movie Movie { get; set; } = null!;
        public int TmdbId { get; set; }
        public bool Played { get; set; }
        public bool Liked { get; set; }
        public DateTime Date { get; set; }
    }

    private class MovieDetailsCache
    {
        public string Language { get; set; } = "en";
        public List<int> Directors { get; set; } = new();
        public List<int> Actors { get; set; } = new();
    }

    private async Task<MovieDetailsCache> GetCachedTmdbDetailsAsync(int tmdbId, CancellationToken cancellationToken)
    {
        if (_tmdbCache.TryGetValue(tmdbId, out var cached))
        {
            return cached;
        }

        var details = new MovieDetailsCache();
        var apiKey = Plugin.Instance?.Configuration?.TmdbApiKey;

        if (!string.IsNullOrWhiteSpace(apiKey))
        {
            try
            {
                var client = _httpClientFactory.CreateClient();
                
                // 1. Language
                var url = $"{TmdbBaseUrl}/movie/{tmdbId}?api_key={apiKey}&language=en-US";
                var res = await client.GetAsync(url, cancellationToken).ConfigureAwait(false);
                if (res.IsSuccessStatusCode)
                {
                    using var doc = JsonDocument.Parse(await res.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false));
                    if (doc.RootElement.TryGetProperty("original_language", out var langEl))
                    {
                        var fetchedLang = langEl.GetString();
                        if (!string.IsNullOrWhiteSpace(fetchedLang))
                            details.Language = fetchedLang;
                    }
                }

                // 2. Credits
                var credUrl = $"{TmdbBaseUrl}/movie/{tmdbId}/credits?api_key={apiKey}";
                var credRes = await client.GetAsync(credUrl, cancellationToken).ConfigureAwait(false);
                if (credRes.IsSuccessStatusCode)
                {
                    using var doc = JsonDocument.Parse(await credRes.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false));
                    
                    if (doc.RootElement.TryGetProperty("crew", out var crew))
                    {
                        foreach (var member in crew.EnumerateArray())
                        {
                            if (member.TryGetProperty("job", out var job) &&
                                job.GetString()?.Equals("Director", StringComparison.OrdinalIgnoreCase) == true &&
                                member.TryGetProperty("id", out var idEl) &&
                                idEl.TryGetInt32(out var personId))
                            {
                                details.Directors.Add(personId);
                            }
                        }
                    }

                    if (doc.RootElement.TryGetProperty("cast", out var cast))
                    {
                        foreach (var member in cast.EnumerateArray().Take(5))
                        {
                            if (member.TryGetProperty("id", out var idEl) &&
                                idEl.TryGetInt32(out var personId))
                            {
                                details.Actors.Add(personId);
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "[UpcomingMovies] Failed to fetch TMDB details for {TmdbId} during sync.", tmdbId);
            }
        }

        _tmdbCache[tmdbId] = details;
        return details;
    }

    // Custom Apply methods so we don't need to rewrite UserProfileService just to accept a Date
    private void ApplyHistoricalWatch(UserProfileData profile, int tmdbId, List<int> genreIds, string language, List<int> directors, List<int> actors, DateTime watchDate)
    {
        const double DecayFactor = 0.92;
        const double BaseWatchWeight = 5.0;

        if (!profile.WatchedTmdbIds.Contains(tmdbId))
            profile.WatchedTmdbIds.Add(tmdbId);

        // Decay
        foreach (var k in profile.GenreWeights.Keys.ToList()) profile.GenreWeights[k] *= DecayFactor;
        foreach (var k in profile.DirectorWeights.Keys.ToList()) profile.DirectorWeights[k] *= DecayFactor;
        foreach (var k in profile.ActorWeights.Keys.ToList()) profile.ActorWeights[k] *= DecayFactor;
        foreach (var k in profile.LanguageWeights.Keys.ToList()) profile.LanguageWeights[k] *= DecayFactor;

        // Add
        foreach (var g in genreIds) profile.GenreWeights[g] = profile.GenreWeights.GetValueOrDefault(g) + BaseWatchWeight;
        if (!string.IsNullOrWhiteSpace(language)) profile.LanguageWeights[language] = profile.LanguageWeights.GetValueOrDefault(language) + BaseWatchWeight;
        foreach (var d in directors) profile.DirectorWeights[d] = profile.DirectorWeights.GetValueOrDefault(d) + (BaseWatchWeight * 2);
        foreach (var a in actors.Take(5)) profile.ActorWeights[a] = profile.ActorWeights.GetValueOrDefault(a) + BaseWatchWeight;

        profile.RecentWatches.Insert(0, new WatchEntry
        {
            TmdbId = tmdbId,
            WatchedAt = watchDate,
            GenreIds = genreIds,
            Language = language ?? "en"
        });

        if (profile.RecentWatches.Count > 200)
            profile.RecentWatches.RemoveRange(200, profile.RecentWatches.Count - 200);

        profile.TotalWatched++;
    }

    private void ApplyHistoricalWatchlist(UserProfileData profile, int tmdbId, List<int> genreIds, string language, List<int> directors, List<int> actors)
    {
        profile.WatchlistTmdbIds.Remove(tmdbId);
        profile.WatchlistTmdbIds.Insert(0, tmdbId);
        if (profile.WatchlistTmdbIds.Count > 100)
            profile.WatchlistTmdbIds.RemoveRange(100, profile.WatchlistTmdbIds.Count - 100);

        const double wlWeight = 5.0 * 0.5;

        foreach (var g in genreIds) profile.GenreWeights[g] = profile.GenreWeights.GetValueOrDefault(g) + wlWeight;
        if (!string.IsNullOrWhiteSpace(language)) profile.LanguageWeights[language] = profile.LanguageWeights.GetValueOrDefault(language) + wlWeight;
        foreach (var d in directors) profile.DirectorWeights[d] = profile.DirectorWeights.GetValueOrDefault(d) + (wlWeight * 2);
        foreach (var a in actors.Take(5)) profile.ActorWeights[a] = profile.ActorWeights.GetValueOrDefault(a) + wlWeight;
    }

    public IEnumerable<TaskTriggerInfo> GetDefaultTriggers()
    {
        return Array.Empty<TaskTriggerInfo>(); // Manual run only
    }
}
