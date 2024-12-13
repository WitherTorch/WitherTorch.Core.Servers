using System;

namespace WitherTorch.Core.Servers
{
    public abstract class LocalServer<T> : Server<T> where T : LocalServer<T>
    {
        protected readonly SystemProcess _process;
        protected bool _isStarted;

        protected LocalServer()
        {
            _isStarted = false;
            SystemProcess process = new SystemProcess();
            process.ProcessStarted += delegate (object? sender, EventArgs e) { _isStarted = true; };
            process.ProcessEnded += delegate (object? sender, EventArgs e) { _isStarted = false; };
            _process = process;
        }

        /// <inheritdoc/>
        public override AbstractProcess GetProcess()
        {
            return _process;
        }
    }
}
