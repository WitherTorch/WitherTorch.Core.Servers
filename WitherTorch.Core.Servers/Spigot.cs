using System.IO;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;

using WitherTorch.Core.Property;
using WitherTorch.Core.Servers.Utils;

namespace WitherTorch.Core.Servers
{
    /// <summary>
    /// Spigot 伺服器
    /// </summary>
    public sealed partial class Spigot : SpigotServerBase
    {
        private const string SoftwareId = "spigot";

        /// <summary>
        /// 取得伺服器的 spigot.yml 設定檔案
        /// </summary>
        public YamlPropertyFile SpigotYMLFile
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => (YamlPropertyFile)_propertyFilesLazy.Value[2];
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            private set
            {
                IPropertyFile[] propertyFiles = _propertyFilesLazy.Value;
                IPropertyFile propertyFile = propertyFiles[2];
                propertyFiles[2] = value;
                propertyFile.Dispose();
            }
        }

        private Spigot(string serverDirectory) : base(serverDirectory) { }

        /// <inheritdoc/>
        public override string GetSoftwareId() => SoftwareId;

        /// <inheritdoc/>
        protected override ValueTask<bool> RunInstallServerTaskAsync(InstallTask task, CancellationToken token)
            => RunInstallServerTaskAsync(task, SpigotBuildTools.BuildTarget.Spigot, token);

        /// <inheritdoc/>
        protected override IPropertyFile[] GetServerPropertyFilesCore()
        {
            string directory = ServerDirectory;
            IPropertyFile[] result = new IPropertyFile[3]
            {
                new JavaPropertyFile(Path.Combine(directory, "./server.properties")),
                new YamlPropertyFile(Path.Combine(directory, "./bukkit.yml")),
                new YamlPropertyFile(Path.Combine(directory, "./spigot.yml"))
            };
            return result;
        }

        /// <inheritdoc/>
        protected override string GetServerJarPath()
            => Path.Combine(ServerDirectory, @"./spigot-" + GetReadableVersion() + ".jar");
    }
}
