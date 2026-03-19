using System;
using System.Collections.Concurrent;
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
/// Handles playback tracking for watch-percentage calculation.
///
/// Why two events?
/// ─────────────────────────────────────────────────────────────────────────────
/// PlaybackStopped alone is unreliable for partial-watch detection because many
/// Jellyfin clients (Infuse, iOS client, some TV apps) send PositionTicks = 0
/// in their stop request.  When that happens the only flag available is
/// PlayedToCompletion — which is TRUE whenever Jellyfin considers the session
/// "played" (default threshold ~90%), but also TRUE for movies the user already
/// had marked as played in a previous session.  Using it naively causes any movie
/// with PositionTicks = 0 to be recorded as 100%.
///
/// Fix: subscribe to PlaybackProgress as well.  Progress events fire every ~5 s
/// during playback and always carry the real PositionTicks.  We keep a per-session
/// "high-water mark" in a ConcurrentDictionary.  When PlaybackStopped fires:
///   1. Use stop-event's PositionTicks if > 0.
///   2. Otherwise fall back to the cached high-water mark.
///   3. Only fall back to 1.0 when PlayedToCompletion=true AND we have no cached
///      position (e.g., very short clip opened, played to end before first progress).
/// ─────────────────────────────────────────────────────────────────────────────
/// </summary>
public class PlaybackStoppedConsumer
{
    private readonly UserProfileService _profileService;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<PlaybackStoppedConsumer> _logger;

    private const string TmdbBaseUrl = "https://api.themoviedb.org/3";

    /// <summary>
    /// Minimum fraction of runtime to bother recording.
    /// Below 3% is almost certainly an accidental open.
    /// </summary>
    private const double MinimumRecordThreshold = 0.03;

    /// <summary>
    /// Per-session high-water mark for PositionTicks.
    /// Key   = SessionInfo.Id (string)
    /// Value = max PositionTicks seen in any Progress event for this session
    /// </summary>
    private readonly ConcurrentDictionary<string, long> _sessionPositions = new();

    public PlaybackStoppedConsumer(
        UserProfileService profileService,
        IHttpClientFactory httpClientFactory,
        ILogger<PlaybackStoppedConsumer> logger)
    {
        _profileService = profileService;
        _httpClientFactory = httpClientFactory;
        _logger = logger;
    }

    // ──────────────────────────────────────────────────────────
    // Progress handler — fires every ~5 seconds during playback
    // ──────────────────────────────────────────────────────────

    /// <summary>Subscribed to ISessionManager.PlaybackProgress.</summary>
    public void OnPlaybackProgress(object? sender, PlaybackProgressEventArgs e)
    {
        if (e.Item is not Movie) return;
        if (e.Session == null) return;
        if (e.PlaybackPositionTicks is null or 0) return;

        // Store the highest position seen so far for this session
        _sessionPositions.AddOrUpdate(
            e.Session.Id,
            e.PlaybackPositionTicks.Value,
            (_, existing) => Math.Max(existing, e.PlaybackPositionTicks.Value));
    }

    // ──────────────────────────────────────────────────────────
    // Stop handler — fires once when the player exits
    // ──────────────────────────────────────────────────────────

    /// <summary>Subscribed to ISessionManager.PlaybackStopped.</summary>
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

        // ── Determine best available position ───────────────────────────────
        // Priority:
        //   1. Stop event's PositionTicks (most accurate, often 0 on client bug)
        //   2. Cached high-water mark from PlaybackProgress events
        //   3. PlayedToCompletion=true with no position → clamp to 0.95 (not 1.0)
        //      because we know the user reached Jellyfin's played-threshold but
        //      can't confirm whether they watched every last second.

        long positionTicks = e.PlaybackPositionTicks ?? 0;

        // Remove the cache entry and get the high-water mark
        _sessionPositions.TryRemove(session.Id, out long cachedMax);

        // Prefer the larger of the two (stop event vs. last progress ping)
        if (cachedMax > positionTicks)
        {
            _logger.LogDebug(
                "[UpcomingMovies] PlaybackStopped: stop-event pos={StopPos} < cached high-water={CachedMax}; using cached.",
                positionTicks, cachedMax);
            positionTicks = cachedMax;
        }

        double watchPercentage;

        if (movie.RunTimeTicks is > 0 && positionTicks > 0)
        {
            watchPercentage = (double)positionTicks / movie.RunTimeTicks.Value;
            watchPercentage = Math.Clamp(watchPercentage, 0.0, 1.0);
        }
        else if (e.PlayedToCompletion && positionTicks == 0)
        {
            // Client sent no position AND we have no progress cache.
            // PlayedToCompletion means Jellyfin's played threshold was crossed (~90%),
            // but we cannot confirm 100%.  Use 0.95 as a safe estimate — the 1.2x
            // "Loved" multiplier still applies (threshold is >0.9) but we avoid
            // falsely claiming 100% completion.
            watchPercentage = 0.95;
            _logger.LogDebug(
                "[UpcomingMovies] PlaybackStopped: TMDB {TmdbId} — no position data but PlayedToCompletion=true; using 95% estimate.",
                tmdbId);
        }
        else
        {
            // No position data at all and not marked as completed — skip
            _logger.LogDebug(
                "[UpcomingMovies] PlaybackStopped: No usable position data for TMDB {TmdbId}, skipping.",
                tmdbId);
            return;
        }

        // Skip trivially short plays (< 3%) — likely accidental
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
            "[UpcomingMovies] PlaybackStopped: user={UserId} tmdb={TmdbId} pos={Pos} runtime={Runtime} pct={Pct:P1} completed={Completed} cached={Cached}",
            userId, tmdbId, positionTicks, movie.RunTimeTicks, watchPercentage, e.PlayedToCompletion, cachedMax);

        Task.Run(() => FetchDetailsAndUpdateAsync(userId, tmdbId, genreIds, watchPercentage));
    }

    // ──────────────────────────────────────────────────────────
    // TMDB enrichment + profile update
    // ──────────────────────────────────────────────────────────

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
