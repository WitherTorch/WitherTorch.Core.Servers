using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

using WitherTorch.Core.Servers.Utils;
using WitherTorch.Core.Utils;

namespace WitherTorch.Core.Servers
{
    /// <summary>
    /// Paper 伺服器
    /// </summary>
    public class Paper : AbstractJavaEditionServer<Paper>
    {
        private const string manifestListURL = "https://api.papermc.io/v2/projects/paper";
        private const string manifestListURL2 = "https://api.papermc.io/v2/projects/paper/versions/{0}";
        private const string manifestListURL3 = "https://api.papermc.io/v2/projects/paper/versions/{0}/builds/{1}";
        private const string downloadURL = "https://api.papermc.io/v2/projects/paper/versions/{0}/builds/{1}/downloads/{2}";

        private static readonly Lazy<string[]> _versionsLazy = new Lazy<string[]>(LoadVersionList, LazyThreadSafetyMode.ExecutionAndPublication);
        private static readonly Lazy<MojangAPI.VersionInfo> mc1_19 = new Lazy<MojangAPI.VersionInfo>(
            () => (MojangAPI.VersionDictionary?.TryGetValue("1.19", out MojangAPI.VersionInfo result) ?? false) ? result : null,
            LazyThreadSafetyMode.PublicationOnly);

        private readonly IPropertyFile[] propertyFiles = new IPropertyFile[4];
        public JavaPropertyFile ServerPropertiesFile => propertyFiles[0] as JavaPropertyFile;
        public YamlPropertyFile BukkitYMLFile => propertyFiles[1] as YamlPropertyFile;
        public YamlPropertyFile SpigotYMLFile => propertyFiles[2] as YamlPropertyFile;
        public YamlPropertyFile PaperYMLFile => propertyFiles[3] as YamlPropertyFile;

        private string _version;
        private int _build = -1;
        private JavaRuntimeEnvironment environment;

        static Paper()
        {
            CallWhenStaticInitialize();
            SoftwareID = "paper";
        }

        public override string ServerVersion => _version;

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
            JObject manifestJSON;
            using (JsonTextReader jtr = new JsonTextReader(new StringReader(manifestString)))
            {
                try
                {
                    manifestJSON = GlobalSerializers.JsonSerializer.Deserialize(jtr) as JObject;
                }
                catch (Exception)
                {
                    return null;
                }
            }
            if (manifestJSON is null || !(manifestJSON.GetValue("versions") is JArray versions))
                return null;
            int length = versions.Count;
            if (length <= 0)
                return null;
            List<string> list = new List<string>(length);
            for (int i = 0; i < length; i++)
            {
                if (versions[i] is JValue versionToken && versionToken.Type == JTokenType.String)
                {
                    string version = versionToken.Value.ToString();
                    list.Add(version);
                }
            }
            string[] result = list.ToArray();
            Array.Reverse(result);
            return result;
        }

        private bool InstallSoftware(MojangAPI.VersionInfo info)
        {
            if (info is null)
                return false;
            try
            {
                InstallTask task = new InstallTask(this, info.Id);
                OnServerInstalling(task);
                task.ChangeStatus(PreparingInstallStatus.Instance);
                Task.Factory.StartNew(() =>
                {
                    try
                    {
                        if (!InstallSoftware(task, info))
                            task.OnInstallFailed();
                    }
                    catch (Exception)
                    {
                        task.OnInstallFailed();
                    }
                }, default, TaskCreationOptions.LongRunning, TaskScheduler.Current);
            }
            catch (Exception)
            {
                return false;
            }
            return true;
        }

