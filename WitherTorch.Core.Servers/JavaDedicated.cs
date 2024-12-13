using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text.Json.Nodes;
using System.Threading.Tasks;

using WitherTorch.Core.Property;
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
                if (!IsVanillaHasServer(info))
                    continue;
                string? id = info.Id;
                if (id is null)
                    continue;
                result.Add(id);
            }
            string[] array = result.ToArray();
            Array.Sort(array, MojangAPI.VersionComparer.Instance.Reverse());
            return array;
        }, System.Threading.LazyThreadSafetyMode.PublicationOnly);

        static JavaDedicated()
        {
            CallWhenStaticInitialize();
            SoftwareId = "javaDedicated";
        }

        private string _version = string.Empty;
        private JavaRuntimeEnvironment? _environment;
        private readonly IPropertyFile[] _propertyFiles = new IPropertyFile[1];

        public JavaPropertyFile? ServerPropertiesFile => _propertyFiles[0] as JavaPropertyFile;
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

        private bool InstallSoftware(MojangAPI.VersionInfo? versionInfo)
        {
            if (versionInfo is null)
                return false;
            string? id = versionInfo.Id;
            if (id is null || id.Length <= 0)
                return false;
            InstallTask task = new InstallTask(this, id);
            OnServerInstalling(task);
            task.ChangeStatus(PreparingInstallStatus.Instance);
            void onInstallFinished(object? sender, EventArgs e)
            {
                if (sender is not InstallTask _task)
                    return;
                _task.InstallFinished -= onInstallFinished;
                _version = id;
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
            string? manifestURL = versionInfo.ManifestURL;
            if (manifestURL is null || manifestURL.Length <= 0)
                return false;
            WebClient2 client = new WebClient2();
            InstallTaskWatcher watcher = new InstallTaskWatcher(task, client);
            JsonObject? jsonObject = JsonNode.Parse(client.GetStringAsync(manifestURL).Result) as JsonObject;
            if (watcher.IsStopRequested || jsonObject is null || !jsonObject.TryGetPropertyValue("downloads", out JsonNode? node))
                return false;
            jsonObject = node as JsonObject;
            if (jsonObject is null || !jsonObject.TryGetPropertyValue("server", out node))
                return false;
            jsonObject = node as JsonObject;
            if (jsonObject is null || !jsonObject.TryGetPropertyValue("url", out node))
                return false;
            byte[]? sha1;
            if (WTCore.CheckFileHashIfExist && jsonObject.TryGetPropertyValue("sha1", out JsonNode? sha1Node))
                sha1 = HashHelper.HexStringToByte(ObjectUtils.ThrowIfNull(sha1Node).ToString());
            else
                sha1 = null;
            watcher.Dispose();
            return FileDownloadHelper.AddTask(task: task, webClient: client, downloadUrl: ObjectUtils.ThrowIfNull(node).ToString(),
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

        protected override MojangAPI.VersionInfo? BuildVersionInfo()
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
            JsonPropertyFile? serverInfoJson = ServerInfoJson;
            if (serverInfoJson is null)
                return false;
            string? version = (serverInfoJson["version"] as JsonValue)?.GetValue<string>();
            if (version is null || version.Length <= 0)
                return false;
            _version = version;
            _propertyFiles[0] = new JavaPropertyFile(Path.Combine(ServerDirectory, "./server.properties"));
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
        public override RuntimeEnvironment? GetRuntimeEnvironment()
        {
            return _environment;
        }

        /// <inheritdoc/>
        public override void SetRuntimeEnvironment(RuntimeEnvironment? environment)
        {
            if (environment is JavaRuntimeEnvironment javaRuntimeEnvironment)
                _environment = javaRuntimeEnvironment;
            else if (environment is null)
                _environment = null;
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
                , Path.Combine(ServerDirectory, @"minecraft_server." + GetReadableVersion() + ".jar")
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
            return InstallSoftware(_version);
        }
    }
}
