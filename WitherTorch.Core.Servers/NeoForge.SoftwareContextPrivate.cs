using System;
using System.Collections.Generic;
using System.Linq;
using System.Xml;

using WitherTorch.Core.Servers.Software;
using WitherTorch.Core.Servers.Utils;


#if NET6_0_OR_GREATER
using System.Collections.Frozen;
#endif

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
            private const string LegacyManifestListURL = "https://maven.neoforged.net/releases/net/neoforged/forge/maven-metadata.xml";
            private const string ManifestListURL = "https://maven.neoforged.net/releases/net/neoforged/neoforge/maven-metadata.xml";

            private IReadOnlyDictionary<string, ForgeVersionEntry[]> _versionDict = EmptyDictionary<string, ForgeVersionEntry[]>.Instance;

            private string[] _versions = Array.Empty<string>();

            public IReadOnlyDictionary<string, ForgeVersionEntry[]> VersionDictionary => _versionDict;

            public SoftwareContextPrivate() : base(SoftwareId) { }

            public override string[] GetSoftwareVersions() => _versions;

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
                => _versionDict.TryGetValue(minecraftVersion, out ForgeVersionEntry[]? result) ? result : Array.Empty<ForgeVersionEntry>();

            public override NeoForge? CreateServerInstance(string serverDirectory) => new NeoForge(serverDirectory);

            public override bool TryInitialize()
            {
                if (!base.TryInitialize())
                    return false;
                IReadOnlyDictionary<string, ForgeVersionEntry[]> versionDict = LoadVersionDictionary();
                _versionDict = versionDict;
                _versions = versionDict.ToKeyArray(MojangAPI.VersionComparer.Instance.Reverse());
                return true;
            }
            private static IReadOnlyDictionary<string, ForgeVersionEntry[]> LoadVersionDictionary()
            {
                try
                {
                    return LoadVersionDictionaryCore() ?? EmptyDictionary<string, ForgeVersionEntry[]>.Instance;
                }
                catch (Exception)
                {
                }
                GC.Collect(2, GCCollectionMode.Optimized);
                return EmptyDictionary<string, ForgeVersionEntry[]>.Instance;
            }

            private static IReadOnlyDictionary<string, ForgeVersionEntry[]> LoadVersionDictionaryCore()
            {
                Dictionary<string, List<ForgeVersionEntry>> dict = new Dictionary<string, List<ForgeVersionEntry>>();
                try
                {
                    LoadLegacyVersionData(dict);
                }
                catch (Exception)
                {
                }
                try
                {
                    LoadVersionData(dict);
                }
                catch (Exception)
                {
                }
                Dictionary<string, ForgeVersionEntry[]> result = new Dictionary<string, ForgeVersionEntry[]>(dict.Count);
                foreach (var item in dict)
                {
                    ForgeVersionEntry[] values = item.Value.ToArray();
                    Array.Reverse(values);
                    result.Add(item.Key, values);
                }

#if NET6_0_OR_GREATER
            return FrozenDictionary.ToFrozenDictionary(result);
#else
                return result;
#endif
            }

            private static void LoadLegacyVersionData(Dictionary<string, List<ForgeVersionEntry>> dict)
            {
                string? manifestString = CachedDownloadClient.Instance.DownloadString(ManifestListURL);
                if (string.IsNullOrEmpty(manifestString))
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
                    string[] versionSplits = node.InnerText.Split(new char[] { '-' });
                    if (versionSplits.Length < 2)
                        continue;
                    string version;
                    unsafe
                    {
                        string rawVersion = versionSplits[0];
                        fixed (char* rawVersionString = rawVersion)
                        {
                            char* rawVersionStringEnd = rawVersionString + rawVersion.Length;
                            char* pointerChar = rawVersionString;
                            while (pointerChar < rawVersionStringEnd)
                            {
                                if (*pointerChar == '_')
                                {
                                    *pointerChar = '-';
                                    break;
                                }
                                pointerChar++;
                            }
                            version = new string(rawVersionString).Replace(".0", "");
                        }
                    }
                    if (!dict.TryGetValue(version, out List<ForgeVersionEntry>? historyVersionList))
                        dict.Add(version, historyVersionList = new List<ForgeVersionEntry>());
                    historyVersionList.Add(new ForgeVersionEntry(versionSplits[1], versionString));
                }
            }

            private static void LoadVersionData(Dictionary<string, List<ForgeVersionEntry>> dict)
            {
                string? manifestString = CachedDownloadClient.Instance.DownloadString(ManifestListURL);
                if (string.IsNullOrEmpty(manifestString))
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
                    string[] versionSplits = versionString.Split(new char[] { '-' });
                    if (versionSplits.Length < 1)
                        continue;
                    string version = versionSplits[0];
                    string mcVersion = "1." + version.Substring(0, version.LastIndexOf('.'));
                    if (!dict.TryGetValue(mcVersion, out List<ForgeVersionEntry>? historyVersionList))
                        dict.Add(mcVersion, historyVersionList = new List<ForgeVersionEntry>());
                    historyVersionList.Add(new ForgeVersionEntry(version, versionString));
                }
            }
        }
    }
}
