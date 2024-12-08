using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Threading;
using System.Xml;

#if NET6_0_OR_GREATER
using System.Collections.Frozen;
#endif

namespace WitherTorch.Core.Servers.Utils
{
    /// <summary>
    /// 提供與 SpigotMC 相關的公用API，此類別是靜態類別
    /// </summary>
    public static class SpigotAPI
    {
        private const string manifestListURL = "https://hub.spigotmc.org/nexus/content/groups/public/org/spigotmc/spigot-api/maven-metadata.xml";
        private const string manifestListURL2 = "https://hub.spigotmc.org/nexus/content/groups/public/org/spigotmc/spigot-api/{0}/maven-metadata.xml";
        private static readonly Lazy<IReadOnlyDictionary<string, string>> _versionDictLazy =
            new Lazy<IReadOnlyDictionary<string, string>>(LoadVersionList, LazyThreadSafetyMode.ExecutionAndPublication);
        private static readonly Lazy<string[]> _versionsLazy = new Lazy<string[]>(
            () => _versionDictLazy.Value.ToKeyArray(MojangAPI.VersionComparer.Instance.Reverse()),
            LazyThreadSafetyMode.ExecutionAndPublication);
        public static IReadOnlyDictionary<string, string> VersionDictionary => _versionDictLazy.Value;
        public static string[] Versions => _versionsLazy.Value;

        public static void Initialize()
        {
            var _ = _versionsLazy.Value;
        }

        private static IReadOnlyDictionary<string, string> LoadVersionList()
        {
            try
            {
                return LoadVersionListInternal() ?? EmptyDictionary<string, string>.Instance;
            }
            catch (Exception)
            {
            }
            return EmptyDictionary<string, string>.Instance;
        }

        private static IReadOnlyDictionary<string, string>? LoadVersionListInternal()
        {
            string? manifestString = CachedDownloadClient.Instance.DownloadString(manifestListURL);
            if (string.IsNullOrEmpty(manifestString))
                return null;
            XmlDocument manifestXML = new XmlDocument();
            manifestXML.LoadXml(manifestString);
            XmlNodeList? nodeList = manifestXML.SelectNodes("/metadata/versioning/versions/version");
            if (nodeList is null)
                return null;

            Dictionary<string, string> result = new Dictionary<string, string>();
            foreach (XmlNode node in nodeList)
            {
                string versionString = node.InnerText;
                string[] versionSplits = versionString.Split('-');
                string version = versionSplits[0];
                for (int i = 1; i < versionSplits.Length; i++)
                {
                    if (versionSplits[i][0] == 'R' || versionSplits[i] == "SNAPSHOT")
                        break;
                    version += "-" + versionSplits[i];
                }
                if (result.ContainsKey(version))
                    continue;
                result.Add(version, versionString);
            }

#if NET6_0_OR_GREATER
            return result.ToFrozenDictionary();
#else
            return result;
#endif
        }

        public static int GetBuildNumber(string version)
        {
            if (!VersionDictionary.TryGetValue(version, out string? result) || result is null || result.Length <= 0)
                return -1;
            try
            {
                return GetBuildNumberInternal(string.Format(manifestListURL2, result));
            }
            catch (Exception)
            {
            }
            return -1;
        }

        private static int GetBuildNumberInternal(string url)
        {
            string manifestString; 
            using (HttpClient client = new HttpClient())
            {
                client.DefaultRequestHeaders.Add("User-Agent", Constants.UserAgent);
                manifestString = client.GetStringAsync(url).Result;
            }
            if (string.IsNullOrEmpty(manifestString))
                return -1;

            XmlDocument manifestXML = new XmlDocument();
            manifestXML.LoadXml(manifestString);
            string? buildNumber = manifestXML.SelectSingleNode("/metadata/versioning/snapshot/buildNumber")?.InnerText;
            if (!string.IsNullOrEmpty(buildNumber) && int.TryParse(buildNumber, out int result))
                return result;
            return -1;
        }
    }

}
