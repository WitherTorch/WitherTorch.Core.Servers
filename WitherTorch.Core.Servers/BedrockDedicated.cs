using System;
using System.Buffers;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
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
            if (!(await _software.GetSoftwareVersionDictionaryAsync()).TryGetValue(version, out string? downloadUrl) || 
                !Uri.TryCreate(downloadUrl, UriKind.Absolute, out Uri? downloadAddress))
                return false;
            using (ITempFileInfo tempFileInfo = WTServer.TempFileFactory.Invoke())
            {
                using WebClient2 client = new WebClient2();
                using InstallTaskWatcher<bool> watcher = new InstallTaskWatcher<bool>(task, client, token);
                task.ChangeStatus(new DownloadStatus(downloadUrl, 0));
                client.DownloadProgressChanged += InstallSoftware_DownloadProgressChanged;
                client.DownloadFileCompleted += InstallSoftware_DownloadFileCompleted;
                client.DownloadFileAsync(downloadAddress, tempFileInfo, watcher);
                if (!await watcher.WaitUtilFinishedAsync() || token.IsCancellationRequested)
                    return false;
                if (task.Status is DownloadStatus downloadStatus)
                    downloadStatus.Percentage = 100;
                try
                {
                    if (!await TryDecompressPackageAsync(task, tempFileInfo, token))
                        return false;
                }
                finally
                {
                    GC.Collect(generation: 1, GCCollectionMode.Forced, blocking: true, compacting: true);
                }
            }
            task.ChangePercentage(100);
            _version = task.Version;
            OnServerVersionChanged();
            return true;
        }

        private void InstallSoftware_DownloadProgressChanged(object? sender, DownloadProgressChangedEventArgs e)
        {
            if (e.UserState is not InstallTaskWatcher<bool> watcher)
                return;
            InstallTask task = watcher.Task;
            if (task.Status is not DownloadStatus status)
                return;
            double percentage = e.ProgressPercentage;
            status.Percentage = percentage;
            task.ChangePercentage(percentage * 0.5);
        }

        private void InstallSoftware_DownloadFileCompleted(object? sender, AsyncCompletedEventArgs e)
        {
            if (sender is not WebClient2 client)
                return;
            client.DownloadProgressChanged -= InstallSoftware_DownloadProgressChanged;
            client.DownloadDataCompleted -= InstallSoftware_DownloadFileCompleted;
            if (e.UserState is not InstallTaskWatcher<bool> watcher)
                return;
            watcher.MarkAsFinished(!e.Cancelled && e.Error is null);
        }

        private async ValueTask<bool> TryDecompressPackageAsync(InstallTask task, ITempFileInfo tempFile, CancellationToken token)
        {
            DecompressionStatus status = new DecompressionStatus();
            task.ChangeStatus(status);
            task.ChangePercentage(DecompressPercentageBase);
            using ThreadLocal<StreamedZipFile> fileLocal = new ThreadLocal<StreamedZipFile>(
                () => new StreamedZipFile(tempFile, Constants.DefaultFileStreamBufferSize),
                trackAllValues: true);
            int entryCount = fileLocal.Value!.Count;
            StrongBox<int> extractCounterBox = new StrongBox<int>();
            using SemaphoreSlim counterSemaphore = new SemaphoreSlim(1, 1);
            using SemaphoreSlim createDirectorySemaphore = new SemaphoreSlim(1, 1);
            IEnumerable<Task> extractTasks = Enumerable.Range(0, entryCount).Select(index => FilterAndExtractFileAsync(task, fileLocal, index,
                createDirectorySemaphore, counterSemaphore, extractCounterBox, entryCount, token));
            await Task.WhenAll(extractTasks).ConfigureAwait(continueOnCapturedContext: false);
            foreach (StreamedZipFile iteratedFile in fileLocal.Values)
                iteratedFile.Dispose();
            return !token.IsCancellationRequested;
        }

        private async Task FilterAndExtractFileAsync(InstallTask task, ThreadLocal<StreamedZipFile> fileLocal, int index,
            SemaphoreSlim createDirectorySemaphore, SemaphoreSlim countingSemaphore, StrongBox<int> extractCounterBox, int entryCount, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            StreamedZipFile file = fileLocal.Value!;
            ZipEntry entry = file[index];
            string filename = entry.FileName;
            string extractFilename = Path.GetFullPath(Path.Combine(ServerDirectory, filename));
            if (CheckExtractFilename(filename, extractFilename))
            {
                token.ThrowIfCancellationRequested();

                if (entry.IsDirectory) // Is Directory
                {
                    if (!Directory.Exists(extractFilename))
                    {
                        await createDirectorySemaphore.WaitAsync(token).ConfigureAwait(false);
                        try
                        {
                            CreateDirectoryIfNotExists(extractFilename);
                        }
                        finally
                        {
                            createDirectorySemaphore.Release();
                        }
                    }
                }
                else // Is File
                {
                    string? directoryPath = Path.GetDirectoryName(extractFilename);
                    if (directoryPath is not null && !Directory.Exists(directoryPath))
                    {
                        await createDirectorySemaphore.WaitAsync(token).ConfigureAwait(false);
                        try
                        {
                            CreateDirectoryIfNotExists(directoryPath);
                        }
                        finally
                        {
                            createDirectorySemaphore.Release();
                        }
                        token.ThrowIfCancellationRequested();
                    }

                    await file.WaitForEnterExtractLockAsync(token).ConfigureAwait(false);
                    try
                    {
                        token.ThrowIfCancellationRequested();
                        await ExtractFileAsync(entry, extractFilename, token);
                    }
                    finally
                    {
                        file.LeaveExtractLock();
                    }
                }
            }

            token.ThrowIfCancellationRequested();
            await countingSemaphore.WaitAsync(token).ConfigureAwait(false);
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

        private static void CreateDirectoryIfNotExists(string directory)
        {
            if (!Directory.Exists(directory))
                Directory.CreateDirectory(directory);
        }

        private static async Task ExtractFileAsync(ZipEntry entry, string targetFilename, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();

            using (Stream destinationStream = new FileStream(targetFilename, FileMode.Create, FileAccess.Write, FileShare.Read, Constants.DefaultFileStreamBufferSize, useAsync: true))
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
            byte[] buffer = pool.Rent(Constants.DefaultPooledBufferSize);
            try
            {
                while (true)
                {
                    int bytesWritten = await source.ReadAsync(buffer, 0, Constants.DefaultPooledBufferSize, token);
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
