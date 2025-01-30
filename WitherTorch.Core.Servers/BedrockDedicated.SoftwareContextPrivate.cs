
using System;
using System.IO;

using WitherTorch.Core.Software;

namespace WitherTorch.Core.Servers
{
    partial class BedrockDedicated
    {
        private static readonly SoftwareContextPrivate _software = new SoftwareContextPrivate();

        public static ISoftwareContext Software => _software;

        private sealed class SoftwareContextPrivate : SoftwareContextBase<BedrockDedicated>
        {
            private const string ManifestListURL = "https://withertorch-bds-helper.vercel.app/api/latest";

            private readonly Lazy<string[]> _versionsLazy = new Lazy<string[]>(LoadVersionList,
                System.Threading.LazyThreadSafetyMode.ExecutionAndPublication);

            public SoftwareContextPrivate() : base(SoftwareId) { }

            public override string[] GetSoftwareVersions() => _versionsLazy.Value;

            public override BedrockDedicated? CreateServerInstance(string serverDirectory) => new BedrockDedicated(serverDirectory);

            public override bool TryInitialize() => true;

            private static string[] LoadVersionList()
            {
                string? manifestString = CachedDownloadClient.Instance.DownloadString(ManifestListURL);
                if (manifestString is null)
                    return Array.Empty<string>();
#if NET5_0_OR_GREATER
            using (StringReader reader = new StringReader(manifestString))
            {
                while (reader.Peek() != -1)
                {
                    string? line = reader.ReadLine();
                    if (string.IsNullOrEmpty(line))
                        continue;
                    if (OperatingSystem.IsLinux())
                    {
                        if (line[..6] == "linux=" && Version.TryParse(line = line[6..], out _))
                        {
                            return new string[] { line };
                        }
                    }
                    else if (OperatingSystem.IsWindows())
                    {
                        if (line[..8] == "windows=" && Version.TryParse(line = line[8..], out _))
                        {
                            return new string[] { line };
                        }
                    }
                }
                reader.Close();
            }
#else
                PlatformID platformID = Environment.OSVersion.Platform;
                using (StringReader reader = new StringReader(manifestString))
                {
                    while (reader.Peek() != -1)
                    {
                        string line = reader.ReadLine();
                        switch (platformID)
                        {
                            case PlatformID.Unix:
                                if (line.StartsWith("linux=") && Version.TryParse(line = line.Substring(6), out _))
                                {
                                    return new string[] { line };
                                }
                                break;
                            case PlatformID.Win32NT:
                                if (line.StartsWith("windows=") && Version.TryParse(line = line.Substring(8), out _))
                                {
                                    return new string[] { line };
                                }
                                break;
                        }
                    }
                    reader.Close();
                }
#endif
                return Array.Empty<string>();
            }
        }
    }
}
