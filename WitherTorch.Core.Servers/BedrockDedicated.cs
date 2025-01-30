using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;

using WitherTorch.Core.Software;
using WitherTorch.Core.Property;
using WitherTorch.Core.Servers.Utils;
using WitherTorch.Core.Utils;

using static WitherTorch.Core.Utils.WebClient2;

using Version = System.Version;
using YamlDotNet.Core;

namespace WitherTorch.Core.Servers
{
    /// <summary>
    /// Bedrock 原版伺服器
    /// </summary>
    public sealed partial class BedrockDedicated : LocalServer
    {
        private const string DownloadURLForLinux = "https://www.minecraft.net/bedrockdedicatedserver/bin-linux/bedrock-server-{0}.zip";
        private const string DownloadURLForWindows = "https://www.minecraft.net/bedrockdedicatedserver/bin-win/bedrock-server-{0}.zip";
        private const string SoftwareId = "bedrockDedicated";

        private string _version = string.Empty;

        private BedrockDedicated(string serverDirectory) : base(serverDirectory) { }

        public override string ServerVersion => _version;

        public override string GetSoftwareId() => SoftwareId;

        public override InstallTask? GenerateInstallServerTask(string version)
        {
            if (string.IsNullOrWhiteSpace(version))
            {
                string[] versions = _software.GetSoftwareVersions();
                if (versions.Length <= 0)
                    return null;
                version = versions[0];
                if (string.IsNullOrWhiteSpace(version))
                    return null;
            } 

            string? downloadURL = null;
#if NET5_0_OR_GREATER
            if (OperatingSystem.IsLinux())
            {
                downloadURL = string.Format(DownloadURLForLinux, version);
            }
            else if (OperatingSystem.IsWindows())
            {
                downloadURL = string.Format(DownloadURLForWindows, version);
            }
#else
            PlatformID platformID = Environment.OSVersion.Platform;
            switch (platformID)
            {
                case PlatformID.Unix:
                    downloadURL = string.Format(DownloadURLForLinux, version);
                    break;
                case PlatformID.Win32NT:
                    downloadURL = string.Format(DownloadURLForWindows, version);
                    break;
            }
#endif

            return InstallSoftware(version, downloadURL);
        }

        private InstallTask? InstallSoftware(string version, string? downloadUrl)
        {
            if (string.IsNullOrEmpty(downloadUrl))
                return null; 

            return new InstallTask(this, version, task =>
            {
                WebClient2 client = new WebClient2();
                InstallTaskWatcher watcher = new InstallTaskWatcher(task, client);
                task.ChangeStatus(new DownloadStatus(ObjectUtils.ThrowIfNull(downloadUrl), 0));
                client.DownloadProgressChanged += InstallSoftware_DownloadProgressChanged;
                client.DownloadDataCompleted += InstallSoftware_DownloadDataCompleted;
                client.DownloadDataAsync(new Uri(downloadUrl), task);
            });
        }

        private void InstallSoftware_DownloadProgressChanged(object? sender, DownloadProgressChangedEventArgs e)
        {
            if (e.UserState is not InstallTask task || task.Status is not DownloadStatus status)
                return;

            double percentage = e.ProgressPercentage;
            status.Percentage = percentage;
            task.ChangePercentage(percentage * 0.5);
        }

        private void InstallSoftware_DownloadDataCompleted(object? sender, DownloadDataCompletedEventArgs e)
        {
            if (sender is not WebClient2 client)
                return;
            client.DownloadProgressChanged -= InstallSoftware_DownloadProgressChanged;
            client.DownloadDataCompleted -= InstallSoftware_DownloadDataCompleted;
            if (e.UserState is not InstallTask task)
                return;
            if (task.Status is DownloadStatus downloadStatus)
            {
                downloadStatus.Percentage = 100;
            }
            task.ChangePercentage(50);
            if (e.Cancelled || e.Error is object)
            {
                task.OnInstallFailed();
                return;
            }
            client.Dispose();
            using InstallTaskWatcher watcher = new InstallTaskWatcher(task, null);
            using MemoryStream stream = new MemoryStream(ObjectUtils.ThrowIfNull(e.Result));
            try
            {
                using (ZipArchive archive = new ZipArchive(stream, ZipArchiveMode.Read, true))
                {
                    DecompessionStatus status = new DecompessionStatus();
                    task.ChangeStatus(status);
                    ReadOnlyCollection<ZipArchiveEntry> entries = archive.Entries;
                    IEnumerator<ZipArchiveEntry> enumerator = entries.GetEnumerator();
                    int currentCount = 0;
                    int count = entries.Count;
                    while (enumerator.MoveNext() && !watcher.IsStopRequested)
                    {
                        ZipArchiveEntry entry = enumerator.Current;
                        string filePath = Path.GetFullPath(Path.Combine(ServerDirectory, entry.FullName));
                        if (filePath.EndsWith(Path.DirectorySeparatorChar.ToString(), StringComparison.Ordinal)) // Is Directory
                        {
                            if (!Directory.Exists(filePath))
                                Directory.CreateDirectory(filePath);
                        }
                        else // Is File
                        {
                            switch (entry.FullName)
                            {
                                case "allowlist.json":
                                case "whitelist.json":
                                case "permissions.json":
                                case "server.properties":
                                    if (!File.Exists(filePath))
                                    {
                                        goto default;
                                    }
                                    break;
                                default:
                                    {
                                        entry.ExtractToFile(filePath, true);
                                    }
                                    break;
                            }
                        }
                        currentCount++;
                        double percentage = currentCount * 100.0 / count;
                        status.Percentage = percentage;
                        task.ChangePercentage(50.0 + percentage * 0.5);
                    }
                }
                stream.Close();
                if (watcher.IsStopRequested)
                {
                    task.OnInstallFailed();
                }
                else
                {
                    task.ChangePercentage(100);
                    _version = task.Version;
                    task.OnInstallFinished();
                }
            }
            catch (Exception)
            {
                task.OnInstallFailed();
            }
            GC.Collect(1, GCCollectionMode.Optimized, false, false);
        }

        public override string GetReadableVersion()
        {
            return _version;
        }

        public override RuntimeEnvironment? GetRuntimeEnvironment()
        {
            return null;
        }

        public override IPropertyFile[] GetServerPropertyFiles()
        {
            return Array.Empty<IPropertyFile>();
        }

        protected override ProcessStartInfo? PrepareProcessStartInfo(RuntimeEnvironment? environment)
        {
            string serverDirectory = ServerDirectory;
            string path = Path.Combine(serverDirectory, "./bedrock_server.exe");
            if (!File.Exists(path))
                return null;
            return new ProcessStartInfo
            {
                FileName = path,
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
        }

        public override InstallTask? GenerateUpdateServerTask() => GenerateInstallServerTask(string.Empty);

        protected override bool CreateServerCore() => true;

        protected override bool LoadServerCore(JsonPropertyFile serverInfoJson)
        {
            string? version = serverInfoJson["version"]?.ToString();
            if (string.IsNullOrEmpty(version))
                return false;
            _version = ObjectUtils.ThrowIfNull(version);
            return true;
        }

        protected override bool SaveServerCore(JsonPropertyFile serverInfoJson)
        {
            serverInfoJson["version"] = _version;
            return true;
        }
    }
}
