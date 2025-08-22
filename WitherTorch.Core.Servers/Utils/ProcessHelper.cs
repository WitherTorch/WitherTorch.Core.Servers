using System;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;

using WitherTorch.Core.Runtime;

namespace WitherTorch.Core.Servers.Utils
{
    internal static class ProcessHelper
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static ValueTask<bool> RunProcessAsync(InstallTask task, double percentage, in LocalProcessStartInfo startInfo, CancellationToken token)
        {
            ProcessStatus status = new ProcessStatus(0.0);
            task.ChangeStatus(status);
            task.ChangePercentage(percentage);
            return RunProcessAsync(task, status, startInfo, token);
        }

        public static async ValueTask<bool> RunProcessAsync(InstallTask task, ProcessStatus status, LocalProcessStartInfo startInfo, CancellationToken token)
        {
            using InstallTaskWatcher<bool> watcher = new InstallTaskWatcher<bool>(task, token);

            void OnProcessEnded(object? sender, EventArgs args)
            {
                if (sender is not ILocalProcess process)
                    return;
                process.MessageReceived -= status.OnProcessMessageReceived;
                process.ProcessEnded -= OnProcessEnded;
                watcher.MarkAsFinished(true);
            }

            using ILocalProcess process = WTServer.LocalProcessFactory.Invoke();
            process.MessageReceived += status.OnProcessMessageReceived;
            process.ProcessEnded += OnProcessEnded;
            if (!process.Start(startInfo))
                return false;
            try
            {
                await watcher.WaitUtilFinishedAsync();
            }
            catch (Exception)
            {
                process.Stop();
            }
            finally
            {
                process.MessageReceived -= status.OnProcessMessageReceived;
                process.ProcessEnded -= OnProcessEnded;
            }
            return !token.IsCancellationRequested;
        }
    }
}
