using System;
using System.Net.Http;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.UpcomingMovies.Services;

/// <summary>
/// Handles auto-watchlist fulfilment when a requested movie becomes available.
/// Called explicitly from the JellyseerrWebhookController when Jellyseerr sends
/// a "media.available" notification, or directly via the /watchlist/fulfill endpoint.
/// This avoids using ILibraryManager.ItemAdded (which has fragile type dependencies
/// across Jellyfin versions) in favour of a webhook-driven approach.
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
    /// Called when a movie with the given TMDB ID has just become available in Jellyfin.
    /// Looks up all users who requested it and adds it to their Jellyfin watchlists.
    /// </summary>
    /// <param name="tmdbId">TMDB ID of the movie that is now available.</param>
    /// <param name="jellyfinItemId">Jellyfin item ID (GUID as string without dashes).</param>
    public Task FulfillAsync(int tmdbId, string jellyfinItemId)
        => FulfillWatchlistAsync(jellyfinItemId, tmdbId);

    private async Task FulfillWatchlistAsync(string jellyfinItemId, int tmdbId)
    {
        var pendingUserIds = _pendingService.GetPendingUserIds(tmdbId);
        if (pendingUserIds.Count == 0) return;

        _logger.LogInformation(
            "[UpcomingMovies] Fulfilling watchlist for tmdbId={TmdbId} jellyfinId={JellyfinId} ({Count} user(s))",
            tmdbId, jellyfinItemId, pendingUserIds.Count);

        var apiKey   = Plugin.Instance?.Configuration?.JellyfinLocalApiKey;
        var localUrl = Plugin.Instance?.Configuration?.JellyfinLocalUrl?.TrimEnd('/');

        if (string.IsNullOrWhiteSpace(apiKey) || string.IsNullOrWhiteSpace(localUrl))
        {
            _logger.LogWarning(
                "[UpcomingMovies] Cannot auto-watchlist: JellyfinLocalUrl or JellyfinLocalApiKey not configured.");
            return;
        }

        var client = _httpClientFactory.CreateClient();

        foreach (var userId in pendingUserIds)
        {
            try
            {
                // Correct Jellyfin API: POST /Users/{uid}/Items/{id}/Rating?Likes=true
                // This sets UserData.Likes = true which is what KefinTweaks reads as watchlist.
                // Auth header must be 'Authorization: MediaBrowser Token="<key>"'
                var url = $"{localUrl}/Users/{userId}/Items/{jellyfinItemId}/Rating?Likes=true";
                using var req = new HttpRequestMessage(HttpMethod.Post, url);
                req.Headers.TryAddWithoutValidation("Authorization", $"MediaBrowser Token=\"{apiKey}\"");

                var res = await client.SendAsync(req).ConfigureAwait(false);
                if (res.IsSuccessStatusCode)
                {
                    _logger.LogInformation(
                        "[UpcomingMovies] Auto-watchlisted item {ItemId} for user {UserId}",
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

        _pendingService.RemovePending(tmdbId);
    }
}
