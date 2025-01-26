using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;
using System.Xml;

using WitherTorch.Core.Servers.Utils;
using WitherTorch.Core.Property;
using YamlDotNet.Core.Tokens;
using System.Runtime.CompilerServices;
using System.Linq;


#if NET6_0_OR_GREATER
using System.Collections.Frozen;
#endif

namespace WitherTorch.Core.Servers
{
    /// <summary>
    /// Forge 伺服器
    /// </summary>
    public class Forge : AbstractJavaEditionServer<Forge>
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

        private const string manifestListURL = "https://maven.minecraftforge.net/net/minecraftforge/forge/maven-metadata.xml";
        private const string downloadURLPrefix = "https://maven.minecraftforge.net/net/minecraftforge/forge/";

        private static readonly int downloadURLPrefixLength = downloadURLPrefix.Length;
        private static readonly ThreadLocal<StringBuilder> localStringBuilder = new ThreadLocal<StringBuilder>(() => new StringBuilder(), false);
        private static readonly Lazy<MojangAPI.VersionInfo?> mc1_3_2 = new Lazy<MojangAPI.VersionInfo?>(
            () => (MojangAPI.VersionDictionary?.TryGetValue("1.3.2", out MojangAPI.VersionInfo? result) ?? false) ? result : null,
            LazyThreadSafetyMode.PublicationOnly);
        private static readonly Lazy<MojangAPI.VersionInfo?> mc1_5_2 = new Lazy<MojangAPI.VersionInfo?>(
            () => (MojangAPI.VersionDictionary?.TryGetValue("1.3.2", out MojangAPI.VersionInfo? result) ?? false) ? result : null,
            LazyThreadSafetyMode.PublicationOnly);
        private static readonly Lazy<IReadOnlyDictionary<string, ForgeVersionData[]>> _versionDictLazy =
            new Lazy<IReadOnlyDictionary<string, ForgeVersionData[]>>(LoadVersionList, LazyThreadSafetyMode.ExecutionAndPublication);
        private static readonly Lazy<string[]> _versionsLazy = new Lazy<string[]>(
            () => _versionDictLazy.Value.ToKeyArray(MojangAPI.VersionComparer.Instance.Reverse())
        , LazyThreadSafetyMode.PublicationOnly);

        private string _minecraftVersion = string.Empty;
        private string _forgeVersion = string.Empty;

        private readonly Lazy<IPropertyFile[]> propertyFilesLazy;

