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
    /// 操作 Spigot 官方的建置工具 (BuildTools) 的類別，此類別無法建立實體
    /// </summary>
    public sealed class SpigotBuildTools
    {
        private delegate void UpdateProgressChangedEventHandler(int progress);

        private const string manifestListURL = "https://hub.spigotmc.org/jenkins/job/BuildTools/api/xml";
        private const string downloadURL = "https://hub.spigotmc.org/jenkins/job/BuildTools/lastSuccessfulBuild/artifact/target/BuildTools.jar";
        private static readonly DirectoryInfo workingDirectoryInfo = new DirectoryInfo(Path.Combine(Environment.CurrentDirectory, WTServer.SpigotBuildToolsPath));
        private static readonly FileInfo buildToolFileInfo = new FileInfo(Path.Combine(workingDirectoryInfo.FullName + "./BuildTools.jar"));
        private static readonly FileInfo buildToolVersionInfo = new FileInfo(Path.Combine(workingDirectoryInfo.FullName + "./BuildTools.version"));

        private static readonly SpigotBuildTools _instance = new SpigotBuildTools();

        private event EventHandler? UpdateStarted;
        private event UpdateProgressChangedEventHandler? UpdateProgressChanged;
        private event EventHandler? UpdateFinished;

        /// <summary>
        /// <see cref="SpigotBuildTools"/> 的唯一實例
        /// </summary>
        public static SpigotBuildTools Instance => _instance;

        /// <summary>
        /// Spigot 建置工具的建置目標
        /// </summary>
        public enum BuildTarget
        {
            /// <summary>
            /// CraftBukkit
            /// </summary>
            CraftBukkit,
            /// <summary>
            /// Spigot
            /// </summary>
            Spigot
        }

        private static bool CheckUpdate(out int updatedVersion)
        {
            int version = -1;
            int nowVersion;
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
                        } while (!int.TryParse(versionText, out version));
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
            string? versionString = manifestXML.SelectSingleNode("//mavenModuleSet/lastSuccessfulBuild/number")?.InnerText;
            if (versionString is null || versionString.Length <= 0)
            {
                updatedVersion = 0;
                return false;
            }
            nowVersion = int.Parse(versionString);
            if (version < nowVersion)
            {
                version = -1;
            }
            updatedVersion = nowVersion;
            return version <= 0;
        }

        private void Update(InstallTask installTask, int version)
        {
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
                    writer.WriteLine(version.ToString());
                    writer.Flush();
                    writer.Close();
                }
                installTask.StopRequested -= StopRequestedHandler;
                UpdateFinished?.Invoke(this, EventArgs.Empty);
            };
            client.DefaultRequestHeaders.Add("User-Agent", Constants.UserAgent);
            client.DownloadFileAsync(new Uri(downloadURL), buildToolFileInfo.FullName);
        }

        /// <summary>
        /// 用指定的建置目標和 Minecraft 版本來建置伺服器軟體
        /// </summary>
        /// <param name="task">要紀錄建置過程的工作物件</param>
        /// <param name="target">要建置的目標伺服器軟體</param>
        /// <param name="version">要建置的 Minecraft 版本</param>
        public void Install(InstallTask task, BuildTarget target, string version)
        {
            InstallTask installTask = task;
            SpigotBuildToolsStatus status = new SpigotBuildToolsStatus(SpigotBuildToolsStatus.ToolState.Initialize, 0);
            installTask.ChangeStatus(status);
            bool isStop = false;
            void StopRequestedHandler(object? sender, EventArgs e)
            {
                isStop = true;
                installTask.StopRequested -= StopRequestedHandler;
            };
            installTask.StopRequested += StopRequestedHandler;
            bool hasUpdate = CheckUpdate(out int newVersion);
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
                    DoInstall(installTask, status, target, version);
                };
                Update(installTask, newVersion);
            }
            else
            {
                installTask.ChangePercentage(50);
                installTask.OnStatusChanged();
                DoInstall(installTask, status, target, version);
            }
        }

        private void DoInstall(InstallTask task, SpigotBuildToolsStatus status, BuildTarget target, string version)
        {
            InstallTask installTask = task;
            SpigotBuildToolsStatus installStatus = status;
            installStatus.State = SpigotBuildToolsStatus.ToolState.Build;
            JavaRuntimeEnvironment environment = RuntimeEnvironment.JavaDefault;
            ProcessStartInfo startInfo = new ProcessStartInfo
            {
                FileName = environment.JavaPath,
                Arguments = string.Format("-Xms512M -Dsun.stdout.encoding=UTF8 -Dsun.stderr.encoding=UTF8 -jar \"{0}\" --rev {1} --compile {2} --output-dir \"{3}\"", buildToolFileInfo.FullName, version, target.ToString().ToLower(), installTask.Owner.ServerDirectory),
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
            void StopRequestedHandler(object? sender, EventArgs e)
            {
                try
                {
                    innerProcess.Kill();
                }
                catch (Exception)
                {
                }
                installTask.StopRequested -= StopRequestedHandler;
            };
            installTask.StopRequested += StopRequestedHandler;
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
                installTask.StopRequested -= StopRequestedHandler;
                installTask.OnInstallFinished();
                installTask.ChangePercentage(100);
                innerProcess.Dispose();
            };
        }
    }
}
