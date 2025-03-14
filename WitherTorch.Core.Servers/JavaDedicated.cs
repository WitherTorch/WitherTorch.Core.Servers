using System;
using System.IO;
using System.Runtime.CompilerServices;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;

using WitherTorch.Core.Property;
using WitherTorch.Core.Servers.Utils;
using WitherTorch.Core.Utils;

using YamlDotNet.Core.Tokens;

namespace WitherTorch.Core.Servers
{
    /// <summary>
    /// Java 原版伺服器
    /// </summary>
    public partial class JavaDedicated : JavaEditionServerBase
    {
        private const string SoftwareId = "javaDedicated";

        private readonly Lazy<IPropertyFile[]> propertyFilesLazy;

        private string _version = string.Empty;

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

        /// <inheritdoc/>
        public override string ServerVersion => _version;

        /// <inheritdoc/>
        public override string GetSoftwareId() => SoftwareId;

        private JavaDedicated(string serverDirectory) : base(serverDirectory)
        {
            propertyFilesLazy = new Lazy<IPropertyFile[]>(GetServerPropertyFilesCore, LazyThreadSafetyMode.ExecutionAndPublication);
        }

        /// <inheritdoc/>
        public override InstallTask? GenerateInstallServerTask(string version)
        {
            MojangAPI.VersionInfo? versionInfo = FindVersionInfo(version);
            if (versionInfo is null)
                return null;
            return GenerateInstallServerTaskCore(versionInfo);
        }

        /// <inheritdoc/>
        private InstallTask? GenerateInstallServerTaskCore(MojangAPI.VersionInfo versionInfo)
        {
            string? id = versionInfo.Id;
            if (id is null || id.Length <= 0)
                return null;
            InstallTask result = new InstallTask(this, id, async (task, token) =>
            {
                if (!await InstallServerCore(task, versionInfo, token))
                    task.OnInstallFailed();
            });
            void onInstallFinished(object? sender, EventArgs e)
            {
                if (sender is not InstallTask senderTask || senderTask.Owner is not JavaDedicated server)
                    return;
                senderTask.InstallFinished -= onInstallFinished;
                server._version = id;
                server._versionInfo = versionInfo;
                server.OnServerVersionChanged();
            }
            result.InstallFinished += onInstallFinished;
            return result;
        }

        /// <inheritdoc/>
        private async Task<bool> InstallServerCore(InstallTask task, MojangAPI.VersionInfo versionInfo, CancellationToken token)
        {
            string? manifestURL = versionInfo.ManifestURL;
            if (manifestURL is null || manifestURL.Length <= 0)
                return false;
            WebClient2 client = new WebClient2();
            InstallTaskWatcher watcher = new InstallTaskWatcher(task, client);
#if NET8_0_OR_GREATER
            JsonObject? jsonObject = JsonNode.Parse(await client.GetStringAsync(manifestURL, token)) as JsonObject;
#else
            JsonObject? jsonObject = JsonNode.Parse(await client.GetStringAsync(manifestURL)) as JsonObject;
            if (token.IsCancellationRequested)
                return false;
#endif
            if (watcher.IsStopRequested || jsonObject is null || !jsonObject.TryGetPropertyValue("downloads", out JsonNode? node))
                return false;
            jsonObject = node as JsonObject;
            if (jsonObject is null || !jsonObject.TryGetPropertyValue("server", out node))
                return false;
            jsonObject = node as JsonObject;
            if (jsonObject is null || !jsonObject.TryGetPropertyValue("url", out node))
                return false;
            byte[]? sha1;
            if (WTCore.CheckFileHashIfExist && jsonObject.TryGetPropertyValue("sha1", out JsonNode? sha1Node))
                sha1 = HashHelper.HexStringToByte(ObjectUtils.ThrowIfNull(sha1Node).ToString());
            else
                sha1 = null;
            watcher.Dispose();
            return FileDownloadHelper.AddTask(task: task, webClient: client, downloadUrl: ObjectUtils.ThrowIfNull(node).ToString(),
                filename: Path.Combine(ServerDirectory, @"minecraft_server." + versionInfo.Id + ".jar"),
                hash: sha1, hashMethod: HashHelper.HashMethod.SHA1).HasValue;
        }

        /// <inheritdoc/>
        public override string GetReadableVersion() => _version;

        /// <inheritdoc/>
        public override IPropertyFile[] GetServerPropertyFiles() => propertyFilesLazy.Value;

        /// <inheritdoc/>
        private IPropertyFile[] GetServerPropertyFilesCore()
        {
            string directory = ServerDirectory;
            return new IPropertyFile[1]
            {
                new JavaPropertyFile(Path.Combine(directory, "./server.properties")),
            };
        }

        /// <inheritdoc/>
        protected override MojangAPI.VersionInfo? BuildVersionInfo() => FindVersionInfo(_version);

        /// <inheritdoc/>
        protected override bool CreateServerCore() => true;

        /// <inheritdoc/>
        protected override bool LoadServerCore(JsonPropertyFile serverInfoJson)
        {
            string? version = (serverInfoJson["version"] as JsonValue)?.GetValue<string>();
            if (version is null || version.Length <= 0)
                return false;
            _version = version;
            return base.LoadServerCore(serverInfoJson);
        }

        /// <inheritdoc/>
        protected override bool SaveServerCore(JsonPropertyFile serverInfoJson)
        {
            serverInfoJson["version"] = _version;
            return base.SaveServerCore(serverInfoJson);
        }

        /// <inheritdoc/>
        protected override string GetServerJarPath()
            => Path.Combine(ServerDirectory, @"./minecraft_server." + GetReadableVersion() + ".jar");
    }
}
