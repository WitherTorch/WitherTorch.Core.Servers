using System;
using System.Buffers;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

using Ionic.Crc;
using Ionic.Zip;

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
        private const int DecompressPercentageBase = 50;
        private const int DefaultFileStreamBufferSize = 4096;
        private const int DefaultPooledBufferSize = 131072; // 原始值是 81920，但因為 ArrayPool 只會取二的次方大小，所以選擇了 131072 作為實際大小

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
            using InstallTaskWatcher<byte[]?> watcher = new InstallTaskWatcher<byte[]?>(task, client, token);
            task.ChangeStatus(new DownloadStatus(downloadUrl, 0));
            client.DownloadProgressChanged += InstallSoftware_DownloadProgressChanged;
            client.DownloadDataCompleted += InstallSoftware_DownloadDataCompleted;
            client.DownloadDataAsync(new Uri(downloadUrl), watcher);
            byte[]? data = await watcher.WaitUtilFinishedAsync();
            if (data is null || token.IsCancellationRequested)
                return false;
            if (task.Status is DownloadStatus downloadStatus)
                downloadStatus.Percentage = 100;
            if (!await TryDecompressData(task, data, token))
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

        private async ValueTask<bool> TryDecompressData(InstallTask task, byte[] data, CancellationToken token)
        {
            DecompressionStatus status = new DecompressionStatus();
            task.ChangeStatus(status);
            task.ChangePercentage(DecompressPercentageBase);
            using Stream stream = new MemoryStream(data, writable: false);
            using ZipFile file = ZipFile.Read(stream, new ReadOptions() { Encoding = Encoding.UTF8 });
            ICollection<ZipEntry> entries = file.Entries;
            int entryCount = entries.Count;
            StrongBox<int> extractCounterBox = new StrongBox<int>();
            using SemaphoreSlim extractingSemaphore = new SemaphoreSlim(1, 1);
            using SemaphoreSlim counterSemaphore = new SemaphoreSlim(1, 1);
            IEnumerable<Task> extractTasks = entries.Select(entry => FilterAndExtractFileAsync(task, entry,
                extractingSemaphore, counterSemaphore, extractCounterBox, entryCount, token));
            await Task.WhenAll(extractTasks).ConfigureAwait(continueOnCapturedContext: false);
            return !token.IsCancellationRequested;
        }

        private async Task FilterAndExtractFileAsync(InstallTask task, ZipEntry entry,
            SemaphoreSlim extractingSemaphore, SemaphoreSlim countingSemaphore,
            StrongBox<int> extractCounterBox, int entryCount, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            string filename = entry.FileName;
            string extractFilename = Path.GetFullPath(Path.Combine(ServerDirectory, filename));
            if (CheckExtractFilename(filename, extractFilename))
            {
                token.ThrowIfCancellationRequested();

                if (extractFilename.EndsWithAny(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)) // Is Directory
                {
                    if (!Directory.Exists(extractFilename))
                        Directory.CreateDirectory(extractFilename);
                }
                else // Is File
                {
                    string? directoryPath = Path.GetDirectoryName(extractFilename);
                    if (directoryPath is not null && !Directory.Exists(directoryPath))
                        Directory.CreateDirectory(directoryPath);
                    token.ThrowIfCancellationRequested();
                    await extractingSemaphore.WaitAsync(token);
                    try
                    {
                        token.ThrowIfCancellationRequested();
                        await ExtractFileAsync(entry, extractFilename, token);
                    }
                    finally
                    {
                        extractingSemaphore.Release();
                    }
                }
            }

            token.ThrowIfCancellationRequested();
            await countingSemaphore.WaitAsync(token);
            try
            {
                token.ThrowIfCancellationRequested();
                if (task.Status is not DecompressionStatus status)
                    return;
                int counter = ++extractCounterBox.Value;
                double percentage = counter * 1.0 / entryCount;
                status.Percentage = percentage * 100.0;
                task.ChangePercentage(DecompressPercentageBase + percentage * (100 - DecompressPercentageBase));
            }
            finally
            {
                countingSemaphore.Release();
            }
        }

        private static bool CheckExtractFilename(string filename, string destination)
            => filename switch
            {
                "allowlist.json" or "whitelist.json" or
                "permissions.json" or "server.properties" => !File.Exists(destination),
                _ => true,
            };

        private static async Task ExtractFileAsync(ZipEntry entry, string targetFilename, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();

            using (Stream destinationStream = new FileStream(targetFilename, FileMode.Create, FileAccess.Write, FileShare.Read, DefaultFileStreamBufferSize, useAsync: true))
            {
                token.ThrowIfCancellationRequested();
                using CrcCalculatorStream sourceStream = entry.OpenReader();
                await CopyToAsync(sourceStream, destinationStream, token);
            }
            File.SetCreationTime(targetFilename, entry.CreationTime);
            File.SetLastWriteTime(targetFilename, entry.ModifiedTime);
            File.SetLastAccessTime(targetFilename, entry.AccessedTime);
        }

        private static async ValueTask CopyToAsync(Stream source, Stream destination, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();

            ArrayPool<byte> pool = ArrayPool<byte>.Shared;
            byte[] buffer = pool.Rent(DefaultPooledBufferSize);
            try
            {
                while (true)
                {
                    int bytesWritten = await source.ReadAsync(buffer, 0, DefaultPooledBufferSize, token);
                    if (bytesWritten <= 0)
                        break;
                    token.ThrowIfCancellationRequested();
                    await destination.WriteAsync(buffer, 0, bytesWritten, token);
                }
            }
            finally
            {
                pool.Return(buffer);
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
