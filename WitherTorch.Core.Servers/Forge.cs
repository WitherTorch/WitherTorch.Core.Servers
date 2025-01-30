using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;

using WitherTorch.Core.Servers.Utils;
using WitherTorch.Core.Property;

using System.Runtime.CompilerServices;
using System.Linq;

namespace WitherTorch.Core.Servers
{
    /// <summary>
    /// Forge 伺服器
    /// </summary>
    public partial class Forge : JavaEditionServerBase
    {
        private const string DownloadURLPrefix = "https://maven.minecraftforge.net/net/minecraftforge/forge/";
        private const string SoftwareId = "forge";

        private static readonly ThreadLocal<StringBuilder> localStringBuilder = new ThreadLocal<StringBuilder>(() => new StringBuilder(), false);
        private static readonly Lazy<MojangAPI.VersionInfo?> mc1_3_2 = new Lazy<MojangAPI.VersionInfo?>(
            () => (MojangAPI.VersionDictionary?.TryGetValue("1.3.2", out MojangAPI.VersionInfo? result) ?? false) ? result : null,
            LazyThreadSafetyMode.PublicationOnly);
        private static readonly Lazy<MojangAPI.VersionInfo?> mc1_5_2 = new Lazy<MojangAPI.VersionInfo?>(
            () => (MojangAPI.VersionDictionary?.TryGetValue("1.3.2", out MojangAPI.VersionInfo? result) ?? false) ? result : null,
            LazyThreadSafetyMode.PublicationOnly);

        private string _minecraftVersion = string.Empty;
        private string _forgeVersion = string.Empty;

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

        private Forge(string serverDirectory) : base(serverDirectory)
        {
            propertyFilesLazy = new Lazy<IPropertyFile[]>(GetServerPropertyFilesCore, LazyThreadSafetyMode.ExecutionAndPublication);
        }

        /// <inheritdoc/>
        public override string ServerVersion => _minecraftVersion;

        /// <inheritdoc/>
        public override string GetSoftwareId() => SoftwareId;

        /// <summary>
        /// 取得 Forge 的版本號
        /// </summary>
        public string ForgeVersion => _forgeVersion;

        /// <inheritdoc/>
        public override InstallTask? GenerateInstallServerTask(string version) => GenerateInstallServerTask(version, string.Empty);

        /// <inheritdoc cref="GenerateInstallServerTask(string)"/>
        /// <param name="minecraftVersion">要更改的 Minecraft 版本</param>
        /// <param name="forgeVersion">要更改的 Forge 版本</param>
        public InstallTask? GenerateInstallServerTask(string minecraftVersion, string forgeVersion)
        {
            ForgeVersionEntry[] versions = _software.GetForgeVersionEntriesFromMinecraftVersion(minecraftVersion);
            if (versions.Length <= 0)
                return null;
            ForgeVersionEntry? targetVersion;
            if (string.IsNullOrWhiteSpace(forgeVersion))
                targetVersion = versions[0];
            else
                targetVersion = Array.Find(versions, val => string.Equals(val.version, forgeVersion));
            if (targetVersion is null)
                return null;
            return GenerateInstallServerTaskCore(minecraftVersion, targetVersion);
        }

        private InstallTask? GenerateInstallServerTaskCore(string minecraftVersion, ForgeVersionEntry selectedVersion)
        {
            return new InstallTask(this, minecraftVersion + "-" + selectedVersion.version, task =>
            {
                if (!InstallServerCore(task, minecraftVersion, selectedVersion))
                    task.OnInstallFailed();
            });
        }

