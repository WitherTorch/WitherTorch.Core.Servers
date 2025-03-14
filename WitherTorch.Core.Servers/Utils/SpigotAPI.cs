using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using System.Xml;

using WitherTorch.Core.Utils;

#if NET8_0_OR_GREATER
using System.Collections.Frozen;
#endif

namespace WitherTorch.Core.Servers.Utils
{
    /// <summary>
    /// 提供與 SpigotMC 相關的公用 API，此類別是靜態類別
    /// </summary>
    public static class SpigotAPI
    {
        private const string manifestListURL = "https://hub.spigotmc.org/nexus/content/groups/public/org/spigotmc/spigot-api/maven-metadata.xml";
        private const string manifestListURL2 = "https://hub.spigotmc.org/nexus/content/groups/public/org/spigotmc/spigot-api/{0}/maven-metadata.xml";
        private static readonly Lazy<IReadOnlyDictionary<string, string>> _versionDictLazy =
            new Lazy<IReadOnlyDictionary<string, string>>(LoadVersionDictionary, LazyThreadSafetyMode.ExecutionAndPublication);
        private static readonly Lazy<string[]> _versionsLazy = new Lazy<string[]>(
            () => _versionDictLazy.Value.ToKeyArray(MojangAPI.VersionComparer.Instance.Reverse()),
            LazyThreadSafetyMode.ExecutionAndPublication);

        /// <summary>
        /// 取得 SpigotMC 的版本資料庫
        /// </summary>
        public static IReadOnlyDictionary<string, string> VersionDictionary => _versionDictLazy.Value;

        /// <summary>
        /// 取得 SpigotMC 的版本列表
        /// </summary>
        public static string[] Versions => _versionsLazy.Value;

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
            CachedDownloadClient client = CachedDownloadClient.Instance;
            HttpClient innerClient = client.InnerHttpClient;
            innerClient.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent", Constants.UserAgent);
            string? manifestString = client.DownloadString(manifestListURL);
            innerClient.DefaultRequestHeaders.Remove("User-Agent");
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

            return result.AsReadOnlyDictionary();
        }

        /// <summary>
        /// 取得與指定的 Minecraft 版本相關聯的 Spigot 組建編號
        /// </summary>
        /// <param name="version">要查詢的 Minecraft 版本</param>
        /// <returns></returns>
        public static int GetBuildNumber(string version)
        {
            if (!VersionDictionary.TryGetValue(version, out string? result) || result is null || result.Length <= 0)
                return -1;
            try
            {
                return GetBuildNumberCoreAsync(string.Format(manifestListURL2, result), CancellationToken.None).Result;
            }
            catch (Exception)
            {
            }
            return -1;
        }

        /// <inheritdoc cref="GetBuildNumber(string)"/>
        public static Task<int> GetBuildNumberAsync(string version)
            => GetBuildNumberAsync(version, CancellationToken.None);

        /// <inheritdoc cref="GetBuildNumber(string)"/>
        public static async Task<int> GetBuildNumberAsync(string version, CancellationToken token)
        {
            if (!VersionDictionary.TryGetValue(version, out string? result) || result is null || result.Length <= 0)
                return -1;
            try
            {
                return await GetBuildNumberCoreAsync(string.Format(manifestListURL2, result), token);
            }
            catch (Exception)
            {
            }
            return -1;
        }

        private static async Task<int> GetBuildNumberCoreAsync(string url, CancellationToken token)
        {
            string manifestString;
            using (HttpClient client = new HttpClient())
            {
                client.DefaultRequestHeaders.Add("User-Agent", Constants.UserAgent);
#if NET8_0_OR_GREATER
                manifestString = await client.GetStringAsync(url, token);
#else
                manifestString = await client.GetStringAsync(url);
                if (token.IsCancellationRequested)
                    return -1;
#endif
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
