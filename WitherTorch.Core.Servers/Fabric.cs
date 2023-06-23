using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
#if NET472
using System.Net;
using System.Text;
#elif NET5_0
using System.Net.Http;
#endif
using System.Threading;
using WitherTorch.Core.Servers.Utils;
using WitherTorch.Core.Utils;
using YamlDotNet.Core.Tokens;

namespace WitherTorch.Core.Servers
{
    /// <summary>
    /// Fabric 伺服器
    /// </summary>
    public class Fabric : AbstractJavaEditionServer<Fabric>
    {
        private const string manifestListURL = "https://meta.fabricmc.net/v2/versions/game";
        private const string manifestListURLForLoader = "https://meta.fabricmc.net/v2/versions/loader";
        internal static string[] versions;
        internal static VersionStruct[] loaderVersions;
        protected bool _isStarted;

        protected SystemProcess process;
        private string versionString;
        private string fabricLoaderVersion;
        private JavaRuntimeEnvironment environment;
        readonly IPropertyFile[] propertyFiles = new IPropertyFile[1];
        public JavaPropertyFile ServerPropertiesFile => propertyFiles[0] as JavaPropertyFile;

        static Fabric()
        {
            CallWhenStaticInitialize();
            SoftwareID = "fabric";
        }

        public override string ServerVersion => versionString;

        private static string[] LoadVersionList()
        {
            var comparer = MojangAPI.VersionComparer.Instance;
            var versions = MojangAPI.Versions;
            if (comparer is null)
            {
                using (AutoResetEvent trigger = new AutoResetEvent(false))
                {
                    void trig(object sender, EventArgs e)
                    {
                        trigger.Set();
                    }
                    MojangAPI.Initialized += trig;
                    if (versions is null)
                    {
                        trigger.WaitOne();
                    }
                    MojangAPI.Initialized -= trig;
                    comparer = MojangAPI.VersionComparer.Instance;
                }
            }
            List<string> versionList = null;
            try
            {
                string manifestString = CachedDownloadClient.Instance.DownloadString(manifestListURL);
                if (manifestString != null)
                {
                    VersionStruct[] array = JsonConvert.DeserializeObject<VersionStruct[]>(manifestString);
                    int count = array.Length;
                    versionList = new List<string>(count);
                    for (int i = 0; i < count; i++)
                    {
                        string version = array[i].Version;
                        if (versions.Contains(version))
                            versionList.Add(version);
                    }
                }
            }
            catch (Exception)
            {

            }
            if (versionList is object)
            {
                versions = versionList.ToArray();
                Array.Sort(versions, comparer);
                Array.Reverse(versions);
                return Fabric.versions = versions;
            }
            else
            {
                return Fabric.versions = null;
            }
        }

        private static VersionStruct[] LoadFabricLoaderVersions()
        {
            try
            {
                string manifestString = CachedDownloadClient.Instance.DownloadString(manifestListURLForLoader);
                if (manifestString is null)
                    return loaderVersions = null;
                else
                    return loaderVersions = JsonConvert.DeserializeObject<VersionStruct[]>(manifestString);
            }
            catch (Exception)
            {
                return loaderVersions = null;
            }
        }

        public static string GetLatestStableFabricLoaderVersion()
        {
            VersionStruct[] loaderVersions = Fabric.loaderVersions ?? LoadFabricLoaderVersions();
            if (loaderVersions is object)
            {
                int count = loaderVersions.Length;
                for (int i = 0; i < count; i++)
                {
                    VersionStruct loaderVersion = loaderVersions[i];
                    if (loaderVersion.Stable)
                        return loaderVersion.Version;
                }
                return count > 0 ? loaderVersions[0].Version : null;
            }
            return null;
        }

        public override bool ChangeVersion(int versionIndex)
            => ChangeVersion(versionIndex, GetLatestStableFabricLoaderVersion());

        public bool ChangeVersion(int versionIndex, string fabricVersion)
        {
            string[] versions = Fabric.versions ?? LoadVersionList();
            if (versions is object)
            {
                versionString = versions[versionIndex];
                BuildVersionInfo();
                fabricLoaderVersion = fabricVersion;
                _cache = null;
                InstallSoftware(fabricVersion);
                return true;
            }
            else
            {
                return false;
            }
        }

        InstallTask installingTask;

        private void InstallSoftware(string fabricVersion)
        {
            installingTask = new InstallTask(this);
            OnServerInstalling(installingTask);
            FabricInstaller.Instance.Install(installingTask, versionString, fabricVersion);
        }

        public override AbstractProcess GetProcess()
        {
            return process;
        }

        string _cache;
        public override string GetReadableVersion()
        {
            return _cache ?? (_cache = versionString + "-" + fabricLoaderVersion);
        }

        /// <summary>
        /// 取得 Fabric Loader 的版本號
        /// </summary>
        public string FabricLoaderVersion => fabricLoaderVersion;

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
            return versions ?? LoadVersionList();
        }

        static string[] loaderVersionKeys;
        public static string[] GetLoaderVersions()
        {
            string[] loaderVersionKeys = Fabric.loaderVersionKeys;
            if (loaderVersionKeys is null)
            {
                VersionStruct[] loaderVersions = Fabric.loaderVersions ?? LoadFabricLoaderVersions();
                if (loaderVersions is object)
                {
                    int length = loaderVersions.Length;
                    loaderVersionKeys = new string[length];
                    for (int i = 0; i < length; i++)
                    {
                        loaderVersionKeys[i] = loaderVersions[i].Version;
                    }
                    Fabric.loaderVersionKeys = loaderVersionKeys;
                }
            }
            return loaderVersionKeys;
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
                        process.StartProcess(startInfo);
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
                    process.Kill();
                }
                else
                {
                    process.InputCommand("stop");
                }
            }
        }
        protected override void BuildVersionInfo()
        {
            MojangAPI.VersionDictionary.TryGetValue(versionString, out mojangVersionInfo);
        }

        /// <inheritdoc/>
        protected override bool CreateServer()
        {
            try
            {
                process = new SystemProcess();
                process.ProcessStarted += delegate (object sender, EventArgs e) { _isStarted = true; };
                process.ProcessEnded += delegate (object sender, EventArgs e) { _isStarted = false; };
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
            try
            {
                JsonPropertyFile serverInfoJson = ServerInfoJson;
                versionString = serverInfoJson["version"].ToString();
                JToken fabricVerNode = serverInfoJson["fabric-version"];
                if (fabricVerNode?.Type == JTokenType.String)
                {
                    fabricLoaderVersion = fabricVerNode.ToString();
                }
                else
                {
                    return false;
                }
                process = new SystemProcess();
                process.ProcessStarted += delegate (object sender, EventArgs e) { _isStarted = true; };
                process.ProcessEnded += delegate (object sender, EventArgs e) { _isStarted = false; };
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
            serverInfoJson["version"] = versionString;
            serverInfoJson["fabric-version"] = fabricLoaderVersion;
            if (environment != null)
            {
                serverInfoJson["java.path"] = environment.JavaPath;
                serverInfoJson["java.preArgs"] = environment.JavaPreArguments;
                serverInfoJson["java.postArgs"] = environment.JavaPostArguments;
            }
            else
            {
                serverInfoJson["java.path"] = null;
                serverInfoJson["java.preArgs"] = null;
                serverInfoJson["java.postArgs"] = null;
            }
            return true;
        }

        public override bool UpdateServer()
        {
            if (versions is null) LoadVersionList();
            return ChangeVersion(Array.IndexOf(versions, versionString));
        }
    }
}
