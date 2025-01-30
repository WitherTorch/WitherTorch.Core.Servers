using System.Diagnostics;
using System.IO;
using System.Runtime.CompilerServices;

using WitherTorch.Core.Property;
using WitherTorch.Core.Servers.Utils;

namespace WitherTorch.Core.Servers
{
    /// <summary>
    /// 此類別為 Java 版伺服器軟體之基底類別，無法直接使用
    /// </summary>
    public abstract partial class JavaEditionServerBase : LocalServer
    {
        protected MojangAPI.VersionInfo? _versionInfo;
        private JavaRuntimeEnvironment? _environment;

        protected JavaEditionServerBase(string serverDirectory) : base(serverDirectory) { }

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

        protected static MojangAPI.VersionInfo? FindVersionInfo(string version)
        {
            if (MojangAPI.VersionDictionary.TryGetValue(version, out MojangAPI.VersionInfo? result))
                return result;
            return null;
        }

        protected override bool LoadServerCore(JsonPropertyFile serverInfoJson)
        {
            string? jvmPath = serverInfoJson["java.path"]?.GetValue<string>() ?? null;
            string? jvmPreArgs = serverInfoJson["java.preArgs"]?.GetValue<string>() ?? null;
            string? jvmPostArgs = serverInfoJson["java.postArgs"]?.GetValue<string>() ?? null;
            if (jvmPath is not null || jvmPreArgs is not null || jvmPostArgs is not null)
            {
                _environment = new JavaRuntimeEnvironment(jvmPath, jvmPreArgs, jvmPostArgs);
            }
            return true;
        }

        protected override bool SaveServerCore(JsonPropertyFile serverInfoJson)
        {
            JavaRuntimeEnvironment? environment = _environment;
            if (environment is null)
            {
                serverInfoJson["java.path"] = null;
                serverInfoJson["java.preArgs"] = null;
                serverInfoJson["java.postArgs"] = null;
            }
            else
            {
                serverInfoJson["java.path"] = environment.JavaPath;
                serverInfoJson["java.preArgs"] = environment.JavaPreArguments;
                serverInfoJson["java.postArgs"] = environment.JavaPostArguments;
            }
            return true;
        }


        public override void SetRuntimeEnvironment(RuntimeEnvironment? environment)
        {
            if (environment is JavaRuntimeEnvironment javaRuntimeEnvironment)
                _environment = javaRuntimeEnvironment;
            else if (environment is null)
                _environment = null;
        }


        public override RuntimeEnvironment? GetRuntimeEnvironment()
        {
            return _environment;
        }

        protected override ProcessStartInfo? PrepareProcessStartInfo(RuntimeEnvironment? environment)
        {
            if (environment is JavaRuntimeEnvironment currentEnv)
                return PrepareProcessStartInfoCore(currentEnv);
            return PrepareProcessStartInfoCore(RuntimeEnvironment.JavaDefault);
        }

        protected virtual ProcessStartInfo? PrepareProcessStartInfoCore(JavaRuntimeEnvironment environment)
        {
            string jarPath = GetServerJarPath();
            if (!File.Exists(jarPath))
                return null;
            return new ProcessStartInfo
            {
                FileName = environment.JavaPath ?? "java",
                Arguments = string.Format(
                    "-Djline.terminal=jline.UnsupportedTerminal -Dfile.encoding=UTF8 -Dsun.stdout.encoding=UTF8 -Dsun.stderr.encoding=UTF8 {0} -jar \"{1}\" {2}",
                    environment.JavaPreArguments ?? string.Empty,
                    jarPath,
                    environment.JavaPostArguments ?? string.Empty
                ),
                WorkingDirectory = ServerDirectory,
                CreateNoWindow = true,
                ErrorDialog = true,
                UseShellExecute = false,
            };
        }

        protected abstract string GetServerJarPath();

        protected override void StopServerCore(SystemProcess process, bool force)
        {
            if (force)
            {
                process.Kill();
                return;
            }
            process.InputCommand("stop");
        }

        protected abstract class SoftwareContextBase<T> : Core.Software.SoftwareContextBase<T> where T : JavaEditionServerBase
        {
            public SoftwareContextBase(string softwareId) : base(softwareId) { }

            public override bool TryInitialize()
            {
                MojangAPI.Initialize(); //呼叫 Mojang API 進行版本列表提取
                return true;
            }
        }
    }
}
