using System;
using System.IO;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;

using WitherTorch.Core.Property;
using WitherTorch.Core.Runtime;
using WitherTorch.Core.Servers.Utils;

namespace WitherTorch.Core.Servers
{
    /// <summary>
    /// PowerNukkit 伺服器
    /// </summary>
    public sealed partial class PowerNukkit : JavaServerBase
    {
        private const string DownloadURL = "https://repo1.maven.org/maven2/org/powernukkit/powernukkit/{0}/powernukkit-{0}-shaded.jar";
        private const string SoftwareId = "powerNukkit";

        private readonly Lazy<IPropertyFile[]> propertyFilesLazy;
        private string _version = string.Empty;

        /// <summary>
        /// 取得伺服器的 nukkit.yml 設定檔案
        /// </summary>
        public YamlPropertyFile NukkitYMLFile
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => (YamlPropertyFile)propertyFilesLazy.Value[0];
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

        private PowerNukkit(string serverDirectory) : base(serverDirectory)
        {
            propertyFilesLazy = new Lazy<IPropertyFile[]>(GetServerPropertyFilesCore, LazyThreadSafetyMode.ExecutionAndPublication);
        }

        /// <inheritdoc/>
        public override InstallTask? GenerateInstallServerTask(string version)
        {
            if (string.IsNullOrEmpty(version))
                return null;
            return new InstallTask(this, version, StartInstallServerTaskAsync);
        }

        private async ValueTask<bool> StartInstallServerTaskAsync(InstallTask task, CancellationToken token)
        {
            string version = task.Version;
            string? fullVersionString = await _software.QueryFullVersionStringAsync(version);
            if (fullVersionString is null || !await FileDownloadHelper.DownloadFileAsync(task,
                sourceAddress: string.Format(DownloadURL, fullVersionString),
                targetFilename: Path.GetFullPath(Path.Combine(ServerDirectory, $"powernukkit-{version}.jar")),
                cancellationToken: token))
                return false;
            _version = version;
            Thread.MemoryBarrier();
            OnServerVersionChanged();
            return true;
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
            return new IPropertyFile[1]
            {
                new JavaPropertyFile(Path.Combine(directory, "./nukkit.yml")),
            };
        }

        /// <inheritdoc/>
        protected override void StopServerCore(ILocalProcess process, bool force)
        {
            if (force)
            {
                process.Stop();
                return;
            }
            process.InputCommand("stop");
        }

        /// <inheritdoc/>
        protected override bool CreateServerCore() => true;

        /// <inheritdoc/>
        protected override bool LoadServerCore(JsonPropertyFile serverInfoJson)
        {
            if (!base.LoadServerCore(serverInfoJson))
                return false;
            string? version = serverInfoJson["version"]?.GetValue<string>();
            if (version is null || version.Length <= 0)
                return false;
            _version = version;
            return true;
        }

        /// <inheritdoc/>
        protected override bool SaveServerCore(JsonPropertyFile serverInfoJson)
        {
            if (!base.SaveServerCore(serverInfoJson))
                return false;
            serverInfoJson["version"] = _version;
            return true;
        }

        /// <inheritdoc/>
        protected override string GetServerJarPath()
            => Path.Combine(ServerDirectory, "./powernukkit-" + GetReadableVersion() + ".jar");
    }
}
