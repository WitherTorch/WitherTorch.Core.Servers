using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json;

namespace WitherTorch.Core.Servers.Utils
{
    [JsonObject(MemberSerialization = MemberSerialization.OptIn)]
    internal struct VersionStruct
    {
        [JsonProperty("version", Required = Required.Always)]
        public string Version { get; set; }

        [JsonProperty("stable", Required = Required.DisallowNull)]
        public bool Stable { get; set; }
    }
}
