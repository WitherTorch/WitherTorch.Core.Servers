﻿using System.Runtime.CompilerServices;

using WitherTorch.Core.Servers.Utils;

namespace WitherTorch.Core.Servers
{
    /// <summary>
    /// 此類別為 Java 版伺服器軟體之基底類別，無法直接使用
    /// </summary>
    public abstract class AbstractJavaEditionServer<T> : LocalServer<T>, IJavaEditionServer where T : AbstractJavaEditionServer<T>
    {
        protected MojangAPI.VersionInfo? mojangVersionInfo;

        protected static void CallWhenStaticInitialize()
        {
            SoftwareRegistrationDelegate = MojangAPI.Initialize; //呼叫 Mojang API 進行版本列表提取
        }

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
            MojangAPI.VersionInfo? mojangVersionInfo = this.mojangVersionInfo;
            if (mojangVersionInfo is null)
                this.mojangVersionInfo = mojangVersionInfo = BuildVersionInfo();
            return mojangVersionInfo;
        }

        protected static MojangAPI.VersionInfo? FindVersionInfo(string version)
        {
            if (MojangAPI.VersionDictionary.TryGetValue(version, out MojangAPI.VersionInfo? result))
                return result;
            return null;
        }

        /// <inheritdoc cref="Server.RunServer(RuntimeEnvironment?)"/>
        public override bool RunServer(RuntimeEnvironment? environment)
        {
            return RunServer(environment as JavaRuntimeEnvironment);
        }

        /// <inheritdoc cref="Server.RunServer(RuntimeEnvironment?)"/>
        public abstract bool RunServer(JavaRuntimeEnvironment? environment);
    }

    public interface IJavaEditionServer
    {
        Utils.MojangAPI.VersionInfo? GetMojangVersionInfo();
    }
}
