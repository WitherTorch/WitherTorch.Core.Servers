using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;

using WitherTorch.Core.Utils;

namespace WitherTorch.Core.Servers.Utils
{
    /// <summary>
    /// 提供與 Mojang 相關的公用 API，此類別是靜態類別
    /// </summary>
    public static partial class MojangAPI
    {
        private const string manifestListURL = "https://piston-meta.mojang.com/mc/game/version_manifest_v2.json";

        private static readonly Lazy<Task<ReadOnlyDictionaryKeyGroup<string, VersionInfo>>> _versionDictGroupLazy =
            new Lazy<Task<ReadOnlyDictionaryKeyGroup<string, VersionInfo>>>(LoadVersionDictionaryAsync, LazyThreadSafetyMode.ExecutionAndPublication);

        /// <summary>
        /// 取得 Minecraft: Java Edition 的版本資料庫
        /// </summary>
        public static IReadOnlyDictionary<string, VersionInfo> VersionDictionary 
            => _versionDictGroupLazy.Value.Result.Dictionary;

        /// <summary>
        /// 取得 Minecraft: Java Edition 的版本列表
        /// </summary>
        public static string[] Versions 
            => _versionDictGroupLazy.Value.Result.Keys;

        /// <summary>
        /// 初始化 API 的功能
        /// </summary>
        public static Task InitializeAsync() => _versionDictGroupLazy.Value;

        private class VersionManifestModel
        {
            [JsonPropertyName("versions")]
            public VersionInfo[]? Versions { get; set; }
        }

        private static async Task<ReadOnlyDictionaryKeyGroup<string, VersionInfo>> LoadVersionDictionaryAsync()
        {
            IReadOnlyDictionary<string, VersionInfo>? dict;
            try
            {
                dict = await LoadVersionDictionaryAsyncCore();
            }
            catch (Exception)
            {
                dict = null;
            }
            finally
            {
                GC.Collect(generation: 1, GCCollectionMode.Optimized);
            }
            if (dict is null || dict.Count <= 0)
                return ReadOnlyDictionaryKeyGroup<string, VersionInfo>.Empty;
            return new ReadOnlyDictionaryKeyGroup<string, VersionInfo>(dict, static (dict, keys) =>
            {
                Array.Sort(keys, new InternalVersionComparer(dict));
                Array.Reverse(keys);
            });
        }

        private static async Task<IReadOnlyDictionary<string, VersionInfo>?> LoadVersionDictionaryAsyncCore()
        {
            string? manifestString = await CachedDownloadClient.Instance.DownloadStringAsync(manifestListURL);
            if (string.IsNullOrEmpty(manifestString))
                return null;
            VersionManifestModel? manifestJSON = JsonSerializer.Deserialize<VersionManifestModel>(manifestString!);
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

            return result.AsReadOnlyDictionary();
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static bool IsValidTime(in DateTime time)
        {
            int month = time.Month;
            int day = time.Day;
            return month != 4 || day != 1; // 過濾愚人節版本
        }
    }
}
