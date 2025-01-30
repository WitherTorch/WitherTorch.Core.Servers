using WitherTorch.Core.Software;

namespace WitherTorch.Core.Servers.Software
{
    /// <summary>
    /// 表示一個與 Fabric 或與之相似之伺服器軟體相關聯的介面
    /// </summary>
    public interface IForgeLikeSoftwareSoftware : ISoftwareContext
    {
        /// <summary>
        /// 取得與 Minecraft 版本對應的 Forge 版本列表
        /// </summary>
        /// <param name="minecraftVersion">Minecraft 版本</param>
        /// <returns></returns>
        string[] GetForgeVersionsFromMinecraftVersion(string minecraftVersion);
    }
}
