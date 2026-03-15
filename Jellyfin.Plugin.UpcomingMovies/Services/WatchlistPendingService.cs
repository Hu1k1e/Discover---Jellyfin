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
/// Manages the list of (userId, tmdbId) pairs that are waiting for a movie
/// to arrive in the Jellyfin library so it can be auto-added to their watchlist.
///
/// Data is persisted in upcomingmovies_watchlist_pending.json in the plugin data folder.
/// Operations are synchronised with a simple lock (file is tiny — rarely >100 entries).
/// </summary>
public class WatchlistPendingService
{
    private readonly string _filePath;
    private readonly ILogger<WatchlistPendingService> _logger;
    private readonly object _lock = new();

    public WatchlistPendingService(IApplicationPaths applicationPaths, ILogger<WatchlistPendingService> logger)
    {
        _logger = logger;
        // Reuse same data directory as user profiles
        var dir = Path.Combine(applicationPaths.DataPath, "upcomingmovies_profiles");
        Directory.CreateDirectory(dir);
        _filePath = Path.Combine(dir, "watchlist_pending.json");
    }

    // ── Public API ──────────────────────────────────────────────────────────────

    /// <summary>
    /// Records that userId wants this TMDB movie added to their watchlist
    /// once it becomes available in Jellyfin.
    /// </summary>
    public void AddPending(string userId, int tmdbId)
    {
        if (string.IsNullOrWhiteSpace(userId) || tmdbId <= 0) return;
        lock (_lock)
        {
            var data = Load();
            // Avoid duplicates
            if (!data.Entries.Any(e => e.UserId == userId && e.TmdbId == tmdbId))
            {
                data.Entries.Add(new WatchlistPendingEntry
                {
                    UserId      = userId,
                    TmdbId      = tmdbId,
                    RequestedAt = DateTime.UtcNow
                });
                Save(data);
                _logger.LogInformation(
                    "[UpcomingMovies] WatchlistPending: added userId={UserId} tmdbId={TmdbId}",
                    userId, tmdbId);
            }
        }
    }

    /// <summary>
    /// Returns all user IDs who are waiting for this TMDB movie.
    /// </summary>
    public List<string> GetPendingUserIds(int tmdbId)
    {
        lock (_lock)
        {
            return Load().Entries
                .Where(e => e.TmdbId == tmdbId)
                .Select(e => e.UserId)
                .Distinct()
                .ToList();
        }
    }

    /// <summary>
    /// Removes all pending entries for this TMDB movie (call after fulfilling them).
    /// </summary>
    public void RemovePending(int tmdbId)
    {
        lock (_lock)
        {
            var data = Load();
            var before = data.Entries.Count;
            data.Entries.RemoveAll(e => e.TmdbId == tmdbId);
            if (data.Entries.Count != before)
            {
                Save(data);
                _logger.LogInformation(
                    "[UpcomingMovies] WatchlistPending: fulfilled and removed {Count} entries for tmdbId={TmdbId}",
                    before - data.Entries.Count, tmdbId);
            }
        }
    }

    // ── Internal helpers ─────────────────────────────────────────────────────────

    private WatchlistPendingData Load()
    {
        if (!File.Exists(_filePath))
            return new WatchlistPendingData();
        try
        {
            var json = File.ReadAllText(_filePath);
            return JsonSerializer.Deserialize<WatchlistPendingData>(json)
                   ?? new WatchlistPendingData();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[UpcomingMovies] Could not load watchlist pending store");
            return new WatchlistPendingData();
        }
    }

    private void Save(WatchlistPendingData data)
    {
        try
        {
            File.WriteAllText(_filePath, JsonSerializer.Serialize(data));
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[UpcomingMovies] Could not save watchlist pending store");
        }
    }
}
