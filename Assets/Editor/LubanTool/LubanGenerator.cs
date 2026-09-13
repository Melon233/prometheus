using System.Diagnostics;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace Xuan.Prometheus.EditorTools
{
    /// <summary>把 Data/Excel 下的配表导出为 Assets/Gen/Config 的 C# 代码与 Assets/BundleResources/Table 的二进制数据。</summary>
    public static class LubanGenerator
    {
        /// <summary>导表脚本相对工程根目录的位置；工具与运行时版本由脚本内部固定，编辑器不重复声明。</summary>
        private const string GenScriptRelativePath = "Tools/Luban/gen.ps1";

        /// <summary>执行导表并在完成后刷新资源数据库，使新生成的代码与数据立刻参与编译和打包。</summary>
        [MenuItem("Prometheus/Luban/导出配表", false, 100)]
        public static void Generate()
        {
            string projectRoot = Directory.GetParent(Application.dataPath).FullName;
            string scriptPath = Path.Combine(projectRoot, GenScriptRelativePath);
            if (!File.Exists(scriptPath)) { UnityEngine.Debug.LogError($"[Luban] 找不到导表脚本：{scriptPath}"); return; }

            ProcessStartInfo startInfo = new ProcessStartInfo
            {
                // 用 pwsh 优先、powershell 兜底会引入分支；统一走 powershell.exe，它在全部 Windows 开发机上存在。
                FileName = "powershell.exe",
                Arguments = $"-NoProfile -ExecutionPolicy Bypass -File \"{scriptPath}\"",
                WorkingDirectory = projectRoot,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };

            using (Process process = Process.Start(startInfo))
            {
                string stdout = process.StandardOutput.ReadToEnd();
                string stderr = process.StandardError.ReadToEnd();
                process.WaitForExit();
                if (process.ExitCode == 0)
                {
                    UnityEngine.Debug.Log($"[Luban] 导表成功\n{stdout}");
                    AssetDatabase.Refresh(ImportAssetOptions.ForceUpdate);
                }
                else
                {
                    // 导表失败时不刷新资源：让工程停在上一份可用数据上，避免半份数据进入打包。
                    UnityEngine.Debug.LogError($"[Luban] 导表失败（错误码 {process.ExitCode}）\n{stdout}\n{stderr}");
                }
            }
        }

        /// <summary>在资源管理器中打开配表目录，方便策划直接编辑 Excel。</summary>
        [MenuItem("Prometheus/Luban/打开配表目录", false, 101)]
        public static void OpenExcelFolder()
        {
            string projectRoot = Directory.GetParent(Application.dataPath).FullName;
            EditorUtility.RevealInFinder(Path.Combine(projectRoot, "Data/Excel/Datas") + Path.DirectorySeparatorChar);
        }
    }
}
