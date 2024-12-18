﻿using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;

using WitherTorch.Core.Property;
using WitherTorch.Core.Servers.Utils;
using WitherTorch.Core.Utils;

using YamlDotNet.Core;

using static WitherTorch.Core.Utils.WebClient2;

using Version = System.Version;

namespace WitherTorch.Core.Servers
{
    /// <summary>
    /// Bedrock 原版伺服器
    /// </summary>
    public class BedrockDedicated : LocalServer<BedrockDedicated>
    {
        private const string manifestListURL = "https://withertorch-bds-helper.vercel.app/api/latest";
        private const string downloadURLForLinux = "https://minecraft.azureedge.net/bin-linux/bedrock-server-{0}.zip";
        private const string downloadURLForWindows = "https://minecraft.azureedge.net/bin-win/bedrock-server-{0}.zip";

        private static readonly Lazy<string[]> _versionsLazy = new Lazy<string[]>(LoadVersionList,
            System.Threading.LazyThreadSafetyMode.ExecutionAndPublication);

        private string _version = string.Empty;

        static BedrockDedicated()
        {
            SoftwareId = "bedrockDedicated";
        }

        private static string[] LoadVersionList()
        {
            string? manifestString = CachedDownloadClient.Instance.DownloadString(manifestListURL);
            if (manifestString is null)
                return Array.Empty<string>();
#if NET5_0_OR_GREATER
            using (StringReader reader = new StringReader(manifestString))
            {
                while (reader.Peek() != -1)
                {
                    string? line = reader.ReadLine();
                    if (string.IsNullOrEmpty(line))
                        continue;
                    if (OperatingSystem.IsLinux())
                    {
                        if (line[..6] == "linux=" && Version.TryParse(line = line[6..], out _))
                        {
                            return new string[] { line };
                        }
                    }
                    else if (OperatingSystem.IsWindows())
                    {
                        if (line[..8] == "windows=" && Version.TryParse(line = line[8..], out _))
                        {
                            return new string[] { line };
                        }
                    }
                }
                reader.Close();
            }
#else 
            PlatformID platformID = Environment.OSVersion.Platform;
            using (StringReader reader = new StringReader(manifestString))
            {
                while (reader.Peek() != -1)
                {
                    string line = reader.ReadLine();
                    switch (platformID)
                    {
                        case PlatformID.Unix:
                            if (line.StartsWith("linux=") && Version.TryParse(line = line.Substring(6), out _))
                            {
                                return new string[] { line };
                            }
                            break;
                        case PlatformID.Win32NT:
                            if (line.StartsWith("windows=") && Version.TryParse(line = line.Substring(8), out _))
                            {
                                return new string[] { line };
                            }
                            break;
                    }
                }
                reader.Close();
            }
#endif
            return Array.Empty<string>();
        }

        public override string ServerVersion => _version;

        public override bool ChangeVersion(int versionIndex)
        {
            return InstallSoftware();
        }

        private bool InstallSoftware()
        {
            string[] versions = _versionsLazy.Value;
            if (versions.Length <= 0)
                return false;
            InstallSoftware(versions[0]);
            return true;
        }

        public void InstallSoftware(string version)
        {
            InstallTask task = new InstallTask(this, version);
            OnServerInstalling(task);
            task.ChangeStatus(PreparingInstallStatus.Instance);
            string? downloadURL = null;
#if NET5_0_OR_GREATER
            if (OperatingSystem.IsLinux())
            {
                downloadURL = string.Format(downloadURLForLinux, version);
            }
            else if (OperatingSystem.IsWindows())
            {
                downloadURL = string.Format(downloadURLForWindows, version);
            }
#else
            PlatformID platformID = Environment.OSVersion.Platform;
            switch (platformID)
            {
                case PlatformID.Unix:
                    downloadURL = string.Format(downloadURLForLinux, version);
                    break;
                case PlatformID.Win32NT:
                    downloadURL = string.Format(downloadURLForWindows, version);
                    break;
            }
#endif
            if (!InstallSoftware(task, downloadURL))
            {
                task.OnInstallFailed();
                return;
            }
        }

        private bool InstallSoftware(InstallTask task, string? downloadUrl)
        {
            if (string.IsNullOrEmpty(downloadUrl))
                return false;
            WebClient2 client = new WebClient2();
            InstallTaskWatcher watcher = new InstallTaskWatcher(task, client);
#pragma warning disable CS8604
            DownloadStatus status = new DownloadStatus(downloadUrl, 0);
#pragma warning restore CS8604
            task.ChangeStatus(status);
            client.DownloadProgressChanged += InstallSoftware_DownloadProgressChanged;
            client.DownloadDataCompleted += InstallSoftware_DownloadDataCompleted;
            client.DownloadDataAsync(new Uri(downloadUrl), task);
            return true;
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

        public override string[] GetSoftwareVersions()
        {
            return _versionsLazy.Value;
        }

        public override bool RunServer(RuntimeEnvironment? environment)
        {
            if (_isStarted)
                return true;
            string path = Path.Combine(ServerDirectory, "bedrock_server.exe");
            if (!File.Exists(path))
                return false;
            ProcessStartInfo startInfo = new ProcessStartInfo
            {
                FileName = path,
                WorkingDirectory = ServerDirectory,
                CreateNoWindow = true,
                ErrorDialog = true,
                UseShellExecute = false,
            };
            return _process.StartProcess(startInfo);
        }

        /// <inheritdoc/>
        public override void StopServer(bool force)
        {
            if (_isStarted)
            {
                if (force)
                {
                    _process.Kill();
                }
                else
                {
                    _process.InputCommand("stop");
                }
            }
        }

        public override void SetRuntimeEnvironment(RuntimeEnvironment? environment)
        {
        }

        public override bool UpdateServer()
        {
            return InstallSoftware();
        }

        protected override bool CreateServer()
        {
            return true;
        }

        protected override bool OnServerLoading()
        {
            JsonPropertyFile? serverInfoJson = ServerInfoJson;
            if (serverInfoJson is null)
                return false;
            string? version = serverInfoJson["version"]?.ToString();
            if (string.IsNullOrEmpty(version))
                return false;
            _version = ObjectUtils.ThrowIfNull(version);
            return true;
        }

        protected override bool BeforeServerSaved()
        {
            JsonPropertyFile? serverInfoJson = ServerInfoJson;
            if (serverInfoJson is null)
                return false;
            serverInfoJson["version"] = _version;
            return true;
        }
    }
}
