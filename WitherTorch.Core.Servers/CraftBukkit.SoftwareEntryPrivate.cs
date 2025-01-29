
using WitherTorch.Core.Servers.Utils;
using WitherTorch.Core.Software;

namespace WitherTorch.Core.Servers
{
    partial class CraftBukkit
    {
        private static readonly SoftwareEntryPrivate _software = new SoftwareEntryPrivate();
        public static ISoftwareEntry Software => _software;

        private sealed class SoftwareEntryPrivate : SoftwareEntryBase<CraftBukkit>
        {
            public SoftwareEntryPrivate() : base(SoftwareId) { }

            public override string[] GetSoftwareVersions() => SpigotAPI.Versions;

            public override CraftBukkit? CreateServerInstance(string serverDirectory) => new CraftBukkit(serverDirectory);
        }
    }
}
