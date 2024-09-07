using System;
using System.Diagnostics;
using System.IO;

using Newtonsoft.Json.Linq;

using WitherTorch.Core.Servers.Utils;

namespace WitherTorch.Core.Servers
{
    /// <summary>
    /// CraftBukkit 伺服器
    /// </summary>
    public class CraftBukkit : AbstractJavaEditionServer<CraftBukkit>
    {
        private readonly IPropertyFile[] propertyFiles = new IPropertyFile[2];
        public JavaPropertyFile ServerPropertiesFile => propertyFiles[0] as JavaPropertyFile;
        public YamlPropertyFile BukkitYMLFile => propertyFiles[1] as YamlPropertyFile;

        private string _version;
        private int _build = -1;
        private JavaRuntimeEnvironment environment;

        static CraftBukkit()
        {
            CallWhenStaticInitialize();
            SoftwareID = "craftbukkit";
        }

        public override string ServerVersion => _version;

        public override bool ChangeVersion(int versionIndex)
        {
            return InstallSoftware(SpigotAPI.Versions[versionIndex]);
        }

        private bool InstallSoftware(string version)
        {
            try
            {
                int build = SpigotAPI.GetBuildNumber(version);
                if (build < 0)
                    return false;
                InstallSoftware(version, build);
            }
            catch (Exception)
            {
                return false;
            }
            return true;
        }

        private void InstallSoftware(string minecraftVersion, int build)
        {
            InstallTask task = new InstallTask(this);
            OnServerInstalling(task);
            void onServerInstallFinished(object sender, EventArgs e)
            {
                if (!(sender is InstallTask _task))
                    return;
                _task.InstallFinished -= onServerInstallFinished;
                _version = minecraftVersion;
                _build = build;
                OnServerVersionChanged();
            }
            task.InstallFinished += onServerInstallFinished;
            SpigotBuildTools.Instance.Install(task, SpigotBuildTools.BuildTarget.CraftBukkit, minecraftVersion);
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
            return SpigotAPI.Versions;
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
            if (string.IsNullOrEmpty(version))
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
                        , Path.Combine(ServerDirectory, @"craftbukkit-" + GetReadableVersion() + ".jar")
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
