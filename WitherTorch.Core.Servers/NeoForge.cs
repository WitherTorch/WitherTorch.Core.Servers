﻿using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Text;
using System.Threading;
using System.Xml;
using Newtonsoft.Json.Linq;
using WitherTorch.Core.Servers.Utils;
using WitherTorch.Core.Utils;

namespace WitherTorch.Core.Servers
{
    /// <summary>
    /// NeoForge 伺服器
    /// </summary>
    public class NeoForge : AbstractJavaEditionServer<NeoForge>
    {
        private const string manifestListURL = "https://maven.neoforged.net/releases/net/neoforged/forge/maven-metadata.xml";
        private const string downloadURLPrefix = "https://maven.neoforged.net/releases/net/neoforged/forge/";
        private readonly static int downloadURLPrefixLength = downloadURLPrefix.Length;
        private static StringBuilder URLBuilder = null;
        internal static string[] versions;
        internal static Dictionary<string, Tuple<string, string>[]> versionDict;
        protected bool _isStarted;

        protected SystemProcess process;
        private string versionString;
        private string forgeVersion;
        private JavaRuntimeEnvironment environment;
        readonly IPropertyFile[] propertyFiles = new IPropertyFile[1];
        public JavaPropertyFile ServerPropertiesFile => propertyFiles[0] as JavaPropertyFile;

        static NeoForge()
        {
            CallWhenStaticInitialize();
            SoftwareRegistrationDelegate += Initialize;
            SoftwareID = "neoforge";
        }

        public override string ServerVersion => versionString;

        /// <summary>
        /// 取得 Forge 的版本號
        /// </summary>
        public string ForgeVersion => forgeVersion;

        private static void Initialize()
        {
            LoadVersionList();
        }

        internal static void LoadVersionList()
        {
            Dictionary<string, List<Tuple<string, string>>> preparingVersionDict = new Dictionary<string, List<Tuple<string, string>>>();
            try
            {
                string manifestString = CachedDownloadClient.Instance.DownloadString(manifestListURL);
                if (manifestString != null)
                {
                    XmlDocument manifestXML = new XmlDocument();
                    manifestXML.LoadXml(manifestString);
                    List<Tuple<string, string>> historyVersionList = null;
                    foreach (XmlNode token in manifestXML.SelectNodes("/metadata/versioning/versions/version"))
                    {
                        if (token.InnerText == "1.20.1-47.1.7") continue; //此版本不存在
                        string[] versionSplits = token.InnerText.Split(new char[] { '-' });
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
                        if (!preparingVersionDict.ContainsKey(version))
                        {
                            historyVersionList = new List<Tuple<string, string>>();
                            preparingVersionDict.Add(version, historyVersionList);
                        }
                        historyVersionList?.Add(new Tuple<string, string>(versionSplits[1], token.InnerText));
                    }
                }
            }
            catch (Exception)
            {

            }
            versionDict = new Dictionary<string, Tuple<string, string>[]>();
            var keys = preparingVersionDict.Keys;
            List<string> versionKeys = new List<string>(keys.Count);
            versionKeys.AddRange(keys);
            var comparer = MojangAPI.VersionComparer.Instance;
            if (comparer is null)
            {
                using (ManualResetEvent trigger = new ManualResetEvent(false))
                {
                    void trig(object sender, EventArgs e)
                    {
                        trigger.Set();
                    }
                    MojangAPI.Initialized += trig;
                    if (MojangAPI.VersionDictionary is null)
                    {
                        trigger.WaitOne();
                    }
                    MojangAPI.Initialized -= trig;
                    comparer = MojangAPI.VersionComparer.Instance;
                }
            }
            string[] versions = versionKeys.ToArray();
            Array.Sort(versions, comparer);
            Array.Reverse(versions);
            NeoForge.versions = versions;
            for (int i = 0; i < versions.Length; i++)
            {
                string key = versions[i];
                var list = preparingVersionDict[key];
                list.Reverse();
                versionDict.Add(key, list.ToArray());
                preparingVersionDict[key] = null;
            }
        }

