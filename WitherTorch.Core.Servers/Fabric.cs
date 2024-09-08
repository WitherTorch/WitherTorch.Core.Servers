using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;

using WitherTorch.Core.Servers.Utils;

namespace WitherTorch.Core.Servers
{
    /// <summary>
    /// Fabric 伺服器
    /// </summary>
    public class Fabric : AbstractJavaEditionServer<Fabric>
    {
        private const string manifestListURL = "https://meta.fabricmc.net/v2/versions/game";
        private const string manifestListURLForLoader = "https://meta.fabricmc.net/v2/versions/loader";
        private static readonly Lazy<string[]> _versionsLazy = new Lazy<string[]>(LoadVersionList, LazyThreadSafetyMode.ExecutionAndPublication);
        private static readonly Lazy<VersionStruct[]> _loaderVersionsLazy =
            new Lazy<VersionStruct[]>(LoadFabricLoaderVersions, LazyThreadSafetyMode.ExecutionAndPublication);
        private static readonly Lazy<string[]> _loaderVersionKeysLazy =
            new Lazy<string[]>(() =>
            {
                VersionStruct[] loaderVersions = _loaderVersionsLazy.Value;
                int length = loaderVersions.Length;
                if (length <= 0)
                    return Array.Empty<string>();
                string[] result = new string[length];
                for (int i = 0; i < length; i++)
                    result[i] = loaderVersions[i].Version;
                return result;
            }, LazyThreadSafetyMode.ExecutionAndPublication);
        private readonly StringBuilder _readableVersionBuilder = new StringBuilder();

        private string _minecraftVersion;
        private string _fabricLoaderVersion;
        private JavaRuntimeEnvironment environment;
        readonly IPropertyFile[] propertyFiles = new IPropertyFile[1];
        public JavaPropertyFile ServerPropertiesFile => propertyFiles[0] as JavaPropertyFile;

        static Fabric()
        {
            CallWhenStaticInitialize();
            SoftwareID = "fabric";
        }

        public override string ServerVersion => _minecraftVersion;

        private static string[] LoadVersionList()
        {
            try
            {
                return LoadVersionListInternal() ?? Array.Empty<string>();
            }
            catch (Exception)
            {
            }
            return Array.Empty<string>();
        }

        private static string[] LoadVersionListInternal()
        {
            string manifestString = CachedDownloadClient.Instance.DownloadString(manifestListURL);
            if (string.IsNullOrEmpty(manifestString))
                return null;
            VersionStruct[] array = JsonConvert.DeserializeObject<VersionStruct[]>(manifestString);
            if (array is null)
                return null;
            int length = array.Length;
            if (length <= 0)
                return null;
            List<string> versionList = new List<string>(length);
            string[] versions = MojangAPI.Versions;
            for (int i = 0; i < length; i++)
            {
                string version = array[i].Version;
                if (versions.Contains(version))
                    versionList.Add(version);
            }
            Array.Sort(versions, MojangAPI.VersionComparer.Instance.Reverse());
            return versions;
        }

        private static VersionStruct[] LoadFabricLoaderVersions()
        {
            try
            {
                return LoadFabricLoaderVersionsInternal() ?? Array.Empty<VersionStruct>();
            }
            catch (Exception)
            {
            }
            return Array.Empty<VersionStruct>();
        }

        private static VersionStruct[] LoadFabricLoaderVersionsInternal()
        {
            string manifestString = CachedDownloadClient.Instance.DownloadString(manifestListURLForLoader);
            if (string.IsNullOrEmpty(manifestString))
                return null;
            return JsonConvert.DeserializeObject<VersionStruct[]>(manifestString);
        }

        public static string GetLatestStableFabricLoaderVersion()
        {
            VersionStruct[] loaderVersions = _loaderVersionsLazy.Value;
            int count = loaderVersions.Length;
            for (int i = 0; i < count; i++)
            {
                VersionStruct loaderVersion = loaderVersions[i];
                if (loaderVersion.Stable)
                    return loaderVersion.Version;
            }
            return count > 0 ? loaderVersions[0].Version : null;
        }

        public override bool ChangeVersion(int versionIndex)
            => ChangeVersion(versionIndex, GetLatestStableFabricLoaderVersion());

        public bool ChangeVersion(int versionIndex, string fabricLoaderVersion)
        {
            return InstallSoftware(_versionsLazy.Value[versionIndex], fabricLoaderVersion);
        }

        private bool InstallSoftware(string minecraftVersion)
            => InstallSoftware(minecraftVersion, GetLatestStableFabricLoaderVersion());

        private bool InstallSoftware(string minecraftVersion, string fabricLoaderVersion)
        {
            if (string.IsNullOrEmpty(minecraftVersion) || string.IsNullOrEmpty(fabricLoaderVersion))
                return false;
            InstallTask task = new InstallTask(this, minecraftVersion + "-" + fabricLoaderVersion);
            OnServerInstalling(task);
            void onInstallFinished(object sender, EventArgs e)
            {
                if (!(sender is InstallTask _task))
                    return;
                task.InstallFinished -= onInstallFinished;
                _minecraftVersion = minecraftVersion;
                _fabricLoaderVersion = fabricLoaderVersion;
                mojangVersionInfo = null;
                OnServerVersionChanged();
            }
            task.InstallFinished += onInstallFinished;
            FabricInstaller.Instance.Install(task, minecraftVersion, fabricLoaderVersion);
            return true;
        }

