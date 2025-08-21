using System;
using System.IO;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

using WitherTorch.Core.Utils;

using YamlDotNet.Core.Tokens;

namespace WitherTorch.Core.Servers.Utils
{
    /// <summary>
    /// 簡易的下載工具類別，可自動校驗雜湊和處理 <see cref="InstallTask"/> 上的校驗失敗處理
    /// </summary>
    internal static partial class FileDownloadHelper
    {
        public static async ValueTask<bool> DownloadFileAsync(InstallTask task, string sourceAddress, string targetFilename,
            CancellationToken cancellationToken, WebClient2? webClient = null,
            double initPercentage = 0.0, double percentageMultiplier = 1.0,
            byte[]? hash = null, HashHelper.HashMethod hashMethod = HashHelper.HashMethod.None)
        {
            if (hashMethod < HashHelper.HashMethod.None || hashMethod > HashHelper.HashMethod.SHA256)
                throw new ArgumentOutOfRangeException(nameof(hashMethod));
            if (!Uri.TryCreate(sourceAddress, UriKind.Absolute, out Uri? sourceUri) || cancellationToken.IsCancellationRequested)
                return false;
            DownloadFileClosure closure = new DownloadFileClosure(initPercentage, percentageMultiplier);
            string tempFileName = GetTempFileName(targetFilename);
            bool needDisposeWebClient;
            if (webClient is null)
            {
                webClient = new WebClient2();
                needDisposeWebClient = true;
            }
            else
            {
                needDisposeWebClient = false;
            }
            try
            {
                if (!await DownloadFileCoreAsync(task, webClient, sourceUri, tempFileName, closure) || cancellationToken.IsCancellationRequested)
                    return false;
                if (hash is not null && hashMethod != HashHelper.HashMethod.None)
                {
                    do
                    {
                        byte[] actualHash;
                        using (FileStream stream = new FileStream(tempFileName, FileMode.Open, FileAccess.Read, FileShare.Read))
                            actualHash = HashHelper.ComputeHash(stream, hashMethod);
                        if (HashHelper.ByteArrayEquals(hash, actualHash))
                            break;
                        bool ignoreHashMismatch;
                        switch (task.OnValidateFailed(tempFileName, actualHash, hash))
                        {
                            case ValidateFailedState.Ignore:
                                ignoreHashMismatch = true;
                                break;
                            case ValidateFailedState.Retry:
                                ignoreHashMismatch = false;
                                break;
                            default:
                                return false;
                        }
                        if (cancellationToken.IsCancellationRequested)
                            return false;
                        if (ignoreHashMismatch)
                            break;
                        File.Delete(tempFileName);
                        if (!await DownloadFileCoreAsync(task, webClient, sourceUri, tempFileName, closure) || cancellationToken.IsCancellationRequested)
                            return false;
                    } while (true);
                }
                MoveFile(tempFileName, targetFilename);
            }
            finally
            {
                File.Delete(tempFileName);
                if (needDisposeWebClient)
                    webClient.Dispose();
            }
            return !cancellationToken.IsCancellationRequested;
        }

        private static async ValueTask<bool> DownloadFileCoreAsync(InstallTask task, WebClient2 webClient, Uri sourceUri, string targetFilename, DownloadFileClosure closure)
        {
            DownloadStatus status = new DownloadStatus(sourceUri.AbsoluteUri);
            task.ChangePercentage(closure.InitialPercentage);
            task.ChangeStatus(status);
            using InstallTaskWatcher<bool> watcher = new InstallTaskWatcher<bool>(task, webClient);
            closure.SubscribeEvents(webClient);
            try
            {
                webClient.DownloadFileAsync(sourceUri, targetFilename, watcher);
                if (!await watcher.WaitUtilFinishedAsync())
                    return false;
                status.Percentage = 100.0;
                task.ChangePercentage(closure.GetAdjustedPercentage(100.0));
                return true;
            }
            finally
            {
                closure.UnsubscribeEvents(webClient);
            }
        }

        private static string GetTempFileName(string filename)
        {
            StringBuilder builder = ThreadLocalObjects.StringBuilder;
            builder.EnsureCapacity(filename.Length + 5);
            builder.Append(filename);
            builder.Append(".tmp");
            string result = builder.ToString();
            int i = -1, length = result.Length;
            while (File.Exists(result))
            {
                if (i >= 0)
                {
                    builder.Remove(length, builder.Length - length);
                }
                builder.Append(++i);
                result = builder.ToString();
            }
            builder.Clear();
            return result;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static void MoveFile(string sourceFilename, string targetFilename)
        {
            StringComparison comparison
#if NET8_0_OR_GREATER
                = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
#else
                = Environment.OSVersion.Platform switch
                {
                    PlatformID.Win32S or PlatformID.Win32Windows or
                    PlatformID.Win32NT or PlatformID.WinCE or
                    PlatformID.Xbox => StringComparison.OrdinalIgnoreCase,
                    _ => StringComparison.Ordinal
                };
#endif
            if (string.Equals(sourceFilename, targetFilename, comparison))
                return;
#if NET8_0_OR_GREATER
            File.Move(sourceFilename, targetFilename, overwrite: true);
#else            
            File.Delete(targetFilename);
            File.Move(sourceFilename, targetFilename);
#endif
        }
    }
}
