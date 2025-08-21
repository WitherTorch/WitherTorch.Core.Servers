using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

using WitherTorch.Core.Servers.Software;
using WitherTorch.Core.Servers.Utils;
using WitherTorch.Core.Utils;

namespace WitherTorch.Core.Servers
{
    partial class Fabric
    {
        private static readonly SoftwareContextPrivate _software = new SoftwareContextPrivate();

        /// <summary>
        /// 取得與 <see cref="Fabric"/> 相關聯的軟體上下文
        /// </summary>
        public static IFabricLikeSoftwareContext Software => _software;

        private sealed class SoftwareContextPrivate : SoftwareContextBase<Fabric>, IFabricLikeSoftwareContext
        {
            private const string ManifestListURL = "https://meta.fabricmc.net/v2/versions/game";
            private const string ManifestListURLForLoader = "https://meta.fabricmc.net/v2/versions/loader";

            private readonly Lazy<Task<IReadOnlyList<string>>> _versionsLazy =
                new(LoadVersionListAsync, LazyThreadSafetyMode.ExecutionAndPublication);
            private readonly Lazy<Task<ReadOnlyDictionaryKeyGroup<string, VersionStruct>>> _loaderVersionsKeyGroupLazy =
                new(LoadFabricLoaderVersionsAsync, LazyThreadSafetyMode.ExecutionAndPublication);

            public SoftwareContextPrivate() : base(SoftwareId) { }

            public override Fabric? CreateServerInstance(string serverDirectory) => new Fabric(serverDirectory);

            public override async Task<IReadOnlyList<string>> GetSoftwareVersionsAsync()
                => await _versionsLazy.Value.ConfigureAwait(false);

            public async Task<IReadOnlyList<string>> GetSoftwareLoaderVersionsAsync()
                => (await _loaderVersionsKeyGroupLazy.Value.ConfigureAwait(false)).Keys;

            public async ValueTask<string?> GetLatestStableFabricLoaderVersionAsync(CancellationToken token)
            {
                IEnumerable<VersionStruct> loaderVersions = (await _loaderVersionsKeyGroupLazy.Value.ConfigureAwait(false)).Dictionary.Values;
                if (token.IsCancellationRequested)
                    return null;
                return loaderVersions.Where(static val => val.Stable).Select(static val => val.Version).FirstOrDefault();
            }

            private static async Task<IReadOnlyList<string>> LoadVersionListAsync()
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
                if (array is null || array.Length <= 0)
                    return null;
                IReadOnlyList<string> vanillaVersions = await MojangAPI.GetVersionsAsync();
                string[] result
#if NET8_0_OR_GREATER
                    = array.Select(static val => val.Version).Intersect(vanillaVersions).ToArray();
#else
                    = array.Select(static val => val.Version).Where(val => vanillaVersions.Contains(val)).ToArray();
#endif
                Array.Sort(result, MojangAPI.VersionComparer.Instance.Reverse());
                return result;
            }

            private static async Task<ReadOnlyDictionaryKeyGroup<string, VersionStruct>> LoadFabricLoaderVersionsAsync()
            {
                VersionStruct[]? versions;
                try
                {
                    versions = await LoadFabricLoaderVersionsAsyncCore();
                }
                catch (Exception)
                {
                    versions = null;
                }
                if (versions is null || versions.Length <= 0)
                    return ReadOnlyDictionaryKeyGroup<string, VersionStruct>.Empty;

                return ReadOnlyDictionaryKeyGroup.Create(versions.Select(static val => val.Version).ToArray(), versions);
            }

            private static async Task<VersionStruct[]?> LoadFabricLoaderVersionsAsyncCore()
            {
                string? manifestString = await CachedDownloadClient.Instance.DownloadStringAsync(ManifestListURLForLoader);
                if (manifestString is null || manifestString.Length <= 0)
                    return null;
                return JsonSerializer.Deserialize<VersionStruct[]>(manifestString);
            }
        }
    }
}
