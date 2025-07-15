using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Xml;

using WitherTorch.Core.Servers.Utils;
using WitherTorch.Core.Software;
using WitherTorch.Core.Utils;
using System.Threading.Tasks;


#if NET8_0_OR_GREATER
using System.Collections.Frozen;
#endif

namespace WitherTorch.Core.Servers
{
    partial class PowerNukkit
    {
        private static readonly SoftwareContextPrivate _software = new SoftwareContextPrivate();

        /// <summary>
        /// 取得與 <see cref="PowerNukkit"/> 相關聯的軟體上下文
        /// </summary>
        public static ISoftwareContext Software => _software;

        private sealed class SoftwareContextPrivate : SoftwareContextBase<PowerNukkit>
        {
            private const string ManifestListURL = "https://repo1.maven.org/maven2/org/powernukkit/powernukkit/maven-metadata.xml";

            private readonly Lazy<IReadOnlyDictionary<string, string>> _versionDictLazy = new Lazy<IReadOnlyDictionary<string, string>>(
                LoadVersionDictionary, LazyThreadSafetyMode.ExecutionAndPublication);
            private readonly Lazy<string[]> _versionsLazy;

            public SoftwareContextPrivate() : base(SoftwareId)
            {
                _versionsLazy = new Lazy<string[]>(GetVersionKeys, LazyThreadSafetyMode.PublicationOnly);
            }

            public override string[] GetSoftwareVersions() => _versionsLazy.Value;

            public string QueryFullVersionString(string version)
                => _versionDictLazy.Value.TryGetValue(version, out string? result) ? result : string.Empty;

            public override PowerNukkit? CreateServerInstance(string serverDirectory) => new PowerNukkit(serverDirectory);

            public override Task<bool> TryInitializeAsync(CancellationToken cancellationToken) => Task.FromResult(true);

            private static IReadOnlyDictionary<string, string> LoadVersionDictionary()
            {
                try
                {
                    return LoadVersionDictionaryCore() ?? EmptyDictionary<string, string>.Instance;
                }
                catch (Exception)
                {
                }
                return EmptyDictionary<string, string>.Instance;
            }

            private static IReadOnlyDictionary<string, string>? LoadVersionDictionaryCore()
            {
                string? manifestString = CachedDownloadClient.Instance.DownloadString(ManifestListURL);
                if (manifestString is null || manifestString.Length <= 0)
                    return null;
                XmlDocument manifestXML = new XmlDocument();
                manifestXML.LoadXml(manifestString);
                XmlNodeList? nodeList = manifestXML.SelectNodes("/metadata/versioning/versions/version");
                if (nodeList is null)
                    return null;
                Dictionary<string, string> result = new Dictionary<string, string>();
                foreach (XmlNode token in nodeList)
                {
                    string rawVersion = token.InnerText;
                    string version;
                    int indexOf = rawVersion.IndexOf("-PN");
                    if (indexOf >= 0)
                        version = rawVersion.Substring(0, indexOf);
                    else
                        version = rawVersion;
                    result[version] = rawVersion;
                }

                return result.AsReadOnlyDictionary();
            }

            private string[] GetVersionKeys()
            {
                string[] result = _versionDictLazy.Value.Keys.ToArray();
                Array.Reverse(result);
                return result;
            }
        }
    }
}
