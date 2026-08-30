using System;
using System.Collections.Generic;
using System.Reflection;
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
        private static readonly Stack<SelectionEntry> backHistory = new Stack<SelectionEntry>();
        private static readonly Stack<SelectionEntry> forwardHistory = new Stack<SelectionEntry>();
        private static SelectionEntry currentSelection;
        private static bool navigating;
        internal static event Action<string> DirectorySelectionRequested;

        /// <summary>注册 Unity 编辑器选择和 Project 条目 GUI 事件。</summary>
        static ProjectNavigationHistory() { Selection.selectionChanged += RecordSelection; SubscribeGlobalEventHandler(); RecordSelection(); }

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
            RecordEntry(GetSelectedProjectSelection());
        }

        /// <summary>记录配置中心目录节点选择；目录选择不改变 Unity 全局资产选择。</summary>
        internal static void RecordDirectorySelection(string path)
        {
            if (navigating) return;
            RecordEntry(SelectionEntry.ForDirectory(path));
        }

        private static void RecordEntry(SelectionEntry selected)
        {
            if (selected == null || selected.Equals(currentSelection)) return;
            if (currentSelection != null) backHistory.Push(currentSelection);
            currentSelection = selected;
            forwardHistory.Clear();
        }

        /// <summary>全局捕获第四、第五鼠标键，焦点位于任意编辑器窗口时均可导航。</summary>
        private static void HandleGlobalEvent()
        {
            UnityEngine.Event currentEvent = UnityEngine.Event.current;
            if (currentEvent == null || currentEvent.type != UnityEngine.EventType.MouseDown) return;
            if (currentEvent.button == BackMouseButton) { NavigateBack(); currentEvent.Use(); }
            else if (currentEvent.button == ForwardMouseButton) { NavigateForward(); currentEvent.Use(); }
        }

        private static void SubscribeGlobalEventHandler()
        {
            FieldInfo field = typeof(EditorApplication).GetField("globalEventHandler", BindingFlags.Static | BindingFlags.NonPublic);
            if (field == null || field.FieldType != typeof(EditorApplication.CallbackFunction)) return;
            EditorApplication.CallbackFunction callback = field.GetValue(null) as EditorApplication.CallbackFunction;
            callback -= HandleGlobalEvent;
            callback += HandleGlobalEvent;
            field.SetValue(null, callback);
        }

        /// <summary>回退到上一条资产路径，并把当前路径压入前进历史。</summary>
        private static void NavigateBack()
        {
            if (backHistory.Count == 0) return;
            SelectionEntry target = backHistory.Pop();
            if (currentSelection != null) forwardHistory.Push(currentSelection);
            SelectEntry(target);
        }

        /// <summary>前进到下一条资产路径，并把当前路径压回后退历史。</summary>
        private static void NavigateForward()
        {
            if (forwardHistory.Count == 0) return;
            SelectionEntry target = forwardHistory.Pop();
            if (currentSelection != null) backHistory.Push(currentSelection);
            SelectEntry(target);
        }

        /// <summary>选择资产或文件夹路径；回放导航期间禁止 selectionChanged 再次创建历史记录。</summary>
        private static void SelectEntry(SelectionEntry entry)
        {
            if (entry.IsDirectory)
            {
                navigating = true;
                currentSelection = entry;
                DirectorySelectionRequested?.Invoke(entry.Path);
                navigating = false;
                return;
            }
            UnityEngine.Object target = entry.Resolve();
            if (target == null) return;
            navigating = true;
            currentSelection = entry;
            Selection.activeObject = target;
            EditorGUIUtility.PingObject(target);
            navigating = false;
        }

        /// <summary>读取当前选择对应的项目路径，只记录 Assets 下的文件或文件夹。</summary>
        private static SelectionEntry GetSelectedProjectSelection()
        {
            UnityEngine.Object activeObject = Selection.activeObject;
            if (activeObject == null) return null;
            string path = AssetDatabase.GetAssetPath(activeObject).Replace('\\', '/');
            if (!(path.StartsWith("Assets/", StringComparison.OrdinalIgnoreCase) || string.Equals(path, "Assets", StringComparison.OrdinalIgnoreCase))) return null;
            GlobalObjectId globalId = GlobalObjectId.GetGlobalObjectIdSlow(activeObject);
            return new SelectionEntry(path, globalId.ToString());
        }

        private sealed class SelectionEntry
        {
            private readonly string path;
            private readonly string globalId;
            public bool IsDirectory { get; }
            public string Path { get { return path; } }

            public SelectionEntry(string path, string globalId, bool isDirectory = false) { this.path = path; this.globalId = globalId; IsDirectory = isDirectory; }
            public static SelectionEntry ForDirectory(string path) { return new SelectionEntry(path ?? string.Empty, null, true); }

            public UnityEngine.Object Resolve()
            {
                if (!string.IsNullOrEmpty(globalId) && GlobalObjectId.TryParse(globalId, out GlobalObjectId id))
                {
                    UnityEngine.Object resolved = GlobalObjectId.GlobalObjectIdentifierToObjectSlow(id);
                    if (resolved != null) return resolved;
                }
                return AssetDatabase.LoadMainAssetAtPath(path);
            }

            public override bool Equals(object obj)
            {
                SelectionEntry other = obj as SelectionEntry;
                return other != null && IsDirectory == other.IsDirectory && string.Equals(globalId, other.globalId, StringComparison.Ordinal) && string.Equals(path, other.path, StringComparison.Ordinal);
            }

            public override int GetHashCode() { return (path ?? string.Empty).GetHashCode() ^ (globalId ?? string.Empty).GetHashCode(); }
        }
    }
}