        public bool ChangeVersion(int versionIndex, string forgeVersion)
        {
            try
            {
                if (versions is null) LoadVersionList();
                versionString = versions[versionIndex];
                BuildVersionInfo();
                Tuple<string, string> selectedVersion;
                if (forgeVersion is null)
                {
                    selectedVersion = versionDict[versionString][0];
                }
                else
                {
                    selectedVersion = Array.Find(versionDict[versionString], x => x.Item1 == forgeVersion);
                    if (selectedVersion is null)
                        return false;
                }
                this.forgeVersion = selectedVersion.Item1;
                _cache = null;
                InstallSoftware(selectedVersion);
            }
            catch (Exception)
            {
                return false;
            }
            return true;
        }

        public override bool ChangeVersion(int versionIndex)
        {
            return ChangeVersion(versionIndex, null);
        }

        public void InstallSoftware(Tuple<string, string> selectedVersion)
        {
            WebClient2 client = new WebClient2();
            InstallTask installingTask = new InstallTask(this);
            OnServerInstalling(installingTask);
            string downloadURL = null;
            if (URLBuilder is null)
            {
                URLBuilder = new StringBuilder(downloadURLPrefix, downloadURLPrefixLength);
            }
            else
            {
                URLBuilder.Append(downloadURLPrefix);
            }
            URLBuilder.AppendFormat("{0}/forge-{0}-installer.jar", selectedVersion.Item2);
            downloadURL = URLBuilder.ToString();
            URLBuilder.Clear();
            if (downloadURL != null)
            {
                string installerLocation;
                installerLocation = Path.Combine(ServerDirectory, @"neoforge-" + selectedVersion.Item2 + "-installer.jar");
                DownloadHelper helper = new DownloadHelper(
                    task: installingTask, webClient: client, downloadUrl: downloadURL,
                    filename: installerLocation, finishInstallTaskAfterDownload: false, percentageMultiplier: 0.5);
                helper.DownloadCompleted += delegate
                {
                    try
                    {
                        RunInstaller(installingTask, installerLocation);
                    }
                    catch (Exception)
                    {
                        installingTask.OnInstallFailed();
                    }
                };
                helper.Start();
            }
        }

