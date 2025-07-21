using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading;

using WitherTorch.Core.Property;
using WitherTorch.Core.Runtime;
using WitherTorch.Core.Servers.Utils;

namespace WitherTorch.Core.Servers
{
    /// <summary>
    /// NeoForge 伺服器
    /// </summary>
    public partial class NeoForge : JavaEditionServerBase
    {
        private const string LegacyDownloadURL = "{0}/net/neoforged/forge/{1}/forge-{1}-installer.jar";
        private const string DownloadURL = "[0}/net/neoforged/neoforge/{1}/neoforge-{1}-installer.jar";
        private const string SoftwareId = "neoforge";

        private readonly Lazy<IPropertyFile[]> propertyFilesLazy;
        private string _minecraftVersion = string.Empty;
        private string _forgeVersion = string.Empty;

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

        private NeoForge(string serverDirectory) : base(serverDirectory)
        {
            propertyFilesLazy = new Lazy<IPropertyFile[]>(GetServerPropertyFilesCore, LazyThreadSafetyMode.ExecutionAndPublication);
        }

        /// <inheritdoc/>
        public override string ServerVersion => _minecraftVersion;

        /// <inheritdoc/>
        public override string GetSoftwareId() => SoftwareId;

        /// <summary>
        /// 取得 NeoForge 的版本號
        /// </summary>
        public string NeoForgeVersion => _forgeVersion;

        /// <inheritdoc/>
        public override InstallTask? GenerateInstallServerTask(string version) => GenerateInstallServerTask(version, string.Empty);

        /// <inheritdoc cref="GenerateInstallServerTask(string)"/>
        /// <param name="minecraftVersion">要更改的 Minecraft 版本</param>
        /// <param name="neoforgeVersion">要更改的 NeoForge 版本</param>
        public InstallTask? GenerateInstallServerTask(string minecraftVersion, string neoforgeVersion)
        {
            ForgeVersionEntry[] versions = _software.GetForgeVersionEntriesFromMinecraftVersion(minecraftVersion);
            if (versions.Length <= 0)
                return null;
            ForgeVersionEntry? targetVersion;
            if (string.IsNullOrWhiteSpace(neoforgeVersion))
                targetVersion = versions[0];
            else
                targetVersion = Array.Find(versions, val => string.Equals(val.version, neoforgeVersion));
            if (targetVersion is null)
                return null;
            return GenerateInstallServerTaskCore(minecraftVersion, targetVersion);
        }

        private InstallTask? GenerateInstallServerTaskCore(string minecraftVersion, ForgeVersionEntry selectedVersion)
        {
            return new InstallTask(this, minecraftVersion + "-" + selectedVersion.version, (task, token) =>
            {
                if (!InstallServerCore(task, minecraftVersion, selectedVersion))
                    task.OnInstallFailed();
            });
        }

        private bool InstallServerCore(InstallTask task, string minecraftVersion, ForgeVersionEntry? selectedVersion)
        {
            if (selectedVersion is null)
                return false;
            string sourceDomain = _software.AvailableSourceDomain;
            string version = selectedVersion.version;
            string versionRaw = selectedVersion.versionRaw;
            if (string.IsNullOrEmpty(sourceDomain) || string.IsNullOrEmpty(version) || string.IsNullOrEmpty(versionRaw))
                return false;
            string downloadURL;
            if (version.StartsWith(minecraftVersion.Substring(2)))
                downloadURL = string.Format(DownloadURL, sourceDomain, versionRaw);
            else //Use Legacy URL
                downloadURL = string.Format(LegacyDownloadURL, sourceDomain, versionRaw);
            string installerLocation = Path.Combine(ServerDirectory, $"neoforge-{versionRaw}-installer.jar");
            int? id = FileDownloadHelper.AddTask(task: task,
                downloadUrl: downloadURL, filename: installerLocation,
                percentageMultiplier: 0.5);
            if (!id.HasValue)
                return false;
            void AfterDownload(object? sender, int sendingId)
            {
                if (sendingId != id.Value)
                    return;
                FileDownloadHelper.TaskFinished -= AfterDownload;
                try
                {
                    RunInstaller(task, installerLocation, minecraftVersion, version);
                }
                catch (Exception)
                {
                    task.OnInstallFailed();
                }
            }
            ;
            FileDownloadHelper.TaskFinished += AfterDownload;
            return true;
        }

        private void RunInstaller(InstallTask task, string jarPath, string minecraftVersion, string forgeVersion)
        {
            ProcessStatus installStatus = new ProcessStatus(50);
            task.ChangeStatus(installStatus);
            task.ChangePercentage(50);
            JavaRuntimeEnvironment environment = RuntimeEnvironment.JavaDefault;
            ProcessStartInfo startInfo = new ProcessStartInfo
            {
                FileName = environment.JavaPath,
                Arguments = string.Format("-Xms512M -Dsun.stdout.encoding=UTF8 -Dsun.stderr.encoding=UTF8 -jar \"{0}\" nogui --installServer", jarPath),
                WorkingDirectory = ServerDirectory,
                CreateNoWindow = true,
                ErrorDialog = true,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8
            };
            Process? innerProcess = Process.Start(startInfo);
            if (innerProcess is null)
            {
                task.OnInstallFailed();
                return;
            }
            void StopRequestedHandler(object? sender, EventArgs e)
            {
                try
                {
                    innerProcess.Kill();
                    innerProcess.Dispose();
                }
                catch (Exception)
                {
                }
                task.StopRequested -= StopRequestedHandler;
            }
            task.StopRequested += StopRequestedHandler;
            innerProcess.EnableRaisingEvents = true;
            innerProcess.BeginOutputReadLine();
            innerProcess.BeginErrorReadLine();
            innerProcess.OutputDataReceived += installStatus.OnProcessMessageReceived;
            innerProcess.ErrorDataReceived += installStatus.OnProcessMessageReceived;
            innerProcess.Exited += (sender, e) =>
            {
                task.StopRequested -= StopRequestedHandler;
                _minecraftVersion = minecraftVersion;
                _forgeVersion = forgeVersion;
                _versionInfo = null;
                OnServerVersionChanged();
                task.OnInstallFinished();
                task.ChangePercentage(100);
                innerProcess.Dispose();
            };
        }

