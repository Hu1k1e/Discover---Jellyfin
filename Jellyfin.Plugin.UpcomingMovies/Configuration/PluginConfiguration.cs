using MediaBrowser.Model.Plugins;

namespace Jellyfin.Plugin.UpcomingMovies.Configuration;

/// <summary>
/// Controls where the Discover nav link is injected in the Jellyfin web client.
/// </summary>
public enum NavPlacement
{
    /// <summary>Inject the Discover link into the left sidebar drawer.</summary>
    Sidebar,

    /// <summary>Inject the Discover link into the top header tab bar.</summary>
    Header
}

/// <summary>
/// Plugin configuration for Upcoming Movies &amp; Recommendations.
/// </summary>
public class PluginConfiguration : BasePluginConfiguration
{
    /// <summary>
    /// Initializes a new instance of the <see cref="PluginConfiguration"/> class.
    /// Sets safe default values for all settings.
    /// </summary>
    public PluginConfiguration()
    {
        TmdbApiKey = string.Empty;
        OmdbApiKey = string.Empty;
        JellyseerrUrl = string.Empty;
        JellyseerrApiKey = string.Empty;
        StreamBaseUrl = "https://stream.hulksmash.ca/movie/";
        NavPlacement = NavPlacement.Sidebar;
        ShowUpcomingSection = true;
        ShowRecommendationsSection = true;
        ShowWatchlistSection = true;
    }

    /// <summary>
    /// Gets or sets the TMDB API key used for fetching upcoming and recommended movies.
    /// </summary>
    public string TmdbApiKey { get; set; }

    /// <summary>
    /// Gets or sets the OMDB API key used for fetching IMDB and Rotten Tomatoes ratings.
    /// Free key available at https://www.omdbapi.com/apikey.aspx (1000 req/day).
    /// Leave empty to skip IMDB/RT ratings in the movie detail modal.
    /// </summary>
    public string OmdbApiKey { get; set; }

    /// <summary>
    /// Gets or sets the base URL of the user's Jellyseerr instance (e.g. https://jellyseerr.example.com).
    /// </summary>
    public string JellyseerrUrl { get; set; }

    /// <summary>
    /// Gets or sets the Jellyseerr API key for submitting media requests.
    /// </summary>
    public string JellyseerrApiKey { get; set; }

    /// <summary>
    /// Gets or sets the base URL for the Stream Directly action.
    /// The TMDB ID is appended to this URL (e.g. https://stream.hulksmash.ca/movie/12345).
    /// </summary>
    public string StreamBaseUrl { get; set; }

    /// <summary>
    /// Gets or sets where the Discover nav link is injected: Sidebar (default) or Header.
    /// </summary>
    public NavPlacement NavPlacement { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether the Upcoming Movies section is visible.
    /// </summary>
    public bool ShowUpcomingSection { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether the Recommended Movies section is visible.
    /// </summary>
    public bool ShowRecommendationsSection { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether the KefinTweaks Watchlist section is visible.
    /// </summary>
    public bool ShowWatchlistSection { get; set; }
}
