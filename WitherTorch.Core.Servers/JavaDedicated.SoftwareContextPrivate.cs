using System;
using System.Buffers;
using System.Collections.Generic;

using WitherTorch.Core.Servers.Utils;
using WitherTorch.Core.Software;

namespace WitherTorch.Core.Servers
{
    partial class JavaDedicated
    {
        private static readonly SoftwareContextPrivate _software = new SoftwareContextPrivate();

        /// <summary>
        /// 取得與 <see cref="JavaDedicated"/> 相關聯的軟體上下文
        /// </summary>
        public static ISoftwareContext Software => _software;

        private sealed class SoftwareContextPrivate : SoftwareContextBase<JavaDedicated>
        {
            private string[] _versions = Array.Empty<string>();

            public SoftwareContextPrivate() : base(SoftwareId) { }

            public override JavaDedicated? CreateServerInstance(string serverDirectory) => new JavaDedicated(serverDirectory);

            public override bool TryInitialize()
            {
                if (!base.TryInitialize())
                    return false;
                _versions = LoadVersionList();
                return true;
            }

            public override string[] GetSoftwareVersions() => _versions;

            private static string[] LoadVersionList()
            {
                IReadOnlyDictionary<string, MojangAPI.VersionInfo> dict = MojangAPI.VersionDictionary;
                int count = dict.Count;
                if (count <= 0)
                    return Array.Empty<string>();
                ArrayPool<string> pool = ArrayPool<string>.Shared;
                string[] buffer = pool.Rent(count);
                int i = 0;
                foreach (MojangAPI.VersionInfo info in dict.Values)
                {
                    if (!IsVanillaHasServer(info))
                        continue;
                    string? id = info.Id;
                    if (id is null)
                        continue;
                    buffer[i++] = id;
                }
                if (i <= 0)
                {
                    pool.Return(buffer, clearArray: true);
                    return Array.Empty<string>();
                }
                string[] result = new string[i];
                Array.Copy(buffer, result, i);
                pool.Return(buffer, clearArray: true);

                Array.Sort(result, MojangAPI.VersionComparer.Instance.Reverse());
                return result;
            }

            private static bool IsVanillaHasServer(MojangAPI.VersionInfo versionInfo)
            {
                DateTime time = versionInfo.ReleaseTime;
                int year = time.Year;
                int month = time.Month;
                int day = time.Day;
                if (year > 2012 || (year == 2012 && (month > 3 || (month == 3 && day >= 29)))) //1.2.5 開始有 server 版本 (1.2.5 發布日期: 2012/3/29)
                {
                    return true;
                }
                return false;
            }
        }
    }
}
