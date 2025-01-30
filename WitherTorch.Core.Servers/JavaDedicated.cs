using System;
using System.IO;
using System.Runtime.CompilerServices;
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

        public override string ServerVersion => _version;

        public override string GetSoftwareId() => SoftwareId;

        private JavaDedicated(string serverDirectory) : base(serverDirectory)
        {
            propertyFilesLazy = new Lazy<IPropertyFile[]>(GetServerPropertyFilesCore, LazyThreadSafetyMode.ExecutionAndPublication);
        }

        public override InstallTask? GenerateInstallServerTask(string version)
        {
            MojangAPI.VersionInfo? versionInfo = FindVersionInfo(version);
            if (versionInfo is null)
                return null;
            return GenerateInstallServerTaskCore(versionInfo);
        }

        private InstallTask? GenerateInstallServerTaskCore(MojangAPI.VersionInfo versionInfo)
        {
            string? id = versionInfo.Id;
            if (id is null || id.Length <= 0)
                return null;
            InstallTask result = new InstallTask(this, id, task =>
            {
                if (!InstallServerCore(task, versionInfo))
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
            };
            result.InstallFinished += onInstallFinished;
            return result;
        }

        private bool InstallServerCore(InstallTask task, MojangAPI.VersionInfo versionInfo)
        {
            string? manifestURL = versionInfo.ManifestURL;
            if (manifestURL is null || manifestURL.Length <= 0)
                return false;
            WebClient2 client = new WebClient2();
            InstallTaskWatcher watcher = new InstallTaskWatcher(task, client);
            JsonObject? jsonObject = JsonNode.Parse(client.GetStringAsync(manifestURL).Result) as JsonObject;
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

        public override string GetReadableVersion() => _version;

        public override IPropertyFile[] GetServerPropertyFiles() => propertyFilesLazy.Value;

        private IPropertyFile[] GetServerPropertyFilesCore()
        {
            string directory = ServerDirectory;
            return new IPropertyFile[1]
            {
                new JavaPropertyFile(Path.Combine(directory, "./server.properties")),
            };
        }

        protected override MojangAPI.VersionInfo? BuildVersionInfo() => FindVersionInfo(_version);

        protected override bool CreateServerCore() => true;

        protected override bool LoadServerCore(JsonPropertyFile serverInfoJson)
        {
            string? version = (serverInfoJson["version"] as JsonValue)?.GetValue<string>();
            if (version is null || version.Length <= 0)
                return false;
            _version = version;
            return base.LoadServerCore(serverInfoJson);
        }

        protected override bool SaveServerCore(JsonPropertyFile serverInfoJson)
        {
            serverInfoJson["version"] = _version;
            return base.SaveServerCore(serverInfoJson);
        }

        protected override string GetServerJarPath()
            => Path.Combine(ServerDirectory, @"./minecraft_server." + GetReadableVersion() + ".jar");
    }
}
