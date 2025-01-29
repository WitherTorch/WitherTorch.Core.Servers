using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;

using WitherTorch.Core.Software;
using WitherTorch.Core.Property;
using WitherTorch.Core.Servers.Utils;
using System.Threading.Tasks;

namespace WitherTorch.Core.Servers
{
    /// <summary>
    /// Quilt 伺服器
    /// </summary>
    public sealed partial class Quilt : JavaEditionServerBase
    {
        private const string SoftwareId = "quilt";

        private static readonly SoftwareEntryPrivate _software = new SoftwareEntryPrivate();
        public static ISoftwareEntry Software => _software;

        private string _minecraftVersion = string.Empty;
        private string _quiltLoaderVersion = string.Empty;

        private readonly Lazy<IPropertyFile[]> propertyFilesLazy;

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

        public override string ServerVersion => _minecraftVersion;

        public override string GetSoftwareId() => SoftwareId;

        public override InstallTask? GenerateInstallServerTask(string version) => GenerateInstallServerTask(version, string.Empty);

        /// <summary>
        /// 生成一個裝載伺服器安裝流程的 <see cref="InstallTask"/> 物件
        /// </summary>
        /// <param name="minecraftVersion">要更改的 Minecraft 版本</param>
        /// <param name="quiltLoaderVersion">要更改的 Quilt Loader 版本</param>
        /// <remarks>(安裝過程一般為非同步執行，伺服器軟體會呼叫 <see cref="InstallTask"/> 內的各項事件以更新目前的安裝狀態)</remarks>
        /// <returns>如果成功裝載安裝流程，則為一個有效的 <see cref="InstallTask"/> 物件，否則會回傳 <see langword="null"/></returns>
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
            return new InstallTask(this, minecraftVersion + "-" + quiltLoaderVersion, task =>
            {
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
                task.InstallFinished += onInstallFinished;
                QuiltInstaller.Instance.Install(task, minecraftVersion, quiltLoaderVersion);
            });
        }

        public override string GetReadableVersion()
        {
            return SoftwareUtils.GetReadableVersionString(_minecraftVersion, _quiltLoaderVersion);
        }

        /// <summary>
        /// 取得 Quilt Loader 的版本號
        /// </summary>
        public string QuiltLoaderVersion => _quiltLoaderVersion;

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

        protected override MojangAPI.VersionInfo? BuildVersionInfo()
        {
            return FindVersionInfo(_minecraftVersion);
        }

        protected override bool CreateServerCore() => true;

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

        protected override bool SaveServerCore(JsonPropertyFile serverInfoJson)
        {
            serverInfoJson["version"] = _minecraftVersion;
            serverInfoJson["quilt-version"] = _quiltLoaderVersion;
            return base.SaveServerCore(serverInfoJson);
        }

        protected override string GetServerJarPath()
            => Path.Combine(ServerDirectory, "./quilt-server-launch.jar");
    }
}
