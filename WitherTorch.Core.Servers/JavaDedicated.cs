using System;
using System.IO;
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
            if (string.IsNullOrEmpty(version))
                return null;
            return new InstallTask(this, version, RunInstallServerTaskAsync);
        }

        private async ValueTask<bool> RunInstallServerTaskAsync(InstallTask task, CancellationToken token)
        {
            string version = task.Version;
            MojangAPI.VersionInfo? versionInfo = await FindVersionInfoAsync(version);
            if (versionInfo is null || token.IsCancellationRequested)
                return false;
            {
                string? replacingVersion = versionInfo.Id;
                if (replacingVersion is not null)
                    version = replacingVersion;
            }
            string? manifestURL = versionInfo.ManifestURL;
            if (string.IsNullOrEmpty(manifestURL))
                return false;
            using WebClient2 client = new WebClient2();
            JsonObject? jsonObject
#if NET8_0_OR_GREATER
                = JsonNode.Parse(await client.GetStringAsync(manifestURL, token)) as JsonObject;
#else
                = JsonNode.Parse(await client.GetStringAsync(manifestURL)) as JsonObject;
#endif
            if (token.IsCancellationRequested || jsonObject is null || !jsonObject.TryGetPropertyValue("downloads", out JsonNode? node))
                return false;
            jsonObject = node as JsonObject;
            if (jsonObject is null || !jsonObject.TryGetPropertyValue("server", out node))
                return false;
            jsonObject = node as JsonObject;
            if (jsonObject is null || !jsonObject.TryGetPropertyValue("url", out node) ||
                node is not JsonValue addressNode || addressNode.GetValueKind() != JsonValueKind.String)
                return false;
            byte[]? sha1 = null;
            if (WTCore.CheckFileHashIfExist && jsonObject.TryGetPropertyValue("sha1", out JsonNode? sha1Node) &&
                sha1Node is JsonValue sha1ValueNode && sha1ValueNode.GetValueKind() == JsonValueKind.String)
                sha1 = HashHelper.HexStringToByte(sha1ValueNode.GetValue<string>());
            if (!await FileDownloadHelper.DownloadFileAsync(task: task,
                sourceAddress: addressNode.GetValue<string>(),
                targetFilename: Path.GetFullPath(Path.Combine(ServerDirectory, $"minecraft_server.{version}.jar")),
                cancellationToken: token, webClient: client,
                hash: sha1, hashMethod: HashHelper.HashMethod.SHA1))
                return false;
            _version = version;
            _versionInfo = versionInfo;
            Thread.MemoryBarrier();
            OnServerVersionChanged();
            return true;
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
        protected override MojangAPI.VersionInfo? BuildVersionInfo() => FindVersionInfoAsync(_version).Result;

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
