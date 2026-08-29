using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace Xuan.Prometheus.Editor
{
    /// <summary>为 Unity Project 窗口提供类似浏览器的资产和文件夹前进后退历史。</summary>
    [InitializeOnLoad]
    internal static class ProjectNavigationHistory
    {
        private const int BackMouseButton = 3;
        private const int ForwardMouseButton = 4;
        private static readonly Stack<string> backHistory = new Stack<string>();
        private static readonly Stack<string> forwardHistory = new Stack<string>();
        private static string currentPath;
        private static bool navigating;

        /// <summary>注册 Unity 编辑器选择和 Project 条目 GUI 事件。</summary>
        static ProjectNavigationHistory() { Selection.selectionChanged += RecordSelection; EditorApplication.projectWindowItemOnGUI += HandleProjectWindowItemGUI; RecordSelection(); }

        /// <summary>提供菜单形式的后退操作，便于没有侧键的设备使用。</summary>
        [MenuItem("Prometheus/Navigation/Back", false, 30)]
        private static void NavigateBackMenu() { NavigateBack(); }

        /// <summary>提供菜单形式的前进操作，便于没有侧键的设备使用。</summary>
        [MenuItem("Prometheus/Navigation/Forward", false, 31)]
        private static void NavigateForwardMenu() { NavigateForward(); }

        /// <summary>仅当后退历史非空时启用后退菜单。</summary>
        [MenuItem("Prometheus/Navigation/Back", true)]
        private static bool ValidateNavigateBackMenu() { return backHistory.Count > 0; }

        /// <summary>仅当前进历史非空时启用前进菜单。</summary>
        [MenuItem("Prometheus/Navigation/Forward", true)]
        private static bool ValidateNavigateForwardMenu() { return forwardHistory.Count > 0; }

        /// <summary>记录 Project 窗口中发生的资产或文件夹选择，并在新跳转后清空前进历史。</summary>
        private static void RecordSelection()
        {
            if (navigating) return;
            string selectedPath = GetSelectedProjectPath();
            if (string.IsNullOrEmpty(selectedPath) || string.Equals(selectedPath, currentPath, StringComparison.Ordinal)) return;
            if (!string.IsNullOrEmpty(currentPath)) backHistory.Push(currentPath);
            currentPath = selectedPath;
            forwardHistory.Clear();
        }

        /// <summary>从 Project 窗口条目回调捕获第四、第五鼠标键，并立即执行浏览器式导航。</summary>
        private static void HandleProjectWindowItemGUI(string guid, Rect selectionRect)
        {
            UnityEngine.Event currentEvent = UnityEngine.Event.current;
            if (currentEvent.type != UnityEngine.EventType.MouseDown || !selectionRect.Contains(currentEvent.mousePosition)) return;
            if (currentEvent.button == BackMouseButton) { NavigateBack(); currentEvent.Use(); }
            else if (currentEvent.button == ForwardMouseButton) { NavigateForward(); currentEvent.Use(); }
        }

        /// <summary>回退到上一条资产路径，并把当前路径压入前进历史。</summary>
        private static void NavigateBack()
        {
            if (backHistory.Count == 0) return;
            string targetPath = backHistory.Pop();
            if (!string.IsNullOrEmpty(currentPath)) forwardHistory.Push(currentPath);
            SelectPath(targetPath);
        }

        /// <summary>前进到下一条资产路径，并把当前路径压回后退历史。</summary>
        private static void NavigateForward()
        {
            if (forwardHistory.Count == 0) return;
            string targetPath = forwardHistory.Pop();
            if (!string.IsNullOrEmpty(currentPath)) backHistory.Push(currentPath);
            SelectPath(targetPath);
        }

        /// <summary>选择资产或文件夹路径；回放导航期间禁止 selectionChanged 再次创建历史记录。</summary>
        private static void SelectPath(string path)
        {
            UnityEngine.Object target = AssetDatabase.LoadMainAssetAtPath(path);
            if (target == null) { currentPath = null; return; }
            navigating = true;
            currentPath = path;
            Selection.activeObject = target;
            EditorGUIUtility.PingObject(target);
            navigating = false;
        }

        /// <summary>读取当前选择对应的项目路径，只记录 Assets 下的文件或文件夹。</summary>
        private static string GetSelectedProjectPath()
        {
            UnityEngine.Object activeObject = Selection.activeObject;
            if (activeObject == null) return null;
            string path = AssetDatabase.GetAssetPath(activeObject).Replace('\\', '/');
            return path.StartsWith("Assets/", StringComparison.OrdinalIgnoreCase) || string.Equals(path, "Assets", StringComparison.OrdinalIgnoreCase) ? path : null;
        }
    }
}