        public JavaPropertyFile ServerPropertiesFile
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => (JavaPropertyFile)propertyFilesLazy.Value[0];
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            private set
            {
                IPropertyFile[] propertyFiles = propertyFilesLazy.Value;
                IPropertyFile propertyFile = propertyFiles[0];
                propertyFiles[0] = value;
                propertyFile.Dispose();
            }
        }

        static Forge()
        {
            CallWhenStaticInitialize();
            SoftwareRegistrationDelegate += Initialize;
            SoftwareId = "forge";
        }

        public Forge()
        {
            propertyFilesLazy = new Lazy<IPropertyFile[]>(GetServerPropertyFilesCore, LazyThreadSafetyMode.ExecutionAndPublication);
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

        private static IReadOnlyDictionary<string, ForgeVersionData[]>? LoadVersionListInternal()
        {
            string? manifestString = CachedDownloadClient.Instance.DownloadString(manifestListURL);
            if (string.IsNullOrEmpty(manifestString))
                return null;
            XmlDocument manifestXML = new XmlDocument();
            manifestXML.LoadXml(manifestString);
            Dictionary<string, List<ForgeVersionData>> dict = new Dictionary<string, List<ForgeVersionData>>();
            XmlNodeList? nodes = manifestXML.SelectNodes("/metadata/versioning/versions/version");
            if (nodes is null)
                return null;
            foreach (XmlNode node in nodes)
            {
                string versionString = node.InnerText;
                if (versionString is null)
                    continue;
                string[] versionSplits = versionString.Split(new char[] { '-' });
                string version;
                unsafe
                {
                    string rawVersion = versionSplits[0];
                    fixed (char* rawVersionString = rawVersion)
                    {
                        char* iterator = rawVersionString;
                        while (*iterator++ != '\0')
                        {
                            if (*iterator == '_')
                            {
                                *iterator = '-';
                                break;
                            }
                        }
                        version = new string(rawVersionString).Replace(".0", "");
                    }
                }
                if (!dict.TryGetValue(version, out List<ForgeVersionData>? historyVersionList))
                    dict.Add(version, historyVersionList = new List<ForgeVersionData>());
                historyVersionList.Add(new ForgeVersionData(versionSplits[1], versionString));
            }

            Dictionary<string, ForgeVersionData[]> result = new Dictionary<string, ForgeVersionData[]>(dict.Count);
            foreach (var pair in dict)
            {
                result.Add(pair.Key, pair.Value.ToArray());
            }

#if NET6_0_OR_GREATER
            return FrozenDictionary.ToFrozenDictionary(result);
#else
            return result;
#endif
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

        private void InstallSoftware(string minecraftVersion, ForgeVersionData selectedVersion)
        {
            InstallTask task = new InstallTask(this, minecraftVersion + "-" + selectedVersion.version);
            OnServerInstalling(task);
            if (!InstallSoftware(task, minecraftVersion, selectedVersion))
            {
                task.OnInstallFailed();
                return;
            }
        }

        private bool InstallSoftware(InstallTask task, string minecraftVersion, ForgeVersionData selectedVersion)
        {
            if (selectedVersion is null)
                return false;
            string version = selectedVersion.version;
            if (string.IsNullOrEmpty(version))
                return false;
            string versionRaw = selectedVersion.versionRaw;
            if (string.IsNullOrEmpty(versionRaw))
                return false;

            bool needInstall;
            string downloadURL;
            StringBuilder URLBuilder = ObjectUtils.ThrowIfNull(localStringBuilder.Value);
            URLBuilder.Append(downloadURLPrefix);
            MojangAPI.VersionInfo? info = FindVersionInfo(minecraftVersion);
            if (info is null)
                return false;
            MojangAPI.VersionInfo? mc1_3_2 = Forge.mc1_3_2.Value;
            MojangAPI.VersionInfo? mc1_5_2 = Forge.mc1_5_2.Value;
            if (mc1_3_2 is null || mc1_5_2 is null)
                return false;
            if (info < mc1_3_2) // 1.1~1.2 > Download Server Zip (i don't know why forge use zip...)
            {
                URLBuilder.AppendFormat("{0}/forge-{0}-server.zip", versionRaw);
                downloadURL = URLBuilder.ToString();
                needInstall = false;
            }
            else
            {
                if (info < mc1_5_2) // 1.3.2~1.5.1 > Download Universal Zip (i don't know why forge use zip...)
                {
                    URLBuilder.AppendFormat("{0}/forge-{0}-universal.zip", versionRaw);
                    downloadURL = URLBuilder.ToString();
                    needInstall = false;
                }
                else  // 1.5.2 or above > Download Installer (*.jar)
                {
                    URLBuilder.AppendFormat("{0}/forge-{0}-installer.jar", versionRaw);
                    downloadURL = URLBuilder.ToString();
                    needInstall = true;
                }
            }
            URLBuilder.Clear();
            string installerLocation = needInstall ? $"forge-{versionRaw}-installer.jar" : $"forge-{versionRaw}.jar";
            installerLocation = Path.Combine(ServerDirectory, installerLocation);
            int? id = FileDownloadHelper.AddTask(task: task,
                downloadUrl: downloadURL, filename: installerLocation,
                percentageMultiplier: needInstall ? 0.5 : 1.0);
            if (id.HasValue)
            {
                if (needInstall)
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
                }
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
                task.OnInstallFinished();
                task.ChangePercentage(100);
                innerProcess.Dispose();
            };
        }

        public override string GetReadableVersion()
        {
            return SoftwareUtils.GetReadableVersionString(_minecraftVersion, _forgeVersion);
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
                new JavaPropertyFile(Path.Combine(directory, "./server.properties")),
            };
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

        protected override MojangAPI.VersionInfo? BuildVersionInfo()
        {
            return FindVersionInfo(_minecraftVersion);
        }

        /// <inheritdoc/>
        protected override bool CreateServer() => true;

        protected override bool LoadServerCore(JsonPropertyFile serverInfoJson)
        {
            string? minecraftVersion = serverInfoJson["version"]?.GetValue<string>();
            if (minecraftVersion is null)
                return false;
            string? forgeVersion = serverInfoJson["forge-version"]?.GetValue<string>();
            if (forgeVersion is null)
                return false;
            _minecraftVersion = minecraftVersion;
            _forgeVersion = forgeVersion;
            return base.LoadServerCore(serverInfoJson);
        }

        protected override bool SaveServerCore(JsonPropertyFile serverInfoJson)
        {
            serverInfoJson["version"] = _minecraftVersion;
            serverInfoJson["forge-version"] = _forgeVersion;
            return base.SaveServerCore(serverInfoJson);
        }

        protected override ProcessStartInfo? PrepareProcessStartInfoCore(JavaRuntimeEnvironment environment)
        {
            string fullVersionString = GetFullVersionString();
            string? path = GetPossibleForgePaths(fullVersionString)
                .TakeWhile(File.Exists)
                .FirstOrDefault();
            if (path is null)
                return PrepareProcessStartInfoForArgFile(environment, "@libraries/net/minecraftforge/forge/" + fullVersionString);
            return new ProcessStartInfo
            {
                FileName = environment.JavaPath ?? "java",
                Arguments = string.Format(
                    "-Djline.terminal=jline.UnsupportedTerminal -Dfile.encoding=UTF8 -Dsun.stdout.encoding=UTF8 -Dsun.stderr.encoding=UTF8 {0} -jar \"{1}\" {2}",
                    environment.JavaPreArguments ?? string.Empty,
                    path,
                    environment.JavaPostArguments ?? string.Empty
                ),
                WorkingDirectory = ServerDirectory,
                CreateNoWindow = true,
                ErrorDialog = true,
                UseShellExecute = false,
            };
        }

        private ProcessStartInfo? PrepareProcessStartInfoForArgFile(JavaRuntimeEnvironment environment, string path)
        {
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
            return new ProcessStartInfo
            {
                FileName = environment.JavaPath ?? "java",
                Arguments = string.Format(
                    "-Dfile.encoding=UTF8 -Dsun.stdout.encoding=UTF8 -Dsun.stderr.encoding=UTF8 {0} {1} {2}",
                    environment.JavaPreArguments ?? string.Empty,
                    path,
                    environment.JavaPostArguments ?? string.Empty
                ),
                WorkingDirectory = ServerDirectory,
                CreateNoWindow = true,
                ErrorDialog = true,
                UseShellExecute = false,
            };
        }

        private IEnumerable<string> GetPossibleForgePaths(string fullVersionString)
        {
            string serverDirectory = ServerDirectory;
            yield return Path.Combine(serverDirectory, "./forge-" + fullVersionString + "-universal.jar");
            yield return Path.Combine(serverDirectory, "./forge-" + fullVersionString + ".jar");
        }

        protected override string GetServerJarPath()
        {
            return GetPossibleForgePaths(GetFullVersionString())
                  .TakeWhile(File.Exists)
                  .FirstOrDefault() ?? string.Empty;
        }

        public override bool UpdateServer()
        {
            return InstallSoftware(_minecraftVersion, string.Empty);
        }
    }
}
