using System.Collections.Generic;
using UnityEditor;
using UnityEditor.Toolbars;
using UnityEngine;

/// <summary>
/// Adds supported Package Manager and Asset Store shortcuts to the left side of Unity's main toolbar.
/// </summary>
public static class ToolbarStoreButtons
{
    private const string ToolbarElementPath = "Prometheus/Store Shortcuts";
    private const string PackageManagerMenuPath = "Window/Package Manager";
    private const string AssetStoreMenuPath = "Window/Asset Store";

    /// <summary>
    /// Registers both shortcuts as one reorderable toolbar group through Unity's supported Unity 6.3 API.
    /// </summary>
    [MainToolbarElement(ToolbarElementPath, defaultDockPosition = MainToolbarDockPosition.Left)]
    private static IEnumerable<MainToolbarElement> CreateStoreButtons()
    {
        yield return CreateToolbarButton("Package Manager", "PM", "打开 Package Manager", PackageManagerMenuPath);
        yield return CreateToolbarButton("Asset Store", "AS", "打开 Asset Store", AssetStoreMenuPath);
    }

    /// <summary>
    /// Creates an icon button and falls back to a compact text label if the requested built-in icon is unavailable.
    /// </summary>
    private static MainToolbarElement CreateToolbarButton(string iconName, string fallbackText, string tooltip, string menuPath)
    {
        var icon = EditorGUIUtility.IconContent(iconName).image as Texture2D;
        var content = icon != null ? new MainToolbarContent(icon, tooltip) : new MainToolbarContent(fallbackText, tooltip);
        return new MainToolbarButton(content, () => OpenEditorWindow(menuPath));
    }

    /// <summary>
    /// Opens the requested Unity window and reports a useful warning only when Unity no longer exposes that menu command.
    /// </summary>
    private static void OpenEditorWindow(string menuPath)
    {
        if (!EditorApplication.ExecuteMenuItem(menuPath))
        {
            Debug.LogWarning($"[ToolbarStoreButtons] 无法执行菜单：{menuPath}");
        }
    }
}
