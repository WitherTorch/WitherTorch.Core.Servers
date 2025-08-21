using System.Collections.Generic;

#if NET8_0_OR_GREATER
#endif

namespace WitherTorch.Core.Servers.Utils
{
public static partial class MojangAPI
    {
        /// <summary>
        /// 版本字串的比較類別，此類別無法建立實體
        /// </summary>
        private sealed class InternalVersionComparer : IComparer<string>
        {
            private readonly IReadOnlyDictionary<string, VersionInfo> _versionDict;
            
            public InternalVersionComparer(IReadOnlyDictionary<string, VersionInfo> versionDict)
            {
                _versionDict = versionDict;
            }

            /// <inheritdoc/>
            public int Compare(string? x, string? y) => Compare(_versionDict, x, y);

            public static int Compare(IReadOnlyDictionary<string, VersionInfo> versionDict, string? x, string? y)
            {
                if (x is null)
                    return y is null ? 0 : -1;
                if (y is null)
                    return 1;
                if (!versionDict.TryGetValue(x, out VersionInfo? infoA))
                    return -1;
                if (versionDict.TryGetValue(y, out VersionInfo? infoB))
                    return infoA.ReleaseTime.CompareTo(infoB.ReleaseTime);
                return 1;
            }
        }

        /// <summary>
        /// 版本字串的比較類別，此類別無法建立實體
        /// </summary>
        public sealed class VersionComparer : IComparer<string>
        {
            private static readonly VersionComparer _instance = new VersionComparer();

            /// <summary>
            /// <see cref="VersionComparer"/> 的預設實體
            /// </summary>
            public static VersionComparer Instance => _instance;

            private VersionComparer() { }

            /// <inheritdoc/>
            public int Compare(string? x, string? y) => InternalVersionComparer.Compare(GetVersionDictionaryAsync().Result, x, y);
        }
    }
}
