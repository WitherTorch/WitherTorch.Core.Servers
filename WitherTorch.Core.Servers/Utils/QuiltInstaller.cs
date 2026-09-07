using System;
using System.Collections.Concurrent;
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

namespace WitherTorch.Core.Servers.Utils;

internal static class QuiltInstaller
{
    private const string ManifestListURL = "https://maven.quiltmc.org/repository/release/org/quiltmc/quilt-installer/maven-metadata.xml";
    private const string DownloadURL = "https://maven.quiltmc.org/repository/release/org/quiltmc/quilt-installer/{0}/quilt-installer-{0}.jar";

    private static readonly ConcurrentDictionary<string, string> _installerFilePathDict = new(), _installerVersionDataPathDict = new();

    private static string GetInstallerFilePath(string directoryPath)
        => _installerFilePathDict.GetOrAdd(directoryPath, static path => Path.GetFullPath(Path.Combine(path, "./quilt-installer.jar")));

    private static string GetInstallerVersionDataPath(string directoryPath)
        => _installerVersionDataPathDict.GetOrAdd(directoryPath, static path => Path.GetFullPath(Path.Combine(path, "./quilt-installer.version")));

    private static async ValueTask<string?> CheckUpdateAsync(string directoryPath, CancellationToken cancellationToken)
    {
        string? currentVersion = null;
        if (Directory.Exists(directoryPath))
        {
            string versionDataPath = GetInstallerVersionDataPath(directoryPath);
            if (File.Exists(versionDataPath) && File.Exists(GetInstallerFilePath(directoryPath)))
            {
                using StreamReader reader = new StreamReader(versionDataPath, Encoding.UTF8, detectEncodingFromByteOrderMarks: false);
                string? line
#if NET8_0_OR_GREATER
                    = await reader.ReadLineAsync(cancellationToken).ConfigureAwait(continueOnCapturedContext: false);
#else
                    = await reader.ReadLineAsync().ConfigureAwait(continueOnCapturedContext: false);
#endif
                if (cancellationToken.IsCancellationRequested)
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

    private static async ValueTask<bool> UpdateAsync(InstallTask task, string directoryPath, string installerVersion, CancellationToken cancellationToken)
    {
        using WebClient2 client = new WebClient2();
        using InstallTaskWatcher<bool> watcher = new InstallTaskWatcher<bool>(task, client, cancellationToken);

        client.DefaultRequestHeaders.Add("User-Agent", Constants.UserAgent);
        client.DownloadProgressChanged += UpdateAsync_DownloadProgressChanged;
        client.DownloadFileCompleted += UpdateAsync_DownloadFileCompleted;
        client.DownloadFileAsync(new Uri(string.Format(DownloadURL, installerVersion)), GetInstallerFilePath(directoryPath), watcher);
        if (!await watcher.WaitUtilFinishedAsync() || cancellationToken.IsCancellationRequested)
            return false;

        using StreamWriter writer = new StreamWriter(GetInstallerVersionDataPath(directoryPath), append: false, encoding: Encoding.UTF8);
        await writer.WriteLineAsync(installerVersion).ConfigureAwait(continueOnCapturedContext: false);
#if NET8_0_OR_GREATER
        await writer.FlushAsync(cancellationToken).ConfigureAwait(continueOnCapturedContext: false);
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

    public static async ValueTask<bool> InstallAsync(InstallTask task, string minecraftVersion, string quiltLoaderVersion, CancellationToken cancellationToken)
    {
        if (cancellationToken.IsCancellationRequested)
            return false;
        QuiltInstallerStatus status = new QuiltInstallerStatus(SpigotBuildToolsStatus.ToolState.Initialize, 0);
        task.ChangeStatus(status);

        string directoryPath = WTServer.QuiltInstallerPath;
        string? newInstallerVersion = await CheckUpdateAsync(directoryPath, cancellationToken);
        if (newInstallerVersion is not null)
        {
            status.State = SpigotBuildToolsStatus.ToolState.Update;
            status.Percentage = 0;
            if (!await UpdateAsync(task, directoryPath, newInstallerVersion, cancellationToken))
                return false;
            status.Percentage = 100;
        }
        else if (cancellationToken.IsCancellationRequested)
            return false;
        task.ChangePercentage(50);
        task.OnStatusChanged();
        if (!await RunInstallerAsync(task, status, directoryPath, minecraftVersion, quiltLoaderVersion, cancellationToken))
            return false;
        task.ChangePercentage(100);
        return true;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static ValueTask<bool> RunInstallerAsync(InstallTask task, QuiltInstallerStatus status, string directoryPath, string minecraftVersion, string quiltLoaderVersion,
        CancellationToken token)
    {
        status.State = SpigotBuildToolsStatus.ToolState.Build;
        return ProcessHelper.RunProcessAsync(task, status, BuildInstallerStartInfo(task, directoryPath, minecraftVersion, quiltLoaderVersion), token);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static LocalProcessStartInfo BuildInstallerStartInfo(InstallTask task, string directoryPath, string minecraftVersion, string quiltLoaderVersion)
        => WTServer.InstallerProcessStartInfoFactory.Invoke(
            task: task,
            arguments: string.Format("-Xms512M -Dsun.stdout.encoding=UTF8 -Dsun.stderr.encoding=UTF8 -jar \"{0}\" install server {1} {2} --install-dir=\\\"{3}\\\" --download-server",
                GetInstallerFilePath(directoryPath), minecraftVersion, quiltLoaderVersion, task.Owner.ServerDirectory),
            workingDirectory: directoryPath);
}
