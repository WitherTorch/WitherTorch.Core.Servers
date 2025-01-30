
using System.Threading;
using System;
using WitherTorch.Core.Servers.Utils;
using System.Collections.Generic;
using System.Text.Json;
using WitherTorch.Core.Servers.Software;
using WitherTorch.Core.Software;

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

            private readonly Lazy<string[]> _versionsLazy = new Lazy<string[]>(LoadVersionList, LazyThreadSafetyMode.ExecutionAndPublication);
            private readonly Lazy<VersionStruct[]> _loaderVersionsLazy =
                new Lazy<VersionStruct[]>(LoadQuiltLoaderVersions, LazyThreadSafetyMode.ExecutionAndPublication);
            private readonly Lazy<string[]> _loaderVersionKeysLazy;

            public SoftwareContextPrivate() : base(SoftwareId)
            {
                _loaderVersionKeysLazy = new Lazy<string[]>(LoadQuiltLoaderVersionKeys, LazyThreadSafetyMode.PublicationOnly);
            }

            public override string[] GetSoftwareVersions() => _versionsLazy.Value;

            public string[] GetSoftwareLoaderVersions() => _loaderVersionKeysLazy.Value;

            public override Quilt? CreateServerInstance(string serverDirectory) => new Quilt(serverDirectory);

            public string GetLatestStableQuiltLoaderVersion()
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
                    return LoadVersionListInternal() ?? Array.Empty<string>();
                }
                catch (Exception)
                {
                }
                return Array.Empty<string>();
            }

            private static string[]? LoadVersionListInternal()
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

            private static VersionStruct[] LoadQuiltLoaderVersions()
            {
                try
                {
                    return LoadQuiltLoaderVersionsInternal() ?? Array.Empty<VersionStruct>();
                }
                catch (Exception)
                {
                }
                return Array.Empty<VersionStruct>();
            }

            private static VersionStruct[]? LoadQuiltLoaderVersionsInternal()
            {
                string? manifestString = CachedDownloadClient.Instance.DownloadString(ManifestListURLForLoader);
                if (manifestString is null || manifestString.Length <= 0)
                    return null;
                return JsonSerializer.Deserialize<VersionStruct[]>(manifestString);
            }

            private string[] LoadQuiltLoaderVersionKeys()
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
