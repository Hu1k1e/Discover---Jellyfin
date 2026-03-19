using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using MediaBrowser.Controller.Entities.Movies;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Session;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.UpcomingMovies.Services;

/// <summary>
/// Handles Jellyfin <see cref="ISessionManager.PlaybackStopped"/> events to record
/// the real watch percentage for every movie the user partially or fully watches.
///
/// Why this is needed instead of UserDataSaved:
///   - UserDataSaved with Played=true fires exactly once per movie lifetime —
///     never for partial-watch sessions that haven't crossed Jellyfin's "played" threshold.
///   - When a movie IS fully completed, Jellyfin resets PlaybackPositionTicks to 0
///     in UserData (so you can resume from the start), making it impossible to read
///     the real percentage after the fact.
///   - PlaybackStopped fires every time the user exits/stops the player and provides
///     PlaybackPositionTicks — the real, live position — before Jellyfin resets it.
/// </summary>
public class PlaybackStoppedConsumer
{
    private readonly UserProfileService _profileService;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<PlaybackStoppedConsumer> _logger;

    private const string TmdbBaseUrl = "https://api.themoviedb.org/3";

    // Minimum fraction of runtime to bother recording — below 3% is likely an accidental press
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
    /// Subscribed to ISessionManager.PlaybackStopped.
    /// Captures the real position ticks and updates the user profile accordingly.
    /// </summary>
    public void OnPlaybackStopped(object? sender, PlaybackStopEventArgs e)
    {
        // We only care about movies
        if (e.Item is not Movie movie) return;

        // Need a TMDB ID to be useful
        if (movie.ProviderIds == null ||
            !movie.ProviderIds.TryGetValue("Tmdb", out var tmdbIdStr) ||
            !int.TryParse(tmdbIdStr, out var tmdbId) ||
            tmdbId <= 0)
            return;

        // Session carries the UserId
        var session = e.Session;
        if (session == null) return;
        var userId = session.UserId.ToString("N");

        // Calculate watch percentage from the LIVE PlaybackPositionTicks in the event args.
        // These are NOT yet reset by Jellyfin, unlike UserData.PlaybackPositionTicks.
        double watchPercentage;
        long positionTicks = e.PlaybackPositionTicks ?? 0;

        if (movie.RunTimeTicks is > 0 && positionTicks > 0)
        {
            watchPercentage = (double)positionTicks / movie.RunTimeTicks.Value;
            watchPercentage = Math.Clamp(watchPercentage, 0.0, 1.0);
        }
        else if (e.PlayedToCompletion)
        {
            // Fallback: movie played to the end but position wasn't reported
            watchPercentage = 1.0;
        }
        else
        {
            // No position data at all — skip
            _logger.LogDebug(
                "[UpcomingMovies] PlaybackStopped: No position data for TMDB {TmdbId}, skipping.",
                tmdbId);
            return;
        }

        // Skip trivially short plays — likely accidental (< 3%)
        if (watchPercentage < MinimumRecordThreshold)
        {
            _logger.LogDebug(
                "[UpcomingMovies] PlaybackStopped: Ignoring trivially short play ({Pct:P1}) for TMDB {TmdbId}",
                watchPercentage, tmdbId);
            return;
        }

        var genreIds = (movie.Genres ?? Array.Empty<string>())
            .Select(g => UserProfileService.JellyfinNameToTmdbGenreId.TryGetValue(g, out var id) ? id : 0)
            .Where(id => id > 0)
            .ToList();

        _logger.LogInformation(
            "[UpcomingMovies] PlaybackStopped: user={UserId} tmdb={TmdbId} pos={Pos} runtime={Runtime} pct={Pct:P1} completed={Completed}",
            userId, tmdbId, positionTicks, movie.RunTimeTicks, watchPercentage, e.PlayedToCompletion);

        Task.Run(() => FetchDetailsAndUpdateAsync(userId, tmdbId, genreIds, watchPercentage));
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

                // 3 — keywords (micro-genres)
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
