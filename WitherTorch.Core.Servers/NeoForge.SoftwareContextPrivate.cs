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
            private const string ManifestListURL = "{0}/net/neoforged/neoforge/maven-metadata.xml";

            private static readonly string[] SourceDomains = [MainSourceDomain, MirrorSourceDomain];

            private readonly Lazy<Task<ReadOnlyDictionaryKeyGroup<string, ForgeVersionEntry[]>>> _versionDictGroupLazy;
            private int _sourceDomainIndex = 0;

            public IReadOnlyDictionary<string, ForgeVersionEntry[]> VersionDictionary
                => _versionDictGroupLazy.Value.Result.Dictionary;

            public string AvailableSourceDomain => _sourceDomainIndex < SourceDomains.Length ? SourceDomains[_sourceDomainIndex] : string.Empty;

            public SoftwareContextPrivate() : base(SoftwareId)
            {
                _versionDictGroupLazy = new Lazy<Task<ReadOnlyDictionaryKeyGroup<string, ForgeVersionEntry[]>>>(
                    LoadVersionDictionaryAsync, LazyThreadSafetyMode.ExecutionAndPublication);
            }

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

            private async Task<ReadOnlyDictionaryKeyGroup<string, ForgeVersionEntry[]>> LoadVersionDictionaryAsync()
            {
                Dictionary<string, List<ForgeVersionEntry>>? legacyDict, dict;
                StrongBox<int> preferredDomainIndexBox = new StrongBox<int>(0);

                try
                {
                    legacyDict = await LoadLegacyVersionDataAsync(preferredDomainIndexBox);
                }
                catch (Exception)
                {
                    legacyDict = null;
                }

                int sourceDomainIndex = preferredDomainIndexBox.Value;
                if (sourceDomainIndex >= SourceDomains.Length)
                {
                    _sourceDomainIndex = sourceDomainIndex;
                    return ReadOnlyDictionaryKeyGroup<string, ForgeVersionEntry[]>.Empty;
                }

                try
                {
                    dict = await LoadVersionDataAsync(preferredDomainIndexBox);
                }
                catch (Exception)
                {
                    dict = null;
                }

                sourceDomainIndex = preferredDomainIndexBox.Value;
                _sourceDomainIndex = sourceDomainIndex;
                if (sourceDomainIndex >= SourceDomains.Length)
                    return ReadOnlyDictionaryKeyGroup<string, ForgeVersionEntry[]>.Empty;

                IReadOnlyDictionary<string, ForgeVersionEntry[]> transformedDict;
                if (legacyDict is null || legacyDict.Count <= 0)
                {
                    if (dict is null || dict.Count <= 0)
                        return ReadOnlyDictionaryKeyGroup<string, ForgeVersionEntry[]>.Empty;

                    transformedDict = dict.ToDictionary(
                        keySelector: pair => pair.Key,
                        elementSelector: pair => pair.Value.ToArray());
                }
                else
                {
                    if (dict is null || dict.Count <= 0)
                    {
                        transformedDict = legacyDict.ToDictionary(
                            keySelector: pair => pair.Key,
                            elementSelector: pair => pair.Value.ToArray());
                    }
                    else
                    {
                        transformedDict = dict.Union(legacyDict, KeyEqualityComparer<string, List<ForgeVersionEntry>>.Default).ToDictionary(
                            keySelector: pair => pair.Key,
                            elementSelector: pair => pair.Value.ToArray());
                    }
                }

                return new ReadOnlyDictionaryKeyGroup<string, ForgeVersionEntry[]>(transformedDict, static keys =>
                    {
                        Array.Sort(keys, MojangAPI.VersionComparer.Instance);
                        Array.Reverse(keys);
                    });
            }

            private static async Task<Dictionary<string, List<ForgeVersionEntry>>?> LoadLegacyVersionDataAsync(StrongBox<int> preferredDomainIndexBox)
            {
                string? manifestString = await DownloadStringFromDomains(SourceDomains, LegacyManifestListURL, preferredDomainIndexBox);
                if (!MavenManifestExtractor.TryEnumerateVersionsFromXml(manifestString, out string? latestVersion, out IEnumerable<string>? versions))
                    return null;
                Dictionary<string, List<ForgeVersionEntry>> result = new Dictionary<string, List<ForgeVersionEntry>>();
                string? firstVersionString = null;
                foreach (string versionString in versions)
                {
                    firstVersionString ??= versionString;
                    if (versionString == "1.20.1-47.1.7") //此版本不存在
                        continue;
                    string[] versionSplits = versionString.Split('-');
                    if (versionSplits.Length < 2)
                        continue;
                    string version = VersionStringHelper.ReplaceOnce(versionSplits[0], '_', '-').Replace(".0", string.Empty);
                    if (!result.TryGetValue(version, out List<ForgeVersionEntry>? historyVersionList))
                        result.Add(version, historyVersionList = new List<ForgeVersionEntry>());
                    historyVersionList.Add(new ForgeVersionEntry(versionSplits[1], versionString));
                }

                if (!latestVersion.Equals(firstVersionString)) // 如果最新版本不在版本列表最前面的話，就把結果倒置
                {
                    foreach (List<ForgeVersionEntry> historyVersionList in result.Values)
                        historyVersionList.Reverse();
                }
                return result;
            }

            private static async Task<Dictionary<string, List<ForgeVersionEntry>>?> LoadVersionDataAsync(StrongBox<int> preferredDomainIndexBox)
            {
                string? manifestString = await DownloadStringFromDomains(SourceDomains, ManifestListURL, preferredDomainIndexBox);
                if (!MavenManifestExtractor.TryEnumerateVersionsFromXml(manifestString, out string? latestVersion, out IEnumerable<string>? versions))
                    return null;
                Dictionary<string, List<ForgeVersionEntry>> result = new Dictionary<string, List<ForgeVersionEntry>>();
                string? firstVersionString = null;
                foreach (string versionString in versions)
                {
                    firstVersionString ??= versionString;
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
                    if (!result.TryGetValue(mcVersion, out List<ForgeVersionEntry>? historyVersionList))
                        result.Add(mcVersion, historyVersionList = new List<ForgeVersionEntry>());
                    historyVersionList.Add(new ForgeVersionEntry(version, versionString));
                }

                if (!latestVersion.Equals(firstVersionString)) // 如果最新版本不在版本列表最前面的話，就把結果倒置
                {
                    foreach (List<ForgeVersionEntry> historyVersionList in result.Values)
                        historyVersionList.Reverse();
                }
                return result;
            }

            private static async Task<string?> DownloadStringFromDomains(string[] domains, string urlFormat, StrongBox<int> indexRecorder)
            {
                int index = indexRecorder.Value;
                int length = domains.Length;
                if (index >= length)
                    return null;
                for (; index < length; index++)
                {
                    string? result = await CachedDownloadClient.Instance.DownloadStringAsync(string.Format(urlFormat, domains[index]));
                    if (result is not null)
                    {
                        indexRecorder.Value = index;
                        return result;
                    }
                }
                indexRecorder.Value = length;
                return null;
            }
        }
    }
}
