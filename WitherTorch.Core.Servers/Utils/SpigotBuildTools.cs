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
    /// <summary>
    /// 操作 Spigot 官方的建置工具 (BuildTools) 的類別，此類別無法建立實體
    /// </summary>
    public static class SpigotBuildTools
    {
        private const string ManifestListURL = "https://hub.spigotmc.org/jenkins/job/BuildTools/api/xml";
        private const string DownloadURL = "https://hub.spigotmc.org/jenkins/job/BuildTools/lastSuccessfulBuild/artifact/target/BuildTools.jar";
        private static readonly string _buildToolDirectoryPath = Path.GetFullPath(Path.Combine(Environment.CurrentDirectory, WTServer.SpigotBuildToolsPath));
        private static readonly string _buildToolFilePath = Path.GetFullPath(Path.Combine(_buildToolDirectoryPath, "./BuildTools.jar"));
        private static readonly string _buildToolVersionDataPath = Path.GetFullPath(Path.Combine(_buildToolDirectoryPath, "./BuildTools.version"));

        /// <summary>
        /// <see cref="InstallAsync(InstallTask, BuildTarget, string, CancellationToken)"/> 的建置目標
        /// </summary>
        public enum BuildTarget
        {
            /// <summary>
            /// CraftBukkit
            /// </summary>
            CraftBukkit,
            /// <summary>
            /// Spigot
            /// </summary>
            Spigot
        }

        private static async ValueTask<int?> CheckUpdateAsync(CancellationToken token)
        {
            int? currentVersion = null;
            string directoryPath = _buildToolDirectoryPath;
            if (Directory.Exists(directoryPath))
            {
                string versionDataPath = _buildToolVersionDataPath;
                if (File.Exists(versionDataPath) && File.Exists(_buildToolFilePath))
                {
                    using StreamReader reader = new StreamReader(versionDataPath, Encoding.UTF8, detectEncodingFromByteOrderMarks: false);
                    while (true)
                    {
                        string? line
#if NET8_0_OR_GREATER
                            = await reader.ReadLineAsync(token).ConfigureAwait(continueOnCapturedContext: false);
#else
                            = await reader.ReadLineAsync().ConfigureAwait(continueOnCapturedContext: false);
#endif
                        if (token.IsCancellationRequested)
                            return null;
                        if (line is null)
                            break;
                        if (int.TryParse(line, out int parsedVersion))
                        {
                            currentVersion = parsedVersion;
                            break;
                        }
                    }
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
                string? versionString = manifestXML.SelectSingleNode("//mavenModuleSet/lastSuccessfulBuild/number")?.InnerText;
                if (!int.TryParse(versionString, out int parsedVersion) || (currentVersion.HasValue && currentVersion.Value >= parsedVersion))
                    return null;
                return parsedVersion;
            }
        }

        private static async ValueTask<bool> UpdateAsync(InstallTask task, int buildToolVersion, CancellationToken token)
        {
            using WebClient2 client = new WebClient2();
            using InstallTaskWatcher<bool> watcher = new InstallTaskWatcher<bool>(task, client, token);

            client.DefaultRequestHeaders.Add("User-Agent", Constants.UserAgent);
            client.DownloadProgressChanged += UpdateAsync_DownloadProgressChanged;
            client.DownloadFileCompleted += UpdateAsync_DownloadFileCompleted;
            client.DownloadFileAsync(new Uri(DownloadURL), _buildToolFilePath, watcher);
            if (!await watcher.WaitUtilFinishedAsync() || token.IsCancellationRequested)
                return false;

            using StreamWriter writer = new StreamWriter(_buildToolVersionDataPath, append: false, encoding: Encoding.UTF8);
            await writer.WriteLineAsync(buildToolVersion.ToString()).ConfigureAwait(continueOnCapturedContext: false);
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
            if (task.Status is not SpigotBuildToolsStatus status || status.State != SpigotBuildToolsStatus.ToolState.Update)
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

        /// <summary>
        /// 啟動 <see cref="SpigotServerBase"/> 伺服器的固定安裝流程
        /// </summary>
        /// <param name="task">要用於傳輸安裝時期資訊的 <see cref="InstallTask"/> 物件</param>
        /// <param name="target">要建置的目標類型</param>
        /// <param name="minecraftVersion">要建置的目標版本</param>
        /// <param name="token">用於控制非同步操作是否取消的 <see cref="CancellationToken"/> 結構</param>
        /// <returns>一個 <see cref="ValueTask"/>，在非同步工作結束後可取得是否成功運行完整個安裝流程的結果</returns>
        public static async ValueTask<bool> InstallAsync(InstallTask task, BuildTarget target, string minecraftVersion, CancellationToken token)
        {
            if (token.IsCancellationRequested)
                return false;
            SpigotBuildToolsStatus status = new SpigotBuildToolsStatus(SpigotBuildToolsStatus.ToolState.Initialize, 0);
            task.ChangeStatus(status);
            int? newBuildToolVersion = await CheckUpdateAsync(token);
            if (newBuildToolVersion.HasValue)
            {
                status.State = SpigotBuildToolsStatus.ToolState.Update;
                status.Percentage = 0;
                if (!await UpdateAsync(task, newBuildToolVersion.Value, token))
                    return false;
                status.Percentage = 100;
            }
            else if (token.IsCancellationRequested)
                return false;
            task.ChangePercentage(50);
            task.OnStatusChanged();
            if (!await RunBuildToolAsync(task, status, target, minecraftVersion, token))
                return false;
            task.ChangePercentage(100);
            return true;
        }


        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static ValueTask<bool> RunBuildToolAsync(InstallTask task, SpigotBuildToolsStatus status, BuildTarget target,
            string minecraftVersion, CancellationToken token)
        {
            status.State = SpigotBuildToolsStatus.ToolState.Build;
            return ProcessHelper.RunProcessAsync(task, status, BuildInstallerStartInfo(task, target, minecraftVersion), token);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static LocalProcessStartInfo BuildInstallerStartInfo(InstallTask task, BuildTarget target, string minecraftVersion)
            => new LocalProcessStartInfo(
                fileName: RuntimeEnvironment.JavaDefault.JavaPath ?? "java",
                arguments: string.Format("-Xms512M -Dsun.stdout.encoding=UTF8 -Dsun.stderr.encoding=UTF8 -jar \"{0}\" --rev {1} --compile {2} --final-name {3} --output-dir \"{4}\"",
                    _buildToolFilePath, minecraftVersion,
                    GetBuildTargetStringAndFilename(target, minecraftVersion, out string targetFilename), targetFilename, task.Owner.ServerDirectory),
                workingDirectory: _buildToolDirectoryPath);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static string GetBuildTargetStringAndFilename(BuildTarget target, string minecraftVersion, out string targetFilename)
        {
            switch (target)
            {
                case BuildTarget.CraftBukkit:
                    targetFilename = $"craftbukkit-{minecraftVersion}.jar";
                    return "craftbukkit";
                case BuildTarget.Spigot:
                    targetFilename = $"spigot-{minecraftVersion}.jar";
                    return "spigot";
                default:
                    throw new InvalidEnumArgumentException(nameof(target), (int)target, typeof(BuildTarget));
            }
        }
    }
}
