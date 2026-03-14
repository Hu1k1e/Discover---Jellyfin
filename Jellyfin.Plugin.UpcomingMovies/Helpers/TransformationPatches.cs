using System.Reflection;
using System.Text.RegularExpressions;
using Jellyfin.Plugin.UpcomingMovies.Model;

namespace Jellyfin.Plugin.UpcomingMovies.Helpers;

/// <summary>
/// Callback methods invoked by the File Transformation Plugin when it intercepts
/// matching files. Injects the discoverPage.js bootstrap script into index.html.
/// </summary>
public static class TransformationPatches
{
    /// <summary>
    /// Called by the File Transformation Plugin when index.html is served.
    /// Reads our embedded bootstrap JS and injects it as an inline deferred script before &lt;/body&gt;.
    /// Technique is identical to jellyfin-plugin-custom-tabs/TransformationPatches.cs.
    /// </summary>
    public static string InjectScriptTag(PatchRequestPayload payload)
    {
        // Read the embedded inject.js bootstrap
        using Stream stream = Assembly.GetExecutingAssembly()
            .GetManifestResourceStream("Jellyfin.Plugin.UpcomingMovies.Web.inject.js")!;
        using TextReader reader = new StreamReader(stream);
        string scriptContent = reader.ReadToEnd();

        // Splice inline script before </body>
        return Regex.Replace(
            payload.Contents!,
            @"(</body>)",
            $"<script defer>{scriptContent}</script>$1",
            RegexOptions.IgnoreCase);
    }
}
