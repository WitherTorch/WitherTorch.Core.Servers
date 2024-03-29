﻿using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Xml;
using Newtonsoft.Json.Linq;
using WitherTorch.Core.Servers.Utils;
using WitherTorch.Core.Utils;

namespace WitherTorch.Core.Servers
{
    public class PowerNukkit : Server<PowerNukkit>
    {
        private const string manifestListURL = "https://repo1.maven.org/maven2/org/powernukkit/powernukkit/maven-metadata.xml";
        private const string downloadURL = "https://repo1.maven.org/maven2/org/powernukkit/powernukkit/{0}/powernukkit-{0}-shaded.jar";
        private static readonly Dictionary<string, string> versionDict = new Dictionary<string, string>();
        private static string[] versions;
        private bool _isStarted;
        private string versionString;
        private SystemProcess process;
        private JavaRuntimeEnvironment environment;
        private readonly IPropertyFile[] propertyFiles = new IPropertyFile[2];

        static PowerNukkit()
        {
            SoftwareID = "powerNukkit";
        }

        public override string ServerVersion => versionString;

        private static void LoadVersionList()
        {
            string manifestString = CachedDownloadClient.Instance.DownloadString(manifestListURL);
            if (manifestString != null)
            {
                XmlDocument manifestXML = new XmlDocument();
                manifestXML.LoadXml(manifestString);
                List<string> versionList = new List<string>();
                foreach (XmlNode token in manifestXML.SelectNodes("/metadata/versioning/versions/version"))
                {
                    string rawVersion = token.InnerText;
                    string[] versions = rawVersion.Split(new char[] { '-' }, 3);
                    if (versions.Length == 3) continue;
                    string key = versions[0];
                    if (versionDict.ContainsKey(key))
                    {
                        versionDict[key] = rawVersion;
                    }
                    else
                    {
                        versionDict.Add(key, rawVersion);
                        versionList.Insert(0, key);
                    }
                }
                versions = versionList.ToArray();
            }
            else
            {
                versions = Array.Empty<string>();
            }
        }

        public override bool ChangeVersion(int versionIndex)
        {
            if (versions is null)
            {
                LoadVersionList();
            }
            versionString = versions[versionIndex];
            InstallSoftware();
            return true;
        }

        private void InstallSoftware()
        {
            InstallTask task = new InstallTask(this);
            OnServerInstalling(task);
            if (!InstallSoftware(task, versionString))
            {
                task.OnInstallFailed();
                return;
            }
        }

        private bool InstallSoftware(InstallTask task, string versionString)
        {
            if (string.IsNullOrEmpty(versionString) || !versionDict.TryGetValue(versionString, out string fullVersionString))
                return false;
            return FileDownloadHelper.AddTask(task: task,
                downloadUrl: string.Format(downloadURL, fullVersionString),
                filename: Path.Combine(ServerDirectory, @"powernukkit-" + versionString + ".jar")).HasValue;
        }

        public override AbstractProcess GetProcess()
        {
            return process;
        }

        public override string GetReadableVersion()
        {
            return versionString;
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
                        , Path.Combine(ServerDirectory, @"powernukkit-" + versionString + ".jar")
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
            if (versions is null) LoadVersionList();
            return ChangeVersion(Array.IndexOf(versions, versionString));
        }

        protected override bool CreateServer()
        {
            try
            {
                process = new SystemProcess();
                process.ProcessStarted += delegate (object sender, EventArgs e) { _isStarted = true; };
                process.ProcessEnded += delegate (object sender, EventArgs e) { _isStarted = false; };
                propertyFiles[0] = new JavaPropertyFile(Path.Combine(ServerDirectory, "./server.properties"));
                propertyFiles[1] = new YamlPropertyFile(Path.Combine(ServerDirectory, "./nukkit.yml"));
            }
            catch (Exception)
            {
                return false;
            }
            return true;
        }

        protected override bool OnServerLoading()
        {
            try
            {
                JsonPropertyFile serverInfoJson = ServerInfoJson;
                versionString = serverInfoJson["version"].ToString();
                process = new SystemProcess();
                process.ProcessStarted += delegate (object sender, EventArgs e) { _isStarted = true; };
                process.ProcessEnded += delegate (object sender, EventArgs e) { _isStarted = false; };
                propertyFiles[0] = new JavaPropertyFile(Path.Combine(ServerDirectory, "./server.properties"));
                propertyFiles[1] = new YamlPropertyFile(Path.Combine(ServerDirectory, "./nukkit.yml"));
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
            serverInfoJson["version"] = versionString;
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
    }
}