        public override string GetReadableVersion()
        {
            StringBuilder readableVersionBuilder = _readableVersionBuilder.Clear();
            readableVersionBuilder.Append(_minecraftVersion);
            readableVersionBuilder.Append('-');
            readableVersionBuilder.Append(_fabricLoaderVersion);
            return readableVersionBuilder.ToString();
        }

        /// <summary>
        /// 取得 Fabric Loader 的版本號
        /// </summary>
        public string FabricLoaderVersion => _fabricLoaderVersion;

        public override RuntimeEnvironment GetRuntimeEnvironment()
        {
            return environment;
        }

        public override IPropertyFile[] GetServerPropertyFiles()
        {
            return propertyFiles;
        }

        public override string[] GetSoftwareVersions()
        {
            return _versionsLazy.Value;
        }

        public static string[] GetLoaderVersions()
        {
            return _loaderVersionKeysLazy.Value;
        }

        public override void RunServer(RuntimeEnvironment environment)
        {
            if (!_isStarted)
            {
                if (environment is null)
                    environment = RuntimeEnvironment.JavaDefault;
                if (environment is JavaRuntimeEnvironment javaRuntimeEnvironment)
                {
                    string path = Path.Combine(ServerDirectory, "fabric-server-launch.jar");
                    if (File.Exists(path))
                    {
                        string javaPath = javaRuntimeEnvironment.JavaPath;
                        if (javaPath is null || !File.Exists(javaPath))
                        {
                            javaPath = RuntimeEnvironment.JavaDefault.JavaPath;
                        }
                        ProcessStartInfo startInfo = new ProcessStartInfo
                        {
                            FileName = javaPath,
                            Arguments = string.Format("-Dfile.encoding=UTF8 -Dsun.stdout.encoding=UTF8 -Dsun.stderr.encoding=UTF8 {0} -jar \"{1}\" {2}"
                            , javaRuntimeEnvironment.JavaPreArguments ?? RuntimeEnvironment.JavaDefault.JavaPreArguments
                            , path
                            , javaRuntimeEnvironment.JavaPostArguments ?? RuntimeEnvironment.JavaDefault.JavaPostArguments),
                            WorkingDirectory = ServerDirectory,
                            CreateNoWindow = true,
                            ErrorDialog = true,
                            UseShellExecute = false,
                        };
                        _process.StartProcess(startInfo);
                    }
                }
            }
        }

        /// <inheritdoc/>
        public override void StopServer(bool force)
        {
            if (_isStarted)
            {
                if (force)
                {
                    _process.Kill();
                }
                else
                {
                    _process.InputCommand("stop");
                }
            }
        }

        protected override MojangAPI.VersionInfo BuildVersionInfo()
        {
            return FindVersionInfo(_minecraftVersion);
        }

        /// <inheritdoc/>
        protected override bool CreateServer()
        {
            try
            {
                propertyFiles[0] = new JavaPropertyFile(Path.GetFullPath(Path.Combine(ServerDirectory, "./server.properties")));
            }
            catch (Exception)
            {
                return false;
            }
            return true;
        }

        /// <inheritdoc/>
        protected override bool OnServerLoading()
        {
            JsonPropertyFile serverInfoJson = ServerInfoJson;
            string minecraftVersion = serverInfoJson["version"]?.ToString();
            if (string.IsNullOrEmpty(minecraftVersion))
                return false;
            _minecraftVersion = minecraftVersion; 
            JToken fabricVerNode = serverInfoJson["fabric-version"];
            if (fabricVerNode?.Type == JTokenType.String)
            {
                _fabricLoaderVersion = fabricVerNode.ToString();
            }
            else
            {
                return false;
            }
            try
            {
                propertyFiles[0] = new JavaPropertyFile(Path.GetFullPath(Path.Combine(ServerDirectory, "./server.properties")));
                string jvmPath = (serverInfoJson["java.path"] as JValue)?.ToString() ?? null;
                string jvmPreArgs = (serverInfoJson["java.preArgs"] as JValue)?.ToString() ?? null;
                string jvmPostArgs = (serverInfoJson["java.postArgs"] as JValue)?.ToString() ?? null;
                if (jvmPath != null || jvmPreArgs != null || jvmPostArgs != null)
                {
                    environment = new JavaRuntimeEnvironment(jvmPath, jvmPreArgs, jvmPostArgs);
                }
            }
            catch (Exception)
            {
                return false;
            }
            return true;
        }

        /// <inheritdoc/>
        public override void SetRuntimeEnvironment(RuntimeEnvironment environment)
        {
            if (environment is JavaRuntimeEnvironment javaRuntimeEnvironment)
                this.environment = javaRuntimeEnvironment;
            else if (environment is null)
                this.environment = null;
        }

        protected override bool BeforeServerSaved()
        {
            JsonPropertyFile serverInfoJson = ServerInfoJson;
            serverInfoJson["version"] = _minecraftVersion;
            serverInfoJson["fabric-version"] = _fabricLoaderVersion;
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

        public override bool UpdateServer()
        {
            return InstallSoftware(_minecraftVersion);
        }
    }
}
