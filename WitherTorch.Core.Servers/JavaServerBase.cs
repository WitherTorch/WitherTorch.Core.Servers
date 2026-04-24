using System.IO;
using System.Text.Json.Nodes;

using WitherTorch.Core.Property;
using WitherTorch.Core.Runtime;
using WitherTorch.Core.Servers.Runtime;
using WitherTorch.Core.Servers.Utils;

namespace WitherTorch.Core.Servers
{
    /// <summary>
    /// 此類別為基於 Java 虛擬機啟動之伺服器軟體的基底類別，無法直接使用
    /// </summary>
    public abstract class JavaServerBase : LocalServerBase, IEnvironmentAssociated<JavaRuntimeEnvironment>
    {
        private const string LegacyJavaNodeHeader = "java";
        private const string LegacyJavaPathNode = LegacyJavaNodeHeader + ".path";
        private const string LegacyJavaPreArgsNode = LegacyJavaNodeHeader + ".preArgs";
        private const string LegacyJavaPostArgsNode = LegacyJavaNodeHeader + ".postArgs";

        private bool _hasLegacyJavaEnvData = false;

        /// <summary>
        /// 取得或設定與伺服器相關聯的 Java 執行環境 (可能為 <see langword="null"/> )
        /// </summary>
        public JavaRuntimeEnvironment? AssociatedEnvironment
        {
            get => TryGetPersistentTag(out JavaRuntimeEnvironment? result) ? result : null;
            set
            {
                RemovePersistentTags<JavaRuntimeEnvironment>();
                AddPersistentTag(value);
            }
        }

        /// <summary>
        /// <see cref="JavaServerBase"/> 的建構子
        /// </summary>
        /// <param name="serverDirectory">伺服器資料夾路徑</param>
        protected JavaServerBase(string serverDirectory) : base(serverDirectory) { }

        /// <inheritdoc/>
        protected override bool LoadServerCore(JsonPropertyFile serverInfoJson)
        {
            if (PropertyHelper.TryGetString(serverInfoJson, LegacyJavaPathNode, out string? jvmPath) |
                PropertyHelper.TryGetString(serverInfoJson, LegacyJavaPreArgsNode, out string? jvmPreArgs) |
                PropertyHelper.TryGetString(serverInfoJson, LegacyJavaPostArgsNode, out string? jvmPostArgs)) // 三條都必須執行，才可讀取到完整的執行環境
            {
                AddPersistentTag(new JavaRuntimeEnvironment(jvmPath, jvmPreArgs, jvmPostArgs));
                _hasLegacyJavaEnvData = true;
            }
            return true;
        }

        /// <inheritdoc/>
        protected override bool SaveServerCore(JsonPropertyFile serverInfoJson)
        {
            if (_hasLegacyJavaEnvData)
            {
                serverInfoJson[LegacyJavaPathNode] = null;
                serverInfoJson[LegacyJavaPreArgsNode] = null;
                serverInfoJson[LegacyJavaPostArgsNode] = null;
                if (serverInfoJson[LegacyJavaNodeHeader] is JsonObject obj && obj.Count <= 0)
                    serverInfoJson[LegacyJavaNodeHeader] = null;
            }
            return true;
        }

        /// <inheritdoc/>
        protected override bool TryPrepareProcessStartInfo(IRuntimeEnvironment environment, out LocalProcessStartInfo startInfo)
        {
            if (environment is not JavaRuntimeEnvironment javaEnv)
            {
                startInfo = default;
                return false;
            }
            return TryPrepareProcessStartInfoCore(javaEnv, out startInfo);
        }

        /// <inheritdoc cref="TryPrepareProcessStartInfo(IRuntimeEnvironment, out LocalProcessStartInfo)"/>
        protected virtual bool TryPrepareProcessStartInfoCore(JavaRuntimeEnvironment environment, out LocalProcessStartInfo startInfo)
        {
            string jarPath = GetServerJarPath();
            if (!File.Exists(jarPath))
            {
                startInfo = default;
                return false;
            }
            startInfo = new LocalProcessStartInfo(
                fileName: environment.JavaPath ?? "java",
                arguments: string.Format(
                    "-Djline.terminal=jline.UnsupportedTerminal -Dfile.encoding=UTF8 -Dsun.stdout.encoding=UTF8 -Dsun.stderr.encoding=UTF8 {0} -jar \"{1}\" {2}",
                    environment.JavaPreArguments ?? string.Empty,
                    jarPath,
                    environment.JavaPostArguments ?? string.Empty),
                workingDirectory: ServerDirectory);
            return true;
        }

        /// <summary>
        /// 取得伺服器的 JAR 程式檔案路徑
        /// </summary>
        /// <returns></returns>
        protected abstract string GetServerJarPath();
    }
}
