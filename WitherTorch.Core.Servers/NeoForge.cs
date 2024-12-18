﻿using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;
using System.Xml;

using WitherTorch.Core.Property;
using WitherTorch.Core.Servers.Utils;

using YamlDotNet.Core;

#if NET6_0_OR_GREATER
using System.Collections.Frozen;
#endif

namespace WitherTorch.Core.Servers
{
    /// <summary>
    /// NeoForge 伺服器
    /// </summary>
    public class NeoForge : AbstractJavaEditionServer<NeoForge>
    {
        private sealed class ForgeVersionData
        {
            public readonly string version;

            public readonly string versionRaw;

            public ForgeVersionData(string version, string versionRaw)
            {
                this.version = version;
                this.versionRaw = versionRaw;
            }
        }

        private const string LegacyManifestListURL = "https://maven.neoforged.net/releases/net/neoforged/forge/maven-metadata.xml";
        private const string ManifestListURL = "https://maven.neoforged.net/releases/net/neoforged/neoforge/maven-metadata.xml";
        private const string LegacyDownloadURL = "https://maven.neoforged.net/releases/net/neoforged/forge/{0}/forge-{0}-installer.jar";
        private const string DownloadURL = "https://maven.neoforged.net/releases/net/neoforged/neoforge/{0}/neoforge-{0}-installer.jar";

        private static readonly Lazy<IReadOnlyDictionary<string, ForgeVersionData[]>> _versionDictLazy =
            new Lazy<IReadOnlyDictionary<string, ForgeVersionData[]>>(LoadVersionList, LazyThreadSafetyMode.ExecutionAndPublication);
        private static readonly Lazy<string[]> _versionsLazy = new Lazy<string[]>(
            () => _versionDictLazy.Value.ToKeyArray(MojangAPI.VersionComparer.Instance.Reverse())
            , LazyThreadSafetyMode.PublicationOnly);

        private string _minecraftVersion = string.Empty;
        private string _forgeVersion = string.Empty;
        private JavaRuntimeEnvironment? _environment;
        private readonly IPropertyFile[] propertyFiles = new IPropertyFile[1];

        public JavaPropertyFile? ServerPropertiesFile => propertyFiles[0] as JavaPropertyFile;

        static NeoForge()
        {
            CallWhenStaticInitialize();
            SoftwareRegistrationDelegate += Initialize;
            SoftwareId = "neoforge";
        }

        public override string ServerVersion => _minecraftVersion;

        /// <summary>
        /// 取得 Forge 的版本號
        /// </summary>
        public string ForgeVersion => _forgeVersion;

        private static void Initialize()
        {
            var _ = _versionsLazy.Value;
        }

        private static IReadOnlyDictionary<string, ForgeVersionData[]> LoadVersionList()
        {
            try
            {
                return LoadVersionListInternal() ?? EmptyDictionary<string, ForgeVersionData[]>.Instance;
            }
            catch (Exception)
            {
            }
            GC.Collect(2, GCCollectionMode.Optimized);
            return EmptyDictionary<string, ForgeVersionData[]>.Instance;
        }

        private static IReadOnlyDictionary<string, ForgeVersionData[]> LoadVersionListInternal()
        {
            Dictionary<string, List<ForgeVersionData>> dict = new Dictionary<string, List<ForgeVersionData>>();
            try
            {
                LoadLegacyVersionData(dict);
            }
            catch (Exception)
            {
            }
            try
            {
                LoadVersionData(dict);
            }
            catch (Exception)
            {
            }
            Dictionary<string, ForgeVersionData[]> result = new Dictionary<string, ForgeVersionData[]>(dict.Count);
            foreach (var item in dict)
            {
                ForgeVersionData[] values = item.Value.ToArray();
                Array.Reverse(values);
                result.Add(item.Key, values);
            }

#if NET6_0_OR_GREATER
            return FrozenDictionary.ToFrozenDictionary(result);
#else
            return result;
#endif
        }

