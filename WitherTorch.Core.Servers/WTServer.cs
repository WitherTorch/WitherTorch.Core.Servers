using System;
using System.Threading;
using System.Threading.Tasks;

using WitherTorch.Core.Runtime;
using WitherTorch.Core.Servers.Runtime;
using WitherTorch.Core.Tagging;

namespace WitherTorch.Core.Servers
{
    /// <summary>
    /// 安裝伺服器過程中建立 <see cref="LocalProcessStartInfo"/> 結構的工廠委派
    /// </summary>
    /// <param name="task">要建立 <see cref="LocalProcessStartInfo"/> 結構的安裝工作</param>
    /// <param name="arguments">安裝指令的執行參數</param>
    /// <param name="workingDirectory">安裝指令的工作目錄</param>
    /// <returns></returns>
    public delegate LocalProcessStartInfo InstallerProcessStartInfoFactoryFunc(InstallTask task, string arguments, string workingDirectory);

    /// <summary>
    /// <b>WitherTorch.Core.Server</b> 的基礎控制類別，此類別是靜態類別<br/>
    /// 此類別收錄各種基礎的伺服器相關設定，如 Spigot 的建置工具位置 等
    /// </summary>
    public static class WTServer
    {
        private static readonly Func<ILocalProcess> _defaultProcessFactory = static () => new LocalProcess();
        private static readonly InstallerProcessStartInfoFactoryFunc _defaultInstallerProcessStartInfoFactory = static (task, arguments, workingDirectory)
            => new LocalProcessStartInfo("java", arguments, workingDirectory);
        private static readonly Func<ITempFileInfo> _defaultTempFileFactory = TempFileInfo.Create;
        private static readonly SemaphoreSlim _syncSemaphore = new SemaphoreSlim(1, 1);

        private static string _spigotBuildToolsPath = "./SpigotBuildTools";
        private static string _fabricInstallerPath = "./FabricInstaller";
        private static string _quiltInstallerPath = "./QuiltInstaller";
        private static bool _isInitialized = false;

        /// <summary>
        /// <b>Spigot BuildTools</b> 的根位置
        /// </summary>
        public static string SpigotBuildToolsPath
        {
            get
            {
                return _spigotBuildToolsPath;
            }
            set
            {
                _spigotBuildToolsPath = value;
                try
                {
                    if (!System.IO.Directory.Exists(_spigotBuildToolsPath))
                    {
                        System.IO.Directory.CreateDirectory(_spigotBuildToolsPath);
                    }
                }
                catch (Exception)
                {
                }
            }
        }

        /// <summary>
        /// <b>Fabric Installer</b> 的根位置
        /// </summary>
        public static string FabricInstallerPath
        {
            get
            {
                return _fabricInstallerPath;
            }
            set
            {
                _fabricInstallerPath = value;
                try
                {
                    if (!System.IO.Directory.Exists(value))
                    {
                        System.IO.Directory.CreateDirectory(value);
                    }
                }
                catch (Exception)
                {
                }
            }
        }

        /// <summary>
        /// <b>Quilt Installer</b> 的根位置
        /// </summary>
        public static string QuiltInstallerPath
        {
            get
            {
                return _quiltInstallerPath;
            }
            set
            {
                _quiltInstallerPath = value;
                try
                {
                    if (!System.IO.Directory.Exists(value))
                    {
                        System.IO.Directory.CreateDirectory(value);
                    }
                }
                catch (Exception)
                {
                }
            }
        }

        /// <summary>
        /// 取得或設定建立 <see cref="ILocalProcess"/> 物件的工廠方法
        /// </summary>
        public static Func<ILocalProcess> LocalProcessFactory { get; set; } = _defaultProcessFactory;

        /// <summary>
        /// 取得或設定建立 <see cref="ITempFileInfo"/> 物件的工廠方法
        /// </summary>
        public static Func<ITempFileInfo> TempFileFactory { get; set; } = _defaultTempFileFactory;

        /// <summary>
        /// 取得或設定在安裝伺服器過程中建立 <see cref="LocalProcessStartInfo"/> 結構的工廠方法
        /// </summary>
        public static InstallerProcessStartInfoFactoryFunc InstallerProcessStartInfoFactory { get; set; } = _defaultInstallerProcessStartInfoFactory;

        /// <summary>
        /// 取得建立 <see cref="ILocalProcess"/> 物件的預設工廠方法
        /// </summary>
        public static Func<ILocalProcess> DefaultLocalProcessFactory => _defaultProcessFactory;

        /// <summary>
        /// 取得建立 <see cref="ITempFileInfo"/> 物件的預設工廠方法
        /// </summary>
        public static Func<ITempFileInfo> DefaultTempFileFactory => _defaultTempFileFactory;

        /// <summary>
        /// 取得在安裝伺服器過程中建立 <see cref="LocalProcessStartInfo"/> 結構的預設工廠方法
        /// </summary>
        public static InstallerProcessStartInfoFactoryFunc DefaultInstallerProcessStartInfoFactory => _defaultInstallerProcessStartInfoFactory;

        /// <summary>
        /// 以非同步方式初始化此程式庫，此方法會執行一些讓功能可正常運作的程式碼
        /// </summary>
        public static async Task InitializeAsync(CancellationToken cancellationToken)
        {
            SemaphoreSlim semaphore = _syncSemaphore;
            await semaphore.WaitAsync(cancellationToken).ConfigureAwait(continueOnCapturedContext: false);
            try
            {
                if (_isInitialized)
                    return;
                await PersistentTagFactoryRegister.TryRegisterFactoryAsync(JavaRuntimeEnvironment.Factory);
                _isInitialized = true;
            }
            finally
            {
                semaphore.Release();
            }
        }
    }
}
