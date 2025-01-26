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
using System.Runtime.CompilerServices;
using System.Threading;

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

        private readonly Lazy<IPropertyFile[]> propertyFilesLazy;

        public YamlPropertyFile NukkitYMLFile
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => (YamlPropertyFile)propertyFilesLazy.Value[0];
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            private set
            {
                IPropertyFile[] propertyFiles = propertyFilesLazy.Value;
                IPropertyFile propertyFile = propertyFiles[0];
                propertyFiles[0] = value;
                propertyFile.Dispose();
            }
        }

        public override string ServerVersion => _version;

        static PowerNukkit()
        {
            SoftwareId = "powerNukkit";
        }

        public PowerNukkit()
        {
            propertyFilesLazy = new Lazy<IPropertyFile[]>(GetServerPropertyFilesCore, LazyThreadSafetyMode.ExecutionAndPublication);
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
            void onInstallFinished(object? sender, EventArgs e)
            {
                if (sender is not InstallTask _task)
                    return;
                _task.InstallFinished -= onInstallFinished;
                _version = version;
                OnServerVersionChanged();
            };
            task.InstallFinished += onInstallFinished;
            OnServerInstalling(task);
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
            return propertyFilesLazy.Value;
        }

        private IPropertyFile[] GetServerPropertyFilesCore()
        {
            string directory = ServerDirectory;
            return new IPropertyFile[1]
            {
                new JavaPropertyFile(Path.Combine(directory, "./nukkit.yml")),
            };
        }

        public override string[] GetSoftwareVersions()
        {
            return _versionsLazy.Value;
        }

        protected override ProcessStartInfo? PrepareProcessStartInfo(RuntimeEnvironment? environment)
        {
            if (environment is JavaRuntimeEnvironment currentEnv)
                return PrepareProcessStartInfoCore(currentEnv);
            return PrepareProcessStartInfoCore(RuntimeEnvironment.JavaDefault);
        }

        private ProcessStartInfo? PrepareProcessStartInfoCore(JavaRuntimeEnvironment environment)
        {
            string serverDirectory = ServerDirectory;
            string jarPath = Path.Combine(serverDirectory, "./powernukkit-" + GetReadableVersion() + ".jar");
            if (!File.Exists(jarPath))
                return null;
            return new ProcessStartInfo
            {
                FileName = environment.JavaPath ?? "java",
                Arguments = string.Format(
                    "-Djline.terminal=jline.UnsupportedTerminal -Dfile.encoding=UTF8 -Dsun.stdout.encoding=UTF8 -Dsun.stderr.encoding=UTF8 {0} -jar \"{1}\" {2}",
                    environment.JavaPreArguments ?? string.Empty,
                    jarPath,
                    environment.JavaPostArguments ?? string.Empty
                ),
                WorkingDirectory = serverDirectory,
                CreateNoWindow = true,
                ErrorDialog = true,
                UseShellExecute = false,
            };
        }

        protected override void StopServerCore(SystemProcess process, bool force)
        {
            if (force)
            {
                process.Kill();
                return;
            }
            process.InputCommand("stop");
        }

        public override void SetRuntimeEnvironment(RuntimeEnvironment? environment)
        {
            if (environment is JavaRuntimeEnvironment runtimeEnvironment)
            {
                _environment = runtimeEnvironment;
            }
            else if (environment is null)
            {
                _environment = null;
            }
        }

        public override bool UpdateServer()
        {
            return InstallSoftware(_version);
        }

        protected override bool CreateServer() => true;

        protected override bool LoadServerCore(JsonPropertyFile serverInfoJson)
        {
            string? version = serverInfoJson["version"]?.GetValue<string>();
            if (version is null || version.Length <= 0)
                return false;
            _version = version;
            string? jvmPath = serverInfoJson["java.path"]?.GetValue<string>() ?? null;
            string? jvmPreArgs = serverInfoJson["java.preArgs"]?.GetValue<string>() ?? null;
            string? jvmPostArgs = serverInfoJson["java.postArgs"]?.GetValue<string>() ?? null;
            if (jvmPath is not null || jvmPreArgs is not null || jvmPostArgs is not null)
            {
                _environment = new JavaRuntimeEnvironment(jvmPath, jvmPreArgs, jvmPostArgs);
            }
            return true;
        }

        protected override bool SaveServerCore(JsonPropertyFile serverInfoJson)
        {
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