        private bool InstallServerCore(InstallTask task, string minecraftVersion, ForgeVersionEntry selectedVersion)
        {
            if (selectedVersion is null)
                return false;
            string version = selectedVersion.version;
            if (string.IsNullOrEmpty(version))
                return false;
            string versionRaw = selectedVersion.versionRaw;
            if (string.IsNullOrEmpty(versionRaw))
                return false;

            bool needInstall;
            string downloadURL;
            StringBuilder URLBuilder = ObjectUtils.ThrowIfNull(localStringBuilder.Value);
            URLBuilder.Append(DownloadURLPrefix);
            MojangAPI.VersionInfo? info = FindVersionInfo(minecraftVersion);
            if (info is null)
                return false;
            MojangAPI.VersionInfo? mc1_3_2 = Forge.mc1_3_2.Value;
            MojangAPI.VersionInfo? mc1_5_2 = Forge.mc1_5_2.Value;
            if (mc1_3_2 is null || mc1_5_2 is null)
                return false;
            if (info < mc1_3_2) // 1.1~1.2 > Download Server Zip (i don't know why forge use zip...)
            {
                URLBuilder.AppendFormat("{0}/forge-{0}-server.zip", versionRaw);
                downloadURL = URLBuilder.ToString();
                needInstall = false;
            }
            else
            {
                if (info < mc1_5_2) // 1.3.2~1.5.1 > Download Universal Zip (i don't know why forge use zip...)
                {
                    URLBuilder.AppendFormat("{0}/forge-{0}-universal.zip", versionRaw);
                    downloadURL = URLBuilder.ToString();
                    needInstall = false;
                }
                else  // 1.5.2 or above > Download Installer (*.jar)
                {
                    URLBuilder.AppendFormat("{0}/forge-{0}-installer.jar", versionRaw);
                    downloadURL = URLBuilder.ToString();
                    needInstall = true;
                }
            }
            URLBuilder.Clear();
            string installerLocation = needInstall ? $"forge-{versionRaw}-installer.jar" : $"forge-{versionRaw}.jar";
            installerLocation = Path.Combine(ServerDirectory, installerLocation);
            int? id = FileDownloadHelper.AddTask(task: task,
                downloadUrl: downloadURL, filename: installerLocation,
                percentageMultiplier: needInstall ? 0.5 : 1.0);
            if (id.HasValue)
            {
                if (needInstall)
                {
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
                    };
                    FileDownloadHelper.TaskFinished += AfterDownload;
                }
                return true;
            }
            return false;
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
            innerProcess.OutputDataReceived += (sender, e) =>
            {
                installStatus.OnProcessMessageReceived(sender, e);
            };
            innerProcess.ErrorDataReceived += (sender, e) =>
            {
                installStatus.OnProcessMessageReceived(sender, e);
            };
            innerProcess.Exited += (sender, e) =>
            {
                task.StopRequested -= StopRequestedHandler;
                _minecraftVersion = minecraftVersion;
                _forgeVersion = forgeVersion;
                _versionInfo = null;
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

        /// <inheritdoc/>
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
        protected override ProcessStartInfo? PrepareProcessStartInfoCore(JavaRuntimeEnvironment environment)
        {
            string fullVersionString = GetFullVersionString();
            string? path = GetPossibleForgePaths(fullVersionString)
                .Where(File.Exists)
                .FirstOrDefault();
            if (path is null)
                return PrepareProcessStartInfoForArgFile(environment, "@libraries/net/minecraftforge/forge/" + fullVersionString);
            return new ProcessStartInfo
            {
                FileName = environment.JavaPath ?? "java",
                Arguments = string.Format(
                    "-Djline.terminal=jline.UnsupportedTerminal -Dfile.encoding=UTF8 -Dsun.stdout.encoding=UTF8 -Dsun.stderr.encoding=UTF8 {0} -jar \"{1}\" {2}",
                    environment.JavaPreArguments ?? string.Empty,
                    path,
                    environment.JavaPostArguments ?? string.Empty
                ),
                WorkingDirectory = ServerDirectory,
                CreateNoWindow = true,
                ErrorDialog = true,
                UseShellExecute = false,
            };
        }

        private ProcessStartInfo? PrepareProcessStartInfoForArgFile(JavaRuntimeEnvironment environment, string path)
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
            return new ProcessStartInfo
            {
                FileName = environment.JavaPath ?? "java",
                Arguments = string.Format(
                    "-Dfile.encoding=UTF8 -Dsun.stdout.encoding=UTF8 -Dsun.stderr.encoding=UTF8 {0} {1} {2}",
                    environment.JavaPreArguments ?? string.Empty,
                    path,
                    environment.JavaPostArguments ?? string.Empty
                ),
                WorkingDirectory = ServerDirectory,
                CreateNoWindow = true,
                ErrorDialog = true,
                UseShellExecute = false,
            };
        }

        private IEnumerable<string> GetPossibleForgePaths(string fullVersionString)
        {
            string serverDirectory = ServerDirectory;
            yield return Path.Combine(serverDirectory, "./forge-" + fullVersionString + "-universal.jar");
            yield return Path.Combine(serverDirectory, "./forge-" + fullVersionString + ".jar");
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
