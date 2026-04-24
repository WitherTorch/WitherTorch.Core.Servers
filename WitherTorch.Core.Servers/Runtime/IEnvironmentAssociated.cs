using WitherTorch.Core.Runtime;

namespace WitherTorch.Core.Servers.Runtime
{
    /// <summary>
    /// 繼承此介面的伺服器，可以動態取得所支援且關聯的執行環境物件
    /// </summary>
    public interface IEnvironmentAssociated<TEnvironment> where TEnvironment : IRuntimeEnvironment
    {
        /// <summary>
        /// 取得或設定伺服器所關聯的執行環境物件 (可能為 <see langword="null"/> )
        /// </summary>
        TEnvironment? AssociatedEnvironment { get; set; }
    }
}
