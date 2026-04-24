using System.Runtime.CompilerServices;
using System.Threading.Tasks;

using WitherTorch.Core.Runtime;
using WitherTorch.Core.Servers.Utils;

namespace WitherTorch.Core.Servers
{
    /// <summary>
    /// 此類別為 Java 版伺服器軟體 (Java 版官方伺服器之第三方修改版本) 之基底類別，無法直接使用
    /// </summary>
    public abstract partial class JavaDedicatedServerBase : JavaServerBase
    {
        /// <summary>
        /// Java 版伺服器的版本資料
        /// </summary>
        protected MojangAPI.VersionInfo? _versionInfo;

        /// <summary>
        /// <see cref="JavaDedicatedServerBase"/> 的建構子
        /// </summary>
        /// <param name="serverDirectory">伺服器資料夾路徑</param>
        protected JavaDedicatedServerBase(string serverDirectory) : base(serverDirectory) { }

        /// <summary>
        /// 子類別需實作此函式，作為 <c>mojangVersionInfo</c> 未主動生成時的備用生成方案
        /// </summary>
        protected abstract MojangAPI.VersionInfo? BuildVersionInfo();

        /// <summary>
        /// 取得這個伺服器的版本詳細資訊 (由 Mojang API 提供)
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public MojangAPI.VersionInfo? GetMojangVersionInfo()
        {
            MojangAPI.VersionInfo? mojangVersionInfo = _versionInfo;
            if (mojangVersionInfo is null)
                _versionInfo = mojangVersionInfo = BuildVersionInfo();
            return mojangVersionInfo;
        }

        /// <summary>
        /// 取得與指定的版本號相對應的版本資料
        /// </summary>
        /// <param name="version">要查找的版本號</param>
        /// <returns></returns>
        protected static async Task<MojangAPI.VersionInfo?> FindVersionInfoAsync(string version)
        {
            if ((await MojangAPI.GetVersionDictionaryAsync()).TryGetValue(version, out MojangAPI.VersionInfo? result))
                return result;
            return null;
        }

        /// <inheritdoc/>
        protected override void StopServerCore(ILocalProcess process, bool force)
        {
            if (force)
            {
                process.Stop();
                return;
            }
            process.InputCommand("stop");
        }
    }
}
