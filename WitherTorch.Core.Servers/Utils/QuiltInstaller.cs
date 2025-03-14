using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Xml;

using WitherTorch.Core.Utils;

using static WitherTorch.Core.Utils.WebClient2;

namespace WitherTorch.Core.Servers.Utils
{
    /// <summary>
    /// 操作 Quilt 官方的安裝工具 (Quilt Installer) 的類別，此類別無法建立實體
    /// </summary>
    public sealed class QuiltInstaller
    {
        private delegate void UpdateProgressChangedEventHandler(int progress);

        private const string manifestListURL = "https://maven.quiltmc.org/repository/release/org/quiltmc/quilt-installer/maven-metadata.xml";
        private const string downloadURL = "https://maven.quiltmc.org/repository/release/org/quiltmc/quilt-installer/{0}/quilt-installer-{0}.jar";

        private static readonly DirectoryInfo workingDirectoryInfo = new DirectoryInfo(Path.Combine(Environment.CurrentDirectory, WTServer.QuiltInstallerPath));
        private static readonly FileInfo buildToolFileInfo = new FileInfo(Path.Combine(workingDirectoryInfo.FullName + "./quilt-installer.jar"));
        private static readonly FileInfo buildToolVersionInfo = new FileInfo(Path.Combine(workingDirectoryInfo.FullName + "./quilt-installer.version"));
        private static readonly QuiltInstaller _instance = new QuiltInstaller();

        private event EventHandler? UpdateStarted;
        private event UpdateProgressChangedEventHandler? UpdateProgressChanged;
        private event EventHandler? UpdateFinished;

        /// <summary>
        /// <see cref="QuiltInstaller"/> 的唯一實例
        /// </summary>
        public static QuiltInstaller Instance => _instance;

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
                manifestXML.LoadXml(client.DownloadString(manifestListURL));
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
            if (version is null)
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
            client.DownloadFileAsync(new Uri(string.Format(downloadURL, version)), buildToolFileInfo.FullName);
        }

        /// <summary>
        /// 用指定的 Minecraft 版本和 Fabric Loader 版本來安裝伺服器軟體
        /// </summary>
        /// <param name="task">要紀錄安裝過程的工作物件</param>
        /// <param name="minecraftVersion">要安裝的 Minecraft 版本</param>
        /// <param name="quiltLoaderVersion">要安裝的 Quilt Loader 版本</param>
        public void Install(InstallTask task, string minecraftVersion, string quiltLoaderVersion)
        {
            InstallTask installTask = task;
            QuiltInstallerStatus status = new QuiltInstallerStatus(SpigotBuildToolsStatus.ToolState.Initialize, 0);
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
                    DoInstall(installTask, status, minecraftVersion, quiltLoaderVersion);
                };
                Update(installTask, newVersion);
            }
            else
            {
                installTask.ChangePercentage(50);
                installTask.OnStatusChanged();
                DoInstall(installTask, status, minecraftVersion, quiltLoaderVersion);
            }
        }

        private static void DoInstall(InstallTask task, QuiltInstallerStatus status, string minecraftVersion, string quiltVersion)
        {
            InstallTask installTask = task;
            QuiltInstallerStatus installStatus = status;
            installStatus.State = SpigotBuildToolsStatus.ToolState.Build;
            JavaRuntimeEnvironment environment = RuntimeEnvironment.JavaDefault;
            ProcessStartInfo startInfo = new ProcessStartInfo
            {
                FileName = environment.JavaPath,
                Arguments = string.Format("-Xms512M -Dsun.stdout.encoding=UTF8 -Dsun.stderr.encoding=UTF8 -jar \"{0}\" install server {1} {2} --install-dir=\\\"{3}\\\" --download-server", buildToolFileInfo.FullName, minecraftVersion, quiltVersion, installTask.Owner.ServerDirectory),
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
                installTask.OnInstallFinished();
                installTask.ChangePercentage(100);
                innerProcess.Dispose();
            };
        }
    }
}
