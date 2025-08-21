using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

using WitherTorch.Core.Property;
using WitherTorch.Core.Runtime;
using WitherTorch.Core.Servers.Utils;

namespace WitherTorch.Core.Servers
{
    /// <summary>
    /// Forge 伺服器
    /// </summary>
    public partial class Forge : JavaEditionServerBase
    {
        private const string DownloadURLPrefix = "{0}/net/minecraftforge/forge/";
        private const string SoftwareId = "forge";

        private static readonly Lazy<Task<MojangAPI.VersionInfo?>> mc1_3_2 = new(
            async () => (await MojangAPI.GetVersionDictionaryAsync()).TryGetValue("1.3.2", out MojangAPI.VersionInfo? result) ? result : null,
            LazyThreadSafetyMode.PublicationOnly);
        private static readonly Lazy<Task<MojangAPI.VersionInfo?>> mc1_5_2 = new(
            async () => (await MojangAPI.GetVersionDictionaryAsync()).TryGetValue("1.5.2", out MojangAPI.VersionInfo? result) ? result : null,
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
        public override InstallTask? GenerateInstallServerTask(string version)
        {
            if (string.IsNullOrEmpty(version))
                return null;
            return new InstallTask(this, version, RunInstallServerTaskAsync);
        }

        /// <inheritdoc cref="GenerateInstallServerTask(string)"/>
        /// <param name="minecraftVersion">要更改的 Minecraft 版本</param>
        /// <param name="forgeVersion">要更改的 Forge 版本</param>
        public InstallTask? GenerateInstallServerTask(string minecraftVersion, string forgeVersion)
        {
            if (string.IsNullOrEmpty(minecraftVersion) || string.IsNullOrEmpty(forgeVersion))
                return null;
            return new InstallTask(this, minecraftVersion + "-" + forgeVersion,
                (task, token) => RunInstallServerTaskAsync(task, minecraftVersion, forgeVersion, token));
        }

        private async ValueTask<bool> RunInstallServerTaskAsync(InstallTask task, CancellationToken token)
        {
            string minecraftVersion = task.Version;
            ForgeVersionEntry[] versionEntries = await _software.GetForgeVersionEntriesFromMinecraftVersionAsync(minecraftVersion);
            if (versionEntries.Length <= 0)
                return false;
            return await RunInstallServerTaskCoreAsync(task, minecraftVersion, versionEntries[0], token);
        }

        private async ValueTask<bool> RunInstallServerTaskAsync(InstallTask task, string minecraftVersion, string forgeVersion, CancellationToken token)
        {
            ForgeVersionEntry[] versionEntries = await _software.GetForgeVersionEntriesFromMinecraftVersionAsync(minecraftVersion);
            if (versionEntries.Length <= 0)
                return false;
            ForgeVersionEntry? foundVersionEntry = Array.Find(versionEntries, val => string.Equals(val.version, forgeVersion));
            if (foundVersionEntry is null)
                return false;
            return await RunInstallServerTaskCoreAsync(task, minecraftVersion, foundVersionEntry, token);
        }

        private async ValueTask<bool> RunInstallServerTaskCoreAsync(InstallTask task, string minecraftVersion, ForgeVersionEntry forgeVersionEntry, CancellationToken token)
        {
            string sourceDomain = _software.AvailableSourceDomain;
            string forgeVersion = forgeVersionEntry.version;
            string forgeVersionRaw = forgeVersionEntry.versionRaw;
            if (string.IsNullOrEmpty(sourceDomain) || string.IsNullOrEmpty(forgeVersion) || string.IsNullOrEmpty(forgeVersionRaw))
                return false;
            MojangAPI.VersionInfo? versionInfo = await FindVersionInfoAsync(minecraftVersion);
            if (versionInfo is null || token.IsCancellationRequested)
                return false;
            bool needInstall;
            string downloadURL;
            StringBuilder builder = ThreadLocalObjects.StringBuilder;
            builder.AppendFormat(DownloadURLPrefix, sourceDomain);
            MojangAPI.VersionInfo? mc1_3_2 = await Forge.mc1_3_2.Value;
            MojangAPI.VersionInfo? mc1_5_2 = await Forge.mc1_5_2.Value;
            if (mc1_3_2 is null || mc1_5_2 is null)
                return false;
            if (versionInfo < mc1_3_2) // 1.1~1.2 > Download Server Zip (i don't know why forge use zip...)
            {
                builder.AppendFormat("{0}/forge-{0}-server.zip", forgeVersionRaw);
                downloadURL = builder.ToString();
                needInstall = false;
            }
            else if (versionInfo < mc1_5_2) // 1.3.2~1.5.1 > Download Universal Zip (i don't know why forge use zip...)
            {
                builder.AppendFormat("{0}/forge-{0}-universal.zip", forgeVersionRaw);
                downloadURL = builder.ToString();
                needInstall = false;
            }
            else  // 1.5.2 or above > Download Installer (*.jar)
            {
                builder.AppendFormat("{0}/forge-{0}-installer.jar", forgeVersionRaw);
                downloadURL = builder.ToString();
                needInstall = true;
            }
            builder.Clear();
            string installerLocation = needInstall ? $"forge-{forgeVersionRaw}-installer.jar" : $"forge-{forgeVersionRaw}.jar";
            installerLocation = Path.GetFullPath(Path.Combine(ServerDirectory, installerLocation));
            if (!await FileDownloadHelper.DownloadFileAsync(task, downloadURL, installerLocation, token, percentageMultiplier: needInstall ? 0.5 : 1.0))
                return false;
            if (!needInstall)
                return true;
            if (!await RunInstallerAsync(task, installerLocation, token))
                return false;
            _minecraftVersion = minecraftVersion;
            _forgeVersion = forgeVersion;
            _versionInfo = versionInfo;
            Thread.MemoryBarrier();
            OnServerVersionChanged();
            return true;
        }

        private async ValueTask<bool> RunInstallerAsync(InstallTask task, string installerPath, CancellationToken token)
        {
            ProcessStatus status = new ProcessStatus(50);
            task.ChangeStatus(status);
            task.ChangePercentage(50);
            using InstallTaskWatcher<bool> watcher = new InstallTaskWatcher<bool>(task, null);

            void OnProcessEnded(object? sender, EventArgs args)
            {
                if (sender is not ILocalProcess process)
                    return;
                process.MessageReceived -= status.OnProcessMessageReceived;
                process.ProcessEnded -= OnProcessEnded;
                watcher.MarkAsFinished(true);
            }

            using ILocalProcess process = WTServer.LocalProcessFactory.Invoke();
            process.MessageReceived += status.OnProcessMessageReceived;
            process.ProcessEnded += OnProcessEnded;
            if (!process.Start(new LocalProcessStartInfo(
                    fileName: RuntimeEnvironment.JavaDefault.JavaPath ?? "java",
                    arguments: string.Format("-Xms512M -Dsun.stdout.encoding=UTF8 -Dsun.stderr.encoding=UTF8 -jar \"{0}\" nogui --installServer", installerPath),
                    workingDirectory: ServerDirectory
                )))
                return false;
            await watcher.WaitUtilFinishedAsync().ContinueWith(completedTask =>
            {
                process.MessageReceived -= status.OnProcessMessageReceived;
                process.ProcessEnded -= OnProcessEnded;
                if (completedTask.IsCanceled)
                    process.Stop();
            }, token);
            return !token.IsCancellationRequested;
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
            => _software.GetForgeVersionEntriesFromMinecraftVersionAsync(_minecraftVersion).Result
            .Where(val => string.Equals(val.version, _forgeVersion))
            .Select(val => val.versionRaw)
            .FirstOrDefault() ?? string.Empty;

        /// <inheritdoc/>
        protected override MojangAPI.VersionInfo? BuildVersionInfo()
        {
            return FindVersionInfoAsync(_minecraftVersion).Result;
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
                return TryPrepareProcessStartInfoForArgFile(environment, "@libraries/net/minecraftforge/forge/" + fullVersionString, out startInfo);
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