        private bool InstallSoftware(InstallTask task, MojangAPI.VersionInfo info)
        {
            string version = info.Id;
            string manifestURL = string.Format(manifestListURL2, version);
            if (string.IsNullOrEmpty(manifestURL))
                return false;
            WebClient2 client = new WebClient2();
            InstallTaskWatcher watcher = new InstallTaskWatcher(task, client);
            JObject jsonObject = JsonConvert.DeserializeObject<JObject>(client.GetStringAsync(manifestURL).Result);
            if (watcher.IsStopRequested || jsonObject is null || !jsonObject.TryGetValue("builds", out JToken token))
                return false;
            JArray jsonArray = token as JArray;
            if (jsonArray is null)
                return false;
            token = jsonArray.Last;
            if (token is null || !(token is JValue _value && _value.Type == JTokenType.Integer))
                return false;
            int build = _value.ToObject<int>();
            jsonObject = JsonConvert.DeserializeObject<JObject>(client.GetStringAsync(string.Format(manifestListURL3, version, build.ToString())).Result);
            if (watcher.IsStopRequested || jsonObject is null || !jsonObject.TryGetValue("downloads", out token))
                return false;
            jsonObject = token as JObject;
            if (jsonObject is null || !jsonObject.TryGetValue("application", out token))
                return false;
            jsonObject = token as JObject;
            if (jsonObject is null || !jsonObject.TryGetValue("name", out token))
                return false;
            byte[] sha256;
            if (WTCore.CheckFileHashIfExist && jsonObject.TryGetValue("sha256", out JToken sha256Token))
                sha256 = HashHelper.HexStringToByte(sha256Token.ToString());
            else
                sha256 = null;
            watcher.Dispose();
            int? id = FileDownloadHelper.AddTask(task: task, webClient: client,
                downloadUrl: string.Format(downloadURL, version, build, token.ToString()),
                filename: Path.Combine(ServerDirectory, @"paper-" + version + ".jar"),
                hash: sha256, hashMethod: HashHelper.HashMethod.SHA256);
            if (id.HasValue)
            {
                void AfterDownload(object sender, int sendingId)
                {
                    if (id.Value != sendingId)
                        return;
                    FileDownloadHelper.TaskFinished -= AfterDownload;
                    _version = version;
                    _build = build;
                    MojangAPI.VersionInfo mojangVersionInfo = FindVersionInfo(version);
                    this.mojangVersionInfo = mojangVersionInfo;
                    string path = mojangVersionInfo >= mc1_19.Value ? "./config/paper-global.yml" : "./paper.yml";
                    path = Path.GetFullPath(Path.Combine(ServerDirectory, path));
                    IPropertyFile propertyFile = propertyFiles[3];
                    if (propertyFile is null || !string.Equals(path, Path.GetFullPath(propertyFile.GetFilePath()), StringComparison.OrdinalIgnoreCase))
                    {
                        try
                        {
                            propertyFile?.Dispose();
                            propertyFiles[3] = new YamlPropertyFile(path);
                        }
                        catch (Exception)
                        {
                        }
                    }
                    OnServerVersionChanged();
                }
                FileDownloadHelper.TaskFinished += AfterDownload;
                return true;
            }
            return false;
        }


        public override bool ChangeVersion(int versionIndex)
        {
            return InstallSoftware(FindVersionInfo(_versionsLazy.Value[versionIndex]));
        }

        /// <inheritdoc/>
        public override string GetReadableVersion()
        {
            return _version;
        }

        /// <inheritdoc/>
        public override IPropertyFile[] GetServerPropertyFiles()
        {
            return propertyFiles;
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
            try
            {
                propertyFiles[0] = new JavaPropertyFile(Path.Combine(ServerDirectory, "./server.properties"));
                propertyFiles[1] = new YamlPropertyFile(Path.Combine(ServerDirectory, "./bukkit.yml"));
                propertyFiles[2] = new YamlPropertyFile(Path.Combine(ServerDirectory, "./spigot.yml"));
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
            string version = serverInfoJson["version"]?.ToString();
            if (version is null)
                return false;
            _version = version;
            JToken buildNode = serverInfoJson["build"];
            if (buildNode?.Type == JTokenType.Integer)
            {
                _build = (int)buildNode;
            }
            else
            {
                _build = 0;
            }
            try
            {
                propertyFiles[0] = new JavaPropertyFile(Path.Combine(ServerDirectory, "./server.properties"));
                propertyFiles[1] = new YamlPropertyFile(Path.Combine(ServerDirectory, "./bukkit.yml"));
                propertyFiles[2] = new YamlPropertyFile(Path.Combine(ServerDirectory, "./spigot.yml"));
                if (GetMojangVersionInfo() >= mc1_19.Value)
                {
                    string path = Path.Combine(ServerDirectory, "./config/paper-global.yml");
                    if (!File.Exists(path))
                    {
                        string alt_path = Path.Combine(ServerDirectory, "./paper.yml");
                        if (File.Exists(alt_path))
                        {
                            try
                            {
                                File.Copy(alt_path, path, true);
                            }
                            catch (Exception)
                            {

                            }
                        }
                    }
                    propertyFiles[3] = new YamlPropertyFile(path);
                }
                else
                {
                    string path = Path.Combine(ServerDirectory, "./paper.yml");
                    propertyFiles[3] = new YamlPropertyFile(path);
                }
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

        /// <inheritdoc/>
        public override RuntimeEnvironment GetRuntimeEnvironment()
        {
            return environment;
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
                        Arguments = string.Format("-Djline.terminal=jline.UnsupportedTerminal -Dfile.encoding=UTF8 -Dsun.stdout.encoding=UTF8 -Dsun.stderr.encoding=UTF8 {0} -jar \"{1}\" {2}"
                        , javaRuntimeEnvironment.JavaPreArguments ?? RuntimeEnvironment.JavaDefault.JavaPreArguments
                        , Path.Combine(ServerDirectory, @"paper-" + GetReadableVersion() + ".jar")
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
            serverInfoJson["build"] = _build;
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
            return InstallSoftware(FindVersionInfo(_version));
        }
    }
}
