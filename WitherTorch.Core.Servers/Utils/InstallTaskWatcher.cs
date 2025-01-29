using System;
using System.Net;

using WitherTorch.Core.Utils;

namespace WitherTorch.Core.Servers.Utils
{
    internal sealed class InstallTaskWatcher : IDisposable
    {
        private bool disposedValue;
        private readonly WebClient2? client;

        public InstallTask Task { get; }

        public bool IsStopRequested { get; private set; }

        public InstallTaskWatcher(InstallTask task, WebClient2? client)
        {
            Task = task;
            this.client = client;
            task.StopRequested += Task_StopRequested;
        }

        private void Task_StopRequested(object? sender, EventArgs e)
        {
            Task.StopRequested -= Task_StopRequested;
            IsStopRequested = true;
            if (client is null)
                return;
            client.CancelAsync();
            client.Dispose();
        }

        private void Dispose(bool disposing)
        {
            if (!disposedValue)
            {
                if (disposing)
                {
                    Task.StopRequested -= Task_StopRequested;
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
}