        private static void LoadLegacyVersionData(Dictionary<string, List<ForgeVersionData>> dict)
        {
            string? manifestString = CachedDownloadClient.Instance.DownloadString(ManifestListURL);
            if (string.IsNullOrEmpty(manifestString))
                return;
            XmlDocument manifestXML = new XmlDocument();
            manifestXML.LoadXml(manifestString);
            XmlNodeList? nodeList = manifestXML.SelectNodes("/metadata/versioning/versions/version");
            if (nodeList is null)
                return;
            foreach (XmlNode node in nodeList)
            {
                string versionString = node.InnerText;
                if (versionString is null || versionString == "1.20.1-47.1.7") //此版本不存在
                    continue;
                string[] versionSplits = node.InnerText.Split(new char[] { '-' });
                if (versionSplits.Length < 2)
                    continue;
                string version;
                unsafe
                {
                    string rawVersion = versionSplits[0];
                    fixed (char* rawVersionString = rawVersion)
                    {
                        char* rawVersionStringEnd = rawVersionString + rawVersion.Length;
                        char* pointerChar = rawVersionString;
                        while (pointerChar < rawVersionStringEnd)
                        {
                            if (*pointerChar == '_')
                            {
                                *pointerChar = '-';
                                break;
                            }
                            pointerChar++;
                        }
                        version = new string(rawVersionString).Replace(".0", "");
                    }
                }
                if (!dict.TryGetValue(version, out List<ForgeVersionData>? historyVersionList))
                    dict.Add(version, historyVersionList = new List<ForgeVersionData>());
                historyVersionList.Add(new ForgeVersionData(versionSplits[1], versionString));
            }
        }

        private static void LoadVersionData(Dictionary<string, List<ForgeVersionData>> dict)
        {
            string? manifestString = CachedDownloadClient.Instance.DownloadString(ManifestListURL);
            if (string.IsNullOrEmpty(manifestString))
                return;
            XmlDocument manifestXML = new XmlDocument();
            manifestXML.LoadXml(manifestString);
            XmlNodeList? nodeList = manifestXML.SelectNodes("/metadata/versioning/versions/version");
            if (nodeList is null)
                return;
            foreach (XmlNode token in nodeList)
            {
                string versionString = token.InnerText;
                if (versionString is null)
                    continue;
                string[] versionSplits = versionString.Split(new char[] { '-' });
                if (versionSplits.Length < 1)
                    continue;
                string version = versionSplits[0];
                string mcVersion = "1." + version.Substring(0, version.LastIndexOf('.'));
                if (!dict.TryGetValue(mcVersion, out List<ForgeVersionData>? historyVersionList))
                    dict.Add(mcVersion, historyVersionList = new List<ForgeVersionData>());
                historyVersionList.Add(new ForgeVersionData(version, versionString));
            }
        }

        public override bool ChangeVersion(int versionIndex)
        {
            return InstallSoftware(_versionsLazy.Value[versionIndex], string.Empty);
        }

        public bool ChangeVersion(int versionIndex, string forgeVersion)
        {
            return InstallSoftware(_versionsLazy.Value[versionIndex], forgeVersion);
        }

        private bool InstallSoftware(string minecraftVersion, string forgeVersion)
        {
            try
            {
                IReadOnlyDictionary<string, ForgeVersionData[]> versionDict = _versionDictLazy.Value;
                ForgeVersionData? selectedVersion;
                if (string.IsNullOrEmpty(forgeVersion))
                {
                    selectedVersion = versionDict[minecraftVersion][0];
                }
                else
                {
                    selectedVersion = Array.Find(versionDict[minecraftVersion], x => x.version == forgeVersion);
                    if (selectedVersion is null)
                        return false;
                }
                InstallSoftware(minecraftVersion, selectedVersion);
            }
            catch (Exception)
            {
                return false;
            }
            return true;
        }

        private bool InstallSoftware(string minecraftVersion, ForgeVersionData? selectedVersion)
        {
            InstallTask task = new InstallTask(this, minecraftVersion);
            OnServerInstalling(task);
            if (!InstallSoftware(task, minecraftVersion, selectedVersion))
            {
                task.OnInstallFailed();
                return false;
            }
            return true;
        }

