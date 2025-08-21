using System;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;

using WitherTorch.Core.Utils;

namespace WitherTorch.Core.Servers.Utils
{
    internal sealed class InstallTaskWatcher<TResult> : IDisposable
    {
        private readonly WebClient2? _client;
        private readonly InstallTask _task;
        private readonly TaskCompletionSource<TResult> _completionSource;

        private bool _disposed;

        public InstallTask Task => _task;
        public WebClient2? WebClient => _client;

        public bool IsStopRequested { get; private set; }

        public InstallTaskWatcher(InstallTask task, WebClient2? client)
        {
            _task = task;
            _client = client;
            _completionSource = new TaskCompletionSource<TResult>(TaskCreationOptions.RunContinuationsAsynchronously);

            task.StopRequested += Task_StopRequested;
        }

        private void Task_StopRequested(object? sender, EventArgs e)
        {
            IsStopRequested = true;
            Dispose();

            WebClient2? client = _client;
            if (client is null)
                return;
            client.CancelAsync();
            client.Dispose();
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public Task<TResult> WaitUtilFinishedAsync() => _completionSource.Task;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void MarkAsFinished(TResult result) => _completionSource.TrySetResult(result);

        ~InstallTaskWatcher() => Dispose(disposing: false);

        private void Dispose(bool disposing)
        {
            if (_disposed)
                return;
            _disposed = true;

            if (disposing)
            {
                _task.StopRequested -= Task_StopRequested;
                _completionSource.TrySetCanceled();
            }
        }

        public void Dispose()
        {
            Dispose(disposing: true);
            GC.SuppressFinalize(this);
        }
    }
}
