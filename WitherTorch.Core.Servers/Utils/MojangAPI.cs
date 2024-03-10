using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using WitherTorch.Core.Utils;

namespace WitherTorch.Core.Servers.Utils
{
    /// <summary>
    /// 提供與 Mojang 相關的公用API，此類別是靜態類別
    /// </summary>
    public static class MojangAPI
    {
        private const string manifestListURL = "https://piston-meta.mojang.com/mc/game/version_manifest_v2.json";
        public static Dictionary<string, VersionInfo> VersionDictionary { get; private set; }

        private static string[] versions;
        public static string[] Versions
        {
            get
            {
                if (versions is null)
                {
                    LoadVersionList();
                }
                return versions;
            }
        }

        private static string[] javaDedicatedVersions;
        internal static string[] JavaDedicatedVersions
        {
            get
            {
                if (javaDedicatedVersions is null)
                {
                    LoadVersionList();
                }
                return javaDedicatedVersions;
            }
        }

        [JsonObject(MemberSerialization = MemberSerialization.OptIn)]
        private class VersionManifestJSONModel
        {
            [JsonProperty("versions", ItemIsReference = true)]
            public VersionInfo[] Versions { get; set; }
        }

        [JsonObject(MemberSerialization = MemberSerialization.OptIn)]
        public sealed class VersionInfo : IComparable<string>, IComparable<VersionInfo>
        {
            [JsonProperty("id")]
            public string Id { get; set; }

            [JsonProperty("url")]
            public string ManifestURL { get; set; }

            [JsonProperty("releaseTime")]
            public DateTime ReleaseTime { get; set; }

            [JsonProperty("type")]
            public string Type { get; set; }

            int IComparable<string>.CompareTo(string other)
            {
                if (other is null) return 0;
                else if (VersionDictionary.ContainsKey(other))
                {
                    return ReleaseTime.CompareTo(VersionDictionary[other].ReleaseTime);
                }
                return 0;
            }

            int IComparable<VersionInfo>.CompareTo(VersionInfo other)
            {
                if (other is null) return 1;
                else return ReleaseTime.CompareTo(other.ReleaseTime);
            }

            public static bool operator <(VersionInfo a, VersionInfo b)
            {
                return (a as IComparable<VersionInfo>).CompareTo(b) < 0;
            }

            public static bool operator <=(VersionInfo a, VersionInfo b)
            {
                return (a as IComparable<VersionInfo>).CompareTo(b) <= 0;
            }

            public static bool operator >(VersionInfo a, VersionInfo b)
            {
                return (a as IComparable<VersionInfo>).CompareTo(b) > 0;
            }

            public static bool operator >=(VersionInfo a, VersionInfo b)
            {
                return (a as IComparable<VersionInfo>).CompareTo(b) >= 0;
            }
        }

        public static event EventHandler Initialized;

        volatile static bool isInInitialize = false;
        public static void Initialize()
        {
            if (isInInitialize) return;
            isInInitialize = true;
            if (VersionDictionary is null) LoadVersionList();
            Initialized?.Invoke(null, EventArgs.Empty);
            isInInitialize = false;
        }
        public static void LoadVersionList()
        {
            try
            {
                string manifestString = CachedDownloadClient.Instance.DownloadString(manifestListURL);
                if (manifestString != null)
                {
                    VersionManifestJSONModel manifestJSON;
                    using (JsonTextReader reader = new JsonTextReader(new StringReader(manifestString)))
                    {
                        try
                        {
                            manifestJSON = GlobalSerializers.JsonSerializer.Deserialize<VersionManifestJSONModel>(reader);
                        }
                        catch (Exception)
                        {
                            manifestJSON = null;
                        }
                        reader.Close();
                    }
                    if (manifestJSON != null)
                    {
                        int count = manifestJSON.Versions.Length;
                        if (count > 0)
                        {
                            Dictionary<string, VersionInfo> versionPairs = new Dictionary<string, VersionInfo>(count);
                            string[] versions = new string[count];
                            string[] versions2 = new string[count];
                            int j = 0, k = 0;
                            for (int i = 0; i < count; i++)
                            {
                                VersionInfo versionInfo = manifestJSON.Versions[i];
                                if (IsValidTime(versionInfo.ReleaseTime))
                                {
                                    string id = versionInfo.Id;
                                    versionPairs.Add(id, versionInfo);
                                    versions[j++] = id;
                                    if (IsVanillaHasServer(versionInfo))
                                        versions2[k++] = id;
                                }
                            }
                            VersionDictionary = versionPairs;
                            MojangAPI.versions = Subarray(versions, j);
                            javaDedicatedVersions = Subarray(versions2, k);
                        }
                        else
                        {
                            VersionDictionary = null;
                            versions = null;
                            javaDedicatedVersions = null;
                        }
                    }
                }
            }
            catch (Exception)
            {
            }
            GC.Collect(0, GCCollectionMode.Optimized);
        }

        private static string[] Subarray(string[] original, int count)
        {
            string[] result = new string[count];
            for (int i = 0; i < count; i++)
            {
                result[i] = original[i];
            }
            return result;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static bool IsValidTime(in DateTime time)
        {
            int month = time.Month;
            int day = time.Day;
            return month != 4 || day != 1; // 過濾愚人節版本
        }

        private static bool IsVanillaHasServer(VersionInfo versionInfo)
        {
            DateTime time = versionInfo.ReleaseTime;
            int year = time.Year;
            int month = time.Month;
            int day = time.Day;
            if (year > 2012 || (year == 2012 && (month > 3 || (month == 3 && day >= 29)))) //1.2.5 開始有 server 版本 (1.2.5 發布日期: 2012/3/29)
            {
                return true;
            }
            return false;
        }

        public sealed class VersionComparer : IComparer<string>
        {
            private VersionComparer() { }

            private static VersionComparer _instance = null;

            public static VersionComparer Instance
            {
                get
                {
                    if (_instance is null && VersionDictionary != null) _instance = new VersionComparer();
                    return _instance;
                }
            }

            public int Compare(string x, string y)
            {
                bool success = VersionDictionary.TryGetValue(x, out VersionInfo infoA);
                if (!success) return -1;
                success = VersionDictionary.TryGetValue(y, out VersionInfo infoB);
                if (success)
                {
                    return infoA.ReleaseTime.CompareTo(infoB.ReleaseTime);
                }
                else
                {
                    return 1;
                }
            }
        }
    }
}
