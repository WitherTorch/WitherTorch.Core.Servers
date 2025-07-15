using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;

using WitherTorch.Core.Property;
using WitherTorch.Core.Servers.Utils;
using WitherTorch.Core.Utils;

namespace WitherTorch.Core.Servers
{
    /// <summary>
    /// Paper 伺服器
    /// </summary>
    public sealed partial class Paper : JavaEditionServerBase
    {
        private const string BuildVersionManifestListURL = "https://fill.papermc.io/v3/projects/paper/versions/{0}";
        private const string BuildVersionManifestListURL2 = "https://fill.papermc.io/v3/projects/paper/versions/{0}/builds/{1}";
        private const string SoftwareId = "paper";

        private static readonly string UserAgentForPaperV3Api = $"withertorch/{Assembly.GetCallingAssembly().GetName().Version} (new1271@outlook.com)";
        private static readonly Lazy<MojangAPI.VersionInfo?> mc1_19 = new Lazy<MojangAPI.VersionInfo?>(
            () => (MojangAPI.VersionDictionary?.TryGetValue("1.19", out MojangAPI.VersionInfo? result) ?? false) ? result : null,
            LazyThreadSafetyMode.PublicationOnly);
        private readonly Lazy<IPropertyFile[]> propertyFilesLazy;
        private string _version = string.Empty;
        private int _build = -1;

        /// <summary>
        /// 取得伺服器的 server.properties 設定檔案
        /// </summary>
        public JavaPropertyFile ServerPropertiesFile
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => (JavaPropertyFile)propertyFilesLazy.Value[0];
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            private set
            {
                IPropertyFile[] propertyFiles = propertyFilesLazy.Value;
                IPropertyFile propertyFile = propertyFiles[0];
                propertyFiles[0] = value;
                propertyFile.Dispose();
            }
        }

