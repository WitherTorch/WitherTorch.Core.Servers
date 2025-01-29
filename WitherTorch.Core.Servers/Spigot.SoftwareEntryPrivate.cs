
using WitherTorch.Core.Software;
using WitherTorch.Core.Servers.Utils;

namespace WitherTorch.Core.Servers
{
    partial class Spigot
    {
        private static readonly SoftwareEntryPrivate _software = new SoftwareEntryPrivate();

        public static ISoftwareEntry Software => _software;

        private sealed class SoftwareEntryPrivate : SoftwareEntryBase<Spigot>
        {
            public SoftwareEntryPrivate() : base(SoftwareId) { }

            public override string[] GetSoftwareVersions() => SpigotAPI.Versions;

            public override Spigot? CreateServerInstance(string serverDirectory) => new Spigot(serverDirectory);
        }
    }
}
