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
    public static partial class FileDownloadHelper
    {
        /// <summary>
        /// 下載位於 <paramref name="sourceAddress"/> 的檔案，並將其儲存至 <paramref name="targetFilename"/> 中
        /// </summary>
        /// <param name="task">要用於追蹤進度的 <see cref="InstallTask"/> 物件</param>
        /// <param name="sourceAddress">檔案的來源網址</param>
        /// <param name="targetFilename">檔案的目標位置</param>
        /// <param name="cancellationToken">用於控制非同步操作是否取消的 <see cref="CancellationToken"/> 結構</param>
        /// <param name="webClient">用於下載檔案的 <see cref="WebClient2"/> 物件，如果此項為 <see langword="null"/> 的話則會自動於內部建立專用的物件</param>
        /// <param name="initPercentage">用於呼叫 <see cref="InstallTask.ChangePercentage(double)"/> 的初始進度數值</param>
        /// <param name="percentageMultiplier">用於呼叫 <see cref="InstallTask.ChangePercentage(double)"/> 的進度增加倍率</param>
        /// <param name="hash">用於校驗檔案完整性的雜湊 <see langword="byte[]"/> 陣列</param>
        /// <param name="hashMethod">用於校驗檔案完整性的雜湊方法</param>
        /// <returns>一個工作。當工作完成時，其結果將指示檔案是否下載成功</returns>
        /// <exception cref="ArgumentOutOfRangeException"/>
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
                if (!await DownloadFileCoreAsync(task, webClient, sourceUri, tempFileName, closure, cancellationToken) || cancellationToken.IsCancellationRequested)
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
                        if (!await DownloadFileCoreAsync(task, webClient, sourceUri, tempFileName, closure, cancellationToken) || cancellationToken.IsCancellationRequested)
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

        private static async ValueTask<bool> DownloadFileCoreAsync(InstallTask task, WebClient2 webClient, 
            Uri sourceUri, string targetFilename, DownloadFileClosure closure, CancellationToken cancellationToken)
        {
            DownloadStatus status = new DownloadStatus(sourceUri.AbsoluteUri);
            task.ChangePercentage(closure.InitialPercentage);
            task.ChangeStatus(status);
            using InstallTaskWatcher<bool> watcher = new InstallTaskWatcher<bool>(task, webClient, cancellationToken);
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
