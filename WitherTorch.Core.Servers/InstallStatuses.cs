namespace WitherTorch.Core.Servers
{
    /// <summary>
    /// 表示正在呼叫 Spigot 建置工具 (BuildTools) 的狀態
    /// </summary>
    public class SpigotBuildToolsStatus : ProcessStatus
    {
        /// <summary>
        /// 運作狀態的列舉類型
        /// </summary>
        public enum ToolState
        {
            /// <summary>
            /// 初始化
            /// </summary>
            Initialize,
            /// <summary>
            /// 更新工具
            /// </summary>
            Update,
            /// <summary>
            /// 建置檔案
            /// </summary>
            Build
        }

        private ToolState _state;

        /// <summary>
        /// 取得或設定建置工具目前的運作狀態
        /// </summary>
        public ToolState State
        {
            get
            {
                return _state;
            }
            set
            {
                _state = value;
                OnUpdated();
            }
        }

        /// <summary>
        /// <see cref="SpigotBuildToolsStatus"/> 的建構子
        /// </summary>
        /// <param name="state">建置工具的初始狀態</param>
        /// <param name="percentage">建置工具運行時的初始進度</param>
        public SpigotBuildToolsStatus(ToolState state, double percentage) : base(percentage)
        {
            State = state;
        }
    }

    /// <summary>
    /// 表示正在呼叫 Fabric 安裝程式的狀態
    /// </summary>
    public class FabricInstallerStatus : SpigotBuildToolsStatus
    {
        /// <summary>
        /// <see cref="FabricInstallerStatus"/> 的建構子
        /// </summary>
        /// <param name="state">安裝程式的初始狀態</param>
        /// <param name="percentage">安裝程式運行時的初始進度</param>
        public FabricInstallerStatus(ToolState state, double percentage) : base(state, percentage)
        {
        }
    }

    /// <summary>
    /// 表示正在呼叫 Quilt 安裝程式的狀態
    /// </summary>
    public class QuiltInstallerStatus : FabricInstallerStatus
    {
        /// <summary>
        /// <see cref="QuiltInstallerStatus"/> 的建構子
        /// </summary>
        /// <param name="state">安裝程式的初始狀態</param>
        /// <param name="percentage">安裝程式運行時的初始進度</param>
        public QuiltInstallerStatus(ToolState state, double percentage) : base(state, percentage)
        {
        }
    }
}
