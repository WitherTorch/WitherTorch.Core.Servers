
using WitherTorch.Core.Servers.Utils;
using WitherTorch.Core.Software;

namespace WitherTorch.Core.Servers
{
    partial class CraftBukkit
    {
        private static readonly SoftwareContextPrivate _software = new SoftwareContextPrivate();

        /// <summary>
        /// 取得與 <see cref="CraftBukkit"/> 相關聯的軟體上下文
        /// </summary>
        public static ISoftwareContext Software => _software;

        private sealed class SoftwareContextPrivate : SoftwareContextBase<CraftBukkit>
        {
            public SoftwareContextPrivate() : base(SoftwareId) { }

            public override string[] GetSoftwareVersions() => SpigotAPI.Versions;

            public override CraftBukkit? CreateServerInstance(string serverDirectory) => new CraftBukkit(serverDirectory);
        }
    }
}
