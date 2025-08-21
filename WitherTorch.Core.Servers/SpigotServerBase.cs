using System;
using System.IO;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;

using WitherTorch.Core.Property;
using WitherTorch.Core.Servers.Utils;

namespace WitherTorch.Core.Servers
{
    /// <summary>
    /// SpigotMC 所提供之伺服器軟體 (<see cref="CraftBukkit"/>, <see cref="Spigot"/>) 的共同基礎類別
    /// </summary>
    public abstract partial class SpigotServerBase : JavaEditionServerBase
    {
        /// <summary>
        /// 設定檔案的陣列，此物件會在初次存取後才呼叫 <see cref="GetServerPropertyFilesCore"/> 進行生成
        /// </summary>
        protected readonly Lazy<IPropertyFile[]> _propertyFilesLazy;

        private string _version = string.Empty;
        private int _build = -1;

        /// <summary>
        /// 取得伺服器的 server.properties 設定檔案
        /// </summary>
        public JavaPropertyFile ServerPropertiesFile
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => (JavaPropertyFile)_propertyFilesLazy.Value[0];
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            private set
            {
                IPropertyFile[] propertyFiles = _propertyFilesLazy.Value;
                IPropertyFile propertyFile = propertyFiles[0];
                propertyFiles[0] = value;
                propertyFile.Dispose();
            }
        }

        /// <summary>
        /// 取得伺服器的 bukkit.yml 設定檔案
        /// </summary>
        public YamlPropertyFile BukkitYMLFile
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => (YamlPropertyFile)_propertyFilesLazy.Value[1];
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            private set
            {
                IPropertyFile[] propertyFiles = _propertyFilesLazy.Value;
                IPropertyFile propertyFile = propertyFiles[1];
                propertyFiles[1] = value;
                propertyFile.Dispose();
            }
        }

        /// <summary>
        /// <see cref="SpigotServerBase"/> 的建構子
        /// </summary>
        /// <param name="serverDirectory">伺服器資料夾路徑</param>
        protected SpigotServerBase(string serverDirectory) : base(serverDirectory)
        {
            _propertyFilesLazy = new Lazy<IPropertyFile[]>(GetServerPropertyFilesCore, LazyThreadSafetyMode.ExecutionAndPublication);
        }

        /// <inheritdoc/>
        public override string ServerVersion => _version;

        /// <inheritdoc/>
        public override InstallTask? GenerateInstallServerTask(string version)
            => new InstallTask(this, version, RunInstallServerTaskAsync);

        /// <summary>
        /// 子類別需覆寫此方法，並呼叫 <see cref="RunInstallServerTaskAsync(InstallTask, SpigotBuildTools.BuildTarget, CancellationToken)"/> 以自動處理安裝流程
        /// </summary>
        /// <param name="task">要用於傳輸安裝時期資訊的 <see cref="InstallTask"/> 物件</param>
        /// <param name="token">用於控制非同步操作是否取消的 <see cref="CancellationToken"/> 結構</param>
        /// <returns></returns>
        protected abstract ValueTask<bool> RunInstallServerTaskAsync(InstallTask task, CancellationToken token);

        /// <summary>
        /// <see cref="GenerateInstallServerTask(string)"/> 的核心功能，子類別可使用這個非同步方法來簡化安裝流程
        /// </summary>
        /// <param name="task">要用於傳輸安裝時期資訊的 <see cref="InstallTask"/> 物件</param>
        /// <param name="target">要建置的目標類型</param>
        /// <param name="token">用於控制非同步操作是否取消的 <see cref="CancellationToken"/> 結構</param>
        /// <returns></returns>
        protected async ValueTask<bool> RunInstallServerTaskAsync(InstallTask task, SpigotBuildTools.BuildTarget target, CancellationToken token)
        {
            string version = task.Version;
            MojangAPI.VersionInfo? versionInfo = await FindVersionInfoAsync(version);
            if (versionInfo is null)
                return false;
            int buildNumber = await SpigotAPI.GetBuildNumberAsync(version, token);
            if (buildNumber < 0 || token.IsCancellationRequested || !await SpigotBuildTools.InstallAsync(task, target, version, token))
                return false;
            _version = version;
            _build = buildNumber;
            _versionInfo = versionInfo;
            Thread.MemoryBarrier();
            OnServerVersionChanged();
            return true;
        }

        /// <inheritdoc/>
        public override string GetReadableVersion() => _version;

        /// <inheritdoc/>
        public override IPropertyFile[] GetServerPropertyFiles() => _propertyFilesLazy.Value;

        /// <summary>
        /// 子類別需覆寫此方法並傳回一個至少有2個元素的 <see cref="IPropertyFile"/> 陣列
        /// </summary>
        /// <remarks>
        /// 傳回值有以下要求: <br/>
        /// <list type="bullet">
        /// <item>第 0 元素必須是一個 <see cref="JavaPropertyFile"/> 物件</item>
        /// <item>第 1 元素必須是一個 <see cref="YamlPropertyFile"/> 物件</item>
        /// </list>
        /// </remarks>
        protected abstract IPropertyFile[] GetServerPropertyFilesCore();

        /// <inheritdoc/>
        protected override MojangAPI.VersionInfo? BuildVersionInfo() => FindVersionInfoAsync(_version).Result;

        /// <inheritdoc/>
        protected override bool CreateServerCore() => true;

        /// <inheritdoc/>
        protected override bool LoadServerCore(JsonPropertyFile serverInfoJson)
        {
            string? version = serverInfoJson["version"]?.GetValue<string>();
            if (version is null || version.Length <= 0)
                return false;
            _version = version;
            JsonNode? buildNode = serverInfoJson["build"];
            if (buildNode is null || buildNode.GetValueKind() != JsonValueKind.Number)
                _build = 0;
            else
                _build = buildNode.GetValue<int>();
            return base.LoadServerCore(serverInfoJson);
        }

        /// <inheritdoc/>
        protected override bool SaveServerCore(JsonPropertyFile serverInfoJson)
        {
            serverInfoJson["version"] = _version;
            serverInfoJson["build"] = _build;
            return base.SaveServerCore(serverInfoJson);
        }
    }
}
