using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading;

using WitherTorch.Core.Software;
using WitherTorch.Core.Property;
using WitherTorch.Core.Servers.Utils;
using System.Threading.Tasks;

#if NET6_0_OR_GREATER
using System.Collections.Frozen;
#endif

namespace WitherTorch.Core.Servers
{
    /// <summary>
    /// NeoForge 伺服器
    /// </summary>
    public partial class NeoForge : JavaEditionServerBase
    {
        private const string LegacyDownloadURL = "https://maven.neoforged.net/releases/net/neoforged/forge/{0}/forge-{0}-installer.jar";
        private const string DownloadURL = "https://maven.neoforged.net/releases/net/neoforged/neoforge/{0}/neoforge-{0}-installer.jar";
        private const string SoftwareId = "neoforge";

        private readonly Lazy<IPropertyFile[]> propertyFilesLazy;
        private string _minecraftVersion = string.Empty;
        private string _forgeVersion = string.Empty;

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

        public override string ServerVersion => _minecraftVersion;

        public override string GetSoftwareId() => SoftwareId;

        /// <summary>
        /// 取得 NeoForge 的版本號
        /// </summary>
        public string NeoForgeVersion => _forgeVersion;

        public override InstallTask? GenerateInstallServerTask(string version) => GenerateInstallServerTask(version, string.Empty);

        /// <summary>
        /// 生成一個裝載伺服器安裝流程的 <see cref="InstallTask"/> 物件
        /// </summary>
        /// <param name="minecraftVersion">要更改的 Minecraft 版本</param>
        /// <param name="neoforgeVersion">要更改的 NeoForge 版本</param>
        /// <returns>如果成功裝載安裝流程，則為一個有效的 <see cref="InstallTask"/> 物件，否則會回傳 <see langword="null"/></returns>
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
            return InstallServerCore(minecraftVersion, targetVersion);
        }

        private InstallTask? InstallServerCore(string minecraftVersion, ForgeVersionEntry selectedVersion)
        {
            return new InstallTask(this, minecraftVersion + "-" + selectedVersion.version, task =>
            {
                if (!InstallServerCore(task, minecraftVersion, selectedVersion))
                    task.OnInstallFailed();
            });
        }

        private bool InstallServerCore(InstallTask task, string minecraftVersion, ForgeVersionEntry? selectedVersion)
        {
            if (selectedVersion is null)
                return false;
            string version = selectedVersion.version;
            if (string.IsNullOrEmpty(version))
                return false;
            string versionRaw = selectedVersion.versionRaw;
            if (string.IsNullOrEmpty(versionRaw))
                return false;
            string downloadURL;
            if (version.StartsWith(minecraftVersion.Substring(2)))
                downloadURL = string.Format(DownloadURL, versionRaw);
            else //Use Legacy URL
                downloadURL = string.Format(LegacyDownloadURL, versionRaw);
            string installerLocation = Path.Combine(ServerDirectory, $"neoforge-{versionRaw}-installer.jar");
            int? id = FileDownloadHelper.AddTask(task: task,
                downloadUrl: downloadURL, filename: installerLocation,
                percentageMultiplier: 0.5);
            if (id.HasValue)
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
                OnServerVersionChanged();
                task.OnInstallFinished();
                task.ChangePercentage(100);
                innerProcess.Dispose();
            };
        }

        public override string GetReadableVersion()
        {
            return SoftwareUtils.GetReadableVersionString(_minecraftVersion, _forgeVersion);
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

        protected override MojangAPI.VersionInfo? BuildVersionInfo()
        {
            return FindVersionInfo(_minecraftVersion);
        }

        protected override bool CreateServerCore() => true;

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

        protected override bool SaveServerCore(JsonPropertyFile serverInfoJson)
        {
            serverInfoJson["version"] = _minecraftVersion;
            serverInfoJson["forge-version"] = _forgeVersion;
            return base.SaveServerCore(serverInfoJson);
        }

        protected override ProcessStartInfo? PrepareProcessStartInfoCore(JavaRuntimeEnvironment environment)
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
                return PrepareProcessStartInfoForArgFile(environment, path);
            }
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
            string serverDir = ServerDirectory;
            yield return Path.Combine(serverDir, "./neoforge-" + fullVersionString + "-universal.jar");
            yield return Path.Combine(serverDir, "./neoforge-" + fullVersionString + ".jar");
            yield return Path.Combine(serverDir, "./forge-" + fullVersionString + "-universal.jar");
            yield return Path.Combine(serverDir, "./forge-" + fullVersionString + ".jar");
        }

        protected override string GetServerJarPath()
        {
            return GetPossibleForgePaths(GetFullVersionString())
                  .TakeWhile(File.Exists)
                  .FirstOrDefault() ?? string.Empty;
        }
    }
}
