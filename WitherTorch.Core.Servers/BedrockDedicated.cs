using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using WitherTorch.Core.Servers.Utils;
using WitherTorch.Core.Utils;
using static WitherTorch.Core.Utils.WebClient2;

namespace WitherTorch.Core.Servers
{
    /// <summary>
    /// Bedrock 原版伺服器
    /// </summary>
    public class BedrockDedicated : Server<BedrockDedicated>
    {
        private const string manifestListURL = "https://withertorch-bds-helper.vercel.app/api/latest";
        private const string downloadURLForLinux = "https://minecraft.azureedge.net/bin-linux/bedrock-server-{0}.zip";
        private const string downloadURLForWindows = "https://minecraft.azureedge.net/bin-win/bedrock-server-{0}.zip";
        protected bool _isStarted;

        protected SystemProcess process;
        private string versionString;
        private readonly IPropertyFile[] propertyFiles = new IPropertyFile[1];
        private static string[] versions;

        static BedrockDedicated()
        {
            SoftwareID = "bedrockDedicated";
        }

        private static void LoadVersionList()
        {
#if NET472
            PlatformID platformID = Environment.OSVersion.Platform;
            using (StringReader reader = new StringReader(CachedDownloadClient.Instance.DownloadString(manifestListURL)))
            {
                bool keep = true;
                while (reader.Peek() != -1 && keep)
                {
                    string line = reader.ReadLine();
                    switch (platformID)
                    {
                        case PlatformID.Unix:
                            if (line.StartsWith("linux=") && Version.TryParse(line = line.Substring(6), out _))
                            {
                                versions = new string[] { line };
                                keep = false;
                            }
                            break;
                        case PlatformID.Win32NT:
                            if (line.StartsWith("windows=") && Version.TryParse(line = line.Substring(8), out _))
                            {
                                versions = new string[] { line };
                                keep = false;
                            }
                            break;
                    }
                }
                reader.Close();
            }
#elif NET5_0
            using (StringReader reader = new StringReader(CachedDownloadClient.Instance.DownloadString(manifestListURL)))
            {
                while (reader.Peek() != -1)
                {
                    string line = reader.ReadLine();
                    if (OperatingSystem.IsLinux())
                    {
                        if (line[..6] == "linux=" && Version.TryParse(line = line[6..], out _))
                        {
                            versions = new string[] { line };
                            break;
                        }
                    }
                    else if (OperatingSystem.IsWindows())
                    {
                        if (line[..8] == "windows=" && Version.TryParse(line = line[8..], out _))
                        {
                            versions = new string[] { line };
                            break;
                        }
                    }
                }
                reader.Close();
            }
#endif
        }

        public override string ServerVersion => versionString;

        public override bool ChangeVersion(int versionIndex)
        {
            if (versions is null) LoadVersionList();
            versionString = versions[0];
            InstallSoftware();
            return true;
        }

        public void InstallSoftware()
        {
            InstallTask task = new InstallTask(this);
            OnServerInstalling(task);
            task.ChangeStatus(PreparingInstallStatus.Instance);
            string downloadURL = null;
#if NET472
            PlatformID platformID = Environment.OSVersion.Platform;
            switch (platformID)
            {
                case PlatformID.Unix:
                    downloadURL = string.Format(downloadURLForLinux, versionString);
                    break;
                case PlatformID.Win32NT:
                    downloadURL = string.Format(downloadURLForWindows, versionString);
                    break;
            }
#elif NET5_0
            if (OperatingSystem.IsLinux())
            {
                downloadURL = string.Format(downloadURLForLinux, versionString);
            }
            else if (OperatingSystem.IsWindows())
            {
                downloadURL = string.Format(downloadURLForWindows, versionString);
            }
#endif
            if (!InstallSoftware(task, downloadURL))
            {
                task.OnInstallFailed();
                return;
            }
        }

