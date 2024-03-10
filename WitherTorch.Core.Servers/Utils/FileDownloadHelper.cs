using System;
using System.Collections.Concurrent;
using System.ComponentModel;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using WitherTorch.Core.Utils;
using static WitherTorch.Core.Utils.WebClient2;

namespace WitherTorch.Core.Servers.Utils
{
    /// <summary>
    /// 簡易的下載工具類別，可自動校驗雜湊和處理 InstallTask 上的校驗失敗處理
    /// </summary>
    internal static class FileDownloadHelper
    {
        private sealed class DownloadInfo : IDisposable
        {
            private bool disposedValue;

            private readonly bool disposeWebClientAfterUsed;

            public int FlowNumber { get; }

            public InstallTask Task { get; }

            public WebClient2 WebClient { get; }

            public DownloadStatus Status { get; }

            public Uri DownloadUrl { get; }

            public string FileName { get; }

            public string TempFileName { get; set; }

            public double InitialPercentage { get; }

            public double PercentageMultiplier { get; }

            public HashHelper.HashMethod HashMethod { get; }

            public byte[] ExceptedHash { get; }

            public DownloadInfo(int flowNumber, InstallTask task, WebClient2 webClient, Uri downloadUrl, string fileName,
                double initialPercentage, double percentageMultiplier, HashHelper.HashMethod hashMethod, byte[] hashes, bool disposeWebClientAfterUsed)
            {
                FlowNumber = flowNumber;
                Task = task;
                if (webClient is null)
                {
                    WebClient = new WebClient2();
                    this.disposeWebClientAfterUsed = true;
                }
                else
                {
                    WebClient = webClient;
                    this.disposeWebClientAfterUsed = disposeWebClientAfterUsed;
                }
                DownloadUrl = downloadUrl;
                FileName = fileName;
                InitialPercentage = initialPercentage;
                Status = new DownloadStatus(downloadUrl.ToString());
                PercentageMultiplier = percentageMultiplier;
                HashMethod = hashes is null ? HashHelper.HashMethod.None : hashMethod;
                ExceptedHash = hashMethod > HashHelper.HashMethod.None ? hashes : null;
            }

            private void Dispose(bool disposing)
            {
                if (!disposedValue)
                {
                    if (disposing && disposeWebClientAfterUsed)
                    {
                        WebClient.Dispose();
                    }
                    disposedValue = true;
                }
            }

            public void Dispose()
            {
                Dispose(disposing: true);
                GC.SuppressFinalize(this);
            }
        }

        private static volatile int flowNumber = 0;

        private static readonly ConcurrentDictionary<int, DownloadInfo> _dict = new ConcurrentDictionary<int, DownloadInfo>();

        private static readonly ConcurrentDictionary<InstallTask, DownloadInfo> _dict2 = new ConcurrentDictionary<InstallTask, DownloadInfo>();

        private static readonly ThreadLocal<StringBuilder> localStringBuilder = new ThreadLocal<StringBuilder>(() => new StringBuilder(), false);

        public static event EventHandler<int> TaskFinished;

        public static event EventHandler<int> TaskFailed;

        public static int? AddTask(InstallTask task, string downloadUrl, string filename, WebClient2 webClient = null,
            double initPercentage = 0.0, double percentageMultiplier = 1.0, byte[] hash = null, 
            HashHelper.HashMethod hashMethod = HashHelper.HashMethod.None,
            bool disposeWebClientAfterUsed = true)
        {
            if (!Uri.TryCreate(downloadUrl, UriKind.Absolute, out Uri downloadUri))
                return null;
            initPercentage = Math.Max(Math.Min(initPercentage, 100.0), 0.0);
            percentageMultiplier = Math.Max(Math.Min(percentageMultiplier, (100.0 - initPercentage) / 100.0), 0.0);
            int flowNumber = FileDownloadHelper.flowNumber++;
            DownloadInfo info = new DownloadInfo(flowNumber, task, webClient, downloadUri, filename, initPercentage,
                percentageMultiplier, hashMethod, hash, disposeWebClientAfterUsed);
            Task.Factory.StartNew((obj) => StartTask(obj as DownloadInfo), info, TaskCreationOptions.PreferFairness | TaskCreationOptions.DenyChildAttach).ConfigureAwait(false);
            return flowNumber;
        }