        /// <inheritdoc/>
        public override string GetReadableVersion()
        {
            return SoftwareUtils.GetReadableVersionString(_minecraftVersion, _forgeVersion);
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
                new JavaPropertyFile(Path.Combine(directory, "./server.properties")),
            };
        }

        private string GetFullVersionString()
        {
            ForgeVersionEntry[] versions = _software.GetForgeVersionEntriesFromMinecraftVersion(_minecraftVersion);
            if (versions.Length <= 0)
                return string.Empty;
            string forgeVersion = _forgeVersion;
            ForgeVersionEntry? versionData = Array.Find(versions, val => string.Equals(val.version, forgeVersion));
            if (versionData is null)
                return string.Empty;
            return versionData.versionRaw;
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
            string? minecraftVersion = serverInfoJson["version"]?.GetValue<string>();
            if (minecraftVersion is null)
                return false;
            string? forgeVersion = serverInfoJson["forge-version"]?.GetValue<string>();
            if (forgeVersion is null)
                return false;
            _minecraftVersion = minecraftVersion;
            _forgeVersion = forgeVersion;
            return base.LoadServerCore(serverInfoJson);
        }

        /// <inheritdoc/>
        protected override bool SaveServerCore(JsonPropertyFile serverInfoJson)
        {
            serverInfoJson["version"] = _minecraftVersion;
            serverInfoJson["forge-version"] = _forgeVersion;
            return base.SaveServerCore(serverInfoJson);
        }

        /// <inheritdoc/>
        protected override bool TryPrepareProcessStartInfoCore(JavaRuntimeEnvironment environment, out LocalProcessStartInfo startInfo)
        {
            string fullVersionString = GetFullVersionString();
            string? path = GetPossibleForgePaths(fullVersionString)
                .Where(File.Exists)
                .FirstOrDefault();
            if (path is null)
            {
                if (fullVersionString.StartsWith(_minecraftVersion.Substring(2)))
                    path = "@libraries/net/neoforged/neoforge/" + fullVersionString;
                else
                    path = "@libraries/net/neoforged/forge/" + fullVersionString;
                return TryPrepareProcessStartInfoForArgFile(environment, path, out startInfo);
            }
            startInfo = new LocalProcessStartInfo(
                fileName: environment.JavaPath ?? "java",
                arguments: string.Format(
                    "-Djline.terminal=jline.UnsupportedTerminal -Dfile.encoding=UTF8 -Dsun.stdout.encoding=UTF8 -Dsun.stderr.encoding=UTF8 {0} -jar \"{1}\" {2}",
                    environment.JavaPreArguments ?? string.Empty,
                    path,
                    environment.JavaPostArguments ?? string.Empty),
                workingDirectory: ServerDirectory);
            return true;
        }

        private bool TryPrepareProcessStartInfoForArgFile(JavaRuntimeEnvironment environment, string path, out LocalProcessStartInfo startInfo)
        {
#if NET5_0_OR_GREATER
                if (OperatingSystem.IsWindows())
                {
                    path += "/win_args.txt";
                }
                else if (OperatingSystem.IsLinux())
                {
                    path += "/unix_args.txt";
                }
                else
                {
                    switch (Environment.OSVersion.Platform)
                    {
                        case PlatformID.Unix:
                            path += "/unix_args.txt";
                            break;
                    }
                }
#else
            switch (Environment.OSVersion.Platform)
            {
                case PlatformID.Win32NT:
                    path += "/win_args.txt";
                    break;
                case PlatformID.Unix:
                    path += "/unix_args.txt";
                    break;
            }
#endif
            startInfo = new LocalProcessStartInfo(
                fileName: environment.JavaPath ?? "java",
                arguments: string.Format(
                    "-Djline.terminal=jline.UnsupportedTerminal -Dfile.encoding=UTF8 -Dsun.stdout.encoding=UTF8 -Dsun.stderr.encoding=UTF8 {0} {1} {2}",
                    environment.JavaPreArguments ?? string.Empty,
                    path,
                    environment.JavaPostArguments ?? string.Empty),
                workingDirectory: ServerDirectory);
            return true;
        }

        private IEnumerable<string> GetPossibleForgePaths(string fullVersionString)
        {
            string serverDir = ServerDirectory;
            yield return Path.Combine(serverDir, "./neoforge-" + fullVersionString + "-universal.jar");
            yield return Path.Combine(serverDir, "./neoforge-" + fullVersionString + ".jar");
            yield return Path.Combine(serverDir, "./forge-" + fullVersionString + "-universal.jar");
            yield return Path.Combine(serverDir, "./forge-" + fullVersionString + ".jar");
        }

        /// <inheritdoc/>
        protected override string GetServerJarPath()
        {
            return GetPossibleForgePaths(GetFullVersionString())
                  .TakeWhile(File.Exists)
                  .FirstOrDefault() ?? string.Empty;
        }
    }
}
