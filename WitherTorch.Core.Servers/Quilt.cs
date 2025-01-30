using System;
using System.IO;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;

using WitherTorch.Core.Property;
using WitherTorch.Core.Servers.Utils;

namespace WitherTorch.Core.Servers
{
    /// <summary>
    /// Quilt 伺服器
    /// </summary>
    public sealed partial class Quilt : JavaEditionServerBase
    {
        private const string SoftwareId = "quilt";

        private string _minecraftVersion = string.Empty;
        private string _quiltLoaderVersion = string.Empty;

        private readonly Lazy<IPropertyFile[]> propertyFilesLazy;

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

        private Quilt(string serverDirectory) : base(serverDirectory)
        {
            propertyFilesLazy = new Lazy<IPropertyFile[]>(GetServerPropertyFilesCore, LazyThreadSafetyMode.ExecutionAndPublication);
        }

        /// <inheritdoc/>
        public override string ServerVersion => _minecraftVersion;

        /// <inheritdoc/>
        public override string GetSoftwareId() => SoftwareId;

        /// <inheritdoc/>
        public override InstallTask? GenerateInstallServerTask(string version) => GenerateInstallServerTask(version, string.Empty);

        /// <inheritdoc cref="GenerateInstallServerTask(string)"/>
        /// <param name="minecraftVersion">要更改的 Minecraft 版本</param>
        /// <param name="quiltLoaderVersion">要更改的 Quilt Loader 版本</param>
        public InstallTask? GenerateInstallServerTask(string minecraftVersion, string quiltLoaderVersion)
        {
            if (string.IsNullOrWhiteSpace(minecraftVersion))
                return null;
            if (string.IsNullOrWhiteSpace(quiltLoaderVersion))
            {
                quiltLoaderVersion = _software.GetLatestStableQuiltLoaderVersion();
                if (string.IsNullOrWhiteSpace(quiltLoaderVersion))
                    return null;
            }
            InstallTask result = new InstallTask(this, minecraftVersion + "-" + quiltLoaderVersion,
                task => QuiltInstaller.Instance.Install(task, minecraftVersion, quiltLoaderVersion));
            void onInstallFinished(object? sender, EventArgs e)
            {
                if (sender is not InstallTask senderTask || senderTask.Owner is not Quilt server)
                    return;
                senderTask.InstallFinished -= onInstallFinished;
                server._minecraftVersion = minecraftVersion;
                server._quiltLoaderVersion = quiltLoaderVersion;
                server._versionInfo = null;
                server.OnServerVersionChanged();
            }
            result.InstallFinished += onInstallFinished;
            return result;
        }

        /// <inheritdoc/>
        public override string GetReadableVersion()
        {
            return SoftwareUtils.GetReadableVersionString(_minecraftVersion, _quiltLoaderVersion);
        }

        /// <summary>
        /// 取得 Quilt Loader 的版本號
        /// </summary>
        public string QuiltLoaderVersion => _quiltLoaderVersion;

        /// <inheritdoc/>
        public override IPropertyFile[] GetServerPropertyFiles()
        {
            return propertyFilesLazy.Value;
        }

        private IPropertyFile[] GetServerPropertyFilesCore()
        {
            string directory = ServerDirectory;
            return new IPropertyFile[1]
            {
                new JavaPropertyFile(Path.Combine(directory, "./server.properties")),
            };
        }

        /// <inheritdoc/>
        protected override MojangAPI.VersionInfo? BuildVersionInfo()
        {
            return FindVersionInfo(_minecraftVersion);
        }

        /// <inheritdoc/>
        protected override bool CreateServerCore() => true;

        /// <inheritdoc/>
        protected override bool LoadServerCore(JsonPropertyFile serverInfoJson)
        {
            string? minecraftVersion = serverInfoJson["version"]?.ToString();
            if (minecraftVersion is null || minecraftVersion.Length <= 0)
                return false;
            _minecraftVersion = minecraftVersion;
            JsonNode? quiltVerNode = serverInfoJson["quilt-version"];
            if (quiltVerNode is null || quiltVerNode.GetValueKind() != JsonValueKind.String)
                return false;
            _quiltLoaderVersion = quiltVerNode.GetValue<string>();
            return base.LoadServerCore(serverInfoJson);
        }

        /// <inheritdoc/>
        protected override bool SaveServerCore(JsonPropertyFile serverInfoJson)
        {
            serverInfoJson["version"] = _minecraftVersion;
            serverInfoJson["quilt-version"] = _quiltLoaderVersion;
            return base.SaveServerCore(serverInfoJson);
        }

        /// <inheritdoc/>
        protected override string GetServerJarPath()
            => Path.Combine(ServerDirectory, "./quilt-server-launch.jar");
    }
}
