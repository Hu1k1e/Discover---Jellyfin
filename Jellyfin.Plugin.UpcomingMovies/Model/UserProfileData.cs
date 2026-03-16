using System;
using System.Collections.Generic;

namespace Jellyfin.Plugin.UpcomingMovies.Model;

/// <summary>
/// One entry in the user's watch history — lightweight, capped at 200 rows.
/// </summary>
public class WatchEntry
{
    public int TmdbId { get; set; }
    public DateTime WatchedAt { get; set; }
    public List<int> GenreIds { get; set; } = new();
    public string Language { get; set; } = "en";
}

/// <summary>
/// Per-user taste profile stored as a JSON file in the plugin data folder.
/// All weights use exponential-decay so recent watches matter more than old ones.
/// </summary>
public class UserProfileData
{
    public string UserId { get; set; } = string.Empty;
    public DateTime LastUpdated { get; set; }

    /// <summary>TMDB genre ID → accumulated weighted watch count.</summary>
    public Dictionary<int, double> GenreWeights { get; set; } = new();

    /// <summary>TMDB person ID → accumulated weighted watch count (directors get 2× bonus).</summary>
    public Dictionary<int, double> DirectorWeights { get; set; } = new();

    /// <summary>TMDB person ID → accumulated weighted watch count.</summary>
    public Dictionary<int, double> ActorWeights { get; set; } = new();

    /// <summary>ISO 639-1 language code → accumulated weight. Drives language affinity.</summary>
    public Dictionary<string, double> LanguageWeights { get; set; } = new();

    /// <summary>All TMDB movie IDs the user has watched — used to exclude from recommendations.</summary>
    public List<int> WatchedTmdbIds { get; set; } = new();

    /// <summary>TMDB movie IDs the user has added to their watchlist (UserData.Likes=true).
    /// Used as recommendation seeds at 0.5× watch signal strength.</summary>
    public List<int> WatchlistTmdbIds { get; set; } = new();

    /// <summary>TMDB movie IDs the user has explicitly dismissed via the X button.
    /// These are permanently excluded from future recommendations.</summary>
    public List<int> DismissedTmdbIds { get; set; } = new();

    /// <summary>Genre ID → negative penalty weight accumulated from dismissed movies.
    /// Applied as a negative multiplier in the scoring engine to suppress similar genres.</summary>
    public Dictionary<int, double> DismissedGenrePenalties { get; set; } = new();

    /// <summary>Last 200 watch events, newest first. Used to rebuild weights on demand.</summary>
    public List<WatchEntry> RecentWatches { get; set; } = new();

    public int TotalWatched { get; set; }
}
