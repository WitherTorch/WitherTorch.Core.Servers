using System.Threading;
using System.Threading.Tasks;

using WitherTorch.Core.Servers.Utils;

namespace WitherTorch.Core.Servers
{
    partial class JavaDedicatedServerBase
    {
        /// <summary>
        /// Java 版伺服器軟體的上下文基底類別
        /// </summary>
        /// <typeparam name="T">與此類別相關聯的伺服器類型</typeparam>
        /// <remarks>此基底類別的 <see cref="TryInitializeAsync"/> 會自動呼叫 <see cref="MojangAPI.InitializeAsync"/> 來初始化 Minecraft 版本列表，子類別無須二次呼叫</remarks>
        protected abstract class SoftwareContextBase<T> : Core.Software.SoftwareContextBase<T> where T : JavaDedicatedServerBase
        {
            /// <summary>
            /// <see cref="SoftwareContextBase{T}"/> 的建構子
            /// </summary>
            /// <param name="softwareId">軟體的唯一辨識符 (ID)</param>
            protected SoftwareContextBase(string softwareId) : base(softwareId) { }

            /// <inheritdoc/>
            public override async Task<bool> TryInitializeAsync(CancellationToken token)
            {
                if (token.IsCancellationRequested)
                    return false;
                await MojangAPI.InitializeAsync(); //呼叫 Mojang API 進行版本列表提取
                return true;
            }
        }
    }
}
