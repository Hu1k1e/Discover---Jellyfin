using System;
using System.Collections.Generic;

namespace Jellyfin.Plugin.UpcomingMovies.Model;

/// <summary>
/// Represents a single pending watchlist entry — a user who requested a movie
/// that has not yet been added to the Jellyfin library.
/// Stored server-side only; never exposed to the browser.
/// </summary>
public class WatchlistPendingEntry
{
    /// <summary>Gets or sets the Jellyfin user GUID (format "N" — no dashes).</summary>
    public string UserId { get; set; } = string.Empty;

    /// <summary>Gets or sets the TMDB movie ID that was requested.</summary>
    public int TmdbId { get; set; }

    /// <summary>Gets or sets the UTC timestamp of when the user requested this movie.</summary>
    public DateTime RequestedAt { get; set; } = DateTime.UtcNow;
}

/// <summary>Wrapper for JSON serialisation of the pending list.</summary>
public class WatchlistPendingData
{
    /// <summary>Gets or sets all pending watchlist entries.</summary>
    public List<WatchlistPendingEntry> Entries { get; set; } = new();
}
