using System;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using Jellyfin.Plugin.UpcomingMovies.Services;
using MediaBrowser.Controller.Entities.Movies;
using MediaBrowser.Controller.Library;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.UpcomingMovies.Services;

/// <summary>
/// Listens for ILibraryManager.ItemAdded events.
/// When a Movie arrives in the Jellyfin library, checks whether any user requested
/// it via Jellyseerr through our plugin. If yes, adds it to that user's
/// Jellyfin watchlist automatically by calling the local Jellyfin REST API.
/// </summary>
public class LibraryItemAddedConsumer
{
    private readonly WatchlistPendingService _pendingService;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<LibraryItemAddedConsumer> _logger;

    public LibraryItemAddedConsumer(
        WatchlistPendingService pendingService,
        IHttpClientFactory httpClientFactory,
        ILogger<LibraryItemAddedConsumer> logger)
    {
        _pendingService    = pendingService;
        _httpClientFactory = httpClientFactory;
        _logger            = logger;
    }

    /// <summary>
    /// Wired to ILibraryManager.ItemAdded in Plugin.cs constructor.
    /// Fires whenever any item is added to the library; we filter to Movies only.
    /// </summary>
    public void OnItemAdded(object? sender, ItemChangeEventArgs e)
    {
        if (e.Item is not Movie movie) return;

        // Needs a TMDB ID to match against pending entries
        if (!movie.ProviderIds.TryGetValue("Tmdb", out var tmdbIdStr) ||
            !int.TryParse(tmdbIdStr, out var tmdbId) || tmdbId <= 0)
            return;

        var pendingUserIds = _pendingService.GetPendingUserIds(tmdbId);
        if (pendingUserIds.Count == 0) return;

        _logger.LogInformation(
            "[UpcomingMovies] Movie tmdbId={TmdbId} ('{Title}') added to library — auto-watchlisting for {Count} user(s)",
            tmdbId, movie.Name, pendingUserIds.Count);

        // Fire-and-forget — don't block the library add event
        Task.Run(() => FulfillWatchlistAsync(movie.Id.ToString("N"), tmdbId, pendingUserIds));
    }

    private async Task FulfillWatchlistAsync(string jellyfinItemId, int tmdbId, System.Collections.Generic.List<string> userIds)
    {
        var apiKey = Plugin.Instance?.Configuration?.JellyfinLocalApiKey;
        var localUrl = Plugin.Instance?.Configuration?.JellyfinLocalUrl?.TrimEnd('/');

        if (string.IsNullOrWhiteSpace(apiKey) || string.IsNullOrWhiteSpace(localUrl))
        {
            _logger.LogWarning(
                "[UpcomingMovies] Cannot auto-watchlist: JellyfinLocalUrl or JellyfinLocalApiKey not configured in plugin settings.");
            return;
        }

        var client = _httpClientFactory.CreateClient();

        foreach (var userId in userIds)
        {
            try
            {
                // POST /UserWatchlistItems/{itemId}?userId={userId}
                // Jellyfin 10.11+ — sets UserData.IsWatchlisted = true
                var url = $"{localUrl}/UserWatchlistItems/{jellyfinItemId}?userId={userId}";
                using var req = new HttpRequestMessage(HttpMethod.Post, url);
                req.Headers.Add("X-Emby-Token", apiKey);

                var res = await client.SendAsync(req).ConfigureAwait(false);
                if (res.IsSuccessStatusCode)
                {
                    _logger.LogInformation(
                        "[UpcomingMovies] Auto-watchlisted Jellyfin item {ItemId} for user {UserId}",
                        jellyfinItemId, userId);
                }
                else
                {
                    _logger.LogWarning(
                        "[UpcomingMovies] Auto-watchlist failed for user {UserId}: HTTP {StatusCode}",
                        userId, res.StatusCode);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "[UpcomingMovies] Auto-watchlist exception for user {UserId}", userId);
            }
        }

        // Clean up fulfilled entries
        _pendingService.RemovePending(tmdbId);
    }
}
