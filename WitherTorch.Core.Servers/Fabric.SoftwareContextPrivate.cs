
using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading;

using WitherTorch.Core.Servers.Software;
using WitherTorch.Core.Servers.Utils;

namespace WitherTorch.Core.Servers
{
    partial class Fabric
    {
        private static readonly SoftwareContextPrivate _software = new SoftwareContextPrivate();

        public static IFabricLikeSoftwareSoftware Software => _software;

        private sealed class SoftwareContextPrivate : SoftwareContextBase<Fabric>, IFabricLikeSoftwareSoftware
        {
            private const string ManifestListURL = "https://meta.fabricmc.net/v2/versions/game";
            private const string ManifestListURLForLoader = "https://meta.fabricmc.net/v2/versions/loader";

            private readonly Lazy<string[]> _versionsLazy = 
                new Lazy<string[]>(LoadVersionList, LazyThreadSafetyMode.ExecutionAndPublication);
            private readonly Lazy<VersionStruct[]> _loaderVersionsLazy =
                new Lazy<VersionStruct[]>(LoadFabricLoaderVersions, LazyThreadSafetyMode.ExecutionAndPublication);
            private readonly Lazy<string[]> _loaderVersionKeysLazy;

            public SoftwareContextPrivate() : base(SoftwareId)
            {
                _loaderVersionKeysLazy = new Lazy<string[]>(LoadFabricLoaderVersionKeys, LazyThreadSafetyMode.PublicationOnly);
            }

            public override Fabric? CreateServerInstance(string serverDirectory) => new Fabric(serverDirectory);

            public override string[] GetSoftwareVersions() => _versionsLazy.Value;

            public string[] GetSoftwareLoaderVersions() => _loaderVersionKeysLazy.Value;

            public string GetLatestStableFabricLoaderVersion()
            {
                VersionStruct[] loaderVersions = _loaderVersionsLazy.Value;
                int count = loaderVersions.Length;
                for (int i = 0; i < count; i++)
                {
                    VersionStruct loaderVersion = loaderVersions[i];
                    if (loaderVersion.Stable)
                        return loaderVersion.Version;
                }
                return count > 0 ? loaderVersions[0].Version : string.Empty;
            }

            private static string[] LoadVersionList()
            {
                try
                {
                    return LoadVersionListCore() ?? Array.Empty<string>();
                }
                catch (Exception)
                {
                }
                return Array.Empty<string>();
            }

            private static string[]? LoadVersionListCore()
            {
                string? manifestString = CachedDownloadClient.Instance.DownloadString(ManifestListURL);
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

            private static VersionStruct[] LoadFabricLoaderVersions()
            {
                try
                {
                    return LoadFabricLoaderVersionsCore() ?? Array.Empty<VersionStruct>();
                }
                catch (Exception)
                {
                }
                return Array.Empty<VersionStruct>();
            }

            private static VersionStruct[]? LoadFabricLoaderVersionsCore()
            {
                string? manifestString = CachedDownloadClient.Instance.DownloadString(ManifestListURLForLoader);
                if (manifestString is null || manifestString.Length <= 0)
                    return null;
                return JsonSerializer.Deserialize<VersionStruct[]>(manifestString);
            }

            private string[] LoadFabricLoaderVersionKeys()
            {
                VersionStruct[] loaderVersions = _loaderVersionsLazy.Value;
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
