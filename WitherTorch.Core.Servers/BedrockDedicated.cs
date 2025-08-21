using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using WitherTorch.Core.Property;
using WitherTorch.Core.Runtime;
using WitherTorch.Core.Servers.Utils;
using WitherTorch.Core.Utils;

using static WitherTorch.Core.Utils.WebClient2;

namespace WitherTorch.Core.Servers
{
    /// <summary>
    /// Bedrock 原版伺服器
    /// </summary>
    public sealed partial class BedrockDedicated : LocalServerBase
    {
        private const string SoftwareId = "bedrockDedicated";

        private string _version = string.Empty;

        private BedrockDedicated(string serverDirectory) : base(serverDirectory) { }

        /// <inheritdoc/>
        public override string ServerVersion => _version;

        /// <inheritdoc/>
        public override string GetSoftwareId() => SoftwareId;

        /// <inheritdoc/>
        public override InstallTask? GenerateInstallServerTask(string version)
        {
            if (string.IsNullOrWhiteSpace(version))
                return null;

            return new InstallTask(this, version, RunInstallServerTaskAsync);
        }

        /// <inheritdoc/>
        public override InstallTask? GenerateUpdateServerTask()
        {
            string? version = _software.GetSoftwareVersionsAsync().Result.FirstOrDefault();
            if (string.IsNullOrEmpty(version))
                return null;
            return GenerateInstallServerTask(version!);
        }

        private async ValueTask<bool> RunInstallServerTaskAsync(InstallTask task, CancellationToken token)
        {
            string version = task.Version;
            if (!(await _software.GetSoftwareVersionDictionaryAsync()).TryGetValue(version, out string? downloadUrl))
                return false;
            using WebClient2 client = new WebClient2();
            using InstallTaskWatcher<byte[]?> watcher = new InstallTaskWatcher<byte[]?>(task, client);
            task.ChangeStatus(new DownloadStatus(downloadUrl, 0));
            client.DownloadProgressChanged += InstallSoftware_DownloadProgressChanged;
            client.DownloadDataCompleted += InstallSoftware_DownloadDataCompleted;
            client.DownloadDataAsync(new Uri(downloadUrl), watcher);
            byte[]? data = await watcher.WaitUtilFinishedAsync();
            if (data is null || token.IsCancellationRequested)
                return false;
            if (task.Status is DownloadStatus downloadStatus)
                downloadStatus.Percentage = 100;
            task.ChangePercentage(50);
            if (!TryDecompressData(task, data, token))
                return false;
            task.ChangePercentage(100);
            _version = task.Version;
            OnServerVersionChanged();
            return true;
        }

        private void InstallSoftware_DownloadProgressChanged(object? sender, DownloadProgressChangedEventArgs e)
        {
            if (e.UserState is not InstallTaskWatcher<byte[]?> watcher)
                return;
            InstallTask task = watcher.Task;
            if (task.Status is not DownloadStatus status)
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
            if (e.UserState is not InstallTaskWatcher<byte[]?> watcher)
                return;
            watcher.MarkAsFinished(e.Result);
        }

        private bool TryDecompressData(InstallTask task, byte[] data, CancellationToken token)
        {
            using MemoryStream stream = new MemoryStream(data);
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
                    while (enumerator.MoveNext() && !token.IsCancellationRequested)
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
                                    if (File.Exists(filePath))
                                        break;

                                    goto default;
                                default:
                                    entry.ExtractToFile(filePath, true);
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
                return !token.IsCancellationRequested;
            }
            finally
            {
                GC.Collect(generation: 1, GCCollectionMode.Optimized, blocking: false, compacting: false);
            }
        }

        /// <inheritdoc/>
        public override string GetReadableVersion()
        {
            return _version;
        }

        /// <inheritdoc/>
        public override RuntimeEnvironment? GetRuntimeEnvironment()
        {
            return null;
        }

        /// <inheritdoc/>
        public override IPropertyFile[] GetServerPropertyFiles()
        {
            return Array.Empty<IPropertyFile>();
        }

        /// <inheritdoc/>
        protected override bool TryPrepareProcessStartInfo(RuntimeEnvironment? environment, out LocalProcessStartInfo startInfo)
        {
            string serverDirectory = ServerDirectory;
            string path = Path.Combine(serverDirectory, "./bedrock_server.exe");
            if (!File.Exists(path))
            {
                startInfo = default;
                return false;
            }
            startInfo = new LocalProcessStartInfo(path, string.Empty, serverDirectory);
            return true;
        }

        /// <inheritdoc/>
        protected override void StopServerCore(ILocalProcess process, bool force)
        {
            if (force)
            {
                process.Stop();
                return;
            }
            process.InputCommand("stop");
        }

        /// <inheritdoc/>
        public override void SetRuntimeEnvironment(RuntimeEnvironment? environment)
        {
        }

        /// <inheritdoc/>
        protected override bool CreateServerCore() => true;

        /// <inheritdoc/>
        protected override bool LoadServerCore(JsonPropertyFile serverInfoJson)
        {
            string? version = serverInfoJson["version"]?.ToString();
            if (string.IsNullOrEmpty(version))
                return false;
            _version = ObjectUtils.ThrowIfNull(version);
            return true;
        }

        /// <inheritdoc/>
        protected override bool SaveServerCore(JsonPropertyFile serverInfoJson)
        {
            serverInfoJson["version"] = _version;
            return true;
        }
    }
}
