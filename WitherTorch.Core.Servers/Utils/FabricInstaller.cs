using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Xml;

using WitherTorch.Core.Runtime;
using WitherTorch.Core.Utils;

using static WitherTorch.Core.Utils.WebClient2;

namespace WitherTorch.Core.Servers.Utils
{
    internal sealed class FabricInstaller
    {
        private delegate void UpdateProgressChangedEventHandler(int progress);
        public delegate void AfterInstalledEventHandler(string minecraftVersion, string fabricLoaderVersion);

        private const string ManifestListURL = "https://maven.fabricmc.net/net/fabricmc/fabric-installer/maven-metadata.xml";
        private const string DownloadURL = "https://maven.fabricmc.net/net/fabricmc/fabric-installer/{0}/fabric-installer-{0}.jar";

        private static readonly DirectoryInfo workingDirectoryInfo = new DirectoryInfo(Path.Combine(Environment.CurrentDirectory, WTServer.FabricInstallerPath));
        private static readonly FileInfo buildToolFileInfo = new FileInfo(Path.Combine(workingDirectoryInfo.FullName + "./fabric-installer.jar"));
        private static readonly FileInfo buildToolVersionInfo = new FileInfo(Path.Combine(workingDirectoryInfo.FullName + "./fabric-installer.version"));
        private static readonly FabricInstaller _instance = new FabricInstaller();

        private event EventHandler? UpdateStarted;
        private event UpdateProgressChangedEventHandler? UpdateProgressChanged;
        private event EventHandler? UpdateFinished;

        public static FabricInstaller Instance => _instance;

        private static bool CheckUpdate(out string? updatedVersion)
        {
            string? version = null, nowVersion;
            if (workingDirectoryInfo.Exists)
            {
                if (buildToolVersionInfo.Exists && buildToolFileInfo.Exists)
                {
                    using (StreamReader reader = buildToolVersionInfo.OpenText())
                    {
                        string? versionText;
                        do
                        {
                            versionText = reader.ReadLine();
                        } while (string.IsNullOrWhiteSpace(versionText));
                        if (!string.IsNullOrWhiteSpace(versionText)) version = versionText;
                    }
                }
            }
            else
            {
                workingDirectoryInfo.Create();
            }

            XmlDocument manifestXML = new XmlDocument();
            using (WebClient2 client = new WebClient2())
            {
                client.DefaultRequestHeaders.Add("User-Agent", Constants.UserAgent);
                manifestXML.LoadXml(client.DownloadString(ManifestListURL));
            }
            nowVersion = manifestXML.SelectSingleNode("//metadata/versioning/latest")?.InnerText;
            if (version != nowVersion)
            {
                version = null;
            }
            updatedVersion = nowVersion;
            return version is null;
        }
        private void Update(InstallTask installTask, string? version)
        {
            if (version is null || version.Length <= 0)
                return;
            UpdateStarted?.Invoke(this, EventArgs.Empty);
            WebClient2? client = new WebClient2();
            void StopRequestedHandler(object? sender, EventArgs e)
            {
                try
                {
                    client?.CancelAsync();
                    client?.Dispose();
                }
                catch (Exception)
                {
                }
                installTask.StopRequested -= StopRequestedHandler;
            };
            installTask.StopRequested += StopRequestedHandler;
            client.DownloadProgressChanged += delegate (object? sender, DownloadProgressChangedEventArgs e)
                        {
                            UpdateProgressChanged?.Invoke(e.ProgressPercentage);
                        };
            client.DownloadFileCompleted += delegate (object? sender, AsyncCompletedEventArgs e)
            {
                client.Dispose();
                client = null;
                using (StreamWriter writer = buildToolVersionInfo.CreateText())
                {
                    writer.WriteLine(version);
                    writer.Flush();
                    writer.Close();
                }
                installTask.StopRequested -= StopRequestedHandler;
                UpdateFinished?.Invoke(this, EventArgs.Empty);
            };
            client.DefaultRequestHeaders.Add("User-Agent", Constants.UserAgent);
            client.DownloadFileAsync(new Uri(string.Format(DownloadURL, version)), buildToolFileInfo.FullName);
        }

        public void Install(InstallTask task, string minecraftVersion, string fabricLoaderVersion, AfterInstalledEventHandler afterInstalledEventHandler)
        {
            InstallTask installTask = task;
            FabricInstallerStatus status = new FabricInstallerStatus(SpigotBuildToolsStatus.ToolState.Initialize, 0);
            installTask.ChangeStatus(status);
            bool isStop = false;
            void StopRequestedHandler(object? sender, EventArgs e)
            {
                isStop = true;
                installTask.StopRequested -= StopRequestedHandler;
            };
            installTask.StopRequested += StopRequestedHandler;
            bool hasUpdate = CheckUpdate(out string? newVersion);
            installTask.StopRequested -= StopRequestedHandler;
            if (isStop) return;
            if (hasUpdate)
            {
                UpdateStarted += (sender, e) =>
                {
                    status.State = SpigotBuildToolsStatus.ToolState.Update;
                };
                UpdateProgressChanged += (progress) =>
                {
                    status.Percentage = progress;
                    installTask.ChangePercentage(progress / 2);
                };
                UpdateFinished += (sender, e) =>
                {
                    installTask.ChangePercentage(50);
                    installTask.OnStatusChanged();
                    DoInstall(installTask, status, minecraftVersion, fabricLoaderVersion, afterInstalledEventHandler);
                };
                Update(installTask, newVersion);
            }
            else
            {
                installTask.ChangePercentage(50);
                installTask.OnStatusChanged();
                DoInstall(installTask, status, minecraftVersion, fabricLoaderVersion, afterInstalledEventHandler);
            }
        }

        private static void DoInstall(InstallTask task, FabricInstallerStatus status, string minecraftVersion, string fabricLoaderVersion, AfterInstalledEventHandler afterInstalledEventHandler)
        {
            InstallTask installTask = task;
            FabricInstallerStatus installStatus = status;
            installStatus.State = SpigotBuildToolsStatus.ToolState.Build;
            JavaRuntimeEnvironment environment = RuntimeEnvironment.JavaDefault;
            ProcessStartInfo startInfo = new ProcessStartInfo
            {
                FileName = environment.JavaPath,
                Arguments = string.Format("-Xms512M -Dsun.stdout.encoding=UTF8 -Dsun.stderr.encoding=UTF8 -jar \"{0}\" server -mcversion {1} -loader {2} -dir \"{3}\" -downloadMinecraft", buildToolFileInfo.FullName, minecraftVersion, fabricLoaderVersion, installTask.Owner.ServerDirectory),
                WorkingDirectory = workingDirectoryInfo.FullName,
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
            innerProcess.EnableRaisingEvents = true;
            innerProcess.BeginOutputReadLine();
            innerProcess.BeginErrorReadLine();
            innerProcess.OutputDataReceived += installStatus.OnProcessMessageReceived;
            innerProcess.ErrorDataReceived += installStatus.OnProcessMessageReceived;
            innerProcess.Exited += (sender, e) =>
            {
                afterInstalledEventHandler.Invoke(minecraftVersion, fabricLoaderVersion);
                installTask.OnInstallFinished();
                installTask.ChangePercentage(100);
                innerProcess.Dispose();
            };
        }
    }
}
