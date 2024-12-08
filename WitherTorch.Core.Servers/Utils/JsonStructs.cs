
using System.Text.Json.Serialization;

namespace WitherTorch.Core.Servers.Utils
{
    internal struct VersionStruct
    {
        [JsonPropertyName("version")]
        public string Version { get; set; }

        [JsonPropertyName("stable")]
        public bool Stable { get; set; }
    }
}
