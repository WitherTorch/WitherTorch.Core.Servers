
namespace WitherTorch.Core.Servers
{
    /// <summary>
    /// 表示一個可獨立選取模組載入器版本進行安裝的伺服器
    /// </summary>
    public interface IModLoaderServer
    {
        /// <inheritdoc cref="Server.GenerateInstallServerTask(string)"/>
        /// <param name="minecraftVersion">要更改的 Minecraft 版本</param>
        /// <param name="modLoaderVersion">要更改的 Mod Loader 版本</param>
        InstallTask? GenerateInstallServerTask(string minecraftVersion, string modLoaderVersion);
    }
}