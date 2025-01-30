﻿using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;

using WitherTorch.Core.Property;
using WitherTorch.Core.Servers.Utils;

namespace WitherTorch.Core.Servers
{
    public sealed partial class PowerNukkit : LocalServer
    {
        private const string DownloadURL = "https://repo1.maven.org/maven2/org/powernukkit/powernukkit/{0}/powernukkit-{0}-shaded.jar";
        private const string SoftwareId = "powerNukkit";

        private readonly Lazy<IPropertyFile[]> propertyFilesLazy;
        private string _version = string.Empty;
        private JavaRuntimeEnvironment? _environment;

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

        public override string ServerVersion => _version;

        public override string GetSoftwareId() => SoftwareId;

        private PowerNukkit(string serverDirectory) : base(serverDirectory)
        {
            propertyFilesLazy = new Lazy<IPropertyFile[]>(GetServerPropertyFilesCore, LazyThreadSafetyMode.ExecutionAndPublication);
        }

        public override InstallTask? GenerateInstallServerTask(string version)
        {
            if (string.IsNullOrWhiteSpace(version))
                return null;
            string fullVersionString = _software.QueryFullVersionString(version);
            if (string.IsNullOrWhiteSpace(fullVersionString))
                return null;

            InstallTask result = new InstallTask(this, version, task =>
            {
                if (!InstallServerCore(task, version, fullVersionString))
                    task.OnInstallFailed();
            });
            void onInstallFinished(object? sender, EventArgs e)
            {
                if (sender is not InstallTask senderTask || senderTask.Owner is not PowerNukkit server)
                    return;
                senderTask.InstallFinished -= onInstallFinished;
                server._version = version;
                server.OnServerVersionChanged();
            };
            result.InstallFinished += onInstallFinished;
            return result;
        }

        private bool InstallServerCore(InstallTask task, string version, string fullVersionString)
        {
            return FileDownloadHelper.AddTask(task: task,
                downloadUrl: string.Format(DownloadURL, fullVersionString),
                filename: Path.Combine(ServerDirectory, @"powernukkit-" + version + ".jar")).HasValue;
        }

        public override string GetReadableVersion()
        {
            return _version;
        }

        public override RuntimeEnvironment? GetRuntimeEnvironment()
        {
            return _environment;
        }

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

        protected override ProcessStartInfo? PrepareProcessStartInfo(RuntimeEnvironment? environment)
        {
            if (environment is JavaRuntimeEnvironment currentEnv)
                return PrepareProcessStartInfoCore(currentEnv);
            return PrepareProcessStartInfoCore(RuntimeEnvironment.JavaDefault);
        }

        private ProcessStartInfo? PrepareProcessStartInfoCore(JavaRuntimeEnvironment environment)
        {
            string serverDirectory = ServerDirectory;
            string jarPath = Path.Combine(serverDirectory, "./powernukkit-" + GetReadableVersion() + ".jar");
            if (!File.Exists(jarPath))
                return null;
            return new ProcessStartInfo
            {
                FileName = environment.JavaPath ?? "java",
                Arguments = string.Format(
                    "-Djline.terminal=jline.UnsupportedTerminal -Dfile.encoding=UTF8 -Dsun.stdout.encoding=UTF8 -Dsun.stderr.encoding=UTF8 {0} -jar \"{1}\" {2}",
                    environment.JavaPreArguments ?? string.Empty,
                    jarPath,
                    environment.JavaPostArguments ?? string.Empty
                ),
                WorkingDirectory = serverDirectory,
                CreateNoWindow = true,
                ErrorDialog = true,
                UseShellExecute = false,
            };
        }

        protected override void StopServerCore(SystemProcess process, bool force)
        {
            if (force)
            {
                process.Kill();
                return;
            }
            process.InputCommand("stop");
        }

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

        protected override bool CreateServerCore() => true;

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
