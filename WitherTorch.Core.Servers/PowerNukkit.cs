using Newtonsoft.Json.Linq;

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Xml;

using WitherTorch.Core.Servers.Utils;

#if NET6_0_OR_GREATER
using System.Collections.Frozen;
#endif

namespace WitherTorch.Core.Servers
{
    public class PowerNukkit : LocalServer<PowerNukkit>
    {
        private const string manifestListURL = "https://repo1.maven.org/maven2/org/powernukkit/powernukkit/maven-metadata.xml";
        private const string downloadURL = "https://repo1.maven.org/maven2/org/powernukkit/powernukkit/{0}/powernukkit-{0}-shaded.jar";
        private static readonly Lazy<IReadOnlyDictionary<string, string>> _versionDictLazy = new Lazy<IReadOnlyDictionary<string, string>>(
            LoadVersionList, System.Threading.LazyThreadSafetyMode.ExecutionAndPublication);
        private static readonly Lazy<string[]> _versionsLazy = new Lazy<string[]>(
            () =>
            {
                string[] result = _versionDictLazy.Value.Keys.ToArray();
                Array.Reverse(result);
                return result;
            }, System.Threading.LazyThreadSafetyMode.ExecutionAndPublication);

        private string _version;
        private JavaRuntimeEnvironment environment;
        private readonly IPropertyFile[] propertyFiles = new IPropertyFile[1];
        public YamlPropertyFile NukkitYMLFile => propertyFiles[0] as YamlPropertyFile;
        public override string ServerVersion => _version;

        static PowerNukkit()
        {
            SoftwareID = "powerNukkit";
        }

        private static IReadOnlyDictionary<string, string> LoadVersionList()
        {
            try
            {
                return LoadVersionListInternal() ?? EmptyDictionary<string, string>.Instance;
            }
            catch (Exception)
            {
            }
            return EmptyDictionary<string, string>.Instance;
        }

        private static IReadOnlyDictionary<string, string> LoadVersionListInternal()
        {
            string manifestString = CachedDownloadClient.Instance.DownloadString(manifestListURL);
            if (string.IsNullOrEmpty(manifestString))
                return null;
            XmlDocument manifestXML = new XmlDocument();
            manifestXML.LoadXml(manifestString);
            Dictionary<string, string> result = new Dictionary<string, string>();
            foreach (XmlNode token in manifestXML.SelectNodes("/metadata/versioning/versions/version"))
            {
                string rawVersion = token.InnerText;
                string version;
                int indexOf = rawVersion.IndexOf("-PN");
                if (indexOf >= 0)
                    version = rawVersion.Substring(0, indexOf);
                else
                    version = rawVersion;
                result[version] = rawVersion;
            }
#if NET6_0_OR_GREATER
            return result.ToFrozenDictionary();
#else
            return result;
#endif
        }

        public override bool ChangeVersion(int versionIndex)
        {
            return InstallSoftware(_versionsLazy.Value[versionIndex]);
        }

        private bool InstallSoftware(string version)
        {
            InstallTask task = new InstallTask(this);
            OnServerInstalling(task);
            void onInstallFinished(object sender, EventArgs e)
            {
                if (!(sender is InstallTask _task))
                    return;
                _task.InstallFinished -= onInstallFinished;
                _version = version;
                OnServerVersionChanged();
            };
            task.InstallFinished += onInstallFinished;
            if (!InstallSoftware(task, version))
            {
                task.OnInstallFailed();
                return false;
            }
            return true;
        }

        private bool InstallSoftware(InstallTask task, string version)
        {
            if (string.IsNullOrEmpty(version) || !_versionDictLazy.Value.TryGetValue(version, out string fullVersionString))
                return false;
            return FileDownloadHelper.AddTask(task: task,
                downloadUrl: string.Format(downloadURL, fullVersionString),
                filename: Path.Combine(ServerDirectory, @"powernukkit-" + version + ".jar")).HasValue;
        }

        public override string GetReadableVersion()
        {
            return _version;
        }

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
                        , Path.Combine(ServerDirectory, @"powernukkit-" + _version + ".jar")
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

        public override void SetRuntimeEnvironment(RuntimeEnvironment environment)
        {
            if (environment is JavaRuntimeEnvironment runtimeEnvironment)
            {
                this.environment = runtimeEnvironment;
            }
            else if (environment is null)
            {
                this.environment = null;
            }
        }

        public override bool UpdateServer()
        {
            return InstallSoftware(_version);
        }

        protected override bool CreateServer()
        {
            try
            {
                propertyFiles[0] = new YamlPropertyFile(Path.Combine(ServerDirectory, "./nukkit.yml"));
            }
            catch (Exception)
            {
                return false;
            }
            return true;
        }

        protected override bool OnServerLoading()
        {
            JsonPropertyFile serverInfoJson = ServerInfoJson;
            string version = serverInfoJson["version"]?.ToString();
            if (string.IsNullOrEmpty(version))
                return false;
            _version = version;
            try
            {
                propertyFiles[0] = new YamlPropertyFile(Path.Combine(ServerDirectory, "./nukkit.yml"));
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

        protected override bool BeforeServerSaved()
        {
            JsonPropertyFile serverInfoJson = ServerInfoJson;
            serverInfoJson["version"] = _version;
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
    }
}
