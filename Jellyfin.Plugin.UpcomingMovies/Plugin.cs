using System;
using System.Collections.Generic;
using System.Globalization;
using Jellyfin.Plugin.UpcomingMovies.Configuration;
using MediaBrowser.Common.Configuration;
using MediaBrowser.Common.Plugins;
using MediaBrowser.Model.Plugins;
using MediaBrowser.Model.Serialization;

namespace Jellyfin.Plugin.UpcomingMovies;

/// <summary>
/// The main plugin class for Upcoming Movies &amp; Recommendations.
/// </summary>
public class Plugin : BasePlugin<PluginConfiguration>, IHasWebPages
{
    /// <summary>
    /// Initializes a new instance of the <see cref="Plugin"/> class.
    /// </summary>
    /// <param name="applicationPaths">Instance of the <see cref="IApplicationPaths"/> interface.</param>
    /// <param name="xmlSerializer">Instance of the <see cref="IXmlSerializer"/> interface.</param>
    public Plugin(IApplicationPaths applicationPaths, IXmlSerializer xmlSerializer)
        : base(applicationPaths, xmlSerializer)
    {
        Instance = this;
    }

    /// <inheritdoc />
    public override string Name => "Upcoming Movies & Recommendations";

    /// <inheritdoc />
    public override Guid Id => Guid.Parse("a3f7c2e1-9b4d-4a1c-8e5f-d6b2a0c3f9e8");

    /// <summary>
    /// Gets the current plugin instance.
    /// </summary>
    public static Plugin? Instance { get; private set; }

    /// <inheritdoc />
    public IEnumerable<PluginPageInfo> GetPages()
    {
        var ns = GetType().Namespace;
        return
        [
            // Settings page shown in Jellyfin Dashboard → Plugins
            new PluginPageInfo
            {
                Name = Name,
                EmbeddedResourcePath = string.Format(CultureInfo.InvariantCulture, "{0}.Configuration.configPage.html", ns)
            },
            // The Discover page itself (no menu entry in dashboard — injected into sidebar by JS)
            new PluginPageInfo
            {
                Name = "discoverPage",
                EmbeddedResourcePath = string.Format(CultureInfo.InvariantCulture, "{0}.Web.discoverPage.html", ns),
                EnableInMainMenu = false
            },
            // Bootstrap JS that injects the sidebar link and page router
            new PluginPageInfo
            {
                Name = "discoverPage.js",
                EmbeddedResourcePath = string.Format(CultureInfo.InvariantCulture, "{0}.Web.discoverPage.js", ns),
                EnableInMainMenu = false
            }
        ];
    }
}
