using System.Reflection;
using System.Runtime.Loader;
using Jellyfin.Plugin.UpcomingMovies.Helpers;
using MediaBrowser.Model.Tasks;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json.Linq;

namespace Jellyfin.Plugin.UpcomingMovies.Services;

/// <summary>
/// Runs at server startup to register our index.html transformation
/// with the File Transformation Plugin (same pattern as jellyfin-plugin-custom-tabs).
/// This injects a &lt;script&gt; tag that loads discoverPage.js automatically on every page load.
/// No JS Injector or manual user configuration is required.
/// </summary>
public class StartupService : IScheduledTask
{
    public string Name => "Upcoming Movies Startup";
    public string Key => "Jellyfin.Plugin.UpcomingMovies.Startup";
    public string Description => "Registers the File Transformation for the Upcoming Movies plugin to auto-inject discoverPage.js into index.html.";
    public string Category => "Startup Services";

    private readonly ILogger<StartupService> _logger;

    public StartupService(ILogger<StartupService> logger)
    {
        _logger = logger;
    }

    public Task ExecuteAsync(IProgress<double> progress, CancellationToken cancellationToken)
    {
        _logger.LogInformation("[UpcomingMovies] Startup — registering File Transformation for index.html.");

        var payload = new JObject
        {
            // Stable unique ID for this transformation registration
            ["id"] = "b2e7a1c4-3d9f-4b8e-a5c2-f1d0e6b3c8a7",
            ["fileNamePattern"] = "index.html",
            ["callbackAssembly"] = GetType().Assembly.FullName,
            ["callbackClass"] = typeof(TransformationPatches).FullName,
            ["callbackMethod"] = nameof(TransformationPatches.InjectScriptTag)
        };

        // Locate the File Transformation Plugin assembly at runtime via reflection,
        // exactly as jellyfin-plugin-custom-tabs does it.
        Assembly? ftAssembly = AssemblyLoadContext.All
            .SelectMany(x => x.Assemblies)
            .FirstOrDefault(x => x.FullName?.Contains(".FileTransformation") ?? false);

        if (ftAssembly == null)
        {
            _logger.LogWarning("[UpcomingMovies] File Transformation Plugin not found — script will not be auto-injected. " +
                               "Install jellyfin-plugin-file-transformation for zero-config injection.");
            return Task.CompletedTask;
        }

        Type? pluginInterface = ftAssembly.GetType("Jellyfin.Plugin.FileTransformation.PluginInterface");
        if (pluginInterface == null)
        {
            _logger.LogWarning("[UpcomingMovies] PluginInterface type not found in File Transformation assembly.");
            return Task.CompletedTask;
        }

        pluginInterface.GetMethod("RegisterTransformation")?.Invoke(null, new object?[] { payload });
        _logger.LogInformation("[UpcomingMovies] File Transformation registered successfully.");

        return Task.CompletedTask;
    }

    public IEnumerable<TaskTriggerInfo> GetDefaultTriggers()
    {
        yield return new TaskTriggerInfo
        {
            Type = TaskTriggerInfo.TriggerStartup
        };
    }
}
