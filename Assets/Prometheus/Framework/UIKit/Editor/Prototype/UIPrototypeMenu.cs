using System;
using UnityEditor;
using UnityEngine;

namespace Xuan.Prometheus.Editor.Prototype
{
    /// <summary>提供 UI 原型重建的编辑器菜单入口。</summary>
    public static class UIPrototypeMenu
    {
        /// <summary>重建工程内全部 UI 原型 Prefab，并把结构回读报告输出到 Console。</summary>
        [MenuItem("Prometheus/UIKit/Rebuild UI Prototypes", false, 120)]
        public static void RebuildAll()
        {
            try
            {
                string report = UIPrototypeBuilder.RebuildAll();
                Debug.Log(string.IsNullOrWhiteSpace(report) ? "[UIProto] No prototype definitions found." : "[UIProto] Rebuild finished.\n" + report);
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
                EditorUtility.DisplayDialog("UI Prototype", exception.Message, "OK");
            }
        }
    }
}
