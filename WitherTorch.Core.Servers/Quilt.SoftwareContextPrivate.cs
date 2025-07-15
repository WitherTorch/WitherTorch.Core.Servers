
using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

using WitherTorch.Core.Servers.Software;
using WitherTorch.Core.Servers.Utils;
using WitherTorch.Core.Utils;

namespace WitherTorch.Core.Servers
{
    partial class Quilt
    {
        private static readonly SoftwareContextPrivate _software = new SoftwareContextPrivate();

        /// <summary>
        /// 取得與 <see cref="Quilt"/> 相關聯的軟體上下文
        /// </summary>
        public static IFabricLikeSoftwareSoftware Software => _software;

        private sealed class SoftwareContextPrivate : SoftwareContextBase<Quilt>, IFabricLikeSoftwareSoftware
        {
            private const string ManifestListURL = "https://meta.quiltmc.org/v3/versions/game";
            private const string ManifestListURLForLoader = "https://meta.quiltmc.org/v3/versions/loader";

            private readonly Lazy<Task<string[]>> _versionsLazy =
                 new Lazy<Task<string[]>>(LoadVersionListAsync, LazyThreadSafetyMode.ExecutionAndPublication);
            private readonly Lazy<Task<VersionStruct[]>> _loaderVersionsLazy =
                new Lazy<Task<VersionStruct[]>>(LoadQuiltLoaderVersionsAsync, LazyThreadSafetyMode.ExecutionAndPublication);
            private readonly Lazy<string[]> _loaderVersionKeysLazy;

            public SoftwareContextPrivate() : base(SoftwareId)
            {
                _loaderVersionKeysLazy = new Lazy<string[]>(LoadQuiltLoaderVersionKeys, LazyThreadSafetyMode.PublicationOnly);
            }

            public override string[] GetSoftwareVersions() => _versionsLazy.Value.Result;

            public string[] GetSoftwareLoaderVersions() => _loaderVersionKeysLazy.Value;

            public override Quilt? CreateServerInstance(string serverDirectory) => new Quilt(serverDirectory);

            public async Task<string> GetLatestStableQuiltLoaderVersionAsync()
            {
                VersionStruct[] loaderVersions = await _loaderVersionsLazy.Value;
                int count = loaderVersions.Length;
                for (int i = 0; i < count; i++)
                {
                    VersionStruct loaderVersion = loaderVersions[i];
                    if (loaderVersion.Stable)
                        return loaderVersion.Version;
                }
                return count > 0 ? loaderVersions[0].Version : string.Empty;
            }

            private static async Task<string[]> LoadVersionListAsync()
            {
                try
                {
                    return await LoadVersionListAsyncCore() ?? Array.Empty<string>();
                }
                catch (Exception)
                {
                }
                return Array.Empty<string>();
            }

            private static async Task<string[]?> LoadVersionListAsyncCore()
            {
                string? manifestString = await CachedDownloadClient.Instance.DownloadStringAsync(ManifestListURL);
                if (manifestString is null || manifestString.Length <= 0)
                    return null;
                VersionStruct[]? array = JsonSerializer.Deserialize<VersionStruct[]>(manifestString);
                if (array is null)
                    return null;
                int length = array.Length;
                if (length <= 0)
                    return null;
                List<string> versionList = new List<string>(length);
                string[] versions = MojangAPI.Versions;
                for (int i = 0; i < length; i++)
                {
                    string version = array[i].Version;
                    if (versions.Contains(version))
                        versionList.Add(version);
                }
                Array.Sort(versions, MojangAPI.VersionComparer.Instance.Reverse());
                return versions;
            }

            private static async Task<VersionStruct[]> LoadQuiltLoaderVersionsAsync()
            {
                try
                {
                    return await LoadQuiltLoaderVersionsAsyncCore() ?? Array.Empty<VersionStruct>();
                }
                catch (Exception)
                {
                }
                return Array.Empty<VersionStruct>();
            }

            private static async Task<VersionStruct[]?> LoadQuiltLoaderVersionsAsyncCore()
            {
                string? manifestString = await CachedDownloadClient.Instance.DownloadStringAsync(ManifestListURLForLoader);
                if (manifestString is null || manifestString.Length <= 0)
                    return null;
                return JsonSerializer.Deserialize<VersionStruct[]>(manifestString);
            }

            private string[] LoadQuiltLoaderVersionKeys()
            {
                VersionStruct[] loaderVersions = _loaderVersionsLazy.Value.Result;
                int length = loaderVersions.Length;
                if (length <= 0)
                    return Array.Empty<string>();
                string[] result = new string[length];
                for (int i = 0; i < length; i++)
                    result[i] = loaderVersions[i].Version;
                return result;
            }
        }
    }
}
