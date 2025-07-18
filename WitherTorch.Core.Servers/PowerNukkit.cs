﻿using System;
using System.IO;
using System.Runtime.CompilerServices;
using System.Threading;

using WitherTorch.Core.Property;
using WitherTorch.Core.Runtime;
using WitherTorch.Core.Servers.Utils;

namespace WitherTorch.Core.Servers
{
    /// <summary>
    /// PowerNukkit 伺服器
    /// </summary>
    public sealed partial class PowerNukkit : LocalServerBase
    {
        private const string DownloadURL = "https://repo1.maven.org/maven2/org/powernukkit/powernukkit/{0}/powernukkit-{0}-shaded.jar";
        private const string SoftwareId = "powerNukkit";

        private readonly Lazy<IPropertyFile[]> propertyFilesLazy;
        private string _version = string.Empty;
        private JavaRuntimeEnvironment? _environment;

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
            if (string.IsNullOrWhiteSpace(version))
                return null;
            return new InstallTask(this, version, (task, token) =>
            {
                string fullVersionString = _software.QueryFullVersionString(version);
                if (string.IsNullOrWhiteSpace(fullVersionString) || token.IsCancellationRequested)
                {
                    task.OnInstallFailed();
                    return;
                }
                if (!InstallServerCore(task, version, fullVersionString))
                    task.OnInstallFailed();
            });
        }

        private bool InstallServerCore(InstallTask task, string version, string fullVersionString)
        {
            void afterInstallFinished()
            {
                _version = version;
                OnServerVersionChanged();
            }
            return FileDownloadHelper.AddTask(task: task,
                downloadUrl: string.Format(DownloadURL, fullVersionString),
                filename: Path.Combine(ServerDirectory, @"powernukkit-" + version + ".jar"),
                afterInstalledAction: afterInstallFinished).HasValue;
        }

        /// <inheritdoc/>
        public override string GetReadableVersion()
        {
            return _version;
        }

        /// <inheritdoc/>
        public override RuntimeEnvironment? GetRuntimeEnvironment()
        {
            return _environment;
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
        protected override bool TryPrepareProcessStartInfo(RuntimeEnvironment? environment, out LocalProcessStartInfo startInfo)
            => TryPrepareProcessStartInfoCore(environment as JavaRuntimeEnvironment ?? RuntimeEnvironment.JavaDefault, out startInfo);

        private bool TryPrepareProcessStartInfoCore(JavaRuntimeEnvironment environment, out LocalProcessStartInfo startInfo)
        {
            string serverDirectory = ServerDirectory;
            string jarPath = Path.Combine(serverDirectory, "./powernukkit-" + GetReadableVersion() + ".jar");
            if (!File.Exists(jarPath))
            {
                startInfo = default;
                return false;
            }
            startInfo = new LocalProcessStartInfo(
                fileName: environment.JavaPath ?? "java",
                arguments: string.Format(
                    "-Djline.terminal=jline.UnsupportedTerminal -Dfile.encoding=UTF8 -Dsun.stdout.encoding=UTF8 -Dsun.stderr.encoding=UTF8 {0} -jar \"{1}\" {2}",
                    environment.JavaPreArguments ?? string.Empty,
                    jarPath,
                    environment.JavaPostArguments ?? string.Empty),
                workingDirectory: serverDirectory);
            return true;
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
        public override void SetRuntimeEnvironment(RuntimeEnvironment? environment)
        {
            if (environment is JavaRuntimeEnvironment runtimeEnvironment)
            {
                _environment = runtimeEnvironment;
            }
            else if (environment is null)
            {
                _environment = null;
            }
        }

        /// <inheritdoc/>
        protected override bool CreateServerCore() => true;

        /// <inheritdoc/>
        protected override bool LoadServerCore(JsonPropertyFile serverInfoJson)
        {
            string? version = serverInfoJson["version"]?.GetValue<string>();
            if (version is null || version.Length <= 0)
                return false;
            _version = version;
            string? jvmPath = serverInfoJson["java.path"]?.GetValue<string>() ?? null;
            string? jvmPreArgs = serverInfoJson["java.preArgs"]?.GetValue<string>() ?? null;
            string? jvmPostArgs = serverInfoJson["java.postArgs"]?.GetValue<string>() ?? null;
            if (jvmPath is not null || jvmPreArgs is not null || jvmPostArgs is not null)
            {
                _environment = new JavaRuntimeEnvironment(jvmPath, jvmPreArgs, jvmPostArgs);
            }
            return true;
        }

        /// <inheritdoc/>
        protected override bool SaveServerCore(JsonPropertyFile serverInfoJson)
        {
            serverInfoJson["version"] = _version;
            JavaRuntimeEnvironment? environment = _environment;
            if (environment is null)
            {
                serverInfoJson["java.path"] = null;
                serverInfoJson["java.preArgs"] = null;
                serverInfoJson["java.postArgs"] = null;
            }
            else
            {
                serverInfoJson["java.path"] = environment.JavaPath;
                serverInfoJson["java.preArgs"] = environment.JavaPreArguments;
                serverInfoJson["java.postArgs"] = environment.JavaPostArguments;
            }
            return true;
        }
    }
}
