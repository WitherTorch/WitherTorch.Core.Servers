using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;

using WitherTorch.Core.Servers.Utils;
using WitherTorch.Core.Utils;

namespace WitherTorch.Core.Servers
{
    /// <summary>
    /// Java 原版伺服器
    /// </summary>
    public class JavaDedicated : AbstractJavaEditionServer<JavaDedicated>
    {
        private static readonly Lazy<string[]> _versionsLazy = new Lazy<string[]>(() =>
        {
            List<string> result = new List<string>();
            var dict = MojangAPI.VersionDictionary;
            foreach (MojangAPI.VersionInfo info in dict.Values)
            {
                if (IsVanillaHasServer(info))
                    result.Add(info.Id);
            }
            string[] array = result.ToArray();
            Array.Sort(array, MojangAPI.VersionComparer.Instance.Reverse());
            return array;
        }, System.Threading.LazyThreadSafetyMode.PublicationOnly);

        static JavaDedicated()
        {
            CallWhenStaticInitialize();
            SoftwareID = "javaDedicated";
        }

        private string _version;
        private JavaRuntimeEnvironment _environment;
        private readonly IPropertyFile[] _propertyFiles = new IPropertyFile[1];

        public JavaPropertyFile ServerPropertiesFile => _propertyFiles[0] as JavaPropertyFile;
        public override string ServerVersion => _version;

        private static bool IsVanillaHasServer(MojangAPI.VersionInfo versionInfo)
        {
            DateTime time = versionInfo.ReleaseTime;
            int year = time.Year;
            int month = time.Month;
            int day = time.Day;
            if (year > 2012 || (year == 2012 && (month > 3 || (month == 3 && day >= 29)))) //1.2.5 開始有 server 版本 (1.2.5 發布日期: 2012/3/29)
            {
                return true;
            }
            return false;
        }

        private bool InstallSoftware(MojangAPI.VersionInfo versionInfo)
        {
            if (versionInfo is null)
                return false;
            InstallTask task = new InstallTask(this, versionInfo.Id);
            OnServerInstalling(task);
            task.ChangeStatus(PreparingInstallStatus.Instance);
            void onInstallFinished(object sender, EventArgs e)
            {
                if (!(sender is InstallTask _task))
                    return;
                _task.InstallFinished -= onInstallFinished;
                _version = versionInfo.Id;
                mojangVersionInfo = versionInfo;
                OnServerVersionChanged();
            };
            task.InstallFinished += onInstallFinished;
            Task.Factory.StartNew(() =>
            {
                try
                {
                    if (!InstallSoftware(task, versionInfo))
                        task.OnInstallFailed();
                }
                catch (Exception)
                {
                    task.OnInstallFailed();
                }
            }, default, TaskCreationOptions.LongRunning, TaskScheduler.Current);
            return true;
        }

        private bool InstallSoftware(InstallTask task, MojangAPI.VersionInfo versionInfo)
        {
            string manifestURL = versionInfo.ManifestURL;
            if (string.IsNullOrEmpty(manifestURL))
                return false;
            WebClient2 client = new WebClient2();
            InstallTaskWatcher watcher = new InstallTaskWatcher(task, client);
            JObject jsonObject = JsonConvert.DeserializeObject<JObject>(client.GetStringAsync(manifestURL).Result);
            if (watcher.IsStopRequested || jsonObject is null || !jsonObject.TryGetValue("downloads", out JToken token))
                return false;
            jsonObject = token as JObject;
            if (jsonObject is null || !jsonObject.TryGetValue("server", out token))
                return false;
            jsonObject = token as JObject;
            if (jsonObject is null || !jsonObject.TryGetValue("url", out token))
                return false;
            byte[] sha1;
            if (WTCore.CheckFileHashIfExist && jsonObject.TryGetValue("sha1", out JToken sha1Token))
                sha1 = HashHelper.HexStringToByte(sha1Token.ToString());
            else
                sha1 = null;
            watcher.Dispose();
            return FileDownloadHelper.AddTask(task: task, webClient: client, downloadUrl: token.ToString(),
                filename: Path.Combine(ServerDirectory, @"minecraft_server." + versionInfo.Id + ".jar"),
                hash: sha1, hashMethod: HashHelper.HashMethod.SHA1).HasValue;
        }

