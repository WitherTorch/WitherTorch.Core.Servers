using WitherTorch.Core.Software;

namespace WitherTorch.Core.Servers.Software
{
    /// <summary>
    /// 表示一個與 Fabric 或與之相似之伺服器軟體相關聯的介面
    /// </summary>
    public interface IFabricLikeSoftwareSoftware : ISoftwareContext
    {
        /// <summary>
        /// 取得軟體的載入器 (Loader) 版本列表
        /// </summary>
        /// <returns></returns>
        string[] GetSoftwareLoaderVersions();
    }
}