        private bool InstallSoftware(InstallTask task, string minecraftVersion, ForgeVersionData? selectedVersion)
        {
            if (selectedVersion is null)
                return false;
            string version = selectedVersion.version;
            if (string.IsNullOrEmpty(version))
                return false;
            string versionRaw = selectedVersion.versionRaw;
            if (string.IsNullOrEmpty(versionRaw))
                return false;
            string downloadURL;
            if (version.StartsWith(minecraftVersion.Substring(2)))
                downloadURL = string.Format(DownloadURL, versionRaw);
            else //Use Legacy URL
                downloadURL = string.Format(LegacyDownloadURL, versionRaw);
            string installerLocation = Path.Combine(ServerDirectory, $"neoforge-{versionRaw}-installer.jar");
            int? id = FileDownloadHelper.AddTask(task: task,
                downloadUrl: downloadURL, filename: installerLocation,
                percentageMultiplier: 0.5);
            if (id.HasValue)
            {
                void AfterDownload(object? sender, int sendingId)
                {
                    if (sendingId != id.Value)
                        return;
                    FileDownloadHelper.TaskFinished -= AfterDownload;
                    try
                    {
                        RunInstaller(task, installerLocation, minecraftVersion, version);
                    }
                    catch (Exception)
                    {
                        task.OnInstallFailed();
                    }
                };
                FileDownloadHelper.TaskFinished += AfterDownload;
                return true;
            }
            return false;
        }

        private void RunInstaller(InstallTask task, string jarPath, string minecraftVersion, string forgeVersion)
        {
            ProcessStatus installStatus = new ProcessStatus(50);
            task.ChangeStatus(installStatus);
            task.ChangePercentage(50);
            JavaRuntimeEnvironment environment = RuntimeEnvironment.JavaDefault;
            ProcessStartInfo startInfo = new ProcessStartInfo
            {
                FileName = environment.JavaPath,
                Arguments = string.Format("-Xms512M -Dsun.stdout.encoding=UTF8 -Dsun.stderr.encoding=UTF8 -jar \"{0}\" nogui --installServer", jarPath),
                WorkingDirectory = ServerDirectory,
                CreateNoWindow = true,
                ErrorDialog = true,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8
            };
            Process? innerProcess = Process.Start(startInfo);
            if (innerProcess is null)
            {
                task.OnInstallFailed();
                return;
            }
            void StopRequestedHandler(object? sender, EventArgs e)
            {
                try
                {
                    innerProcess.Kill();
                    innerProcess.Dispose();
                }
                catch (Exception)
                {
                }
                task.StopRequested -= StopRequestedHandler;
            }
            task.StopRequested += StopRequestedHandler;
            innerProcess.EnableRaisingEvents = true;
            innerProcess.BeginOutputReadLine();
            innerProcess.BeginErrorReadLine();
            innerProcess.OutputDataReceived += (sender, e) =>
            {
                installStatus.OnProcessMessageReceived(sender, e);
            };
            innerProcess.ErrorDataReceived += (sender, e) =>
            {
                installStatus.OnProcessMessageReceived(sender, e);
            };
            innerProcess.Exited += (sender, e) =>
            {
                task.StopRequested -= StopRequestedHandler;
                _minecraftVersion = minecraftVersion;
                _forgeVersion = forgeVersion;
                mojangVersionInfo = null;
                OnServerVersionChanged();
                task.OnInstallFinished();
                task.ChangePercentage(100);
                innerProcess.Dispose();
            };
        }

