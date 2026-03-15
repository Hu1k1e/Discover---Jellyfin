using System;
using System.Collections.Generic;
using System.Globalization;
using Jellyfin.Plugin.UpcomingMovies.Configuration;
using Jellyfin.Plugin.UpcomingMovies.Services;
using MediaBrowser.Common.Configuration;
using MediaBrowser.Common.Plugins;
using MediaBrowser.Controller.Library;
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
    private readonly LibraryItemAddedConsumer? _libraryConsumer;
    private readonly ILibraryManager? _libraryManager;

    /// <summary>
    /// Initializes a new instance of the <see cref="Plugin"/> class.
    /// Jellyfin injects extra constructor parameters automatically via DI.
    /// </summary>
    public Plugin(
        IApplicationPaths applicationPaths,
        IXmlSerializer xmlSerializer,
        MediaBrowser.Controller.Library.IUserDataManager userDataManager,
        ILibraryManager libraryManager,
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

        // Create and wire the UserData event consumer (profile updates on watch)
        _consumer = new UserDataSavedConsumer(
            ProfileService,
            httpClientFactory,
            loggerFactory.CreateLogger<UserDataSavedConsumer>());
        userDataManager.UserDataSaved += _consumer.OnUserDataSaved;

        // Create and wire the Library event consumer (auto-watchlist when movie arrives)
        _libraryManager = libraryManager;
        _libraryConsumer = new LibraryItemAddedConsumer(
            WatchlistService,
            httpClientFactory,
            loggerFactory.CreateLogger<LibraryItemAddedConsumer>());
        libraryManager.ItemAdded += _libraryConsumer.OnItemAdded;
    }

    /// <inheritdoc />
    public override string Name => "Upcoming Movies & Recommendations";

    /// <inheritdoc />
    public override Guid Id => Guid.Parse("a3f7c2e1-9b4d-4a1c-8e5f-d6b2a0c3f9e8");

    /// <summary>
    /// Gets the current plugin instance (set in constructor).
    /// </summary>
    public static Plugin? Instance { get; private set; }

    /// <summary>
    /// Gets the user profile service. Null only before plugin is loaded.
    /// TmdbController accesses this statically to avoid Jellyfin.Common DI issues.
    /// </summary>
    public static UserProfileService? ProfileService { get; private set; }

    /// <summary>
    /// Gets the watchlist pending service. Null only before plugin is loaded.
    /// JellyseerrController accesses this statically when recording a new request.
    /// </summary>
    public static WatchlistPendingService? WatchlistService { get; private set; }

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
        // Unwire events to prevent memory leaks on plugin reload
        if (_libraryManager != null && _libraryConsumer != null)
            _libraryManager.ItemAdded -= _libraryConsumer.OnItemAdded;
        GC.SuppressFinalize(this);
    }

