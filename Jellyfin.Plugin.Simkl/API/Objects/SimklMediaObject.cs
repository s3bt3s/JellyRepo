using System.Text.Json.Serialization;

namespace Jellyfin.Plugin.SimklWatched.API.Objects
{
    /// <summary>
    /// Simkl media object.
    /// </summary>
    public class SimklMediaObject
    {
        /// <summary>
        /// Gets or sets ids.
        /// </summary>
        [JsonPropertyName("ids")]
        public SimklIds? Ids { get; set; }
    }
}
