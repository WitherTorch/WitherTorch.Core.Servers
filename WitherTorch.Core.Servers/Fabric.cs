﻿using System;
using System.IO;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;

using WitherTorch.Core.Property;
using WitherTorch.Core.Servers.Utils;

using YamlDotNet.Core;

namespace WitherTorch.Core.Servers
{
    /// <summary>
    /// Fabric 伺服器
    /// </summary>
    public partial class Fabric : JavaEditionServerBase
    {
        private const string SoftwareId = "fabric";

        private string _minecraftVersion = string.Empty;
        private string _fabricLoaderVersion = string.Empty;

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

        private Fabric(string serverDirectory) : base(serverDirectory)
        {
            propertyFilesLazy = new Lazy<IPropertyFile[]>(GetServerPropertyFilesCore, LazyThreadSafetyMode.ExecutionAndPublication);
        }

        /// <inheritdoc/>
        public override string ServerVersion => _minecraftVersion;

        /// <inheritdoc/>
        public override string GetSoftwareId() => SoftwareId;

        /// <inheritdoc/>
        public override InstallTask? GenerateInstallServerTask(string version)
        {
            if (string.IsNullOrWhiteSpace(version))
                return null;
            return new InstallTask(this, version,
                (task, token) =>
                {
                    if (token.IsCancellationRequested)
                    {
                        task.OnInstallFailed();
                        return;
                    }
                    string fabricLoaderVersion = _software.GetLatestStableFabricLoaderVersionAsync().Result;
                    if (string.IsNullOrWhiteSpace(fabricLoaderVersion) || token.IsCancellationRequested)
                    {
                        task.OnInstallFailed();
                        return;
                    }    
                    FabricInstaller.Instance.Install(task, version, fabricLoaderVersion, CallWhenInstallerFinished);
                });
        }

        /// <inheritdoc cref="GenerateInstallServerTask(string)"/>
        /// <param name="minecraftVersion">要更改的 Minecraft 版本</param>
        /// <param name="fabricLoaderVersion">要更改的 Fabric Loader 版本</param>
        public InstallTask? GenerateInstallServerTask(string minecraftVersion, string fabricLoaderVersion)
        {
            if (string.IsNullOrWhiteSpace(minecraftVersion) || string.IsNullOrWhiteSpace(fabricLoaderVersion))
                return null;
            return new InstallTask(this, minecraftVersion + "-" + fabricLoaderVersion,
                (task, token) =>
                {
                    if (token.IsCancellationRequested)
                    {
                        task.OnInstallFailed();
                        return;
                    }
                    FabricInstaller.Instance.Install(task, minecraftVersion, fabricLoaderVersion, CallWhenInstallerFinished);
                });
        }

        private void CallWhenInstallerFinished(string minecraftVersion, string fabricLoaderVersion)
        {
            _minecraftVersion = minecraftVersion;
            _fabricLoaderVersion = fabricLoaderVersion;
            _versionInfo = null;
            OnServerVersionChanged();
        }

        /// <inheritdoc/>
        public override string GetReadableVersion()
        {
            return SoftwareUtils.GetReadableVersionString(_minecraftVersion, _fabricLoaderVersion);
        }

        /// <summary>
        /// 取得 Fabric Loader 的版本號
        /// </summary>
        public string FabricLoaderVersion => _fabricLoaderVersion;

        /// <inheritdoc/>
        public override IPropertyFile[] GetServerPropertyFiles()
        {
            return propertyFilesLazy.Value;
        }

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
            JsonNode? fabricVerNode = serverInfoJson["fabric-version"];
            if (fabricVerNode is null || fabricVerNode.GetValueKind() != JsonValueKind.String)
                return false;
            _fabricLoaderVersion = fabricVerNode.GetValue<string>();
            return base.LoadServerCore(serverInfoJson);
        }

        /// <inheritdoc/>
        protected override bool SaveServerCore(JsonPropertyFile serverInfoJson)
        {
            serverInfoJson["version"] = _minecraftVersion;
            serverInfoJson["fabric-version"] = _fabricLoaderVersion;
            return base.SaveServerCore(serverInfoJson);
        }

        /// <inheritdoc/>
        protected override string GetServerJarPath()
            => Path.Combine(ServerDirectory, "./fabric-server-launch.jar");
    }
}
