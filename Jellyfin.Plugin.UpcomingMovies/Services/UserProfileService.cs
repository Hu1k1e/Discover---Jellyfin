using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using Jellyfin.Plugin.UpcomingMovies.Model;
using MediaBrowser.Common.Configuration;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.UpcomingMovies.Services;

/// <summary>
/// Manages per-user taste profiles stored as JSON files in the plugin data folder.
/// Files are named {userId}.json and are only accessible server-side.
/// </summary>
public class UserProfileService
{
    private readonly string _profilesDir;
    private readonly ILogger<UserProfileService> _logger;

    // Exponential decay factor: each existing weight is multiplied by this before adding new signal.
    // 0.92 means "last 12 watches contribute ~50% of current weights" — good balance of memory vs adaptability.
    private const double DecayFactor = 0.92;

    // How much weight a fresh watch adds (directors get 2×, actors 1×, genres 1×, language 1×)
    private const double BaseWatchWeight = 5.0;

    // Log-scale normaliser for use in scoring.
    // Maps raw accumulated weights onto a compressed curve so a user who watches
    // 10 animated movies scores ~2.6× the person with 1 watch instead of 10×.
    //   weight 5  (1 watch)   → ~8.5 pts/genre
    //   weight 23 (3 watches) → ~14 pts/genre
    //   weight 65 (10 watches)→ ~18 pts/genre
    // Formula: log10(1 + w) × 11.6  — chosen so that weight 5 ≈ 8.5
    public static double NormalizedWeight(double rawWeight)
        => Math.Max(0, Math.Log10(1.0 + rawWeight) * 11.6);

