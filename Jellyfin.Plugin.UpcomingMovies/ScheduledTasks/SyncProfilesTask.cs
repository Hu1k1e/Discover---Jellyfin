using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.UpcomingMovies.Model;
using MediaBrowser.Controller.Dto;
using MediaBrowser.Controller.Entities;
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
        IHttpClientFactory httpClientFactory,
        ILogger<SyncProfilesTask> logger)
    {
        _userManager = userManager;
        _libraryManager = libraryManager;
        _userDataManager = userDataManager;
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
            IncludeItemTypes = new[] { Jellyfin.Data.Enums.BaseItemKind.Movie }
        }).OfType<Movie>().ToList();

        _logger.LogInformation("[UpcomingMovies] Found {UserCount} users and {MovieCount} total movies.", users.Count, allMovies.Count);

        int userIndex = 0;
        foreach (var user in users)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var userIdStr = user.Id.ToString("N");
            _logger.LogInformation("[UpcomingMovies] Processing user {UserName} ({UserId})", user.Username, userIdStr);

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
                bool played = userData?.Played == true;
                bool liked = userData?.Likes == true;

                if (!played && !liked) continue;

                if (movie.ProviderIds == null || !movie.ProviderIds.TryGetValue("Tmdb", out var tmdbIdStr) || !int.TryParse(tmdbIdStr, out var tmdbId) || tmdbId <= 0)
                    continue;

                double watchPercentage = 1.0;
                if (played && movie.RunTimeTicks > 0 && userData?.PlaybackPositionTicks > 0)
                {
                    watchPercentage = (double)userData.PlaybackPositionTicks / movie.RunTimeTicks.Value;
                }

                userMovies.Add(new HistoricalEvent
                {
                    Movie = movie,
                    TmdbId = tmdbId,
                    Played = played,
                    Liked = liked,
                    // Note: UserData doesn't always have a strict 'date watched' that's easy to pull without IUserDataRepository.
                    // We'll use LastPlayedDate if available
                    Date = userData?.LastPlayedDate ?? DateTime.UtcNow.AddYears(-1),
                    WatchPercentage = watchPercentage
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
                    ApplyHistoricalWatch(profile, evt.TmdbId, genreIds, details.Language, details.Directors, details.Actors, details.Keywords, evt.Date, evt.WatchPercentage);
                }
                else if (evt.Liked)
                {
                    ApplyHistoricalWatchlist(profile, evt.TmdbId, genreIds, details.Language, details.Directors, details.Actors, details.Keywords);
                }

                movieIndex++;
                if (movieIndex % 25 == 0)
                {
                    _logger.LogInformation("[UpcomingMovies] User {UserName}: Processed {MIndex}/{Total} movies...", user.Username, movieIndex, userMovies.Count);
                }
            }

            Plugin.ProfileService?.SaveProfile(profile);
            
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
        public double WatchPercentage { get; set; } = 1.0;
    }

    private class MovieDetailsCache
    {
        public string Language { get; set; } = "en";
        public List<int> Directors { get; set; } = new();
        public List<int> Actors { get; set; } = new();
        public List<int> Keywords { get; set; } = new();
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

                // 3. Keywords
                var kwUrl = $"{TmdbBaseUrl}/movie/{tmdbId}/keywords?api_key={apiKey}";
                var kwRes = await client.GetAsync(kwUrl, cancellationToken).ConfigureAwait(false);
                if (kwRes.IsSuccessStatusCode)
                {
                    using var doc = JsonDocument.Parse(await kwRes.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false));
                    if (doc.RootElement.TryGetProperty("keywords", out var kwArr))
                    {
                        foreach (var kw in kwArr.EnumerateArray())
                        {
                            if (kw.TryGetProperty("id", out var idEl) && idEl.TryGetInt32(out var kwId))
                            {
                                details.Keywords.Add(kwId);
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
    private void ApplyHistoricalWatch(UserProfileData profile, int tmdbId, List<int> genreIds, string language, List<int> directors, List<int> actors, List<int> keywords, DateTime watchDate, double watchPercentage)
    {
        const double DecayFactor = 0.92;
        const double BaseWatchWeight = 5.0;

        double multiplier = watchPercentage >= 0.9 ? 1.2
                          : watchPercentage >= 0.5 ? 1.0
                          : watchPercentage >= 0.2 ? 0.3
                          :                         -0.5;
        double weightChange = BaseWatchWeight * multiplier;

        if (!profile.WatchedTmdbIds.Contains(tmdbId))
            profile.WatchedTmdbIds.Add(tmdbId);

        // Decay
        foreach (var k in profile.GenreWeights.Keys.ToList()) profile.GenreWeights[k] *= DecayFactor;
        foreach (var k in profile.KeywordWeights.Keys.ToList()) profile.KeywordWeights[k] *= DecayFactor;
        foreach (var k in profile.DirectorWeights.Keys.ToList()) profile.DirectorWeights[k] *= DecayFactor;
        foreach (var k in profile.ActorWeights.Keys.ToList()) profile.ActorWeights[k] *= DecayFactor;
        foreach (var k in profile.LanguageWeights.Keys.ToList()) profile.LanguageWeights[k] *= DecayFactor;

        // Add
        foreach (var g in genreIds) profile.GenreWeights[g] = Math.Max(0, profile.GenreWeights.GetValueOrDefault(g) + weightChange);
        foreach (var k in keywords) profile.KeywordWeights[k] = Math.Max(0, profile.KeywordWeights.GetValueOrDefault(k) + weightChange);
        if (!string.IsNullOrWhiteSpace(language)) profile.LanguageWeights[language] = Math.Max(0, profile.LanguageWeights.GetValueOrDefault(language) + weightChange);
        foreach (var d in directors) profile.DirectorWeights[d] = Math.Max(0, profile.DirectorWeights.GetValueOrDefault(d) + (weightChange * 2));
        foreach (var a in actors.Take(5)) profile.ActorWeights[a] = Math.Max(0, profile.ActorWeights.GetValueOrDefault(a) + weightChange);

        profile.RecentWatches.Insert(0, new WatchEntry
        {
            TmdbId = tmdbId,
            WatchedAt = watchDate,
            GenreIds = genreIds,
            KeywordIds = keywords,
            Language = language ?? "en",
            WatchPercentage = watchPercentage
        });

        if (profile.RecentWatches.Count > 200)
            profile.RecentWatches.RemoveRange(200, profile.RecentWatches.Count - 200);

        profile.TotalWatched++;
    }

    private void ApplyHistoricalWatchlist(UserProfileData profile, int tmdbId, List<int> genreIds, string language, List<int> directors, List<int> actors, List<int> keywords)
    {
        profile.WatchlistTmdbIds.Remove(tmdbId);
        profile.WatchlistTmdbIds.Insert(0, tmdbId);
        if (profile.WatchlistTmdbIds.Count > 100)
            profile.WatchlistTmdbIds.RemoveRange(100, profile.WatchlistTmdbIds.Count - 100);

        const double wlWeight = 5.0 * 0.5;

        foreach (var g in genreIds) profile.GenreWeights[g] = profile.GenreWeights.GetValueOrDefault(g) + wlWeight;
        foreach (var k in keywords) profile.KeywordWeights[k] = profile.KeywordWeights.GetValueOrDefault(k) + wlWeight;
        if (!string.IsNullOrWhiteSpace(language)) profile.LanguageWeights[language] = profile.LanguageWeights.GetValueOrDefault(language) + wlWeight;
        foreach (var d in directors) profile.DirectorWeights[d] = profile.DirectorWeights.GetValueOrDefault(d) + (wlWeight * 2);
        foreach (var a in actors.Take(5)) profile.ActorWeights[a] = profile.ActorWeights.GetValueOrDefault(a) + wlWeight;
    }

    public IEnumerable<TaskTriggerInfo> GetDefaultTriggers()
    {
        return Array.Empty<TaskTriggerInfo>(); // Manual run only
    }
}