        private bool InstallSoftware(InstallTask task, string downloadUrl)
        {
            if (string.IsNullOrEmpty(downloadUrl))
                return false;
            WebClient2 client = new WebClient2();
            InstallTaskWatcher watcher = new InstallTaskWatcher(task, client);
            DownloadStatus status = new DownloadStatus(downloadUrl, 0);
            task.ChangeStatus(status);
            client.DownloadProgressChanged += InstallSoftware_DownloadProgressChanged;
            client.DownloadDataCompleted += InstallSoftware_DownloadDataCompleted;
            client.DownloadDataAsync(new Uri(downloadUrl), task);
            return true;
        }

        private void InstallSoftware_DownloadProgressChanged(object sender, DownloadProgressChangedEventArgs e)
        {
            if (!(e.UserState is InstallTask task) || !(task.Status is DownloadStatus status))
                return;

            double percentage = e.ProgressPercentage;
            status.Percentage = percentage;
            task.ChangePercentage(percentage * 0.5);
        }

        private void InstallSoftware_DownloadDataCompleted(object sender, DownloadDataCompletedEventArgs e)
        {
            if (!(sender is WebClient2 client))
                return;
            client.DownloadProgressChanged -= InstallSoftware_DownloadProgressChanged;
            client.DownloadDataCompleted -= InstallSoftware_DownloadDataCompleted;
            if (!(e.UserState is InstallTask task))
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
            using (InstallTaskWatcher watcher = new InstallTaskWatcher(task, null))
            using (MemoryStream stream = new MemoryStream(e.Result))
            {
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
                        task.OnInstallFinished();
                    }
                }
                catch (Exception)
                {
                    task.OnInstallFailed();
                }
            }
            GC.Collect(1, GCCollectionMode.Optimized, false, false);
        }

        public override AbstractProcess GetProcess()
        {
            return process;
        }

        public override string GetReadableVersion()
        {
            return versionString;
        }

        public override RuntimeEnvironment GetRuntimeEnvironment()
        {
            return null;
        }

        public override IPropertyFile[] GetServerPropertyFiles()
        {
            return propertyFiles;
        }

        public override string[] GetSoftwareVersions()
        {
            if (versions is null) LoadVersionList();
            return versions;
        }

        public override void RunServer(RuntimeEnvironment environment)
        {
            if (!_isStarted)
            {
                string path = Path.Combine(ServerDirectory, "bedrock_server.exe");
                if (File.Exists(path))
                {
                    ProcessStartInfo startInfo = new ProcessStartInfo
                    {
                        FileName = path,
                        WorkingDirectory = ServerDirectory,
                        CreateNoWindow = true,
                        ErrorDialog = true,
                        UseShellExecute = false,
                    };
                    process.StartProcess(startInfo);
                }
            }
        }

        /// <inheritdoc/>
        public override void StopServer(bool force)
        {
            if (_isStarted)
            {
                if (force)
                {
                    process.Kill();
                }
                else
                {
                    process.InputCommand("stop");
                }
            }
        }

        public override void SetRuntimeEnvironment(RuntimeEnvironment environment)
        {
        }

        public override bool UpdateServer()
        {
            return ChangeVersion(default);
        }

        protected override bool CreateServer()
        {
            try
            {
                process = new SystemProcess();
                process.ProcessStarted += delegate (object sender, EventArgs e) { _isStarted = true; };
                process.ProcessEnded += delegate (object sender, EventArgs e) { _isStarted = false; };
                propertyFiles[0] = new JavaPropertyFile(Path.Combine(ServerDirectory, "./server.properties"));
            }
            catch (Exception)
            {
                return false;
            }
            return true;
        }

        protected override bool OnServerLoading()
        {
            try
            {
                JsonPropertyFile serverInfoJson = ServerInfoJson;
                versionString = serverInfoJson["version"].ToString();
                process = new SystemProcess();
                process.ProcessStarted += delegate (object sender, EventArgs e) { _isStarted = true; };
                process.ProcessEnded += delegate (object sender, EventArgs e) { _isStarted = false; };
                propertyFiles[0] = new JavaPropertyFile(Path.Combine(ServerDirectory, "./server.properties"));
            }
            catch (Exception)
            {
                return false;
            }
            return true;
        }

        protected override bool BeforeServerSaved()
        {
            JsonPropertyFile serverInfoJson = ServerInfoJson;
            serverInfoJson["version"] = versionString;
            return true;
        }
    }
}