        /// <summary>
        /// 取得伺服器的 bukkit.yml 設定檔案
        /// </summary>
        public YamlPropertyFile BukkitYMLFile
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => (YamlPropertyFile)propertyFilesLazy.Value[1];
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            private set
            {
                IPropertyFile[] propertyFiles = propertyFilesLazy.Value;
                IPropertyFile propertyFile = propertyFiles[1];
                propertyFiles[1] = value;
                propertyFile.Dispose();
            }
        }

        /// <summary>
        /// 取得伺服器的 spigot.yml 設定檔案
        /// </summary>
        public YamlPropertyFile SpigotYMLFile
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => (YamlPropertyFile)propertyFilesLazy.Value[2];
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            private set
            {
                IPropertyFile[] propertyFiles = propertyFilesLazy.Value;
                IPropertyFile propertyFile = propertyFiles[2];
                propertyFiles[2] = value;
                propertyFile.Dispose();
            }
        }

        /// <summary>
        /// 取得伺服器的 paper.yml 設定檔案
        /// </summary>
        public YamlPropertyFile PaperYMLFile
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => (YamlPropertyFile)propertyFilesLazy.Value[3];
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            private set
            {
                IPropertyFile[] propertyFiles = propertyFilesLazy.Value;
                IPropertyFile propertyFile = propertyFiles[3];
                propertyFiles[3] = value;
                propertyFile.Dispose();
            }
        }

        private Paper(string serverDirectory) : base(serverDirectory)
        {
            propertyFilesLazy = new Lazy<IPropertyFile[]>(GetServerPropertyFilesCore, LazyThreadSafetyMode.ExecutionAndPublication);
        }

        /// <inheritdoc/>
        public override string ServerVersion => _version;

        /// <inheritdoc/>
        public override string GetSoftwareId() => SoftwareId;

        /// <inheritdoc/>
        public override InstallTask? GenerateInstallServerTask(string version)
        {
            MojangAPI.VersionInfo? versionInfo = FindVersionInfo(version);
            if (versionInfo is null)
                return null;
            return InstallServerCore(versionInfo);
        }

        private InstallTask? InstallServerCore(MojangAPI.VersionInfo info)
        {
            string? id = info.Id;
            if (id is null || id.Length <= 0)
                return null;
            return new InstallTask(this, id, async (task, token) =>
            {
                if (!await InstallServerCore(task, info, token))
                    task.OnInstallFailed();
            });
        }

        private async Task<bool> InstallServerCore(InstallTask task, MojangAPI.VersionInfo info, CancellationToken token)
        {
            string? version = info.Id;
            if (version is null || version.Length <= 0)
                return false;
            string manifestURL = string.Format(BuildVersionManifestListURL, version);
            if (string.IsNullOrEmpty(manifestURL))
                return false;
            WebClient2 client = new WebClient2();
            client.DefaultRequestHeaders.Add("User-Agent", UserAgentForPaperV3Api);
            InstallTaskWatcher watcher = new InstallTaskWatcher(task, client);
#if NET8_0_OR_GREATER
            JsonObject? jsonObject = JsonNode.Parse(await client.GetStringAsync(manifestURL, token)) as JsonObject;
#else
            JsonObject? jsonObject = JsonNode.Parse(await client.GetStringAsync(manifestURL)) as JsonObject;
            if (token.IsCancellationRequested)
                return false;
#endif
            if (watcher.IsStopRequested || jsonObject is null || !jsonObject.TryGetPropertyValue("builds", out JsonNode? node))
                return false;
            JsonArray? jsonArray = node as JsonArray;
            if (jsonArray is null)
                return false;
            node = jsonArray.Last();
            if (node is null || !(node is JsonValue _value && _value.GetValueKind() == JsonValueKind.Number))
                return false;
            int build = _value.GetValue<int>();
            manifestURL = string.Format(BuildVersionManifestListURL2, version, build.ToString());
#if NET8_0_OR_GREATER
            jsonObject = JsonNode.Parse(await client.GetStringAsync(manifestURL, token)) as JsonObject;
#else
            jsonObject = JsonNode.Parse(await client.GetStringAsync(manifestURL)) as JsonObject;
            if (token.IsCancellationRequested)
                return false;
#endif
            if (watcher.IsStopRequested || jsonObject is null || !jsonObject.TryGetPropertyValue("downloads", out node))
                return false;
            jsonObject = node as JsonObject;
            if (jsonObject is null || !jsonObject.TryGetPropertyValue("server:default", out node))
                return false;
            jsonObject = node as JsonObject;
            if (jsonObject is null || !jsonObject.TryGetPropertyValue("url", out node) || node is not JsonValue urlValueNode || urlValueNode.GetValueKind() != JsonValueKind.String)
                return false;
            byte[]? sha256 = null;
            if (WTCore.CheckFileHashIfExist && jsonObject.TryGetPropertyValue("checksums", out node))
            {
                jsonObject = node as JsonObject;
                if (jsonObject is not null && jsonObject.TryGetPropertyValue("sha256", out JsonNode? sha256Node) && 
                    sha256Node is JsonValue sha256ValueNode && sha256ValueNode.GetValueKind() == JsonValueKind.String)
                    sha256 = HashHelper.HexStringToByte(sha256ValueNode.GetValue<string>());
            }
            watcher.Dispose();
            void afterInstallFinished()
            {
                _version = version;
                _build = build;
                MojangAPI.VersionInfo? versionInfo = FindVersionInfo(version);
                _versionInfo = versionInfo;
                if (propertyFilesLazy.IsValueCreated)
                    PaperYMLFile = GetPaperConfigFile(versionInfo);
                OnServerVersionChanged();
            }
            return FileDownloadHelper.AddTask(task: task, webClient: client,
                downloadUrl: urlValueNode.GetValue<string>(),
                filename: Path.Combine(ServerDirectory, $"paper-{version}.jar"),
                hash: sha256, hashMethod: HashHelper.HashMethod.SHA256, afterInstalledAction: afterInstallFinished).HasValue;
        }

        /// <inheritdoc/>
        public override string GetReadableVersion()
        {
            return _version;
        }

        /// <inheritdoc/>
        public override IPropertyFile[] GetServerPropertyFiles()
        {
            return propertyFilesLazy.Value;
        }

        private IPropertyFile[] GetServerPropertyFilesCore()
        {
            string directory = ServerDirectory;
            IPropertyFile[] result = new IPropertyFile[4]
            {
                new JavaPropertyFile(Path.Combine(directory, "./server.properties")),
                new YamlPropertyFile(Path.Combine(directory, "./bukkit.yml")),
                new YamlPropertyFile(Path.Combine(directory, "./spigot.yml")),
                GetPaperConfigFile(),
            };
            return result;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private YamlPropertyFile GetPaperConfigFile(MojangAPI.VersionInfo? versionInfo = null)
        {
            string directory = ServerDirectory;
            string paperConfigPath;
            versionInfo ??= GetMojangVersionInfo();
            MojangAPI.VersionInfo? mc1_19 = Paper.mc1_19.Value;
            if (versionInfo is null || mc1_19 is null || versionInfo < mc1_19)
            {
                paperConfigPath = Path.Combine(directory, "./paper.yml");
            }
            else
            {
                paperConfigPath = Path.Combine(directory, "./config/paper-global.yml");
                if (!File.Exists(paperConfigPath))
                {
                    string paperConfigPath_Alt = Path.Combine(directory, "./paper.yml");
                    if (File.Exists(paperConfigPath_Alt))
                    {
                        bool success = true;
                        try
                        {
                            File.Copy(paperConfigPath_Alt, paperConfigPath, overwrite: false);
                        }
                        catch (Exception)
                        {
                            success = false;
                        }
                        if (!success)
                            paperConfigPath = paperConfigPath_Alt;
                    }
                }
            }
            return new YamlPropertyFile(paperConfigPath);
        }

        /// <inheritdoc/>
        protected override MojangAPI.VersionInfo? BuildVersionInfo()
        {
            return FindVersionInfo(_version);
        }

        /// <inheritdoc/>
        protected override bool CreateServerCore() => true;

        /// <inheritdoc/>
        protected override bool LoadServerCore(JsonPropertyFile serverInfoJson)
        {
            string? version = serverInfoJson["version"]?.ToString();
            if (version is null || version.Length <= 0)
                return false;
            _version = version;
            JsonNode? buildNode = serverInfoJson["build"];
            if (buildNode is null || buildNode.GetValueKind() != JsonValueKind.Number)
                _build = 0;
            else
                _build = buildNode.GetValue<int>();
            return base.LoadServerCore(serverInfoJson);
        }

        /// <inheritdoc/>
        protected override bool SaveServerCore(JsonPropertyFile serverInfoJson)
        {
            serverInfoJson["version"] = _version;
            serverInfoJson["build"] = _build;
            return base.SaveServerCore(serverInfoJson);
        }

        /// <inheritdoc/>
        protected override string GetServerJarPath()
            => Path.Combine(ServerDirectory, "./paper-" + GetReadableVersion() + ".jar");
    }
}
