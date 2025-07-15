using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Threading;
using System.Xml;

using WitherTorch.Core.Servers.Software;
using WitherTorch.Core.Servers.Utils;
using WitherTorch.Core.Utils;
using System.Runtime.CompilerServices;

namespace WitherTorch.Core.Servers
{
    partial class NeoForge
    {
        private static readonly SoftwareContextPrivate _software = new SoftwareContextPrivate();

        /// <summary>
        /// 取得與 <see cref="NeoForge"/> 相關聯的軟體上下文
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

        private class SoftwareContextPrivate : SoftwareContextBase<NeoForge>, IForgeLikeSoftwareSoftware
        {
            private const string MainSourceDomain = "https://maven.neoforged.net/releases";
            private const string MirrorSourceDomain = "https://maven.creeperhost.net";
            private const string LegacyManifestListURL = "{0}/net/neoforged/forge/maven-metadata.xml";
            private const string ManifestListURL = "{0}/releases/net/neoforged/neoforge/maven-metadata.xml";

            private static readonly string[] SourceDomains = [MainSourceDomain, MirrorSourceDomain];
            private readonly Lazy<Task<ReadOnlyDictionaryKeyGroup<string, ForgeVersionEntry[]>>> _versionDictGroupLazy =
                new Lazy<Task<ReadOnlyDictionaryKeyGroup<string, ForgeVersionEntry[]>>>(LoadVersionDictionaryAsync, LazyThreadSafetyMode.ExecutionAndPublication);

            public IReadOnlyDictionary<string, ForgeVersionEntry[]> VersionDictionary
                => _versionDictGroupLazy.Value.Result.Dictionary;

            public SoftwareContextPrivate() : base(SoftwareId) { }

            public override string[] GetSoftwareVersions()
                => _versionDictGroupLazy.Value.Result.Keys;

            public string[] GetForgeVersionsFromMinecraftVersion(string minecraftVersion)
            {
                ForgeVersionEntry[] versions = _software.GetForgeVersionEntriesFromMinecraftVersion(minecraftVersion);
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

            public override NeoForge? CreateServerInstance(string serverDirectory) => new NeoForge(serverDirectory);

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
                Dictionary<string, List<ForgeVersionEntry>> dict = new Dictionary<string, List<ForgeVersionEntry>>();
                StrongBox<int> preferredDomainIndexBox = new StrongBox<int>(0);

                try
                {
                    await LoadLegacyVersionDataAsync(dict, preferredDomainIndexBox);
                }
                catch (Exception)
                {
                }

                if (preferredDomainIndexBox.Value >= SourceDomains.Length)
                    return null;

                try
                {
                    await LoadVersionData(dict, preferredDomainIndexBox.Value);
                }
                catch (Exception)
                {
                }

                if (dict.Count <= 0)
                    return null;

                Dictionary<string, ForgeVersionEntry[]> result = new Dictionary<string, ForgeVersionEntry[]>(dict.Count);
                foreach (var item in dict)
                {
                    ForgeVersionEntry[] values = item.Value.ToArray();
                    Array.Reverse(values);
                    result.Add(item.Key, values);
                }
                return result.AsReadOnlyDictionary();
            }

            private static async Task LoadLegacyVersionDataAsync(Dictionary<string, List<ForgeVersionEntry>> dict, StrongBox<int> preferredDomainIndexBox)
            {
                string? manifestString = null;
                string[] sourceDomains = SourceDomains;
                int preferredDomainIndex = preferredDomainIndexBox.Value;
                int domainCount = sourceDomains.Length;
                for (; preferredDomainIndex < domainCount; preferredDomainIndex++)
                {
                    string url = string.Format(LegacyManifestListURL, sourceDomains[preferredDomainIndex]);
                    manifestString = await CachedDownloadClient.Instance.DownloadStringAsync(url);
                    if (!string.IsNullOrEmpty(manifestString))
                        break;
                }
                preferredDomainIndexBox.Value = preferredDomainIndex;
                if (preferredDomainIndex >= domainCount || manifestString is null)
                    return;
                XmlDocument manifestXML = new XmlDocument();
                manifestXML.LoadXml(manifestString);
                XmlNodeList? nodeList = manifestXML.SelectNodes("/metadata/versioning/versions/version");
                if (nodeList is null)
                    return;
                foreach (XmlNode node in nodeList)
                {
                    string versionString = node.InnerText;
                    if (versionString is null || versionString == "1.20.1-47.1.7") //此版本不存在
                        continue;
                    string[] versionSplits = node.InnerText.Split('-');
                    if (versionSplits.Length < 2)
                        continue;
                    string version = VersionStringHelper.ReplaceOnce(versionSplits[0], '_', '-').Replace(".0", string.Empty);
                    if (!dict.TryGetValue(version, out List<ForgeVersionEntry>? historyVersionList))
                        dict.Add(version, historyVersionList = new List<ForgeVersionEntry>());
                    historyVersionList.Add(new ForgeVersionEntry(versionSplits[1], versionString));
                }
            }

            private static async Task LoadVersionData(Dictionary<string, List<ForgeVersionEntry>> dict, int preferredDomainIndex)
            {
                string? manifestString = null;
                string[] sourceDomains = SourceDomains;
                int domainCount = sourceDomains.Length;
                for (; preferredDomainIndex < domainCount; preferredDomainIndex++)
                {
                    string url = string.Format(LegacyManifestListURL, sourceDomains[preferredDomainIndex]);
                    manifestString = await CachedDownloadClient.Instance.DownloadStringAsync(url);
                    if (!string.IsNullOrEmpty(manifestString))
                        break;
                }
                if (preferredDomainIndex >= domainCount || manifestString is null)
                    return;
                XmlDocument manifestXML = new XmlDocument();
                manifestXML.LoadXml(manifestString);
                XmlNodeList? nodeList = manifestXML.SelectNodes("/metadata/versioning/versions/version");
                if (nodeList is null)
                    return;
                foreach (XmlNode token in nodeList)
                {
                    string versionString = token.InnerText;
                    if (versionString is null)
                        continue;
                    string[] versionSplits = versionString.Split('-');
                    if (versionSplits.Length < 1)
                        continue;
                    string version = versionSplits[0];
                    string mcVersion = version.Substring(0, version.LastIndexOf('.'));
                    if (mcVersion.StartsWith("0."))
                        continue;
                    if (mcVersion.EndsWith(".0"))
                        mcVersion = mcVersion.Substring(0, mcVersion.Length - 2);
                    mcVersion = "1." + mcVersion;
                    if (!dict.TryGetValue(mcVersion, out List<ForgeVersionEntry>? historyVersionList))
                        dict.Add(mcVersion, historyVersionList = new List<ForgeVersionEntry>());
                    historyVersionList.Add(new ForgeVersionEntry(version, versionString));
                }
            }
        }
    }
}
