using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;

#if NET6_0_OR_GREATER
using System.Collections.Frozen;
#endif

namespace WitherTorch.Core.Servers.Utils
{
    /// <summary>
    /// 提供與 Mojang 相關的公用 API，此類別是靜態類別
    /// </summary>
    public static class MojangAPI
    {
        private const string manifestListURL = "https://piston-meta.mojang.com/mc/game/version_manifest_v2.json";

        private static readonly Lazy<IReadOnlyDictionary<string, VersionInfo>> _versionDictLazy = new Lazy<IReadOnlyDictionary<string, VersionInfo>>(
            LoadVersionList, LazyThreadSafetyMode.ExecutionAndPublication);

        private static readonly Lazy<string[]> _versionsLazy = new Lazy<string[]>(
            () => _versionDictLazy.Value.ToKeyArray(VersionComparer.Instance.Reverse())
            , LazyThreadSafetyMode.PublicationOnly);

        /// <summary>
        /// 取得 Minecraft: Java Edition 的版本資料庫
        /// </summary>
        public static IReadOnlyDictionary<string, VersionInfo> VersionDictionary => _versionDictLazy.Value;

        /// <summary>
        /// 取得 Minecraft: Java Edition 的版本列表
        /// </summary>
        public static string[] Versions => _versionsLazy.Value;

        /// <summary>
        /// 初始化 API 的功能
        /// </summary>
        public static void Initialize()
        {
            _ = _versionsLazy.Value;
        }

        private class VersionManifestModel
        {
            [JsonPropertyName("versions")]
            public VersionInfo[]? Versions { get; set; }
        }

        /// <summary>
        /// 表示一筆 Minecraft: Java Edition 的版本資料
        /// </summary>
        public sealed class VersionInfo : IComparable<string>, IComparable<VersionInfo>
        {
            /// <summary>
            /// 版本的唯一標識符 (ID)
            /// </summary>
            [JsonPropertyName("id")]
            public string? Id { get; set; }

            /// <summary>
            /// 版本的資訊清單位址
            /// </summary>
            [JsonPropertyName("url")]
            public string? ManifestURL { get; set; }

            /// <summary>
            /// 版本的發布時間
            /// </summary>
            [JsonPropertyName("releaseTime")]
            public DateTime ReleaseTime { get; set; }

            /// <summary>
            /// 版本的類型
            /// </summary>
            [JsonPropertyName("type")]
            public string? Type { get; set; }

            int IComparable<string>.CompareTo(string? other)
            {
                if (other is null) return 0;
                else if (VersionDictionary.ContainsKey(other))
                {
                    return ReleaseTime.CompareTo(VersionDictionary[other].ReleaseTime);
                }
                return 0;
            }

            int IComparable<VersionInfo>.CompareTo(VersionInfo? other)
            {
                if (other is null) return 1;
                else return ReleaseTime.CompareTo(other.ReleaseTime);
            }

            /// <inheritdoc/>
            public static bool operator <(VersionInfo a, VersionInfo b)
            {
                return (a as IComparable<VersionInfo>).CompareTo(b) < 0;
            }

            /// <inheritdoc/>
            public static bool operator <=(VersionInfo a, VersionInfo b)
            {
                return (a as IComparable<VersionInfo>).CompareTo(b) <= 0;
            }

            /// <inheritdoc/>
            public static bool operator >(VersionInfo a, VersionInfo b)
            {
                return (a as IComparable<VersionInfo>).CompareTo(b) > 0;
            }

            /// <inheritdoc/>
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

        private static IReadOnlyDictionary<string, VersionInfo>? LoadVersionListInternal()
        {
            string? manifestString = CachedDownloadClient.Instance.DownloadString(manifestListURL);
            if (string.IsNullOrEmpty(manifestString))
                return null;
#pragma warning disable CS8604
            VersionManifestModel? manifestJSON = JsonSerializer.Deserialize<VersionManifestModel>(manifestString);
#pragma warning restore CS8604
            if (manifestJSON is null)
                return null;
            VersionInfo[]? versions = manifestJSON.Versions;
            if (versions is null)
                return null;
            int count = versions.Length;
            if (count <= 0)
                return null;
            Dictionary<string, VersionInfo> result = new Dictionary<string, VersionInfo>(count);
            for (int i = 0; i < count; i++)
            {
                VersionInfo versionInfo = versions[i];
                if (!IsValidTime(versionInfo.ReleaseTime))
                    continue;
                string? id = versionInfo.Id;
                if (id is null)
                    continue;
                result.Add(id, versionInfo);
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

        /// <summary>
        /// 版本字串的比較類別，此類別無法建立實體
        /// </summary>
        public sealed class VersionComparer : IComparer<string>
        {
            private static readonly VersionComparer _instance = new VersionComparer();

            /// <summary>
            /// <see cref="VersionComparer"/> 的唯一實體
            /// </summary>
            public static VersionComparer Instance => _instance;

            private VersionComparer() { }

            /// <inheritdoc/>
            public int Compare(string? x, string? y)
            {
                if (x is null)
                    return y is null ? 0 : -1;
                if (y is null)
                    return 1;
                IReadOnlyDictionary<string, VersionInfo> versionDict = VersionDictionary;
                if (!versionDict.TryGetValue(x, out VersionInfo? infoA))
                    return -1;
                if (versionDict.TryGetValue(y, out VersionInfo? infoB))
                    return infoA.ReleaseTime.CompareTo(infoB.ReleaseTime);
                return 1;
            }
        }
    }
}
