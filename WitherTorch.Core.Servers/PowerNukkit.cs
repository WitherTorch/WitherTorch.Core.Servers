using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json.Nodes;
using System.Text.Json;
using System.Xml;

using WitherTorch.Core.Property;
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

        private string _version = string.Empty;
        private JavaRuntimeEnvironment? _environment;
        private readonly IPropertyFile[] propertyFiles = new IPropertyFile[1];
        public YamlPropertyFile? NukkitYMLFile => propertyFiles[0] as YamlPropertyFile;
        public override string ServerVersion => _version;

        static PowerNukkit()
        {
            SoftwareId = "powerNukkit";
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

        private static IReadOnlyDictionary<string, string>? LoadVersionListInternal()
        {
            string? manifestString = CachedDownloadClient.Instance.DownloadString(manifestListURL);
            if (manifestString is null || manifestString.Length <= 0)
                return null;
            XmlDocument manifestXML = new XmlDocument();
            manifestXML.LoadXml(manifestString);
            XmlNodeList? nodeList = manifestXML.SelectNodes("/metadata/versioning/versions/version");
            if (nodeList is null)
                return null;
            Dictionary<string, string> result = new Dictionary<string, string>();
            foreach (XmlNode token in nodeList)
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
            InstallTask task = new InstallTask(this, version);
            OnServerInstalling(task);
            void onInstallFinished(object? sender, EventArgs e)
            {
                if (sender is not InstallTask _task)
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
            if (string.IsNullOrEmpty(version) || !_versionDictLazy.Value.TryGetValue(version, out string? fullVersionString))
                return false;
            return FileDownloadHelper.AddTask(task: task,
                downloadUrl: string.Format(downloadURL, fullVersionString),
                filename: Path.Combine(ServerDirectory, @"powernukkit-" + version + ".jar")).HasValue;
        }

        public override string GetReadableVersion()
        {
            return _version;
        }

        public override RuntimeEnvironment? GetRuntimeEnvironment()
        {
            return _environment;
        }

        public override IPropertyFile[] GetServerPropertyFiles()
        {
            return propertyFiles;
        }

        public override string[] GetSoftwareVersions()
        {
            return _versionsLazy.Value;
        }

        public override bool RunServer(RuntimeEnvironment? environment)
        {
            return RunServer(environment as JavaRuntimeEnvironment);
        }       
        
        public bool RunServer(JavaRuntimeEnvironment? environment)
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
                , Path.Combine(ServerDirectory, @"powernukkit-" + GetReadableVersion() + ".jar")
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

        public override void SetRuntimeEnvironment(RuntimeEnvironment? environment)
        {
            if (environment is JavaRuntimeEnvironment runtimeEnvironment)
            {
                this._environment = runtimeEnvironment;
            }
            else if (environment is null)
            {
                this._environment = null;
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
            JsonPropertyFile? serverInfoJson = ServerInfoJson;
            if (serverInfoJson is null)
                return false;
            string? version = serverInfoJson["version"]?.GetValue<string>();
            if (version is null || version.Length <= 0)
                return false;
            _version = version;
            propertyFiles[0] = new YamlPropertyFile(Path.Combine(ServerDirectory, "./nukkit.yml"));
            string? jvmPath = serverInfoJson["java.path"]?.GetValue<string>() ?? null;
            string? jvmPreArgs = serverInfoJson["java.preArgs"]?.GetValue<string>() ?? null;
            string? jvmPostArgs = serverInfoJson["java.postArgs"]?.GetValue<string>() ?? null;
            if (jvmPath is not null || jvmPreArgs is not null || jvmPostArgs is not null)
            {
                _environment = new JavaRuntimeEnvironment(jvmPath, jvmPreArgs, jvmPostArgs);
            }
            return true;
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
    }
}
