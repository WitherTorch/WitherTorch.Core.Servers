using System.Threading;
using System;
using WitherTorch.Core.Software;
using WitherTorch.Core.Servers.Utils;
using System.Collections.Generic;
using System.Text.Json.Nodes;
using System.Text.Json;

namespace WitherTorch.Core.Servers
{
    partial class Paper
    {
        private static readonly SoftwareContextPrivate _software = new SoftwareContextPrivate();

        public static ISoftwareContext Software => _software;

        private sealed class SoftwareContextPrivate : SoftwareContextBase<Paper>
        {
            private const string ManifestListURL = "https://api.papermc.io/v2/projects/paper";

            private readonly Lazy<string[]> _versionsLazy = new Lazy<string[]>(LoadVersionList, LazyThreadSafetyMode.ExecutionAndPublication);

            public SoftwareContextPrivate() : base(SoftwareId) { }

            public override string[] GetSoftwareVersions() => _versionsLazy.Value;

            public override Paper? CreateServerInstance(string serverDirectory) => new Paper(serverDirectory);

            private static string[] LoadVersionList()
            {
                try
                {
                    return LoadVersionListInternal() ?? Array.Empty<string>();
                }
                catch (Exception)
                {
                }
                return Array.Empty<string>();
            }

            private static string[]? LoadVersionListInternal()
            {
                string? manifestString = CachedDownloadClient.Instance.DownloadString(ManifestListURL);
                if (manifestString is null || manifestString.Length <= 0)
                    return null;
                JsonObject? manifestJSON = JsonNode.Parse(manifestString) as JsonObject;
                if (manifestJSON is null || manifestJSON["versions"] is not JsonArray versions)
                    return null;
                int length = versions.Count;
                if (length <= 0)
                    return null;
                List<string> list = new List<string>(length);
                for (int i = 0; i < length; i++)
                {
                    if (versions[i] is JsonValue versionToken && versionToken.GetValueKind() == JsonValueKind.String)
                    {
                        list.Add(versionToken.GetValue<string>());
                    }
                }
                string[] result = list.ToArray();
                Array.Reverse(result);
                return result;
            }

        }
    }
}