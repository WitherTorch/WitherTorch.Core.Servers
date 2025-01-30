using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;

using WitherTorch.Core.Software;
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
        private const string BuildVersionManifestListURL = "https://api.papermc.io/v2/projects/paper/versions/{0}";
        private const string BuildVersionManifestListURL2 = "https://api.papermc.io/v2/projects/paper/versions/{0}/builds/{1}";
        private const string DownloadURL = "https://api.papermc.io/v2/projects/paper/versions/{0}/builds/{1}/downloads/{2}";
        private const string SoftwareId = "paper";

        private static readonly Lazy<MojangAPI.VersionInfo?> mc1_19 = new Lazy<MojangAPI.VersionInfo?>(
            () => (MojangAPI.VersionDictionary?.TryGetValue("1.19", out MojangAPI.VersionInfo? result) ?? false) ? result : null,
            LazyThreadSafetyMode.PublicationOnly);
        private readonly Lazy<IPropertyFile[]> propertyFilesLazy;
        private string _version = string.Empty;
        private int _build = -1;

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

        public override string ServerVersion => _version;

        public override string GetSoftwareId() => SoftwareId;

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
            return new InstallTask(this, id, task =>
            {
                if (!InstallServerCore(task, info))
                    task.OnInstallFailed();
            });
        }

        private bool InstallServerCore(InstallTask task, MojangAPI.VersionInfo info)
        {
            string? version = info.Id;
            if (version is null || version.Length <= 0)
                return false;
            string manifestURL = string.Format(BuildVersionManifestListURL, version);
            if (string.IsNullOrEmpty(manifestURL))
                return false;
            WebClient2 client = new WebClient2();
            InstallTaskWatcher watcher = new InstallTaskWatcher(task, client);
            JsonObject? jsonObject = JsonNode.Parse(client.GetStringAsync(manifestURL).Result) as JsonObject;
            if (watcher.IsStopRequested || jsonObject is null || !jsonObject.TryGetPropertyValue("builds", out JsonNode? node))
                return false;
            JsonArray? jsonArray = node as JsonArray;
            if (jsonArray is null)
                return false;
            node = jsonArray.Last();
            if (node is null || !(node is JsonValue _value && _value.GetValueKind() == JsonValueKind.Number))
                return false;
            int build = _value.GetValue<int>();
            jsonObject = JsonNode.Parse(client.GetStringAsync(string.Format(BuildVersionManifestListURL2, version, build.ToString())).Result) as JsonObject;
            if (watcher.IsStopRequested || jsonObject is null || !jsonObject.TryGetPropertyValue("downloads", out node))
                return false;
            jsonObject = node as JsonObject;
            if (jsonObject is null || !jsonObject.TryGetPropertyValue("application", out node))
                return false;
            jsonObject = node as JsonObject;
            if (jsonObject is null || !jsonObject.TryGetPropertyValue("name", out node))
                return false;
            byte[]? sha256;
            if (WTCore.CheckFileHashIfExist && jsonObject.TryGetPropertyValue("sha256", out JsonNode? sha256Token) && sha256Token is not null)
                sha256 = HashHelper.HexStringToByte(sha256Token.ToString());
            else
                sha256 = null;
            watcher.Dispose();
            int? id = FileDownloadHelper.AddTask(task: task, webClient: client,
                downloadUrl: string.Format(DownloadURL, version, build, ObjectUtils.ThrowIfNull(node).ToString()),
                filename: Path.Combine(ServerDirectory, @"paper-" + version + ".jar"),
                hash: sha256, hashMethod: HashHelper.HashMethod.SHA256);
            if (id.HasValue)
            {
                void AfterDownload(object? sender, int sendingId)
                {
                    if (id.Value != sendingId)
                        return;
                    FileDownloadHelper.TaskFinished -= AfterDownload;
                    _version = version;
                    _build = build;
                    MojangAPI.VersionInfo? versionInfo = FindVersionInfo(version);
                    _versionInfo = versionInfo;
                    if (propertyFilesLazy.IsValueCreated)
                        PaperYMLFile = GetPaperConfigFile(versionInfo);
                    OnServerVersionChanged();
                }
                FileDownloadHelper.TaskFinished += AfterDownload;
                return true;
            }
            return false;
        }

        public override string GetReadableVersion()
        {
            return _version;
        }


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

        protected override MojangAPI.VersionInfo? BuildVersionInfo()
        {
            return FindVersionInfo(_version);
        }

        protected override bool CreateServerCore() => true;

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

        protected override bool SaveServerCore(JsonPropertyFile serverInfoJson)
        {
            serverInfoJson["version"] = _version;
            serverInfoJson["build"] = _build;
            return base.SaveServerCore(serverInfoJson);
        }

        protected override string GetServerJarPath()
            => Path.Combine(ServerDirectory, "./paper-" + GetReadableVersion() + ".jar");
    }
}
