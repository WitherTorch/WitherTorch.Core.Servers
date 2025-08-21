
using System.Collections.Generic;
using System.Threading.Tasks;

using WitherTorch.Core.Servers.Utils;

namespace WitherTorch.Core.Servers
{
    partial class SpigotServerBase
    {
        /// <summary>
        /// SpigotMC 所提供之伺服器軟體的上下文基底類別
        /// </summary>
        /// <typeparam name="T">與此類別相關聯的伺服器類型</typeparam>
        protected abstract new class SoftwareContextBase<T> : JavaEditionServerBase.SoftwareContextBase<T> where T : SpigotServerBase
        {
            /// <summary>
            /// <see cref="SoftwareContextBase{T}"/> 的建構子
            /// </summary>
            /// <param name="softwareId">軟體的唯一辨識符 (ID)</param>
            protected SoftwareContextBase(string softwareId) : base(softwareId) { }

            /// <inheritdoc/>
            public override Task<IReadOnlyList<string>> GetSoftwareVersionsAsync() => SpigotAPI.GetVersionsAsync();
        }
    }
}
