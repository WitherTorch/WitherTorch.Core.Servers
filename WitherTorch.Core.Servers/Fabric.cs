﻿using System;
using System.Collections.Generic;
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
    /// Fabric 伺服器
    /// </summary>
    public class Fabric : AbstractJavaEditionServer<Fabric>
    {
        private const string manifestListURL = "https://meta.fabricmc.net/v2/versions/game";
        private const string manifestListURLForLoader = "https://meta.fabricmc.net/v2/versions/loader";
        private static readonly Lazy<string[]> _versionsLazy = new Lazy<string[]>(LoadVersionList, LazyThreadSafetyMode.ExecutionAndPublication);
        private static readonly Lazy<VersionStruct[]> _loaderVersionsLazy =
            new Lazy<VersionStruct[]>(LoadFabricLoaderVersions, LazyThreadSafetyMode.ExecutionAndPublication);
        private static readonly Lazy<string[]> _loaderVersionKeysLazy =
            new Lazy<string[]>(() =>
            {
                VersionStruct[] loaderVersions = _loaderVersionsLazy.Value;
                int length = loaderVersions.Length;
                if (length <= 0)
                    return Array.Empty<string>();
                string[] result = new string[length];
                for (int i = 0; i < length; i++)
                    result[i] = loaderVersions[i].Version;
                return result;
            }, LazyThreadSafetyMode.ExecutionAndPublication);

        private string _minecraftVersion = string.Empty;
        private string _fabricLoaderVersion = string.Empty;

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

        static Fabric()
        {
            CallWhenStaticInitialize();
            SoftwareId = "fabric";
        }

        public Fabric()
        {
            propertyFilesLazy = new Lazy<IPropertyFile[]>(GetServerPropertyFilesCore, LazyThreadSafetyMode.ExecutionAndPublication);
        }

        public override string ServerVersion => _minecraftVersion;

        private static string[] LoadVersionList()
        {
            try
            {
                return LoadVersionListInternal() ?? Array.Empty<string>();
            }
            catch (Exception)
            {
            }
            return Array.Empty<string>();
        }

        private static string[]? LoadVersionListInternal()
        {
            string? manifestString = CachedDownloadClient.Instance.DownloadString(manifestListURL);
            if (manifestString is null || manifestString.Length <= 0)
                return null;
            VersionStruct[]? array = JsonSerializer.Deserialize<VersionStruct[]>(manifestString);
            if (array is null)
                return null;
            int length = array.Length;
            if (length <= 0)
                return null;
            List<string> versionList = new List<string>(length);
            string[] versions = MojangAPI.Versions;
            for (int i = 0; i < length; i++)
            {
                string version = array[i].Version;
                if (versions.Contains(version))
                    versionList.Add(version);
            }
            Array.Sort(versions, MojangAPI.VersionComparer.Instance.Reverse());
            return versions;
        }

        private static VersionStruct[] LoadFabricLoaderVersions()
        {
            try
            {
                return LoadFabricLoaderVersionsInternal() ?? Array.Empty<VersionStruct>();
            }
            catch (Exception)
            {
            }
            return Array.Empty<VersionStruct>();
        }

        private static VersionStruct[]? LoadFabricLoaderVersionsInternal()
        {
            string? manifestString = CachedDownloadClient.Instance.DownloadString(manifestListURLForLoader);
            if (manifestString is null || manifestString.Length <= 0)
                return null;
            return JsonSerializer.Deserialize<VersionStruct[]>(manifestString);
        }

        public static string? GetLatestStableFabricLoaderVersion()
        {
            VersionStruct[] loaderVersions = _loaderVersionsLazy.Value;
            int count = loaderVersions.Length;
            for (int i = 0; i < count; i++)
            {
                VersionStruct loaderVersion = loaderVersions[i];
                if (loaderVersion.Stable)
                    return loaderVersion.Version;
            }
            return count > 0 ? loaderVersions[0].Version : null;
        }

        public override bool ChangeVersion(int versionIndex)
            => ChangeVersion(versionIndex, GetLatestStableFabricLoaderVersion());

        public bool ChangeVersion(int versionIndex, string? fabricLoaderVersion)
        {
            return InstallSoftware(_versionsLazy.Value[versionIndex], fabricLoaderVersion);
        }

        private bool InstallSoftware(string minecraftVersion)
            => InstallSoftware(minecraftVersion, GetLatestStableFabricLoaderVersion());

        private bool InstallSoftware(string? minecraftVersion, string? fabricLoaderVersion)
        {
            if (minecraftVersion is null || minecraftVersion.Length <= 0 ||
                fabricLoaderVersion is null || fabricLoaderVersion.Length <= 0)
                return false;
            InstallTask task = new InstallTask(this, minecraftVersion + "-" + fabricLoaderVersion);
            void onInstallFinished(object? sender, EventArgs e)
            {
                if (sender is not InstallTask _task)
                    return;
                task.InstallFinished -= onInstallFinished;
                _minecraftVersion = minecraftVersion;
                _fabricLoaderVersion = fabricLoaderVersion;
                mojangVersionInfo = null;
                OnServerVersionChanged();
            }
            task.InstallFinished += onInstallFinished;
            OnServerInstalling(task);
            FabricInstaller.Instance.Install(task, minecraftVersion, fabricLoaderVersion);
            return true;
        }

        public override string GetReadableVersion()
        {
            return SoftwareUtils.GetReadableVersionString(_minecraftVersion, _fabricLoaderVersion);
        }

        /// <summary>
        /// 取得 Fabric Loader 的版本號
        /// </summary>
        public string FabricLoaderVersion => _fabricLoaderVersion;

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

        public override string[] GetSoftwareVersions()
        {
            return _versionsLazy.Value;
        }

        public static string[] GetLoaderVersions()
        {
            return _loaderVersionKeysLazy.Value;
        }

        protected override MojangAPI.VersionInfo? BuildVersionInfo()
        {
            return FindVersionInfo(_minecraftVersion);
        }

        /// <inheritdoc/>
        protected override bool CreateServer() => true;

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

        protected override bool SaveServerCore(JsonPropertyFile serverInfoJson)
        {
            serverInfoJson["version"] = _minecraftVersion;
            serverInfoJson["fabric-version"] = _fabricLoaderVersion;
            return base.SaveServerCore(serverInfoJson);
        }

        protected override string GetServerJarPath()
            => Path.Combine(ServerDirectory, "./fabric-server-launch.jar");

        public override bool UpdateServer()
        {
            return InstallSoftware(_minecraftVersion);
        }
    }
}
