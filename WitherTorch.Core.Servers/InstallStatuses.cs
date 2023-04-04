namespace WitherTorch.Core.Servers
{
    public class SpigotBuildToolsStatus : ProcessStatus
    {
        public enum ToolState
        {
            Initialize,
            Update,
            Build
        }

        private ToolState _state;

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

        public SpigotBuildToolsStatus(ToolState state, double percentage) : base(percentage)
        {
            State = state;
        }
    }

    public class FabricInstallerStatus : SpigotBuildToolsStatus
    {
        public FabricInstallerStatus(ToolState state, double percentage) : base(state, percentage)
        {
        }
    }
}
