using System.Text.Json.Serialization;

namespace Jellyfin.Plugin.UpcomingMovies.Model;

/// <summary>
/// Payload type passed by the File Transformation Plugin to registered callback methods.
/// Must exactly mirror the structure used by jellyfin-plugin-file-transformation.
/// </summary>
public class PatchRequestPayload
{
    /// <summary>
    /// The raw file contents that should be transformed.
    /// </summary>
    [JsonPropertyName("contents")]
    public string? Contents { get; set; }
}
