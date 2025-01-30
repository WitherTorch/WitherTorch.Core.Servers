
using WitherTorch.Core.Software;
using WitherTorch.Core.Servers.Utils;

namespace WitherTorch.Core.Servers
{
    partial class Spigot
    {
        private static readonly SoftwareContextPrivate _software = new SoftwareContextPrivate();

        /// <summary>
        /// 取得與 <see cref="Spigot"/> 相關聯的軟體上下文
        /// </summary>
        public static ISoftwareContext Software => _software;

        private sealed class SoftwareContextPrivate : SoftwareContextBase<Spigot>
        {
            public SoftwareContextPrivate() : base(SoftwareId) { }

            public override string[] GetSoftwareVersions() => SpigotAPI.Versions;

            public override Spigot? CreateServerInstance(string serverDirectory) => new Spigot(serverDirectory);
        }
    }
}
