using System;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;

using WitherTorch.Core.Utils;

namespace WitherTorch.Core.Servers.Utils
{
    internal sealed class InstallTaskWatcher<TResult> : IDisposable
    {
        private readonly WebClient2? _client;
        private readonly InstallTask _task;
        private readonly TaskCompletionSource<TResult> _completionSource;
        private readonly CancellationTokenRegistration _registration;

        private bool _disposed;

        public InstallTask Task => _task;
        public WebClient2? WebClient => _client;

        public InstallTaskWatcher(InstallTask task, CancellationToken cancellationToken) : this(task, null, cancellationToken) { }

        public InstallTaskWatcher(InstallTask task, WebClient2? client, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();

            _task = task;
            _client = client;
            _completionSource = new TaskCompletionSource<TResult>(TaskCreationOptions.RunContinuationsAsynchronously);

            _registration = cancellationToken.Register(OnCancellationRequested, useSynchronizationContext: false);
        }

        private void OnCancellationRequested()
        {
            WebClient2? client = _client;
            if (client is not null && !client.IsDisposed)
                client.CancelAsync();
            Dispose();
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
            _registration.Dispose();
            if (disposing)
                _completionSource.TrySetCanceled();
        }

        public void Dispose()
        {
            Dispose(disposing: true);
            GC.SuppressFinalize(this);
        }
    }
}