        /// <inheritdoc/>
        public override bool ChangeVersion(int versionIndex)
        {
            return InstallSoftware(MojangAPI.Versions[versionIndex]);
        }        
        
        private bool InstallSoftware(string version)
        {
            try
            {
                return InstallSoftware(FindVersionInfo(version));
            }
            catch (Exception)
            {
                return false;
            }
        }

        /// <inheritdoc/>
        public override string GetReadableVersion()
        {
            return _version;
        }

        /// <inheritdoc/>
        public override IPropertyFile[] GetServerPropertyFiles()
        {
            return _propertyFiles;
        }

        /// <inheritdoc/>
        public override string[] GetSoftwareVersions()
        {
            return _versionsLazy.Value;
        }

        protected override MojangAPI.VersionInfo BuildVersionInfo()
        {
            return FindVersionInfo(_version);
        }

        /// <inheritdoc/>
        protected override bool CreateServer()
        {
            JavaPropertyFile propertyFile;
            try
            {
                propertyFile = new JavaPropertyFile(Path.Combine(ServerDirectory, "./server.properties"));
            }
            catch (Exception)
            {
                return false;
            }
            _propertyFiles[0] = propertyFile;
            return true;
        }

        /// <inheritdoc/>
        protected override bool OnServerLoading()
        {
            JsonPropertyFile serverInfoJson = ServerInfoJson;
            string version = (serverInfoJson["version"] as JValue)?.ToString();
            if (version is null)
                return false;
            JavaPropertyFile propertyFile;
            try
            {
                propertyFile = new JavaPropertyFile(Path.Combine(ServerDirectory, "./server.properties"));
            }
            catch (Exception)
            {
                return false;
            }
            _version = version;
            _propertyFiles[0] = propertyFile;
            try
            {
                string jvmPath = (serverInfoJson["java.path"] as JValue)?.ToString() ?? null;
                string jvmPreArgs = (serverInfoJson["java.preArgs"] as JValue)?.ToString() ?? null;
                string jvmPostArgs = (serverInfoJson["java.postArgs"] as JValue)?.ToString() ?? null;
                if (jvmPath != null || jvmPreArgs != null || jvmPostArgs != null)
                {
                    _environment = new JavaRuntimeEnvironment(jvmPath, jvmPreArgs, jvmPostArgs);
                }
            }
            catch (Exception)
            {
                return false;
            }
            return true;
        }

        /// <inheritdoc/>
        public override RuntimeEnvironment GetRuntimeEnvironment()
        {
            return _environment;
        }

        /// <inheritdoc/>
        public override void SetRuntimeEnvironment(RuntimeEnvironment environment)
        {
            if (environment is JavaRuntimeEnvironment javaRuntimeEnvironment)
                _environment = javaRuntimeEnvironment;
            else if (environment is null)
                _environment = null;
        }

        /// <inheritdoc/>
        public override void RunServer(RuntimeEnvironment environment)
        {
            if (!_isStarted)
            {
                if (environment is null)
                    environment = RuntimeEnvironment.JavaDefault;
                if (environment is JavaRuntimeEnvironment javaRuntimeEnvironment)
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
                        , Path.Combine(ServerDirectory, @"minecraft_server." + GetReadableVersion() + ".jar")
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

        protected override bool BeforeServerSaved()
        {
            JsonPropertyFile serverInfoJson = ServerInfoJson;
            serverInfoJson["version"] = _version;
            JavaRuntimeEnvironment environment = _environment;
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
            return InstallSoftware(_version);
        }
    }
}
