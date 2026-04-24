using System;
using System.Threading;
using System.Threading.Tasks;

using WitherTorch.Core.Tagging;

namespace WitherTorch.Core.Servers.Runtime
{
    partial class JavaRuntimeEnvironment
    {
        private static readonly FactoryPrivate _factory = new FactoryPrivate();

        /// <summary>
        /// 取得與 <see cref="JavaRuntimeEnvironment"/> 相關聯的工廠物件
        /// </summary>
        public static IPersistentTagFactory Factory => _factory;

        private sealed class FactoryPrivate : IPersistentTagFactory
        {
            public IPersistentTag Create() => new JavaRuntimeEnvironment();

            public Type GetTagType() => typeof(JavaRuntimeEnvironment);

            public string GetTagTypeId() => "java_environment";

            public Task<bool> TryInitializeAsync(CancellationToken cancellationToken) => Task.FromResult(true);
        }
    }
}
