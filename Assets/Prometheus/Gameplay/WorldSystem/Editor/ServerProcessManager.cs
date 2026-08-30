#if UNITY_EDITOR
using System;
using System.Diagnostics;
using System.IO;
using System.Net.Sockets;
using UnityEditor;

namespace Xuan.Prometheus.World.Editor
{
    /// <summary>
    /// 进入 Play 时自动构建并启动 Go 服务器，退出 Play 时关闭。
    /// 注意：proto 的拉取/编译是手动步骤（见 Server/gen_proto.ps1），本脚本只负责 go build + run，不生成协议代码。
    /// </summary>
        [InitializeOnLoad]
    public static class ServerProcessManager
    {
        /// <summary>服务器监听地址，需与 WorldSystem.ServerHost/ServerPort 及 Server/main.go 默认值一致。</summary>
        private const string ListenAddr = "127.0.0.1:9000";

        /// <summary>项目根目录下 Go 服务器的相对路径。</summary>
        private const string ServerDirName = "Server";

        /// <summary>构建产物相对项目根目录的路径。</summary>
        private const string ExeRelativePath = "Server/bin/server.exe";

        private static Process serverProcess;

        static ServerProcessManager()
        {
            EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
        }

        /// <summary>编辑器菜单：停止当前项目构建的 Go 服务器，即使服务器不是由本次 Unity 会话启动也可释放端口。</summary>
        [MenuItem("Prometheus/World/Stop POI Server")]
        private static void StopServerFromMenu()
        {
            StopServer();
            string expectedPath = Path.GetFullPath(Path.Combine(UnityEngine.Application.dataPath, "..", ExeRelativePath));
            foreach (Process process in Process.GetProcessesByName("server"))
            {
                try
                {
                    if (string.Equals(process.MainModule.FileName, expectedPath, StringComparison.OrdinalIgnoreCase))
                    {
                        process.Kill();
                        process.Dispose();
                        UnityEngine.Debug.Log($"[Server] 已停止项目 POI 服务器：{expectedPath}");
                    }
                    else
                    {
                        process.Dispose();
                    }
                }
                catch (Exception exception)
                {
                    process.Dispose();
                    UnityEngine.Debug.LogWarning($"[Server] 停止 POI 服务器失败：{exception.Message}");
                }
            }
        }

        private static void OnPlayModeStateChanged(PlayModeStateChange state)
        {
            if (state == PlayModeStateChange.ExitingEditMode) StartServer();
            else if (state == PlayModeStateChange.EnteredEditMode) StopServer();
        }

        /// <summary>构建并启动服务器；端口已被占用时视为已在运行而跳过。</summary>
        private static void StartServer()
        {
            string projectRoot = Path.GetFullPath(Path.Combine(UnityEngine.Application.dataPath, ".."));
            string serverDir = Path.Combine(projectRoot, ServerDirName);
            string exePath = Path.Combine(projectRoot, ExeRelativePath);
            string exportPath = Path.Combine(projectRoot, "Assets", "Resources", "Config", "PoiExport.json");

            if (IsPortInUse(ListenAddr))
            {
                UnityEngine.Debug.Log($"[Server] 端口 {ListenAddr} 已被占用，跳过启动（可能已在运行）。");
                return;
            }

            if (!BuildServer(serverDir, exePath)) return;

            ProcessStartInfo psi = new ProcessStartInfo
            {
                FileName = exePath,
                Arguments = $"-addr {ListenAddr} -export \"{exportPath}\"",
                WorkingDirectory = serverDir,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            };
            serverProcess = new Process { StartInfo = psi };
            serverProcess.OutputDataReceived += (_, e) => { if (!string.IsNullOrEmpty(e.Data)) UnityEngine.Debug.Log("[Server] " + e.Data); };
            serverProcess.ErrorDataReceived += (_, e) => { if (!string.IsNullOrEmpty(e.Data)) UnityEngine.Debug.LogWarning("[Server] " + e.Data); };
            serverProcess.Start();
            serverProcess.BeginOutputReadLine();
            serverProcess.BeginErrorReadLine();
            UnityEngine.Debug.Log($"[Server] Go 服务器已启动：{ListenAddr}");
        }

        /// <summary>在服务器目录执行 go build；失败时打印错误并返回 false。</summary>
        private static bool BuildServer(string serverDir, string exePath)
        {
            ProcessStartInfo psi = new ProcessStartInfo
            {
                FileName = "go",
                Arguments = $"build -o \"{exePath}\" .",
                WorkingDirectory = serverDir,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            };
            using (Process p = Process.Start(psi))
            {
                string stdout = p.StandardOutput.ReadToEnd();
                string stderr = p.StandardError.ReadToEnd();
                p.WaitForExit();
                if (p.ExitCode != 0)
                {
                    UnityEngine.Debug.LogError("[Server] go build 失败：\n" + (string.IsNullOrEmpty(stderr) ? stdout : stderr));
                    return false;
                }
            }
            return true;
        }

        /// <summary>退出 Play 时关闭服务器进程。</summary>
        private static void StopServer()
        {
            if (serverProcess != null && !serverProcess.HasExited)
            {
                serverProcess.Kill();
                serverProcess.Dispose();
                UnityEngine.Debug.Log("[Server] Go 服务器已关闭");
            }
            serverProcess = null;
        }

        /// <summary>判断端口是否已被监听（用于避免重复启动）。</summary>
        private static bool IsPortInUse(string addr)
        {
            string[] parts = addr.Split(':');
            int port = int.Parse(parts[parts.Length - 1]);
            try
            {
                using (TcpClient client = new TcpClient())
                {
                    client.Connect(parts[0], port);
                    return true;
                }
            }
            catch (SocketException)
            {
                return false;
            }
        }
    }
}
#endif
