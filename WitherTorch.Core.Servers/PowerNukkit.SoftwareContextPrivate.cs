using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using WitherTorch.Core.Servers.Utils;
using WitherTorch.Core.Software;
using WitherTorch.Core.Utils;


#if NET8_0_OR_GREATER
using System.Collections.Frozen;
#endif

namespace WitherTorch.Core.Servers
{
    partial class PowerNukkit
    {
        private static readonly SoftwareContextPrivate _software = new SoftwareContextPrivate();

        /// <summary>
        /// 取得與 <see cref="PowerNukkit"/> 相關聯的軟體上下文
        /// </summary>
        public static ISoftwareContext Software => _software;

        private sealed class SoftwareContextPrivate : SoftwareContextBase<PowerNukkit>
        {
            private const string ManifestListURL = "https://repo1.maven.org/maven2/org/powernukkit/powernukkit/maven-metadata.xml";

            private readonly Lazy<Task<ReadOnlyDictionaryKeyGroup<string, string>>> _versionDictGroupLazy = new(
                LoadVersionDictionaryGroupAsync, LazyThreadSafetyMode.ExecutionAndPublication);

            public SoftwareContextPrivate() : base(SoftwareId) { }

            public override async Task<IReadOnlyList<string>> GetSoftwareVersionsAsync()
                => (await _versionDictGroupLazy.Value.ConfigureAwait(false)).Keys;

            public async Task<string?> QueryFullVersionStringAsync(string version)
                => (await _versionDictGroupLazy.Value.ConfigureAwait(false)).Dictionary.TryGetValue(version, out string? result) ? result : null;

            public override PowerNukkit? CreateServerInstance(string serverDirectory) => new PowerNukkit(serverDirectory);

            public override Task<bool> TryInitializeAsync(CancellationToken cancellationToken) => Task.FromResult(true);

            private static async Task<ReadOnlyDictionaryKeyGroup<string, string>> LoadVersionDictionaryGroupAsync()
            {
                try
                {
                    return await LoadVersionDictionaryGroupCoreAsync() ?? ReadOnlyDictionaryKeyGroup<string, string>.Empty;
                }
                catch (Exception)
                {
                    return ReadOnlyDictionaryKeyGroup<string, string>.Empty;
                }
            }

            private static async Task<ReadOnlyDictionaryKeyGroup<string, string>?> LoadVersionDictionaryGroupCoreAsync()
            {
                string? manifestString = await CachedDownloadClient.Instance.DownloadStringAsync(ManifestListURL);
                if (!MavenManifestExtractor.TryEnumerateVersionsFromXml(manifestString, out string? latestVersion, out IEnumerable<string>? versions))
                    return null;

                HashSet<string> cleanVersionList = new HashSet<string>(), versionList = new HashSet<string>();
                foreach (string version in versions)
                {
                    string cleanVersion;
                    int indexOf = version.IndexOf("-PN");
                    if (indexOf >= 0)
                        cleanVersion = version.Substring(0, indexOf);
                    else
                        cleanVersion = version;
                    if (!cleanVersionList.Add(cleanVersion))
                        continue;
                    versionList.Add(version);
                }

                int count = cleanVersionList.Count;
                if (count <= 0)
                    return null;

                string[] keys = cleanVersionList.ToArray();
                string[] values = versionList.ToArray();
                if (values[0] != latestVersion)
                {
                    Array.Reverse(keys);
                    Array.Reverse(values);
                }

                return ReadOnlyDictionaryKeyGroup.Create(keys, values);
            }
        }
    }
}
