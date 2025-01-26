using System;
using System.Diagnostics;

namespace WitherTorch.Core.Servers
{
    public abstract class LocalServer<T> : Server<T> where T : LocalServer<T>
    {
        private bool _isStarted;
        protected readonly SystemProcess _process;

        /// <summary>
        /// 在 <see cref="RunServer(RuntimeEnvironment?)"/> 被呼叫且準備啟動伺服器時觸發
        /// </summary>
        public event EventHandler? BeforeRunServer;

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

        public override bool RunServer(RuntimeEnvironment? environment)
        {
            if (_isStarted)
                return true;
            ProcessStartInfo? startInfo = PrepareProcessStartInfo(environment);
            if (startInfo is null)
                return false;
            return _process.StartProcess(startInfo);
        }

        public override void StopServer(bool force)
        {
            if (!_isStarted)
                return;
            SystemProcess process = _process;
            if (!process.IsAlive)
                return;
            StopServerCore(process, force);
        }

        protected abstract ProcessStartInfo? PrepareProcessStartInfo(RuntimeEnvironment? environment);

        protected abstract void StopServerCore(SystemProcess process, bool force);
    }
}
