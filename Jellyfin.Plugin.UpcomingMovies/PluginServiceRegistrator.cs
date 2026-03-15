using Jellyfin.Plugin.UpcomingMovies.Services;
using MediaBrowser.Common.Plugins;
using MediaBrowser.Controller;
using MediaBrowser.Controller.Plugins;
using Microsoft.Extensions.DependencyInjection;

namespace Jellyfin.Plugin.UpcomingMovies;

/// <summary>
/// Registers plugin services into Jellyfin's DI container.
/// Jellyfin auto-discovers classes implementing IPluginServiceRegistrator.
/// </summary>
public class PluginServiceRegistrator : IPluginServiceRegistrator
{
    public void RegisterServices(IServiceCollection serviceCollection, IServerApplicationHost applicationHost)
    {
        // Singleton profile service — one instance for the server lifetime, manages all user profiles
        serviceCollection.AddSingleton<UserProfileService>();

        // IServerEntryPoint — Jellyfin auto-starts these and wires them into the DI lifetime
        serviceCollection.AddScoped<IServerEntryPoint, UserDataSavedConsumer>();
    }
}
