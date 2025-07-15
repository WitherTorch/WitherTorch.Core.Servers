using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;

using WitherTorch.Core.Servers.Utils;
using WitherTorch.Core.Software;
using WitherTorch.Core.Utils;

namespace WitherTorch.Core.Servers
{
    partial class Paper
    {
        private static readonly SoftwareContextPrivate _software = new SoftwareContextPrivate();

        /// <summary>
        /// 取得與 <see cref="Paper"/> 相關聯的軟體上下文
        /// </summary>
        public static ISoftwareContext Software => _software;

        private sealed class SoftwareContextPrivate : SoftwareContextBase<Paper>
        {
            private const string ManifestListURL = "https://fill.papermc.io/v3/projects/paper";

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
                CachedDownloadClient client = CachedDownloadClient.Instance;
                HttpClient innerClient = client.InnerHttpClient;
                innerClient.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent", UserAgentForPaperV3Api);
                string? manifestString = client.DownloadString(ManifestListURL);
                innerClient.DefaultRequestHeaders.Remove("User-Agent");

                if (manifestString is null || manifestString.Length <= 0)
                    return null;
                JsonObject? jsonObject = JsonNode.Parse(manifestString) as JsonObject;
                if (jsonObject is null || !jsonObject.TryGetPropertyValue("versions", out JsonNode? node))
                    return null;
                jsonObject = node as JsonObject;
                if (jsonObject is null)
                    return null;
                List<string> versionList = new List<string>();
                foreach (KeyValuePair<string, JsonNode?> versionGroupNodePair in jsonObject)
                {
                    if (versionGroupNodePair.Value is not JsonArray versionGroupArrayNode)
                        continue;
                    foreach (JsonNode? versionNode in versionGroupArrayNode)
                    {
                        if (versionNode is not JsonValue versionValueNode || versionValueNode.GetValueKind() != JsonValueKind.String)
                            continue;
                        versionList.Add(versionValueNode.GetValue<string>());
                    }
                }
                if (versionList.Count <= 0)
                    return Array.Empty<string>();
                string[] result = versionList.ToArray();
                Array.Sort(result, MojangAPI.VersionComparer.Instance);
                Array.Reverse(result);
                return result;
            }

        }
    }
}