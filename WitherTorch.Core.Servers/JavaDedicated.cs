using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.CompilerServices;
using System.Text.Json.Nodes;
using System.Threading;
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

        public override string ServerVersion => _version;

        public JavaDedicated()
        {
            propertyFilesLazy = new Lazy<IPropertyFile[]>(GetServerPropertyFilesCore, LazyThreadSafetyMode.ExecutionAndPublication);
        }

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
            OnServerInstalling(task);
            task.ChangeStatus(PreparingInstallStatus.Instance);
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
        protected override bool CreateServer() => true;

        /// <inheritdoc/>
        protected override bool LoadServerCore(JsonPropertyFile serverInfoJson)
        {
            string? version = (serverInfoJson["version"] as JsonValue)?.GetValue<string>();
            if (version is null || version.Length <= 0)
                return false;
            _version = version;
            return base.LoadServerCore(serverInfoJson);
        }

        protected override bool SaveServerCore(JsonPropertyFile serverInfoJson)
        {
            serverInfoJson["version"] = _version;
            return base.SaveServerCore(serverInfoJson);
        }

        protected override string GetServerJarPath()
            => Path.Combine(ServerDirectory, @"./minecraft_server." + GetReadableVersion() + ".jar");

        public override bool UpdateServer()
        {
            return InstallSoftware(_version);
        }
    }
}
