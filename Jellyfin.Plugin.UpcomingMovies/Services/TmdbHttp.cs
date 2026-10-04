using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading;
using System.Threading.Tasks;

namespace Jellyfin.Plugin.UpcomingMovies.Services;

/// <summary>
/// Creates HttpClients for TMDB calls that share ONE in-memory response cache, a global
/// concurrency limit and a request timeout. Callers keep using plain <c>client.GetAsync(url)</c>;
/// identical TMDB requests (same URL, ignoring the api_key) are answered from memory until their TTL expires.
/// Non-TMDB hosts and non-GET requests pass straight through.
/// </summary>
public static class TmdbHttp
{
    private static readonly SocketsHttpHandler SharedInner = new()
    {
        PooledConnectionLifetime = TimeSpan.FromMinutes(5),
        AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate | DecompressionMethods.Brotli
    };

    private static readonly TmdbCachingHandler Handler = new(SharedInner);

    /// <summary>Creates a lightweight client that uses the shared cache + connection pool.</summary>
    public static HttpClient CreateClient()
        => new HttpClient(Handler, disposeHandler: false) { Timeout = TimeSpan.FromSeconds(20) };
}

internal sealed class TmdbCachingHandler : DelegatingHandler
{
    private sealed record Entry(byte[] Body, string? ContentType, DateTime Expires, DateTime Stored);

    private const long MaxBytes = 48L * 1024 * 1024;     // total cached body bytes
    private const int MaxEntries = 3000;
    private const int MaxEntryBytes = 2 * 1024 * 1024;   // never cache very large bodies
    private const int MaxConcurrentRequests = 6;         // simultaneous real requests to TMDB

    private readonly ConcurrentDictionary<string, Entry> _cache = new();
    private readonly SemaphoreSlim _gate = new(MaxConcurrentRequests, MaxConcurrentRequests);
    private long _bytes;
    private int _stores;

    public TmdbCachingHandler(HttpMessageHandler inner)
        : base(inner)
    {
    }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var uri = request.RequestUri;
        if (request.Method != HttpMethod.Get
            || uri is null
            || !uri.Host.Equals("api.themoviedb.org", StringComparison.OrdinalIgnoreCase))
        {
            return await base.SendAsync(request, cancellationToken).ConfigureAwait(false);
        }

        var key = BuildKey(uri);
        if (_cache.TryGetValue(key, out var hit) && hit.Expires > DateTime.UtcNow)
        {
            return FromEntry(hit, request);
        }

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            // Another caller may have filled the cache while we were waiting for a slot.
            if (_cache.TryGetValue(key, out hit) && hit.Expires > DateTime.UtcNow)
            {
                return FromEntry(hit, request);
            }

            var response = await base.SendAsync(request, cancellationToken).ConfigureAwait(false);
            if (response.IsSuccessStatusCode)
            {
                // Reading the bytes buffers the content, so the caller can still read it from 'response'.
                var body = await response.Content.ReadAsByteArrayAsync(cancellationToken).ConfigureAwait(false);
                if (body.Length <= MaxEntryBytes)
                {
                    var now = DateTime.UtcNow;
                    Store(key, new Entry(body, response.Content.Headers.ContentType?.ToString(), now + TtlFor(uri), now));
                }
            }

            return response;
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>Cache key = path + sorted query without the api_key, so the key never contains the secret.</summary>
    private static string BuildKey(Uri uri)
    {
        var parts = uri.Query.TrimStart('?')
            .Split('&', StringSplitOptions.RemoveEmptyEntries)
            .Where(p => !p.StartsWith("api_key=", StringComparison.OrdinalIgnoreCase))
            .OrderBy(p => p, StringComparer.Ordinal);
        return uri.AbsolutePath + "?" + string.Join("&", parts);
    }

    /// <summary>How long a TMDB answer may be reused.</summary>
    private static TimeSpan TtlFor(Uri uri)
    {
        var path = uri.AbsolutePath;

        if (path.Contains("/discover/movie", StringComparison.Ordinal))
        {
            // Upcoming-release lists change slowly; personalised discover pools are refreshed hourly.
            return uri.Query.Contains("primary_release_date", StringComparison.Ordinal)
                ? TimeSpan.FromHours(6)
                : TimeSpan.FromMinutes(60);
        }

        if (path.Contains("/recommendations", StringComparison.Ordinal)
            || path.Contains("/similar", StringComparison.Ordinal)
            || path.Contains("/trending", StringComparison.Ordinal))
        {
            return TimeSpan.FromMinutes(60);
        }

        if (path.Contains("/movie/", StringComparison.Ordinal))
        {
            // Movie details, credits and keywords almost never change.
            return TimeSpan.FromHours(24);
        }

        return TimeSpan.FromMinutes(30);
    }

    private static HttpResponseMessage FromEntry(Entry entry, HttpRequestMessage request)
    {
        var response = new HttpResponseMessage(HttpStatusCode.OK)
        {
            RequestMessage = request,
            Content = new ByteArrayContent(entry.Body)
        };

        if (!string.IsNullOrEmpty(entry.ContentType) && MediaTypeHeaderValue.TryParse(entry.ContentType, out var mediaType))
        {
            response.Content.Headers.ContentType = mediaType;
        }

        return response;
    }

    private void Store(string key, Entry entry)
    {
        if (_cache.TryRemove(key, out var old))
        {
            Interlocked.Add(ref _bytes, -old.Body.Length);
        }

        _cache[key] = entry;
        Interlocked.Add(ref _bytes, entry.Body.Length);

        if (Interlocked.Increment(ref _stores) % 25 == 0
            || Interlocked.Read(ref _bytes) > MaxBytes
            || _cache.Count > MaxEntries)
        {
            Sweep();
        }
    }

    /// <summary>Drops expired entries, then the oldest ones until the cache is back under its limits.</summary>
    private void Sweep()
    {
        var now = DateTime.UtcNow;
        foreach (var kv in _cache)
        {
            if (kv.Value.Expires <= now && _cache.TryRemove(kv.Key, out var gone))
            {
                Interlocked.Add(ref _bytes, -gone.Body.Length);
            }
        }

        while ((Interlocked.Read(ref _bytes) > MaxBytes || _cache.Count > MaxEntries) && !_cache.IsEmpty)
        {
            var oldestKey = _cache.OrderBy(kv => kv.Value.Stored).Select(kv => kv.Key).FirstOrDefault();
            if (oldestKey is null)
            {
                break;
            }

            if (_cache.TryRemove(oldestKey, out var removed))
            {
                Interlocked.Add(ref _bytes, -removed.Body.Length);
            }
        }
    }
}
