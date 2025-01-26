using System;
using System.Diagnostics;
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
    /// Spigot 伺服器
    /// </summary>
    public class Spigot : AbstractJavaEditionServer<Spigot>
    {

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

        private string _version = string.Empty;
        private int _build = -1;

        static Spigot()
        {
            CallWhenStaticInitialize();
            SoftwareId = "spigot";
        }

        public Spigot()
        {
            propertyFilesLazy = new Lazy<IPropertyFile[]>(GetServerPropertyFilesCore, LazyThreadSafetyMode.ExecutionAndPublication);
        }

        public override string ServerVersion => _version;

        public override bool ChangeVersion(int versionIndex)
        {
            return InstallSoftware(SpigotAPI.Versions[versionIndex]);
        }

        private bool InstallSoftware(string version)
        {
            try
            {
                int build = SpigotAPI.GetBuildNumber(version);
                if (build < 0)
                    return false;
                InstallSoftware(version, build);
            }
            catch (Exception)
            {
                return false;
            }
            return true;
        }

        private void InstallSoftware(string minecraftVersion, int build)
        {
            InstallTask task = new InstallTask(this, minecraftVersion);
            void onServerInstallFinished(object? sender, EventArgs e)
            {
                if (sender is not InstallTask _task)
                    return;
                _task.InstallFinished -= onServerInstallFinished;
                _version = minecraftVersion;
                _build = build;
                OnServerVersionChanged();
            }
            task.InstallFinished += onServerInstallFinished;
            OnServerInstalling(task);
            SpigotBuildTools.Instance.Install(task, SpigotBuildTools.BuildTarget.Spigot, minecraftVersion);
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
            IPropertyFile[] result = new IPropertyFile[3]
            {
                new JavaPropertyFile(Path.Combine(directory, "./server.properties")),
                new YamlPropertyFile(Path.Combine(directory, "./bukkit.yml")),
                new YamlPropertyFile(Path.Combine(directory, "./spigot.yml"))
            };
            return result;
        }

        /// <inheritdoc/>
        public override string[] GetSoftwareVersions()
        {
            return SpigotAPI.Versions;
        }

        protected override MojangAPI.VersionInfo? BuildVersionInfo()
        {
            return FindVersionInfo(_version);
        }

        /// <inheritdoc/>
        protected override bool CreateServer() => true;

        protected override bool LoadServerCore(JsonPropertyFile serverInfoJson)
        {
            string? version = serverInfoJson["version"]?.GetValue<string>();
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
            => Path.Combine(ServerDirectory, @"./spigot-" + GetReadableVersion() + ".jar");

        public override bool UpdateServer()
        {
            return InstallSoftware(_version);
        }
    }
}
