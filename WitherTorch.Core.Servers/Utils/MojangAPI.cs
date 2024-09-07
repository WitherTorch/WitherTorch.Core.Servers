using Newtonsoft.Json;

using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.CompilerServices;

using WitherTorch.Core.Utils;

#if NET6_0_OR_GREATER
using System.Collections.Frozen;
#endif

namespace WitherTorch.Core.Servers.Utils
{
    /// <summary>
    /// 提供與 Mojang 相關的公用API，此類別是靜態類別
    /// </summary>
    public static class MojangAPI
    {
        private const string manifestListURL = "https://piston-meta.mojang.com/mc/game/version_manifest_v2.json";

        private static readonly Lazy<IReadOnlyDictionary<string, VersionInfo>> _versionDictionaryLazy = new Lazy<IReadOnlyDictionary<string, VersionInfo>>(
            LoadVersionList, System.Threading.LazyThreadSafetyMode.ExecutionAndPublication);

        private static readonly Lazy<string[]> _versionsLazy = new Lazy<string[]>(
            () => VersionDictionary?.ToKeyArray() ?? Array.Empty<string>(), System.Threading.LazyThreadSafetyMode.PublicationOnly);

        public static IReadOnlyDictionary<string, VersionInfo> VersionDictionary => _versionDictionaryLazy.Value;
        public static string[] Versions => _versionsLazy.Value;

        public static void Initialize()
        {
            string[] _ = _versionsLazy.Value;
        }

        [JsonObject(MemberSerialization = MemberSerialization.OptIn)]
        private class VersionManifestModel
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

        private static IReadOnlyDictionary<string, VersionInfo> LoadVersionList()
        {
            try
            {
                return LoadVersionListInternal() ?? EmptyDictionary<string, VersionInfo>.Instance;
            }
            catch (Exception)
            {
            }
            GC.Collect(2, GCCollectionMode.Optimized);
            return EmptyDictionary<string, VersionInfo>.Instance;
        }

        private static IReadOnlyDictionary<string, VersionInfo> LoadVersionListInternal()
        {
            string manifestString = CachedDownloadClient.Instance.DownloadString(manifestListURL);
            if (string.IsNullOrEmpty(manifestString))
                return null;
            VersionManifestModel manifestJSON;
            using (JsonTextReader reader = new JsonTextReader(new StringReader(manifestString)))
            {
                try
                {
                    manifestJSON = GlobalSerializers.JsonSerializer.Deserialize<VersionManifestModel>(reader);
                }
                catch (Exception)
                {
                    return null;
                }
                reader.Close();
            }
            if (manifestJSON is null)
                return null;
            VersionInfo[] versions = manifestJSON.Versions;
            int count = versions.Length;
            if (count <= 0)
                return null;
            Dictionary<string, VersionInfo> result = new Dictionary<string, VersionInfo>(count);
            for (int i = 0; i < count; i++)
            {
                VersionInfo versionInfo = manifestJSON.Versions[i];
                if (!IsValidTime(versionInfo.ReleaseTime))
                    continue;
                result.Add(versionInfo.Id, versionInfo);
            }
#if NET6_0_OR_GREATER
            return FrozenDictionary.ToFrozenDictionary(result);
#else
            return result;
#endif
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static bool IsValidTime(in DateTime time)
        {
            int month = time.Month;
            int day = time.Day;
            return month != 4 || day != 1; // 過濾愚人節版本
        }

        public sealed class VersionComparer : IComparer<string>
        {
            private static readonly Lazy<VersionComparer> _instLazy = new Lazy<VersionComparer>(() => new VersionComparer(),
                System.Threading.LazyThreadSafetyMode.PublicationOnly);

            public static VersionComparer Instance => _instLazy.Value;

            private VersionComparer() { }

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
