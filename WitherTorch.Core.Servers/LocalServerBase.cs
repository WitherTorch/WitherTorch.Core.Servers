using System;
using System.Diagnostics;

namespace WitherTorch.Core.Servers
{
    /// <summary>
    /// 此類別為本地端伺服器軟體的基底類別，無法直接使用
    /// </summary>
    public abstract class LocalServerBase : Server
    {
        private readonly SystemProcess _process;

        private bool _isStarted;

        /// <summary>
        /// <see cref="LocalServerBase"/> 的建構子
        /// </summary>
        /// <param name="serverDirectory">伺服器資料夾路徑</param>
        protected LocalServerBase(string serverDirectory) : base(serverDirectory)
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

        /// <inheritdoc/>
        public override bool RunServer(RuntimeEnvironment? environment)
        {
            if (_isStarted)
                return true;
            ProcessStartInfo? startInfo = PrepareProcessStartInfo(environment);
            if (startInfo is null)
                return false;
            OnBeforeRunServer();
            return _process.StartProcess(startInfo);
        }

        /// <inheritdoc/>
        public override void StopServer(bool force)
        {
            if (!_isStarted)
                return;
            SystemProcess process = _process;
            if (!process.IsAlive)
                return;
            StopServerCore(process, force);
        }

        /// <summary>
        /// 傳回 <see cref="RunServer(RuntimeEnvironment?)"/> 所使用的處理序啟動資訊
        /// </summary>
        /// <param name="environment">執行時的環境</param>
        /// <returns></returns>
        protected abstract ProcessStartInfo? PrepareProcessStartInfo(RuntimeEnvironment? environment);

        /// <summary>
        /// 子類別需覆寫為關閉伺服器的程式碼
        /// </summary>
        /// <param name="process">要關閉的 <see cref="SystemProcess"/> 物件</param>
        /// <param name="force">是否使用強制關閉模式</param>
        protected abstract void StopServerCore(SystemProcess process, bool force);
    }
}
