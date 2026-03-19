using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using MediaBrowser.Controller.Entities.Movies;
using MediaBrowser.Controller.Session;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.UpcomingMovies.Services;

/// <summary>
/// Handles Jellyfin <see cref="ISessionManager.PlaybackStopped"/> events to record
/// the real watch percentage for every movie the user partially or fully watches.
///
/// Why this is needed:
///   - <c>UserDataSaved</c> with <c>Played = true</c> fires exactly once per movie lifetime —
///     never for partial-watch sessions that haven't reached the "played" threshold.
///   - When a movie IS fully completed, Jellyfin resets <c>PlaybackPositionTicks</c> to 0
///     in UserData, so the percentage can't be recovered from there afterwards.
///   - <c>PlaybackStopped</c> fires every time playback stops (user exits, seeks to end, etc.)
///     and provides <c>e.PlaybackStopInfo.PositionTicks</c> — the real, live position.
/// </summary>
public class PlaybackStoppedConsumer
{
    private readonly UserProfileService _profileService;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<PlaybackStoppedConsumer> _logger;

    private const string TmdbBaseUrl = "https://api.themoviedb.org/3";

    // Minimum fraction of runtime to bother recording — below 3% is likely accidental play
    private const double MinimumRecordThreshold = 0.03;

    public PlaybackStoppedConsumer(
        UserProfileService profileService,
        IHttpClientFactory httpClientFactory,
        ILogger<PlaybackStoppedConsumer> logger)
    {
        _profileService = profileService;
        _httpClientFactory = httpClientFactory;
        _logger = logger;
    }

    /// <summary>
    /// Subscribed to <see cref="ISessionManager.PlaybackStopped"/>.
    /// Captures the real position ticks and updates the user profile accordingly.
    /// </summary>
    public void OnPlaybackStopped(object? sender, PlaybackStopEventArgs e)
    {
        // We only care about movies
        if (e.Item is not Movie movie) return;

        // Need a TMDB ID
        if (movie.ProviderIds == null ||
            !movie.ProviderIds.TryGetValue("Tmdb", out var tmdbIdStr) ||
            !int.TryParse(tmdbIdStr, out var tmdbId) ||
            tmdbId <= 0)
            return;

        // Need a valid user
        var userId = e.Session?.UserId;
        if (userId == null || userId == Guid.Empty) return;
        var userIdStr = userId.Value.ToString("N");

        // Calculate watch percentage from the LIVE position ticks provided by PlaybackStopped
        double watchPercentage = 1.0;
        long positionTicks = e.PlaybackStopInfo?.PositionTicks ?? 0;

        if (movie.RunTimeTicks is > 0 && positionTicks > 0)
        {
            watchPercentage = (double)positionTicks / movie.RunTimeTicks.Value;
            watchPercentage = Math.Clamp(watchPercentage, 0.0, 1.0);
        }
        else if (e.PlaybackStopInfo?.PlayedToCompletion == true)
        {
            // Fallback: if Jellyfin says it was played to completion, treat as 100%
            watchPercentage = 1.0;
        }

        // Skip accidental presses / very short plays
        if (watchPercentage < MinimumRecordThreshold)
        {
            _logger.LogDebug(
                "[UpcomingMovies] PlaybackStopped: Ignoring trivially short play (< {Threshold:P0}) for TMDB {TmdbId}",
                MinimumRecordThreshold, tmdbId);
            return;
        }

        var genreIds = (movie.Genres ?? Array.Empty<string>())
            .Select(g => UserProfileService.JellyfinNameToTmdbGenreId.TryGetValue(g, out var id) ? id : 0)
            .Where(id => id > 0)
            .ToList();

        _logger.LogInformation(
            "[UpcomingMovies] PlaybackStopped: user={UserId} tmdb={TmdbId} position={Pos} runtime={Runtime} pct={Pct:P1}",
            userIdStr, tmdbId, positionTicks, movie.RunTimeTicks, watchPercentage);

        Task.Run(() => FetchDetailsAndUpdateAsync(userIdStr, tmdbId, genreIds, watchPercentage));
    }

    private async Task FetchDetailsAndUpdateAsync(
        string userId, int tmdbId, List<int> genreIds, double watchPercentage)
    {
        var directors = new List<int>();
        var actors    = new List<int>();
        var keywords  = new List<int>();
        var language  = "en";

        try
        {
            var apiKey = Plugin.Instance?.Configuration?.TmdbApiKey;
            if (!string.IsNullOrWhiteSpace(apiKey))
            {
                var client = _httpClientFactory.CreateClient();

                // 1 — original_language
                try
                {
                    var url = $"{TmdbBaseUrl}/movie/{tmdbId}?api_key={apiKey}&language=en-US";
                    var res = await client.GetAsync(url).ConfigureAwait(false);
                    if (res.IsSuccessStatusCode)
                    {
                        using var doc = JsonDocument.Parse(
                            await res.Content.ReadAsStringAsync().ConfigureAwait(false));
                        if (doc.RootElement.TryGetProperty("original_language", out var langEl))
                        {
                            var fetched = langEl.GetString();
                            if (!string.IsNullOrWhiteSpace(fetched)) language = fetched;
                        }
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "[UpcomingMovies] PlaybackStopped: TMDB details failed for {Id}", tmdbId);
                }

                // 2 — directors + actors
                try
                {
                    var url = $"{TmdbBaseUrl}/movie/{tmdbId}/credits?api_key={apiKey}";
                    var res = await client.GetAsync(url).ConfigureAwait(false);
                    if (res.IsSuccessStatusCode)
                    {
                        using var doc = JsonDocument.Parse(
                            await res.Content.ReadAsStringAsync().ConfigureAwait(false));

                        if (doc.RootElement.TryGetProperty("crew", out var crew))
                        {
                            foreach (var m in crew.EnumerateArray())
                            {
                                if (m.TryGetProperty("job", out var job) &&
                                    job.GetString()?.Equals("Director", StringComparison.OrdinalIgnoreCase) == true &&
                                    m.TryGetProperty("id", out var idEl) &&
                                    idEl.TryGetInt32(out var personId))
                                    directors.Add(personId);
                            }
                        }

                        if (doc.RootElement.TryGetProperty("cast", out var cast))
                        {
                            foreach (var m in cast.EnumerateArray().Take(5))
                            {
                                if (m.TryGetProperty("id", out var idEl) &&
                                    idEl.TryGetInt32(out var personId))
                                    actors.Add(personId);
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "[UpcomingMovies] PlaybackStopped: TMDB credits failed for {Id}", tmdbId);
                }

                // 3 — keywords
                try
                {
                    var url = $"{TmdbBaseUrl}/movie/{tmdbId}/keywords?api_key={apiKey}";
                    var res = await client.GetAsync(url).ConfigureAwait(false);
                    if (res.IsSuccessStatusCode)
                    {
                        using var doc = JsonDocument.Parse(
                            await res.Content.ReadAsStringAsync().ConfigureAwait(false));
                        if (doc.RootElement.TryGetProperty("keywords", out var kwArr))
                        {
                            foreach (var kw in kwArr.EnumerateArray())
                            {
                                if (kw.TryGetProperty("id", out var idEl) &&
                                    idEl.TryGetInt32(out var kwId))
                                    keywords.Add(kwId);
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "[UpcomingMovies] PlaybackStopped: TMDB keywords failed for {Id}", tmdbId);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[UpcomingMovies] PlaybackStopped: outer fetch failed for {Id}", tmdbId);
        }

        _profileService.UpdateWithWatch(userId, tmdbId, genreIds, language, directors, actors, keywords, watchPercentage);
    }
}
