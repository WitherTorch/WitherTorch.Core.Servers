using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
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

        protected bool _isStarted;
        readonly IPropertyFile[] propertyFiles = new IPropertyFile[4];
        public JavaPropertyFile ServerPropertiesFile => propertyFiles[0] as JavaPropertyFile;
        public YamlPropertyFile BukkitYMLFile => propertyFiles[1] as YamlPropertyFile;
        public YamlPropertyFile SpigotYMLFile => propertyFiles[2] as YamlPropertyFile;
        public YamlPropertyFile PaperYMLFile => propertyFiles[3] as YamlPropertyFile;
        private string versionString;
        private long build = -1;
        private JavaRuntimeEnvironment environment;
        protected SystemProcess process;
        internal static string[] versions;
        private static MojangAPI.VersionInfo mc1_19;

        static Paper()
        {
            CallWhenStaticInitialize();
            SoftwareRegistrationDelegate += Initialize;
            SoftwareID = "paper";
        }

        public override string ServerVersion => versionString;

        private static void Initialize()
        {
            if (mc1_19 is null) MojangAPI.VersionDictionary?.TryGetValue("1.19", out mc1_19);
        }

        internal static void LoadVersionList()
        {
            List<string> preparingVersionList = new List<string>();
            try
            {
                string manifestString = CachedDownloadClient.Instance.DownloadString(manifestListURL);
                if (manifestString != null)
                {
                    JObject manifestJSON;
                    using (StringReader reader = new StringReader(manifestString))
                    {
                        using (JsonTextReader jtr = new JsonTextReader(reader))
                        {
                            try
                            {
                                manifestJSON = GlobalSerializers.JsonSerializer.Deserialize(jtr) as JObject;
                            }
                            catch (Exception)
                            {
                                manifestJSON = null;
                            }
                        }
                        try
                        {
                            reader?.Close();
                        }
                        catch (Exception)
                        {
                        }
                    }
                    if (manifestJSON != null)
                    {
                        JArray versionArray = manifestJSON.GetValue("versions") as JArray;
                        if (versionArray != null)
                        {
                            foreach (JToken token in versionArray)
                            {
                                if (token is JValue tokenValue && tokenValue.Type == JTokenType.String)
                                {
                                    string version = tokenValue.Value.ToString();
                                    preparingVersionList.Add(version);
                                }
                            }
                        }
                    }
                }
            }
            catch (Exception)
            {

            }
            preparingVersionList.Reverse();
            versions = preparingVersionList.ToArray();
        }

        private async void InstallSoftware()
        {
            InstallTask task = new InstallTask(this);
            OnServerInstalling(task);
            task.ChangeStatus(PreparingInstallStatus.Instance);
            if (!await InstallSoftware(task, string.Format(manifestListURL2, versionString)))
            {
                task.OnInstallFailed();
                return;
            }
        }

        private async Task<bool> InstallSoftware(InstallTask task, string manifestURL)
        {
            if (string.IsNullOrEmpty(manifestURL))
                return false;
            WebClient2 client = new WebClient2();
            InstallTaskWatcher watcher = new InstallTaskWatcher(task, client);
            JObject jsonObject = JsonConvert.DeserializeObject<JObject>(await client.GetStringAsync(manifestURL));
            if (watcher.IsStopRequested || jsonObject is null || !jsonObject.TryGetValue("builds", out JToken token))
                return false;
            JArray jsonArray = token as JArray;
            if (jsonArray is null)
                return false;
            token = jsonArray.Last;
            if (token is null || !(token is JValue _value && _value.Value is long build))
                return false;
            jsonObject = JsonConvert.DeserializeObject<JObject>(await client.GetStringAsync(string.Format(manifestListURL3, versionString, build.ToString())));
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
                downloadUrl: string.Format(downloadURL, versionString, build, token.ToString()),
                filename: Path.Combine(ServerDirectory, @"paper-" + versionString + ".jar"),
                hash: sha256, hashMethod: HashHelper.HashMethod.SHA256);
            if (id.HasValue)
            {
                void AfterDownload(object sender, int sendingId)
                {
                    if (id.Value != sendingId)
                        return;
                    FileDownloadHelper.TaskFinished -= AfterDownload;
                    if (mc1_19 is null)
                        MojangAPI.VersionDictionary?.TryGetValue("1.19", out mc1_19);
                    string path = GetMojangVersionInfo() >= mc1_19 ? "./config/paper-global.yml" : "./paper.yml";
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
                }
                FileDownloadHelper.TaskFinished += AfterDownload;
                return true;
            }
            return false;
        }


        public override bool ChangeVersion(int versionIndex)
        {
            try
            {
                if (versions is null) LoadVersionList();
                versionString = versions[versionIndex];
                BuildVersionInfo();
                InstallSoftware();
            }
            catch (Exception)
            {
                return false;
            }
            return true;
        }

        /// <inheritdoc/>
        public override AbstractProcess GetProcess()
        {
            return process;
        }

        /// <inheritdoc/>
        public override string GetReadableVersion()
        {
            return versionString;
        }

        /// <inheritdoc/>
        public override IPropertyFile[] GetServerPropertyFiles()
        {
            return propertyFiles;
        }

        /// <inheritdoc/>
        public override string[] GetSoftwareVersions()
        {
            if (versions is null)
            {
                LoadVersionList();
            }
            return versions;
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
            try
            {
                JsonPropertyFile serverInfoJson = ServerInfoJson;
                versionString = serverInfoJson["version"].ToString();
                JToken buildNode = serverInfoJson["build"];
                if (buildNode?.Type == JTokenType.Integer)
                {
                    build = (long)buildNode;
                }
                else
                {
                    build = 0L;
                }
                process = new SystemProcess();
                process.ProcessStarted += delegate (object sender, EventArgs e) { _isStarted = true; };
                process.ProcessEnded += delegate (object sender, EventArgs e) { _isStarted = false; };
                propertyFiles[0] = new JavaPropertyFile(Path.Combine(ServerDirectory, "./server.properties"));
                propertyFiles[1] = new YamlPropertyFile(Path.Combine(ServerDirectory, "./bukkit.yml"));
                propertyFiles[2] = new YamlPropertyFile(Path.Combine(ServerDirectory, "./spigot.yml"));
                if (mc1_19 is null) MojangAPI.VersionDictionary?.TryGetValue("1.19", out mc1_19);
                if (GetMojangVersionInfo() >= mc1_19)
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
                    process.StartProcess(startInfo);
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

        protected override bool BeforeServerSaved()
        {
            JsonPropertyFile serverInfoJson = ServerInfoJson;
            serverInfoJson["version"] = versionString;
            serverInfoJson["build"] = build;
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
