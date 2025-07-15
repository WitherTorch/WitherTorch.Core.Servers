using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using System.Xml;

using WitherTorch.Core.Servers.Software;
using WitherTorch.Core.Servers.Utils;
using WitherTorch.Core.Utils;

namespace WitherTorch.Core.Servers
{
    partial class Forge
    {
        private static readonly SoftwareContextPrivate _software = new SoftwareContextPrivate();

        /// <summary>
        /// 取得與 <see cref="Forge"/> 相關聯的軟體上下文
        /// </summary>
        public static IForgeLikeSoftwareSoftware Software => _software;

        private sealed class ForgeVersionEntry
        {
            public readonly string version;

            public readonly string versionRaw;

            public ForgeVersionEntry(string version, string versionRaw)
            {
                this.version = version;
                this.versionRaw = versionRaw;
            }
        }

        private sealed class SoftwareContextPrivate : SoftwareContextBase<Forge>, IForgeLikeSoftwareSoftware
        {
            private const string ManifestListURL = "https://maven.minecraftforge.net/net/minecraftforge/forge/maven-metadata.xml";

            private readonly Lazy<Task<ReadOnlyDictionaryKeyGroup<string, ForgeVersionEntry[]>>> _versionDictGroupLazy =
                new Lazy<Task<ReadOnlyDictionaryKeyGroup<string, ForgeVersionEntry[]>>>(LoadVersionDictionaryAsync, LazyThreadSafetyMode.ExecutionAndPublication);

            public IReadOnlyDictionary<string, ForgeVersionEntry[]> VersionDictionary
                => _versionDictGroupLazy.Value.Result.Dictionary;

            public SoftwareContextPrivate() : base(SoftwareId) { }

            public override string[] GetSoftwareVersions()
                => _versionDictGroupLazy.Value.Result.Keys;

            public string[] GetForgeVersionsFromMinecraftVersion(string minecraftVersion)
            {
                ForgeVersionEntry[] versions = GetForgeVersionEntriesFromMinecraftVersion(minecraftVersion);
                int length = versions.Length;
                if (length <= 0)
                    return Array.Empty<string>();
                string[] result = new string[length];
                for (int i = 0; i < length; i++)
                {
                    result[i] = versions[i].version;
                }
                return result;
            }

            public ForgeVersionEntry[] GetForgeVersionEntriesFromMinecraftVersion(string minecraftVersion)
                => VersionDictionary.TryGetValue(minecraftVersion, out ForgeVersionEntry[]? result) ? result : Array.Empty<ForgeVersionEntry>();

            public override Forge? CreateServerInstance(string serverDirectory) => new Forge(serverDirectory);

            public override async Task<bool> TryInitializeAsync(CancellationToken token)
            {
                if (!await base.TryInitializeAsync(token))
                    return false;
                await _versionDictGroupLazy.Value;
                return true;
            }

            private static async Task<ReadOnlyDictionaryKeyGroup<string, ForgeVersionEntry[]>> LoadVersionDictionaryAsync()
            {
                IReadOnlyDictionary<string, ForgeVersionEntry[]>? dict;
                try
                {
                    dict = await LoadVersionDictionaryCoreAsync();
                }
                catch (Exception)
                {
                    dict = null;
                }
                finally
                {
                    GC.Collect(2, GCCollectionMode.Optimized);
                }
                if (dict is null || dict.Count <= 0)
                    return ReadOnlyDictionaryKeyGroup<string, ForgeVersionEntry[]>.Empty;
                return new ReadOnlyDictionaryKeyGroup<string, ForgeVersionEntry[]>(dict, static keys =>
                {
                    Array.Sort(keys, MojangAPI.VersionComparer.Instance);
                    Array.Reverse(keys);
                });
            }

            private static async Task<IReadOnlyDictionary<string, ForgeVersionEntry[]>?> LoadVersionDictionaryCoreAsync()
            {
                string? manifestString = await CachedDownloadClient.Instance.DownloadStringAsync(ManifestListURL);
                if (string.IsNullOrEmpty(manifestString))
                    return null;
                XmlDocument manifestXML = new XmlDocument();
                manifestXML.LoadXml(manifestString);
                Dictionary<string, List<ForgeVersionEntry>> dict = new Dictionary<string, List<ForgeVersionEntry>>();
                XmlNodeList? nodes = manifestXML.SelectNodes("/metadata/versioning/versions/version");
                if (nodes is null)
                    return null;
                foreach (XmlNode node in nodes)
                {
                    string versionString = node.InnerText;
                    if (versionString is null)
                        continue;
                    string[] versionSplits = versionString.Split('-');
                    string version = VersionStringHelper.ReplaceOnce(versionSplits[0], '_', '-').Replace(".0", string.Empty);
                    if (!dict.TryGetValue(version, out List<ForgeVersionEntry>? historyVersionList))
                        dict.Add(version, historyVersionList = new List<ForgeVersionEntry>());
                    historyVersionList.Add(new ForgeVersionEntry(versionSplits[1], versionString));
                }

                Dictionary<string, ForgeVersionEntry[]> result = new Dictionary<string, ForgeVersionEntry[]>(dict.Count);
                foreach (var pair in dict)
                {
                    result.Add(pair.Key, pair.Value.ToArray());
                }

                return result;
            }
        }
    }
}
