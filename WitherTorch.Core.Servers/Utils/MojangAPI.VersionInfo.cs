using System;
using System.Text.Json.Serialization;

#if NET8_0_OR_GREATER
#endif

namespace WitherTorch.Core.Servers.Utils
{
public static partial class MojangAPI
    {
        /// <summary>
        /// 表示一筆 Minecraft: Java Edition 的版本資料
        /// </summary>
        public sealed class VersionInfo : IComparable<string>, IComparable<VersionInfo>
        {
            /// <summary>
            /// 版本的唯一標識符 (ID)
            /// </summary>
            [JsonPropertyName("id")]
            public string? Id { get; set; }

            /// <summary>
            /// 版本的資訊清單位址
            /// </summary>
            [JsonPropertyName("url")]
            public string? ManifestURL { get; set; }

            /// <summary>
            /// 版本的發布時間
            /// </summary>
            [JsonPropertyName("releaseTime")]
            public DateTime ReleaseTime { get; set; }

            /// <summary>
            /// 版本的類型
            /// </summary>
            [JsonPropertyName("type")]
            public string? Type { get; set; }

            int IComparable<string>.CompareTo(string? other)
            {
                if (other is null) return 0;
                else if (VersionDictionary.ContainsKey(other))
                {
                    return ReleaseTime.CompareTo(VersionDictionary[other].ReleaseTime);
                }
                return 0;
            }

            int IComparable<VersionInfo>.CompareTo(VersionInfo? other)
            {
                if (other is null) return 1;
                else return ReleaseTime.CompareTo(other.ReleaseTime);
            }

            /// <inheritdoc/>
            public static bool operator <(VersionInfo a, VersionInfo b)
            {
                return (a as IComparable<VersionInfo>).CompareTo(b) < 0;
            }

            /// <inheritdoc/>
            public static bool operator <=(VersionInfo a, VersionInfo b)
            {
                return (a as IComparable<VersionInfo>).CompareTo(b) <= 0;
            }

            /// <inheritdoc/>
            public static bool operator >(VersionInfo a, VersionInfo b)
            {
                return (a as IComparable<VersionInfo>).CompareTo(b) > 0;
            }

            /// <inheritdoc/>
            public static bool operator >=(VersionInfo a, VersionInfo b)
            {
                return (a as IComparable<VersionInfo>).CompareTo(b) >= 0;
            }
        }
    }
}
