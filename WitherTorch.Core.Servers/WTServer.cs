using System;

using WitherTorch.Core.Runtime;
using WitherTorch.Core.Servers.Utils;

namespace WitherTorch.Core.Servers
{
    /// <summary>
    /// <b>WitherTorch.Core.Server</b> 的基礎控制類別，此類別是靜態類別<br/>
    /// 此類別收錄各種基礎的伺服器相關設定，如 Spigot 的建置工具位置 等
    /// </summary>
    public static class WTServer
    {
        private static readonly Func<ILocalProcess> _defaultProcessFactory = static () => new LocalProcess();
        private static readonly Func<ITempFileInfo> _defaultTempFileFactory = TempFileInfo.Create;
        private static string _spigotBuildToolsPath = "./SpigotBuildTools";
        private static string _fabricInstallerPath = "./FabricInstaller";
        private static string _quiltInstallerPath = "./QuiltInstaller";

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
        /// 取得建立 <see cref="ILocalProcess"/> 物件的預設工廠方法
        /// </summary>
        public static Func<ILocalProcess> DefaultLocalProcessFactory => _defaultProcessFactory;

        /// <summary>
        /// 取得建立 <see cref="ITempFileInfo"/> 物件的預設工廠方法
        /// </summary>
        public static Func<ITempFileInfo> DefaultTempFileFactory => _defaultTempFileFactory;
    }
}
