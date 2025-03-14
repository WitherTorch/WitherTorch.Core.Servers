#if NET8_0_OR_GREATER
using System.Collections.Frozen;
#endif

using System.Collections.Generic;
using System.Xml;
using System;

using WitherTorch.Core.Servers.Utils;

using System.Linq;

using WitherTorch.Core.Servers.Software;
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

            private IReadOnlyDictionary<string, ForgeVersionEntry[]> _versionDict = EmptyDictionary<string, ForgeVersionEntry[]>.Instance;

            private string[] _versions = Array.Empty<string>();

            public IReadOnlyDictionary<string, ForgeVersionEntry[]> VersionDictionary => _versionDict;

            public SoftwareContextPrivate() : base(SoftwareId) { }

            public override string[] GetSoftwareVersions() => _versions;

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
                => _versionDict.TryGetValue(minecraftVersion, out ForgeVersionEntry[]? result) ? result : Array.Empty<ForgeVersionEntry>();

            public override Forge? CreateServerInstance(string serverDirectory) => new Forge(serverDirectory);

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

            private static IReadOnlyDictionary<string, ForgeVersionEntry[]>? LoadVersionDictionaryCore()
            {
                string? manifestString = CachedDownloadClient.Instance.DownloadString(ManifestListURL);
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
                    string[] versionSplits = versionString.Split(new char[] { '-' });
                    string version;
                    unsafe
                    {
                        string rawVersion = versionSplits[0];
                        fixed (char* rawVersionString = rawVersion)
                        {
                            char* iterator = rawVersionString;
                            while (*iterator++ != '\0')
                            {
                                if (*iterator == '_')
                                {
                                    *iterator = '-';
                                    break;
                                }
                            }
                            version = new string(rawVersionString).Replace(".0", "");
                        }
                    }
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