        private static string GetTempFileName(string filename)
        {
            StringBuilder builder = localStringBuilder.Value;
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

        private static void StartTask(DownloadInfo info)
        {
            if (info is null && !_dict.TryAdd(info.FlowNumber, info))
                return;

            InstallTask task = info.Task;
            WebClient2 webClient = info.WebClient;
            DownloadStatus status = info.Status;

            string tempFileName = info.TempFileName = GetTempFileName(info.FileName);

            if (_dict2.TryAdd(task, info))
                task.StopRequested += InstallTask_StopRequested;

            webClient.DownloadProgressChanged += WebClient_DownloadProgressChanged;
            webClient.DownloadFileCompleted += WebClient_DownloadFileCompleted;

            status.Percentage = 0;
            task.ChangeStatus(status);
            task.ChangePercentage(info.InitialPercentage);

            webClient.DownloadFileAsync(info.DownloadUrl, tempFileName, info);
        }

        private static void RestartTask(DownloadInfo info)
        {
            if (info is null)
                return;

            string tempFileName = info.TempFileName;
            if (File.Exists(tempFileName))
            {
                try
                {
                    File.Delete(tempFileName);
                }
                catch (Exception)
                {
                }
            }

            InstallTask task = info.Task;
            WebClient2 webClient = info.WebClient;
            DownloadStatus status = info.Status;

            status.Percentage = 0;
            task.ChangeStatus(status);
            task.ChangePercentage(info.InitialPercentage);

            webClient.DownloadFileAsync(info.DownloadUrl, tempFileName, info);
        }

        private static void EndTask(DownloadInfo info, bool failed)
        {
            if (info is null)
                return;

            InstallTask task = info.Task;
            WebClient2 webClient = info.WebClient;

            if (_dict2.TryRemove(task, out _))
                task.StopRequested -= InstallTask_StopRequested;

            webClient.DownloadProgressChanged -= WebClient_DownloadProgressChanged;
            webClient.DownloadFileCompleted -= WebClient_DownloadFileCompleted;
            webClient.CancelAsync();

            if (_dict.TryRemove(info.FlowNumber, out _))
                info.Dispose();

            string tempFileName = info.TempFileName;
            if (failed)
            {
                if (File.Exists(tempFileName))
                {
                    try
                    {
                        File.Delete(tempFileName);
                    }
                    catch (Exception)
                    {
                    }
                }
                task.OnInstallFailed();
                TaskFailed?.Invoke(null, info.FlowNumber);
            }
            else
            {
                string fileName = info.FileName;
                if (!string.Equals(fileName, tempFileName, StringComparison.OrdinalIgnoreCase))
                {
#if NET5_0_OR_GREATER
                    try
                    {
                        File.Move(tempFileName, fileName, true);
                    }
                    catch (Exception)
                    {
                    }
#elif NET472_OR_GREATER
                    if (File.Exists(fileName))
                    {
                        try
                        {
                            File.Delete(fileName);
                        }
                        catch (Exception)
                        {
                        }
                    }
                    try
                    {
                        File.Move(tempFileName, fileName);
                    }
                    catch (Exception)
                    {
                    }
#endif
                }
                TaskFinished?.Invoke(null, info.FlowNumber);
                if (task.InstallPercentage >= 100.0)
                    task.OnInstallFinished();
            }
        }

        private static void InstallTask_StopRequested(object sender, EventArgs e)
        {
            if (sender is InstallTask task && _dict2.TryRemove(task, out DownloadInfo info))
            {
                task.StopRequested -= InstallTask_StopRequested;
                EndTask(info, true);
            }
        }

        private static void WebClient_DownloadProgressChanged(object sender, DownloadProgressChangedEventArgs e)
        {
            if (!(e.UserState is DownloadInfo info))
                return;

            DownloadStatus status = info.Status;
            InstallTask task = info.Task;

            double percentage = e.ProgressPercentage,
                initPercentage = info.InitialPercentage,
                percentageMultiplier = info.PercentageMultiplier;

            status.Percentage = percentage;

            if (percentageMultiplier < 1.0)
                percentage *= percentageMultiplier;
            if (initPercentage > 0)
                task.ChangePercentage(initPercentage + percentage);
            else
                task.ChangePercentage(percentage);
        }

        private static void WebClient_DownloadFileCompleted(object sender, AsyncCompletedEventArgs e)
        {
            if (!(e.UserState is DownloadInfo info))
                return;

            DownloadStatus status = info.Status;
            InstallTask task = info.Task;
            HashHelper.HashMethod hashMethod = info.HashMethod;

            double percentage = 100.0,
                initPercentage = info.InitialPercentage,
                percentageMultiplier = info.PercentageMultiplier;

            status.Percentage = percentage;

            if (percentageMultiplier < 1.0)
                percentage *= percentageMultiplier;
            if (initPercentage > 0)
                task.ChangePercentage(initPercentage + percentage);
            else
                task.ChangePercentage(percentage);

            if (e.Error is object)
            {
                Task.Factory.StartNew((obj) => EndTask(obj as DownloadInfo, failed: true), info,
                    TaskCreationOptions.PreferFairness | TaskCreationOptions.DenyChildAttach)
                    .ConfigureAwait(false); ;
                return;
            }

            string filename = info.FileName;
            string tempFilename = info.TempFileName;
            byte[] exceptedHash = info.ExceptedHash;
            byte[] actualHash;

            switch (hashMethod)
            {
                case HashHelper.HashMethod.None:
                    Task.Factory.StartNew((obj) => EndTask(obj as DownloadInfo, failed: false), info,
                        TaskCreationOptions.PreferFairness | TaskCreationOptions.DenyChildAttach)
                        .ConfigureAwait(false); ;
                    return;
                case HashHelper.HashMethod.MD5:
                case HashHelper.HashMethod.SHA1:
                case HashHelper.HashMethod.SHA256:
                    task.ChangeStatus(new ValidatingStatus(filename));
                    try
                    {
                        using (FileStream stream = File.Open(tempFilename, FileMode.Open, FileAccess.Read, FileShare.Read))
                            actualHash = HashHelper.ComputeHash(stream, hashMethod);
                    }
                    catch (Exception)
                    {
                        actualHash = null;
                    }
                    break;
                default:
                    goto case HashHelper.HashMethod.None;
            }

            if (HashHelper.ByteArrayEquals(exceptedHash, actualHash))
            {
                Task.Factory.StartNew((obj) => EndTask(obj as DownloadInfo, failed: false), info,
                    TaskCreationOptions.PreferFairness | TaskCreationOptions.DenyChildAttach)
                    .ConfigureAwait(false); ;
            }
            else
            {
                Task.Factory.StartNew((obj) =>
                {
                    if (!(obj is Tuple<DownloadInfo, byte[]> tuple))
                        return;

                    DownloadInfo _info = tuple.Item1;
                    InstallTask _task = _info.Task;
                    string _tempFilename = _info.TempFileName;
                    byte[] _actualHash = tuple.Item2;
                    byte[] _exceptedHash = _info.ExceptedHash;

                    switch (_task.OnValidateFailed(_tempFilename, _actualHash, _exceptedHash))
                    {
                        case InstallTask.ValidateFailedState.Cancel:
                            EndTask(_info, failed: true);
                            return;
                        case InstallTask.ValidateFailedState.Ignore:
                            EndTask(_info, failed: false);
                            return;
                        case InstallTask.ValidateFailedState.Retry:
                            RestartTask(_info);
                            return;
                    }
                }, Tuple.Create(info, actualHash), TaskCreationOptions.PreferFairness | TaskCreationOptions.DenyChildAttach).ConfigureAwait(false);
            }
        }
    }
}
