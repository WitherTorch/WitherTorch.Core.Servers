﻿using System;
using System.IO;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;

using WitherTorch.Core.Property;
using WitherTorch.Core.Servers.Utils;

namespace WitherTorch.Core.Servers
{
    /// <summary>
    /// CraftBukkit 伺服器
    /// </summary>
    public partial class CraftBukkit : JavaEditionServerBase
    {
        private const string SoftwareId = "craftbukkit";

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

        private CraftBukkit(string serverDirectory) : base(serverDirectory)
        {
            propertyFilesLazy = new Lazy<IPropertyFile[]>(GetServerPropertyFilesCore, LazyThreadSafetyMode.ExecutionAndPublication);
        }

        public override string ServerVersion => _version;

        public override string GetSoftwareId() => SoftwareId;

        public override InstallTask? GenerateInstallServerTask(string version)
        {
            int build = SpigotAPI.GetBuildNumber(version);
            if (build < 0)
                return null;
            return InstallServerCore(version, build);
        }

        private InstallTask? InstallServerCore(string minecraftVersion, int build)
        {
            return new InstallTask(this, minecraftVersion, task =>
            {
                void onServerInstallFinished(object? sender, EventArgs e)
                {
                    if (sender is not InstallTask senderTask || senderTask.Owner is not CraftBukkit server)
                        return;
                    senderTask.InstallFinished -= onServerInstallFinished;
                    server._version = minecraftVersion;
                    server._build = build;
                    server.OnServerVersionChanged();
                }
                task.InstallFinished += onServerInstallFinished;
                SpigotBuildTools.Instance.Install(task, SpigotBuildTools.BuildTarget.CraftBukkit, minecraftVersion);
            });
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
            IPropertyFile[] result = new IPropertyFile[3]
            {
                new JavaPropertyFile(Path.Combine(directory, "./server.properties")),
                new YamlPropertyFile(Path.Combine(directory, "./bukkit.yml")),
                new YamlPropertyFile(Path.Combine(directory, "./spigot.yml"))
            };
            return result;
        }

        protected override MojangAPI.VersionInfo? BuildVersionInfo() => FindVersionInfo(_version);

        protected override bool CreateServerCore() => true;

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

        protected override string GetServerJarPath()
            => Path.Combine(ServerDirectory, @"./craftbukkit-" + GetReadableVersion() + ".jar");

        protected override bool SaveServerCore(JsonPropertyFile serverInfoJson)
        {
            serverInfoJson["version"] = _version;
            serverInfoJson["build"] = _build;
            return base.SaveServerCore(serverInfoJson);
        }
    }
}
