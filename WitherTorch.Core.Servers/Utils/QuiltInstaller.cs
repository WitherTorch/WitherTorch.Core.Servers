using System;
using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Xml;

using WitherTorch.Core.Runtime;
using WitherTorch.Core.Utils;

using static WitherTorch.Core.Utils.WebClient2;

namespace WitherTorch.Core.Servers.Utils
{
    internal static class QuiltInstaller
    {
        private const string ManifestListURL = "https://maven.quiltmc.org/repository/release/org/quiltmc/quilt-installer/maven-metadata.xml";
        private const string DownloadURL = "https://maven.quiltmc.org/repository/release/org/quiltmc/quilt-installer/{0}/quilt-installer-{0}.jar";

        private static readonly string _installerDirectoryPath = Path.GetFullPath(Path.Combine(Environment.CurrentDirectory, WTServer.QuiltInstallerPath));
        private static readonly string _installerFilePath = Path.GetFullPath(Path.Combine(_installerDirectoryPath, "./quilt-installer.jar"));
        private static readonly string _installerVersionDataPath = Path.GetFullPath(Path.Combine(_installerDirectoryPath, "./quilt-installer.version"));

        private static async ValueTask<string?> CheckUpdateAsync(CancellationToken token)
        {
            string? currentVersion = null;
            string directoryPath = _installerDirectoryPath;
            if (Directory.Exists(directoryPath))
            {
                string versionDataPath = _installerVersionDataPath;
                if (File.Exists(versionDataPath) && File.Exists(_installerFilePath))
                {
                    using StreamReader reader = new StreamReader(versionDataPath, Encoding.UTF8, detectEncodingFromByteOrderMarks: false);
                    string? line
#if NET8_0_OR_GREATER
                        = await reader.ReadLineAsync(token).ConfigureAwait(continueOnCapturedContext: false);
#else
                        = await reader.ReadLineAsync().ConfigureAwait(continueOnCapturedContext: false);
#endif
                    if (token.IsCancellationRequested)
                        return null;
                    currentVersion = line;
                }
            }
            else
            {
                Directory.CreateDirectory(directoryPath);
            }

            {
                XmlDocument manifestXML = new XmlDocument();
                using (WebClient2 client = new WebClient2())
                {
                    client.DefaultRequestHeaders.Add("User-Agent", Constants.UserAgent);
                    manifestXML.LoadXml(await client.DownloadStringTaskAsync(ManifestListURL).ConfigureAwait(continueOnCapturedContext: false));
                }
                string? versionString = manifestXML.SelectSingleNode("//metadata/versioning/latest")?.InnerText;
                if (versionString is null)
                    return currentVersion;
                return versionString.Equals(currentVersion, StringComparison.Ordinal) ? null : versionString;
            }
        }

        private static async ValueTask<bool> UpdateAsync(InstallTask task, string installerVersion, CancellationToken token)
        {
            using WebClient2 client = new WebClient2();
            using InstallTaskWatcher<bool> watcher = new InstallTaskWatcher<bool>(task, client, token);

            client.DefaultRequestHeaders.Add("User-Agent", Constants.UserAgent);
            client.DownloadProgressChanged += UpdateAsync_DownloadProgressChanged;
            client.DownloadFileCompleted += UpdateAsync_DownloadFileCompleted;
            client.DownloadFileAsync(new Uri(string.Format(DownloadURL, installerVersion)), _installerFilePath, watcher);
            if (!await watcher.WaitUtilFinishedAsync() || token.IsCancellationRequested)
                return false;

            using StreamWriter writer = new StreamWriter(_installerVersionDataPath, append: false, encoding: Encoding.UTF8);
            await writer.WriteLineAsync(installerVersion).ConfigureAwait(continueOnCapturedContext: false);
#if NET8_0_OR_GREATER
            await writer.FlushAsync(token).ConfigureAwait(continueOnCapturedContext: false);
#else
            await writer.FlushAsync().ConfigureAwait(continueOnCapturedContext: false);
#endif
            writer.Close();
            return true;
        }

        private static void UpdateAsync_DownloadProgressChanged(object? sender, DownloadProgressChangedEventArgs e)
        {
            if (e.UserState is not InstallTaskWatcher<bool> watcher)
                return;
            InstallTask task = watcher.Task;
            if (task.Status is not QuiltInstallerStatus status || status.State != SpigotBuildToolsStatus.ToolState.Update)
                return;
            double percentage = e.ProgressPercentage;
            status.Percentage = percentage;
            task.ChangePercentage(percentage * 0.5);
        }

        private static void UpdateAsync_DownloadFileCompleted(object? sender, AsyncCompletedEventArgs e)
        {
            if (sender is not WebClient2 client || e.UserState is not InstallTaskWatcher<bool> watcher)
                return;
            client.DownloadProgressChanged -= UpdateAsync_DownloadProgressChanged;
            client.DownloadFileCompleted -= UpdateAsync_DownloadFileCompleted;
            watcher.MarkAsFinished(!e.Cancelled && e.Error is null);
        }

        public static async ValueTask<bool> InstallAsync(InstallTask task, string minecraftVersion, string quiltLoaderVersion, CancellationToken token)
        {
            if (token.IsCancellationRequested)
                return false;
            QuiltInstallerStatus status = new QuiltInstallerStatus(SpigotBuildToolsStatus.ToolState.Initialize, 0);
            task.ChangeStatus(status);
            string? newInstallerVersion = await CheckUpdateAsync(token);
            if (newInstallerVersion is not null)
            {
                status.State = SpigotBuildToolsStatus.ToolState.Update;
                status.Percentage = 0;
                if (!await UpdateAsync(task, newInstallerVersion, token))
                    return false;
                status.Percentage = 100;
            }
            else if (token.IsCancellationRequested)
                return false;
            task.ChangePercentage(50);
            task.OnStatusChanged();
            if (!await RunInstallerAsync(task, status, minecraftVersion, quiltLoaderVersion, token))
                return false;
            task.ChangePercentage(100);
            return true;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static ValueTask<bool> RunInstallerAsync(InstallTask task, QuiltInstallerStatus status, string minecraftVersion, string quiltLoaderVersion,
            CancellationToken token)
        {
            status.State = SpigotBuildToolsStatus.ToolState.Build;
            return ProcessHelper.RunProcessAsync(task, status, BuildInstallerStartInfo(task, minecraftVersion, quiltLoaderVersion), token);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static LocalProcessStartInfo BuildInstallerStartInfo(InstallTask task, string minecraftVersion, string quiltLoaderVersion)
            => new LocalProcessStartInfo(
                fileName: RuntimeEnvironment.JavaDefault.JavaPath ?? "java",
                arguments: string.Format("-Xms512M -Dsun.stdout.encoding=UTF8 -Dsun.stderr.encoding=UTF8 -jar \"{0}\" install server {1} {2} --install-dir=\\\"{3}\\\" --download-server",
                    _installerFilePath, minecraftVersion, quiltLoaderVersion, task.Owner.ServerDirectory),
                workingDirectory: _installerDirectoryPath);
    }
}