        public override string GetReadableVersion()
        {
            return SoftwareUtils.GetReadableVersionString(_minecraftVersion, _forgeVersion);
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

        public static string[] GetForgeVersionsFromMCVersion(string mcVersion)
        {
            if (_versionDictLazy.Value.TryGetValue(mcVersion, out ForgeVersionData[]? versionPairs) == true)
            {
                int length = versionPairs.Length;
                string[] result = new string[length];
                for (int i = 0; i < length; i++)
                {
                    result[i] = versionPairs[i].version;
                }
                return result;
            }
            return Array.Empty<string>();
        }

        private string GetFullVersionString()
        {
            return Array.Find(_versionDictLazy.Value[_minecraftVersion], item => item.version == _forgeVersion)?.versionRaw ?? string.Empty;
        }

        private IEnumerable<string> GetPossibleForgePaths(string fullVersionString)
        {
            string serverDir = ServerDirectory;
            yield return Path.Combine(serverDir, "neoforge-" + fullVersionString + "-universal.jar");
            yield return Path.Combine(serverDir, "neoforge-" + fullVersionString + ".jar");
            yield return Path.Combine(serverDir, "forge-" + fullVersionString + "-universal.jar");
            yield return Path.Combine(serverDir, "forge-" + fullVersionString + ".jar");
        }

        public override bool RunServer(JavaRuntimeEnvironment? environment)
        {
            if (_isStarted)
                return true;
            environment ??= RuntimeEnvironment.JavaDefault;
            string? javaPath = environment.JavaPath;
            if (javaPath is null || !File.Exists(javaPath))
                javaPath = RuntimeEnvironment.JavaDefault.JavaPath;
            string fullVersionString = GetFullVersionString(), argument;
            string? path = null;
            foreach (string _path in GetPossibleForgePaths(fullVersionString))
            {
                if (File.Exists(_path))
                {
                    path = _path;
                    break;
                }
            }
            if (path is null)
            {
                string argPath;
                if (fullVersionString.StartsWith(_minecraftVersion.Substring(2)))
                    argPath = "@libraries/net/neoforged/neoforge/" + fullVersionString;
                else
                    argPath = "@libraries/net/neoforged/forge/" + fullVersionString;
#if NET5_0_OR_GREATER
                if (OperatingSystem.IsWindows())
                {
                    path += "/win_args.txt";
                }
                else if (OperatingSystem.IsLinux())
                {
                    path += "/unix_args.txt";
                }
                else
                {
                    switch (Environment.OSVersion.Platform)
                    {
                        case PlatformID.Unix:
                            path += "/unix_args.txt";
                            break;
                    }
                }
#else
                switch (Environment.OSVersion.Platform)
                {
                    case PlatformID.Win32NT:
                        path += "/win_args.txt";
                        break;
                    case PlatformID.Unix:
                        path += "/unix_args.txt";
                        break;
                }
#endif
                argument = "-Dfile.encoding=UTF8 -Dsun.stdout.encoding=UTF8 -Dsun.stderr.encoding=UTF8 {0} {1} {2}";
            }
            else
            {
                argument = "-Dfile.encoding=UTF8 -Dsun.stdout.encoding=UTF8 -Dsun.stderr.encoding=UTF8 {0} -jar \"{1}\" {2}";
            }
            ProcessStartInfo startInfo = new ProcessStartInfo
            {
                FileName = javaPath,
                Arguments = string.Format(argument
                                , environment.JavaPreArguments ?? RuntimeEnvironment.JavaDefault.JavaPreArguments
                                , path
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

        protected override MojangAPI.VersionInfo? BuildVersionInfo()
        {
            return FindVersionInfo(_minecraftVersion);
        }

        /// <inheritdoc/>
        protected override bool CreateServer()
        {
            try
            {
                propertyFiles[0] = new JavaPropertyFile(Path.Combine(ServerDirectory, "./server.properties"));
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
            string? minecraftVersion = serverInfoJson["version"]?.GetValue<string>();
            if (minecraftVersion is null)
                return false;
            string? forgeVersion = serverInfoJson["forge-version"]?.GetValue<string>();
            if (forgeVersion is null)
                return false;
            _minecraftVersion = minecraftVersion;
            _forgeVersion = forgeVersion;
            propertyFiles[0] = new JavaPropertyFile(Path.GetFullPath(Path.Combine(ServerDirectory, "./server.properties")));
            string? jvmPath = serverInfoJson["java.path"]?.GetValue<string>() ?? null;
            string? jvmPreArgs = serverInfoJson["java.preArgs"]?.ToString() ?? null;
            string? jvmPostArgs = serverInfoJson["java.postArgs"]?.ToString() ?? null;
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

        protected override bool BeforeServerSaved()
        {
            JsonPropertyFile? serverInfoJson = ServerInfoJson;
            if (serverInfoJson is null)
                return false;
            serverInfoJson["version"] = _minecraftVersion;
            serverInfoJson["forge-version"] = _forgeVersion;
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
            return InstallSoftware(_minecraftVersion, string.Empty);
        }

    }
}
