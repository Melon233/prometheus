using System;
using System.Diagnostics;
using System.IO;
using UnityEditor;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace Xuan.Prometheus.EditorTools
{
    /// <summary>
    /// 提供一个仅在 Unity Editor 中可用的 BAT 脚本启动窗口，并把每个项目的标题与脚本路径持久化到当前用户的 EditorPrefs。
    /// </summary>
    public sealed class BatScriptLauncherWindow : EditorWindow
    {
        /// <summary>定义没有配置自定义标题时显示的默认按钮标题。</summary>
        private const string DefaultButtonTitle = "执行 BAT 脚本";

        /// <summary>定义用于隔离不同项目按钮标题配置的 EditorPrefs 键前缀。</summary>
        private const string ButtonTitlePreferencePrefix = "Xuan.Prometheus.BatScriptLauncher.ButtonTitle.";

        /// <summary>定义用于隔离不同项目脚本路径配置的 EditorPrefs 键前缀。</summary>
        private const string ScriptPathPreferencePrefix = "Xuan.Prometheus.BatScriptLauncher.ScriptPath.";

        /// <summary>保存当前项目配置的按钮标题。</summary>
        private string buttonTitle = DefaultButtonTitle;

        /// <summary>保存当前项目配置的 BAT 路径；项目内部路径使用相对于项目根目录的形式持久化。</summary>
        private string storedScriptPath = string.Empty;

        /// <summary>
        /// 从 Unity 菜单打开 BAT 脚本启动器，并设置适合配置路径的最小窗口尺寸。
        /// </summary>
        [MenuItem("Tools/Prometheus/BAT Script Launcher")]
        private static void OpenWindow()
        {
            BatScriptLauncherWindow window = GetWindow<BatScriptLauncherWindow>();
            window.minSize = new Vector2(520f, 205f);
            window.UpdateWindowTitle();
            window.Show();
        }

        /// <summary>
        /// 在窗口创建或脚本重载后恢复当前项目的本地配置。
        /// </summary>
        private void OnEnable()
        {
            buttonTitle = EditorPrefs.GetString(GetButtonTitlePreferenceKey(), DefaultButtonTitle);
            storedScriptPath = EditorPrefs.GetString(GetScriptPathPreferenceKey(), string.Empty);
            UpdateWindowTitle();
        }

        /// <summary>
        /// 绘制标题、路径选择器、路径诊断信息与执行按钮。
        /// </summary>
        private void OnGUI()
        {
            EditorGUILayout.Space(8f);
            EditorGUILayout.LabelField("BAT 脚本启动器", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox("标题与脚本路径保存在当前用户的 EditorPrefs 中，不会写入项目资产或提交到版本库。项目内脚本会保存为相对路径，项目外脚本会保存为绝对路径。", MessageType.Info);
            EditorGUILayout.Space(4f);

            EditorGUI.BeginChangeCheck();
            buttonTitle = EditorGUILayout.TextField("按钮标题", buttonTitle);
            DrawScriptPathField();
            if (EditorGUI.EndChangeCheck())
            {
                SavePreferences();
                UpdateWindowTitle();
            }

            string resolvedScriptPath = ResolveStoredScriptPath();
            bool canExecute = TryValidateScript(resolvedScriptPath, out string validationMessage);
            EditorGUILayout.Space(4f);
            EditorGUILayout.HelpBox(validationMessage, canExecute ? MessageType.None : MessageType.Warning);
            EditorGUILayout.Space(4f);

            using (new EditorGUI.DisabledScope(!canExecute))
            {
                string visibleButtonTitle = string.IsNullOrWhiteSpace(buttonTitle) ? DefaultButtonTitle : buttonTitle.Trim();
                if (GUILayout.Button(visibleButtonTitle, GUILayout.Height(34f))) ExecuteScript(resolvedScriptPath);
            }
        }

        /// <summary>
        /// 绘制可手动编辑的脚本路径和系统文件选择按钮。
        /// </summary>
        private void DrawScriptPathField()
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                storedScriptPath = EditorGUILayout.TextField("BAT 脚本路径", storedScriptPath);
                if (GUILayout.Button("浏览...", GUILayout.Width(76f))) SelectScriptWithFilePanel();
            }
        }

        /// <summary>
        /// 打开文件选择窗口并把选中的 BAT 路径转换为适合持久化的项目相对路径或绝对路径。
        /// </summary>
        private void SelectScriptWithFilePanel()
        {
            string selectedPath = EditorUtility.OpenFilePanel("选择 BAT 脚本", GetFilePanelInitialDirectory(), "bat");
            if (string.IsNullOrWhiteSpace(selectedPath)) return;
            storedScriptPath = ConvertToStoredPath(selectedPath);
            SavePreferences();
            GUI.FocusControl(null);
            Repaint();
        }

        /// <summary>
        /// 获取文件选择窗口的初始目录；已配置有效路径时使用脚本目录，否则使用项目根目录。
        /// </summary>
        private string GetFilePanelInitialDirectory()
        {
            string resolvedPath = ResolveStoredScriptPath();
            if (!string.IsNullOrWhiteSpace(resolvedPath))
            {
                string directory = Path.GetDirectoryName(resolvedPath);
                if (!string.IsNullOrWhiteSpace(directory) && Directory.Exists(directory)) return directory;
            }
            return GetProjectRootPath();
        }

        /// <summary>
        /// 验证运行平台、路径格式、扩展名和文件存在性，并生成直接显示给用户的诊断信息。
        /// </summary>
        private static bool TryValidateScript(string resolvedScriptPath, out string validationMessage)
        {
            if (Application.platform != RuntimePlatform.WindowsEditor)
            {
                validationMessage = "BAT 脚本只能在 Windows Editor 中执行。";
                return false;
            }
            if (string.IsNullOrWhiteSpace(resolvedScriptPath))
            {
                validationMessage = "请选择或输入一个 BAT 脚本路径。";
                return false;
            }
            if (!string.Equals(Path.GetExtension(resolvedScriptPath), ".bat", StringComparison.OrdinalIgnoreCase))
            {
                validationMessage = $"仅支持 .bat 文件：{resolvedScriptPath}";
                return false;
            }
            if (!File.Exists(resolvedScriptPath))
            {
                validationMessage = $"脚本文件不存在：{resolvedScriptPath}";
                return false;
            }
            validationMessage = $"将执行：{resolvedScriptPath}";
            return true;
        }

        /// <summary>
        /// 使用 Windows 命令解释器在脚本所在目录中执行 BAT，并保留正常可见的控制台窗口行为。
        /// </summary>
        private static void ExecuteScript(string resolvedScriptPath)
        {
            try
            {
                string commandInterpreter = Environment.GetEnvironmentVariable("ComSpec");
                if (string.IsNullOrWhiteSpace(commandInterpreter)) commandInterpreter = "cmd.exe";
                string workingDirectory = Path.GetDirectoryName(resolvedScriptPath);
                ProcessStartInfo startInfo = new ProcessStartInfo
                {
                    FileName = commandInterpreter,
                    Arguments = $"/d /s /c \"\"{resolvedScriptPath}\"\"",
                    WorkingDirectory = string.IsNullOrWhiteSpace(workingDirectory) ? GetProjectRootPath() : workingDirectory,
                    UseShellExecute = true,
                    WindowStyle = ProcessWindowStyle.Normal
                };
                Process process = Process.Start(startInfo);
                if (process == null) throw new InvalidOperationException("系统没有返回已启动的脚本进程。");
                Debug.Log($"[BAT Script Launcher] 已启动脚本：{resolvedScriptPath}");
            }
            catch (Exception exception)
            {
                Debug.LogError($"[BAT Script Launcher] 启动脚本失败：{resolvedScriptPath}\n{exception}");
                EditorUtility.DisplayDialog("BAT 脚本启动失败", exception.Message, "确定");
            }
        }

        /// <summary>
        /// 保存当前项目的按钮标题和脚本路径到当前用户的 EditorPrefs。
        /// </summary>
        private void SavePreferences()
        {
            EditorPrefs.SetString(GetButtonTitlePreferenceKey(), buttonTitle ?? string.Empty);
            EditorPrefs.SetString(GetScriptPathPreferenceKey(), storedScriptPath ?? string.Empty);
        }

        /// <summary>
        /// 使用自定义按钮标题同步更新 EditorWindow 标题，空标题时使用固定工具名称。
        /// </summary>
        private void UpdateWindowTitle()
        {
            string visibleTitle = string.IsNullOrWhiteSpace(buttonTitle) ? "BAT Launcher" : buttonTitle.Trim();
            titleContent = new GUIContent(visibleTitle);
        }

        /// <summary>
        /// 将持久化路径解析为标准绝对路径；无效路径不会抛出异常，而是返回空字符串等待界面显示诊断。
        /// </summary>
        private string ResolveStoredScriptPath()
        {
            if (string.IsNullOrWhiteSpace(storedScriptPath)) return string.Empty;
            try
            {
                string expandedPath = Environment.ExpandEnvironmentVariables(storedScriptPath.Trim());
                string absolutePath = Path.IsPathRooted(expandedPath) ? expandedPath : Path.Combine(GetProjectRootPath(), expandedPath);
                return Path.GetFullPath(absolutePath);
            }
            catch (Exception)
            {
                return string.Empty;
            }
        }

        /// <summary>
        /// 将项目目录内的脚本转换为相对路径，以便项目移动后仍能使用；项目外路径保持标准绝对路径。
        /// </summary>
        private static string ConvertToStoredPath(string selectedPath)
        {
            string absolutePath = Path.GetFullPath(selectedPath);
            string projectRootPath = GetProjectRootPath();
            string projectRootWithSeparator = projectRootPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
            if (!absolutePath.StartsWith(projectRootWithSeparator, StringComparison.OrdinalIgnoreCase)) return absolutePath.Replace(Path.DirectorySeparatorChar, '/');
            string relativePath = absolutePath.Substring(projectRootWithSeparator.Length);
            return relativePath.Replace(Path.DirectorySeparatorChar, '/');
        }

        /// <summary>
        /// 获取当前 Unity 项目的标准绝对根目录。
        /// </summary>
        private static string GetProjectRootPath()
        {
            return Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
        }

        /// <summary>
        /// 获取当前项目独占的按钮标题配置键，防止多个 Unity 项目共享同一份配置。
        /// </summary>
        private static string GetButtonTitlePreferenceKey()
        {
            return ButtonTitlePreferencePrefix + GetProjectRootPath();
        }

        /// <summary>
        /// 获取当前项目独占的脚本路径配置键，防止多个 Unity 项目共享同一份配置。
        /// </summary>
        private static string GetScriptPathPreferenceKey()
        {
            return ScriptPathPreferencePrefix + GetProjectRootPath();
        }
    }
}
