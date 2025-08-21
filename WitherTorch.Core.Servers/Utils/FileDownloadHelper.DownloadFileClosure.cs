using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;

using WitherTorch.Core.Utils;

namespace WitherTorch.Core.Servers.Utils
{
    partial class FileDownloadHelper
    {
        private readonly struct DownloadFileClosure
        {
            private readonly double _initialPercentage, _percentageMultiplier;

            public double InitialPercentage => _initialPercentage;

            public DownloadFileClosure(double initialPercentage, double percentageMultiplier)
            {
                _initialPercentage = initialPercentage = Clamp(initialPercentage, 0.0, 100.0);
                _percentageMultiplier = Clamp(percentageMultiplier, 0.0, (100.0 - initialPercentage) / 100.0);
            }

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            private static double Clamp(double value, double min, double max)
            {
#if NETSTANDARD2_0
                if (min > max)
                    throw new ArgumentOutOfRangeException(nameof(max));
                return Math.Min(Math.Max(value, min), max);
#else
                return Math.Clamp(value, min, max);
#endif
            }

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public double GetAdjustedPercentage(double percentage)
                => percentage < 0 ? -1.0 : _initialPercentage + percentage * _percentageMultiplier;

            public void SubscribeEvents(WebClient2 client)
            {
                client.DownloadProgressChanged += WebClient_DownloadProgressChanged;
                client.DownloadFileCompleted += WebClient_DownloadFileCompleted;
            }

            public void UnsubscribeEvents(WebClient2 client)
            {
                client.DownloadProgressChanged -= WebClient_DownloadProgressChanged;
                client.DownloadFileCompleted -= WebClient_DownloadFileCompleted;
            }

            public void WebClient_DownloadProgressChanged(object? sender, WebClient2.DownloadProgressChangedEventArgs e)
            {
                if (e.UserState is not InstallTaskWatcher<bool> watcher)
                    return;
                InstallTask task = watcher.Task;
                if (task.Status is not DownloadStatus status)
                    return;
                double percentage = e.ProgressPercentage;
                status.Percentage = percentage;
                task.ChangePercentage(GetAdjustedPercentage(percentage));
            }

            public void WebClient_DownloadFileCompleted(object? sender, AsyncCompletedEventArgs e)
            {
                if (sender is not WebClient2 client || e.UserState is not InstallTaskWatcher<bool> watcher)
                    return;
                UnsubscribeEvents(client);
                watcher.MarkAsFinished(!e.Cancelled && e.Error is null);
            }
        }
    }
}