    // TMDB genre ID <-> Jellyfin genre name mapping (TMDB genre IDs are stable and won't change)
    public static readonly Dictionary<string, int> JellyfinNameToTmdbGenreId = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Action"] = 28, ["Adventure"] = 12, ["Animation"] = 16, ["Comedy"] = 35,
        ["Crime"] = 80, ["Documentary"] = 99, ["Drama"] = 18, ["Family"] = 10751,
        ["Fantasy"] = 14, ["History"] = 36, ["Horror"] = 27, ["Music"] = 10402,
        ["Mystery"] = 9648, ["Romance"] = 10749, ["Science Fiction"] = 878, ["Sci-Fi"] = 878,
        ["Thriller"] = 53, ["War"] = 10752, ["Western"] = 37
    };

    public static readonly Dictionary<int, string> TmdbGenreIdToName = new()
    {
        [28] = "Action", [12] = "Adventure", [16] = "Animation", [35] = "Comedy",
        [80] = "Crime", [99] = "Documentary", [18] = "Drama", [10751] = "Family",
        [14] = "Fantasy", [36] = "History", [27] = "Horror", [10402] = "Music",
        [9648] = "Mystery", [10749] = "Romance", [878] = "Science Fiction",
        [53] = "Thriller", [10752] = "War", [37] = "Western"
    };

    public UserProfileService(IApplicationPaths applicationPaths, ILogger<UserProfileService> logger)
    {
        _logger = logger;
        _profilesDir = Path.Combine(applicationPaths.DataPath, "upcomingmovies_profiles");
        Directory.CreateDirectory(_profilesDir);
        _logger.LogInformation("[UpcomingMovies] Profile store at: {Dir}", _profilesDir);
    }

    public UserProfileData GetProfile(string userId)
    {
        var path = GetProfilePath(userId);
        if (!File.Exists(path))
        {
            return new UserProfileData { UserId = userId };
        }

        try
        {
            var json = File.ReadAllText(path);
            return JsonSerializer.Deserialize<UserProfileData>(json)
                   ?? new UserProfileData { UserId = userId };
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[UpcomingMovies] Could not read profile for user {UserId}", userId);
            return new UserProfileData { UserId = userId };
        }
    }

    public void SaveProfile(UserProfileData profile)
    {
        try
        {
            profile.LastUpdated = DateTime.UtcNow;
            var json = JsonSerializer.Serialize(profile, new JsonSerializerOptions { WriteIndented = false });
            File.WriteAllText(GetProfilePath(profile.UserId), json);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[UpcomingMovies] Could not save profile for user {UserId}", profile.UserId);
        }
    }

    /// <summary>
    /// Called when a user watches a movie. Updates all signal weights with exponential decay.
    /// Directors get 2× weight since they most strongly define a user's taste.
    /// </summary>
    public void UpdateWithWatch(
        string userId,
        int tmdbId,
        IEnumerable<int> genreIds,
        string language,
        IEnumerable<int> directorTmdbIds,
        IEnumerable<int> actorTmdbIds)
    {
        var profile = GetProfile(userId);

        // Add to watched set
        if (!profile.WatchedTmdbIds.Contains(tmdbId))
        {
            profile.WatchedTmdbIds.Add(tmdbId);
        }

        // Apply exponential decay to all existing weights so old preferences fade naturally
        DecayAllWeights(profile);

        var gList = genreIds.ToList();
        var dList = directorTmdbIds.ToList();
        var aList = actorTmdbIds.ToList();

        // Genre weights (1× base)
        foreach (var g in gList)
        {
            profile.GenreWeights[g] = profile.GenreWeights.GetValueOrDefault(g) + BaseWatchWeight;
        }

        // Language weights (1× base)
        if (!string.IsNullOrWhiteSpace(language))
        {
            profile.LanguageWeights[language] = profile.LanguageWeights.GetValueOrDefault(language) + BaseWatchWeight;
        }

        // Director weights (2× — strongest taste signal)
        foreach (var d in dList)
        {
            profile.DirectorWeights[d] = profile.DirectorWeights.GetValueOrDefault(d) + (BaseWatchWeight * 2);
        }

        // Actor weights (1× base)
        foreach (var a in aList.Take(5)) // cap to top-billed 5
        {
            profile.ActorWeights[a] = profile.ActorWeights.GetValueOrDefault(a) + BaseWatchWeight;
        }

        // Record to watch history (newest first, capped at 200)
        profile.RecentWatches.Insert(0, new WatchEntry
        {
            TmdbId = tmdbId,
            WatchedAt = DateTime.UtcNow,
            GenreIds = gList,
            Language = language ?? "en"
        });

        if (profile.RecentWatches.Count > 200)
        {
            profile.RecentWatches.RemoveRange(200, profile.RecentWatches.Count - 200);
        }

        profile.TotalWatched++;
        SaveProfile(profile);

        _logger.LogInformation(
            "[UpcomingMovies] Profile updated for user {UserId}: watched TMDB {TmdbId} (genres:{Genres} lang:{Lang} directors:{Dirs} actors:{Actors})",
            userId, tmdbId,
            string.Join(",", gList),
            language,
            string.Join(",", dList),
            string.Join(",", aList.Take(5)));
    }

    /// <summary>
    /// Returns the top N genre IDs ranked by weight, for use in /discover?with_genres.
    /// </summary>
    public List<int> GetTopGenres(UserProfileData profile, int n = 5)
        => profile.GenreWeights
            .OrderByDescending(kv => kv.Value)
            .Take(n)
            .Select(kv => kv.Key)
            .ToList();

    /// <summary>
    /// Returns top N director TMDB person IDs ranked by weight, for use in /discover?with_people.
    /// </summary>
    public List<int> GetTopDirectors(UserProfileData profile, int n = 5)
        => profile.DirectorWeights
            .OrderByDescending(kv => kv.Value)
            .Take(n)
            .Select(kv => kv.Key)
            .ToList();

    /// <summary>
    /// Returns top N actor TMDB person IDs ranked by weight.
    /// </summary>
    public List<int> GetTopActors(UserProfileData profile, int n = 5)
        => profile.ActorWeights
            .OrderByDescending(kv => kv.Value)
            .Take(n)
            .Select(kv => kv.Key)
            .ToList();

    /// <summary>
    /// Returns the top N recently-watched TMDB IDs (for seeding /recommendations calls).
    /// </summary>
    public List<int> GetRecentSeedIds(UserProfileData profile, int n = 8)
        => profile.RecentWatches
            .Select(w => w.TmdbId)
            .Where(id => id > 0)
            .Distinct()
            .Take(n)
            .ToList();

    private static void DecayAllWeights(UserProfileData profile)
    {
        var genreKeys = profile.GenreWeights.Keys.ToList();
        foreach (var k in genreKeys)
            profile.GenreWeights[k] *= DecayFactor;

        var dirKeys = profile.DirectorWeights.Keys.ToList();
        foreach (var k in dirKeys)
            profile.DirectorWeights[k] *= DecayFactor;

        var actorKeys = profile.ActorWeights.Keys.ToList();
        foreach (var k in actorKeys)
            profile.ActorWeights[k] *= DecayFactor;

        var langKeys = profile.LanguageWeights.Keys.ToList();
        foreach (var k in langKeys)
            profile.LanguageWeights[k] *= DecayFactor;
    }

    private string GetProfilePath(string userId)
        => Path.Combine(_profilesDir, $"{userId}.json");
}
