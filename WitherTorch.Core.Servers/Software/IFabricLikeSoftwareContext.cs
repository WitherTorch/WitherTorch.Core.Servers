using System.Collections.Generic;
using System.Threading.Tasks;

using WitherTorch.Core.Software;

namespace WitherTorch.Core.Servers.Software
{
    /// <summary>
    /// 表示一個 Fabric 或與之相似之伺服器軟體相關聯的介面
    /// </summary>
    public interface IFabricLikeSoftwareContext : ISoftwareContext
    {
        /// <summary>
        /// 取得軟體的載入器 (Loader) 版本列表
        /// </summary>
        /// <returns></returns>
        Task<IReadOnlyList<string>> GetSoftwareLoaderVersionsAsync();
    }
}
