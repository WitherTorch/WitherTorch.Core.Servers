using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;

using WitherTorch.Core.Property;
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
        private static readonly Lazy<MojangAPI.VersionInfo?> mc1_19 = new Lazy<MojangAPI.VersionInfo?>(
            () => (MojangAPI.VersionDictionary?.TryGetValue("1.19", out MojangAPI.VersionInfo? result) ?? false) ? result : null,
            LazyThreadSafetyMode.PublicationOnly);

        private readonly IPropertyFile[] propertyFiles = new IPropertyFile[4];
        public JavaPropertyFile? ServerPropertiesFile => propertyFiles[0] as JavaPropertyFile;
        public YamlPropertyFile? BukkitYMLFile => propertyFiles[1] as YamlPropertyFile;
        public YamlPropertyFile? SpigotYMLFile => propertyFiles[2] as YamlPropertyFile;
        public YamlPropertyFile? PaperYMLFile => propertyFiles[3] as YamlPropertyFile;

        private string _version = string.Empty;
        private int _build = -1;
        private JavaRuntimeEnvironment? _environment;

        static Paper()
        {
            CallWhenStaticInitialize();
            SoftwareId = "paper";
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

        private static string[]? LoadVersionListInternal()
        {
            string? manifestString = CachedDownloadClient.Instance.DownloadString(manifestListURL);
            if (manifestString is null || manifestString.Length <= 0)
                return null;
            JsonObject? manifestJSON = JsonNode.Parse(manifestString) as JsonObject;
            if (manifestJSON is null || manifestJSON["versions"] is not JsonArray versions)
                return null;
            int length = versions.Count;
            if (length <= 0)
                return null;
            List<string> list = new List<string>(length);
            for (int i = 0; i < length; i++)
            {
                if (versions[i] is JsonValue versionToken && versionToken.GetValueKind() == JsonValueKind.String)
                {
                    list.Add(versionToken.GetValue<string>());
                }
            }
            string[] result = list.ToArray();
            Array.Reverse(result);
            return result;
        }

        private bool InstallSoftware(MojangAPI.VersionInfo? info)
        {
            if (info is null)
                return false;
            string? id = info.Id;
            if (id is null || id.Length <= 0)
                return false;
            InstallTask task = new InstallTask(this, id);
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
            return true;
        }

        private bool InstallSoftware(InstallTask task, MojangAPI.VersionInfo info)
        {
            string? version = info.Id;
            if (version is null || version.Length <= 0)
                return false;
            string manifestURL = string.Format(manifestListURL2, version);
            if (string.IsNullOrEmpty(manifestURL))
                return false;
            WebClient2 client = new WebClient2();
            InstallTaskWatcher watcher = new InstallTaskWatcher(task, client);
            JsonObject? jsonObject = JsonNode.Parse(client.GetStringAsync(manifestURL).Result) as JsonObject;
            if (watcher.IsStopRequested || jsonObject is null || !jsonObject.TryGetPropertyValue("builds", out JsonNode? node))
                return false;
            JsonArray? jsonArray = node as JsonArray;
            if (jsonArray is null)
                return false;
            node = jsonArray.Last();
            if (node is null || !(node is JsonValue _value && _value.GetValueKind() == JsonValueKind.Number))
                return false;
            int build = _value.GetValue<int>();
            jsonObject = JsonNode.Parse(client.GetStringAsync(string.Format(manifestListURL3, version, build.ToString())).Result) as JsonObject;
            if (watcher.IsStopRequested || jsonObject is null || !jsonObject.TryGetPropertyValue("downloads", out node))
                return false;
            jsonObject = node as JsonObject;
            if (jsonObject is null || !jsonObject.TryGetPropertyValue("application", out node))
                return false;
            jsonObject = node as JsonObject;
            if (jsonObject is null || !jsonObject.TryGetPropertyValue("name", out node))
                return false;
            byte[]? sha256;
            if (WTCore.CheckFileHashIfExist && jsonObject.TryGetPropertyValue("sha256", out JsonNode? sha256Token) && sha256Token is not null)
                sha256 = HashHelper.HexStringToByte(sha256Token.ToString());
            else
                sha256 = null;
            watcher.Dispose();
            int? id = FileDownloadHelper.AddTask(task: task, webClient: client,
                downloadUrl: string.Format(downloadURL, version, build, ObjectUtils.ThrowIfNull(node).ToString()),
                filename: Path.Combine(ServerDirectory, @"paper-" + version + ".jar"),
                hash: sha256, hashMethod: HashHelper.HashMethod.SHA256);
            if (id.HasValue)
            {
                void AfterDownload(object? sender, int sendingId)
                {
                    if (id.Value != sendingId)
                        return;
                    FileDownloadHelper.TaskFinished -= AfterDownload;
                    _version = version;
                    _build = build;
                    MojangAPI.VersionInfo? mojangVersionInfo = FindVersionInfo(version);
                    this.mojangVersionInfo = mojangVersionInfo;
                    MojangAPI.VersionInfo? mc1_19 = Paper.mc1_19.Value;
                    string path;
                    if (mojangVersionInfo is null || mc1_19 is null || mojangVersionInfo < mc1_19)
                        path = "./paper.yml";
                    else
                        path = "./config/paper-global.yml";
                    path = Path.GetFullPath(Path.Combine(ServerDirectory, path));
                    IPropertyFile propertyFile = propertyFiles[3];
                    if (propertyFile is null || !string.Equals(path, Path.GetFullPath(propertyFile.FilePath), StringComparison.OrdinalIgnoreCase))
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

        protected override MojangAPI.VersionInfo? BuildVersionInfo()
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
            JsonPropertyFile? serverInfoJson = ServerInfoJson;
            if (serverInfoJson is null)
                return false;
            string? version = serverInfoJson["version"]?.ToString();
            if (version is null || version.Length <= 0)
                return false;
            _version = version;
            JsonNode? buildNode = serverInfoJson["build"];
            if (buildNode is null || buildNode.GetValueKind() != JsonValueKind.Number)
                _build = 0;
            else
                _build = buildNode.GetValue<int>();
            propertyFiles[0] = new JavaPropertyFile(Path.Combine(ServerDirectory, "./server.properties"));
            propertyFiles[1] = new YamlPropertyFile(Path.Combine(ServerDirectory, "./bukkit.yml"));
            propertyFiles[2] = new YamlPropertyFile(Path.Combine(ServerDirectory, "./spigot.yml"));
            MojangAPI.VersionInfo? mojangVersionInfo = FindVersionInfo(version);
            this.mojangVersionInfo = mojangVersionInfo;
            MojangAPI.VersionInfo? mc1_19 = Paper.mc1_19.Value;
            string path;
            if (mojangVersionInfo is null || mc1_19 is null || mojangVersionInfo < mc1_19)
                path = Path.Combine(ServerDirectory, "./paper.yml");
            else
            {
                path = Path.Combine(ServerDirectory, "./config/paper-global.yml");
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
            }
            propertyFiles[3] = new YamlPropertyFile(path);
            string? jvmPath = serverInfoJson["java.path"]?.GetValue<string>() ?? null;
            string? jvmPreArgs = serverInfoJson["java.preArgs"]?.GetValue<string>() ?? null;
            string? jvmPostArgs = serverInfoJson["java.postArgs"]?.GetValue<string>() ?? null;
            if (jvmPath is not null || jvmPreArgs is not null || jvmPostArgs is not null)
            {
                _environment = new JavaRuntimeEnvironment(jvmPath, jvmPreArgs, jvmPostArgs);
            }
            return true;
        }

        /// <inheritdoc/>
        public override void SetRuntimeEnvironment(RuntimeEnvironment? environment)
        {
            if (environment is JavaRuntimeEnvironment javaRuntimeEnvironment)
                this._environment = javaRuntimeEnvironment;
            else if (environment is null)
                this._environment = null;
        }

        /// <inheritdoc/>
        public override RuntimeEnvironment? GetRuntimeEnvironment()
        {
            return _environment;
        }
        /// <inheritdoc/>
        public override bool RunServer(JavaRuntimeEnvironment? environment)
        {
            if (_isStarted)
                return true;
            environment ??= RuntimeEnvironment.JavaDefault;
            string? javaPath = environment.JavaPath;
            if (javaPath is null || !File.Exists(javaPath))
                javaPath = RuntimeEnvironment.JavaDefault.JavaPath;
            ProcessStartInfo startInfo = new ProcessStartInfo
            {
                FileName = javaPath,
                Arguments = string.Format("-Djline.terminal=jline.UnsupportedTerminal -Dfile.encoding=UTF8 -Dsun.stdout.encoding=UTF8 -Dsun.stderr.encoding=UTF8 {0} -jar \"{1}\" {2}"
                , environment.JavaPreArguments ?? RuntimeEnvironment.JavaDefault.JavaPreArguments
                , Path.Combine(ServerDirectory, @"paper-" + GetReadableVersion() + ".jar")
                , environment.JavaPostArguments ?? RuntimeEnvironment.JavaDefault.JavaPostArguments),
                WorkingDirectory = ServerDirectory,
                CreateNoWindow = true,
                ErrorDialog = true,
                UseShellExecute = false,
            };
            return _process.StartProcess(startInfo);
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
            JsonPropertyFile? serverInfoJson = ServerInfoJson;
            if (serverInfoJson is null)
                return false;
            serverInfoJson["version"] = _version;
            serverInfoJson["build"] = _build;
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

        public override bool UpdateServer()
        {
            return InstallSoftware(FindVersionInfo(_version));
        }
    }
}
