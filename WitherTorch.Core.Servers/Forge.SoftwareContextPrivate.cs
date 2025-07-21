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
            private const string MainSourceDomain = "https://maven.neoforged.net/releases";
            private const string MirrorSourceDomain = "https://maven.creeperhost.net";
            private const string ManifestListURL = "{0}/net/minecraftforge/forge/maven-metadata.xml";

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

            private async Task<ReadOnlyDictionaryKeyGroup<string, ForgeVersionEntry[]>> LoadVersionDictionaryAsync()
            {
                IReadOnlyDictionary<string, List<ForgeVersionEntry>>? dict;
                StrongBox<int> preferredDomainIndexBox = new StrongBox<int>(0);
                try
                {
                    dict = await LoadVersionDictionaryCoreAsync(preferredDomainIndexBox);
                }
                catch (Exception)
                {
                    dict = null;
                }

                int sourceDomainIndex = preferredDomainIndexBox.Value;
                _sourceDomainIndex = sourceDomainIndex;
                if (sourceDomainIndex >= SourceDomains.Length)
                    return ReadOnlyDictionaryKeyGroup<string, ForgeVersionEntry[]>.Empty;

                if (dict is null)
                    return ReadOnlyDictionaryKeyGroup<string, ForgeVersionEntry[]>.Empty;

                return new ReadOnlyDictionaryKeyGroup<string, ForgeVersionEntry[]>(dict.ToDictionary(
                    keySelector: val => val.Key,
                    elementSelector: val => val.Value.ToArray()), static keys =>
                    {
                        Array.Sort(keys, MojangAPI.VersionComparer.Instance);
                        Array.Reverse(keys);
                    });
            }

            private static async Task<IReadOnlyDictionary<string, List<ForgeVersionEntry>>?> LoadVersionDictionaryCoreAsync(StrongBox<int> preferredDomainIndexBox)
            {
                string? manifestString = await DownloadStringFromDomains(SourceDomains, ManifestListURL, preferredDomainIndexBox);
                if (!MavenManifestExtractor.TryEnumerateVersionsFromXml(manifestString, out string? latestVersion, out IEnumerable<string>? versions))
                    return null;

                Dictionary<string, List<ForgeVersionEntry>> result = new Dictionary<string, List<ForgeVersionEntry>>();
                string? firstVersionString = null;
                foreach (string versionString in versions)
                {
                    if (firstVersionString is null)
                        firstVersionString = versionString;
                    string[] versionSplits = versionString.Split('-');
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
