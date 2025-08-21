
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;

using WitherTorch.Core.Servers.Utils;
using WitherTorch.Core.Software;
using WitherTorch.Core.Utils;

namespace WitherTorch.Core.Servers
{
    partial class BedrockDedicated
    {
        private static readonly SoftwareContextPrivate _software = new SoftwareContextPrivate();

        /// <summary>
        /// 取得與 <see cref="BedrockDedicated"/> 相關聯的軟體上下文
        /// </summary>
        public static ISoftwareContext Software => _software;

        private sealed class SoftwareContextPrivate : SoftwareContextBase<BedrockDedicated>
        {
            private const string ManifestListURL = "https://net-secondary.web.minecraft-services.net/api/v1.0/download/links";

            private readonly Lazy<Task<ReadOnlyDictionaryKeyGroup<string, string>>> _versionDictLazy = new(
                LoadVersionDictionaryGroupAsync, LazyThreadSafetyMode.ExecutionAndPublication);

            public SoftwareContextPrivate() : base(SoftwareId) { }

            public override async Task<IReadOnlyList<string>> GetSoftwareVersionsAsync() => (await _versionDictLazy.Value).Keys;

            public async Task<IReadOnlyDictionary<string, string>> GetSoftwareVersionDictionaryAsync() => (await _versionDictLazy.Value).Dictionary;

            public override BedrockDedicated? CreateServerInstance(string serverDirectory) => new BedrockDedicated(serverDirectory);

            public override Task<bool> TryInitializeAsync(CancellationToken cancellationToken) => Task.FromResult(true);

            private static async Task<ReadOnlyDictionaryKeyGroup<string, string>> LoadVersionDictionaryGroupAsync()
            {
                string[]? links;
                try
                {
                    links = await LoadVersionLinksAsync();
                }
                catch (Exception)
                {
                    links = null;
                }
                if (links is null || links.Length <= 0)
                    return ReadOnlyDictionaryKeyGroup<string, string>.Empty;
                IEnumerable<KeyValuePair<string, string>> source = links.Select(static val =>
                {
                    int indexOf = val.LastIndexOf('-');
                    if (indexOf < 0)
                        return new KeyValuePair<string, string>(string.Empty, string.Empty);
                    int dotIndexOf = val.LastIndexOf(".zip");
                    if (dotIndexOf <= indexOf)
                        return new KeyValuePair<string, string>(string.Empty, string.Empty);
                    string key = val.Substring(indexOf + 1, dotIndexOf - indexOf - 1);
                    return new KeyValuePair<string, string>(key, val);
                }).Where(static pair => Version.TryParse(pair.Key, out _));
                return ReadOnlyDictionaryKeyGroup.Create(source, static keys =>
                {
                    Array.Sort(keys, VersionStringComparer.Instance);
                    Array.Reverse(keys);
                });
            }

            private static async Task<string[]?> LoadVersionLinksAsync()
            {
                string[] acceptedTypes;
#if NET8_0_OR_GREATER
                if (OperatingSystem.IsWindows())
                    acceptedTypes = ["serverBedrockWindows", "serverBedrockPreviewWindows"];
                else if (OperatingSystem.IsLinux())
                    acceptedTypes = ["serverBedrockLinux", "serverBedrockPreviewLinux"];
                else
                    return null;
#else
                switch (Environment.OSVersion.Platform)
                {
                    case PlatformID.Win32S:
                    case PlatformID.Win32Windows:
                    case PlatformID.Win32NT:
                        acceptedTypes = ["serverBedrockWindows", "serverBedrockPreviewWindows"];
                        break;
                    case PlatformID.Unix:
                        acceptedTypes = ["serverBedrockLinux", "serverBedrockPreviewLinux"];
                        break;
                    default:
                        return null;
                }
#endif
                string? manifestString = await CachedDownloadClient.Instance.DownloadStringAsync(ManifestListURL);
                if (manifestString is null)
                    return Array.Empty<string>();
                try
                {
                    RestResult result = JsonSerializer.Deserialize<RestResult>(manifestString);
                    Link[] links = result.Result.Links;
                    return result.Result.Links
                        .Where(val => Array.IndexOf(acceptedTypes, val.Type) >= 0)
                        .Select(static val => val.Url)
                        .Where(static val => val is not null)
                        .ToArray()!;
                }
                catch (Exception)
                {
                    return Array.Empty<string>();
                }
            }

            private sealed class VersionStringComparer : IComparer<string>
            {
                public static readonly VersionStringComparer Instance = new VersionStringComparer();

                private VersionStringComparer() { }

                public int Compare(string? x, string? y)
                {
                    if (!Version.TryParse(x, out Version? versionX))
                        return Version.TryParse(y, out _) ? 0 : -1;
                    if (!Version.TryParse(y, out Version? versionY))
                        return 1;
                    return versionX.CompareTo(versionY);
                }
            }

            // JSON Structures
            private struct RestResult
            {
                [JsonPropertyName("result")]
                public ResultLinks Result { get; set; }
            }

            private struct ResultLinks
            {
                [JsonPropertyName("links")]
                public Link[] Links { get; set; }
            }

            private struct Link
            {
                [JsonPropertyName("downloadType")]
                public string? Type { get; set; }

                [JsonPropertyName("downloadUrl")]
                public string? Url { get; set; }
            }
        }
    }
}