        private void RunInstaller(InstallTask task, string jarPath)
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
            System.Diagnostics.Process innerProcess = System.Diagnostics.Process.Start(startInfo);
            void StopRequestedHandler(object sender, EventArgs e)
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
                task.OnInstallFinished();
                task.ChangePercentage(100);
                innerProcess.Dispose();
            };
        }


        public override AbstractProcess GetProcess()
        {
            return process;
        }

        string _cache;
        public override string GetReadableVersion()
        {
            if (_cache is null)
            {
                _cache = versionString + "-" + forgeVersion;
            }
            return _cache;
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
            if (versions is null)
            {
                LoadVersionList();
            }
            return versions;
        }

        public static string[] GetForgeVersionsFromMCVersion(string mcVersion)
        {
            if (versionDict is null)
            {
                LoadVersionList();
            }
            if (versionDict?.TryGetValue(mcVersion, out Tuple<string, string>[] versionPairs) == true)
            {
                int length = versionPairs.Length;
                string[] result = new string[length];
                for (int i = 0; i < length; i++)
                {
                    result[i] = versionPairs[i].Item1;
                }
                return result;
            }
            return Array.Empty<string>();
        }

        private string GetFullVersionString()
        {
            return Array.Find(versionDict[versionString], tuple => tuple.Item1 == forgeVersion).Item2;
        }

        private IEnumerable<string> GetPossibleForgePaths(string fullVersionString)
        {
            string serverDir = ServerDirectory;
            yield return Path.Combine(serverDir, "neoforge-" + fullVersionString + "-universal.jar");
            yield return Path.Combine(serverDir, "neoforge-" + fullVersionString + ".jar");
            yield return Path.Combine(serverDir, "forge-" + fullVersionString + "-universal.jar");
            yield return Path.Combine(serverDir, "forge-" + fullVersionString + ".jar");
        }

        public override void RunServer(RuntimeEnvironment environment)
        {
            if (!_isStarted)
            {
                if (environment is null)
                    environment = RuntimeEnvironment.JavaDefault;
                if (environment is JavaRuntimeEnvironment javaRuntimeEnvironment)
                {
                    ProcessStartInfo startInfo = null;
                    string fullVersionString = GetFullVersionString();
                    string path = null;
                    foreach (string _path in GetPossibleForgePaths(fullVersionString))
                    {
                        if (File.Exists(_path))
                        {
                            path = _path;
                            break;
                        }
                    }
                    if (path is object)
                    {
                        string javaPath = javaRuntimeEnvironment.JavaPath;
                        if (javaPath is null || !File.Exists(javaPath))
                        {
                            javaPath = RuntimeEnvironment.JavaDefault.JavaPath;
                        }
                        startInfo = new ProcessStartInfo
                        {
                            FileName = javaPath,
                            Arguments = string.Format("-Dfile.encoding=UTF8 -Dsun.stdout.encoding=UTF8 -Dsun.stderr.encoding=UTF8 {0} -jar \"{1}\" {2}"
                            , javaRuntimeEnvironment.JavaPreArguments ?? RuntimeEnvironment.JavaDefault.JavaPreArguments
                            , path
                            , javaRuntimeEnvironment.JavaPostArguments ?? RuntimeEnvironment.JavaDefault.JavaPostArguments),
                            WorkingDirectory = ServerDirectory,
                            CreateNoWindow = true,
                            ErrorDialog = true,
                            UseShellExecute = false,
                        };
                    }
                    else
                    {
                        string argPath = "@libraries/net/neoforged/forge/" + fullVersionString;
#if NET472
                        switch (Environment.OSVersion.Platform)
                        {
                            case PlatformID.Win32NT:
                                argPath += "/win_args.txt";
                                break;
                            case PlatformID.Unix:
                                argPath += "/unix_args.txt";
                                break;
                        }
#elif NET5_0
                        if (OperatingSystem.IsWindows())
                        {
                            argPath += "/win_args.txt";
                        }
                        else if (OperatingSystem.IsLinux())
                        {
                            argPath += "/unix_args.txt";
                        }
                        else
                        {
                            switch (Environment.OSVersion.Platform)
                            {
                                case PlatformID.Unix:
                                    argPath += "/unix_args.txt";
                                    break;
                            }
                        }
#endif
                        if (File.Exists(Path.Combine(ServerDirectory, "./" + argPath.Substring(1))))
                        {
                            string javaPath = javaRuntimeEnvironment.JavaPath;
                            if (javaPath is null || !File.Exists(javaPath))
                            {
                                javaPath = RuntimeEnvironment.JavaDefault.JavaPath;
                            }
                            startInfo = new ProcessStartInfo
                            {
                                FileName = javaPath,
                                Arguments = string.Format("-Dfile.encoding=UTF8 -Dsun.stdout.encoding=UTF8 -Dsun.stderr.encoding=UTF8 {0} {1} {2}"
                                , javaRuntimeEnvironment.JavaPreArguments ?? RuntimeEnvironment.JavaDefault.JavaPreArguments
                                , argPath
                                , javaRuntimeEnvironment.JavaPostArguments ?? RuntimeEnvironment.JavaDefault.JavaPostArguments),
                                WorkingDirectory = ServerDirectory,
                                CreateNoWindow = true,
                                ErrorDialog = true,
                                UseShellExecute = false,
                            };
                        }
                    }
                    if (startInfo != null)
                    {
                        process.StartProcess(startInfo);
                    }
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
                JToken forgeVerNode = serverInfoJson["forge-version"];
                if (forgeVerNode?.Type == JTokenType.String)
                {
                    forgeVersion = forgeVerNode.ToString();
                }
                else
                {
                    return false;
                }
                process = new SystemProcess();
                process.ProcessStarted += delegate (object sender, EventArgs e) { _isStarted = true; };
                process.ProcessEnded += delegate (object sender, EventArgs e) { _isStarted = false; };
                propertyFiles[0] = new JavaPropertyFile(Path.GetFullPath(Path.Combine(ServerDirectory, "./server.properties")));
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

        protected override bool BeforeServerSaved()
        {
            JsonPropertyFile serverInfoJson = ServerInfoJson;
            serverInfoJson["version"] = versionString;
            serverInfoJson["forge-version"] = forgeVersion;
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
