using System;
using System.Collections.Generic;
using System.Globalization;
using Jellyfin.Plugin.UpcomingMovies.Configuration;
using Jellyfin.Plugin.UpcomingMovies.Services;
using MediaBrowser.Common.Configuration;
using MediaBrowser.Common.Plugins;
using MediaBrowser.Controller.Session;
using MediaBrowser.Model.Plugins;
using MediaBrowser.Model.Serialization;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.UpcomingMovies;

/// <summary>
/// The main plugin class for Upcoming Movies &amp; Recommendations.
/// Owns the UserProfileService singleton and wires up the UserDataSaved event listener
/// directly in the constructor -- no IPluginServiceRegistrator or IHostedService needed.
/// </summary>
public class Plugin : BasePlugin<PluginConfiguration>, IHasWebPages, IDisposable
{
    private readonly UserDataSavedConsumer? _consumer;
    private readonly PlaybackStoppedConsumer? _playbackConsumer;

    /// <summary>
    /// Initializes a new instance of the <see cref="Plugin"/> class.
    /// Jellyfin injects extra constructor parameters automatically via DI.
    /// </summary>
    public Plugin(
        IApplicationPaths applicationPaths,
        IXmlSerializer xmlSerializer,
        MediaBrowser.Controller.Library.IUserDataManager userDataManager,
        ISessionManager sessionManager,
        System.Net.Http.IHttpClientFactory httpClientFactory,
        ILoggerFactory loggerFactory)
        : base(applicationPaths, xmlSerializer)
    {
        Instance = this;

        // Create the profile service -- stored static so TmdbController can access without DI
        ProfileService = new UserProfileService(
            applicationPaths,
            loggerFactory.CreateLogger<UserProfileService>());

        // Watchlist pending service -- stores (userId, tmdbId) pairs waiting for library arrival
        WatchlistService = new WatchlistPendingService(
            applicationPaths,
            loggerFactory.CreateLogger<WatchlistPendingService>());

        // Watchlist fulfillment consumer -- called by webhook when movie becomes available
        WatchlistConsumer = new LibraryItemAddedConsumer(
            WatchlistService,
            httpClientFactory,
            loggerFactory.CreateLogger<LibraryItemAddedConsumer>());

        // UserDataSaved — used ONLY for watchlist (Likes=true) signals.
        // Watch signals (Played=true) are handled by PlaybackStopped below so we can
        // capture the real PositionTicks before Jellyfin resets them to 0.
        _consumer = new UserDataSavedConsumer(
            ProfileService,
            httpClientFactory,
            loggerFactory.CreateLogger<UserDataSavedConsumer>());
        userDataManager.UserDataSaved += _consumer.OnUserDataSaved;

        // PlaybackStopped — the authoritative source for watch percentage.
        // Fires every time playback ends (mid-way through OR at the end) and provides
        // the live PositionTicks that Jellyfin hasn't reset yet.
        // PlaybackProgress — fires every ~5 seconds with the current position.
        // Used as a fallback when some clients send positionTicks=0 in their stop request.
        _playbackConsumer = new PlaybackStoppedConsumer(
            ProfileService,
            httpClientFactory,
            loggerFactory.CreateLogger<PlaybackStoppedConsumer>());
        sessionManager.PlaybackProgress += _playbackConsumer.OnPlaybackProgress;
        sessionManager.PlaybackStopped  += _playbackConsumer.OnPlaybackStopped;
    }

    /// <inheritdoc />
    public override string Name => "Upcoming Movies & Recommendations";

    /// <inheritdoc />
    public override Guid Id => Guid.Parse("a3f7c2e1-9b4d-4a1c-8e5f-d6b2a0c3f9e8");

    /// <summary>Gets the current plugin instance (set in constructor).</summary>
    public static Plugin? Instance { get; private set; }

    /// <summary>Gets the user profile service.</summary>
    public static UserProfileService? ProfileService { get; private set; }

    /// <summary>Gets the watchlist pending service.</summary>
    public static WatchlistPendingService? WatchlistService { get; private set; }

    /// <summary>Gets the watchlist fulfillment consumer.</summary>
    public static LibraryItemAddedConsumer? WatchlistConsumer { get; private set; }

    /// <inheritdoc />
    public IEnumerable<PluginPageInfo> GetPages()
    {
        var ns = GetType().Namespace;
        return
        [
            new PluginPageInfo
            {
                Name = Name,
                EmbeddedResourcePath = string.Format(CultureInfo.InvariantCulture, "{0}.Configuration.configPage.html", ns)
            },
            new PluginPageInfo
            {
                Name = "discoverPage",
                EmbeddedResourcePath = string.Format(CultureInfo.InvariantCulture, "{0}.Web.discoverPage.html", ns),
                EnableInMainMenu = false
            },
            new PluginPageInfo
            {
                Name = "discoverPage.js",
                EmbeddedResourcePath = string.Format(CultureInfo.InvariantCulture, "{0}.Web.discoverPage.js", ns),
                EnableInMainMenu = false
            }
        ];
    }

    /// <inheritdoc />
    public void Dispose()
    {
        GC.SuppressFinalize(this);
    }
}
