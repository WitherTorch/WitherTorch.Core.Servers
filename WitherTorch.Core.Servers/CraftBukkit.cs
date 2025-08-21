using System.IO;
using System.Threading;
using System.Threading.Tasks;

using WitherTorch.Core.Property;
using WitherTorch.Core.Servers.Utils;

namespace WitherTorch.Core.Servers
{
    /// <summary>
    /// Spigot 伺服器
    /// </summary>
    public sealed partial class CraftBukkit : SpigotServerBase
    {
        private const string SoftwareId = "craftbukkit";

        private CraftBukkit(string serverDirectory) : base(serverDirectory) { }

        /// <inheritdoc/>
        public override string GetSoftwareId() => SoftwareId;

        /// <inheritdoc/>
        protected override ValueTask<bool> RunInstallServerTaskAsync(InstallTask task, CancellationToken token)
            => RunInstallServerTaskAsync(task, SpigotBuildTools.BuildTarget.CraftBukkit, token);

        /// <inheritdoc/>
        protected override IPropertyFile[] GetServerPropertyFilesCore()
        {
            string directory = ServerDirectory;
            IPropertyFile[] result = new IPropertyFile[2]
            {
                new JavaPropertyFile(Path.Combine(directory, "./server.properties")),
                new YamlPropertyFile(Path.Combine(directory, "./bukkit.yml")),
            };
            return result;
        }

        /// <inheritdoc/>
        protected override string GetServerJarPath()
            => Path.Combine(ServerDirectory, @"./craftbukkit-" + GetReadableVersion() + ".jar");
    }
}
