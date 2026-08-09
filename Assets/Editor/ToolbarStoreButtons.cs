using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
#if UNITY_6000_3_OR_NEWER
using UnityEditor.Toolbars;
#else
using System.Reflection;
using UnityEngine.UIElements;
#endif

/// <summary>
/// Adds Package Manager and Asset Store shortcuts to the left side of Unity's main toolbar.
/// </summary>
#if !UNITY_6000_3_OR_NEWER
[InitializeOnLoad]
#endif
public static class ToolbarStoreButtons
{
    const string PackageManagerMenuPath = "Window/Package Manager";
    const string AssetStoreMenuPath = "Window/Asset Store";
#if UNITY_6000_3_OR_NEWER
    const string ToolbarElementPath = "Prometheus/Store Shortcuts";
#else
    const string ContainerName = "Prometheus.Toolbar.StoreButtons";

    static bool buttonsInstalled;

    static ToolbarStoreButtons()
    {
        // The main toolbar may not exist immediately after a domain reload.
        EditorApplication.update += TryInstallButtons;
    }
#endif

#if UNITY_6000_3_OR_NEWER
    /// <summary>
    /// Registers both shortcuts as one left-docked group through Unity 6.3's supported main-toolbar API.
    /// </summary>
    /// <returns>The ordered Package Manager and Asset Store button descriptors.</returns>
    [MainToolbarElement(ToolbarElementPath, defaultDockPosition = MainToolbarDockPosition.Left, defaultDockIndex = 0)]
    public static IEnumerable<MainToolbarElement> CreateStoreToolbarButtons()
    {
        yield return CreateMainToolbarButton("Package Manager", "Package Manager", "打开 Package Manager", PackageManagerMenuPath);
        yield return CreateMainToolbarButton("Asset Store", "Asset Store", "打开 Asset Store", AssetStoreMenuPath);
    }

    /// <summary>
    /// Creates a supported main-toolbar button and falls back to text when the requested editor icon is unavailable.
    /// </summary>
    static MainToolbarButton CreateMainToolbarButton(string iconName, string fallbackText, string tooltip, string menuPath)
    {
        var icon = EditorGUIUtility.IconContent(iconName).image as Texture2D;
        var content = icon != null ? new MainToolbarContent(icon, tooltip) : new MainToolbarContent(fallbackText, tooltip);
        return new MainToolbarButton(content, () => OpenEditorWindow(menuPath));
    }
#else
    /// <summary>
    /// Installs the shortcut group into the legacy toolbar visual tree after the left alignment zone becomes available.
    /// </summary>
    static void TryInstallButtons()
    {
        if (buttonsInstalled)
        {
            EditorApplication.update -= TryInstallButtons;
            return;
        }

        var toolbarType = typeof(Editor).Assembly.GetType("UnityEditor.Toolbar");
        if (toolbarType == null)
            return;

        var toolbars = Resources.FindObjectsOfTypeAll(toolbarType);
        if (toolbars.Length == 0)
            return;

        var toolbarRoot = GetToolbarRoot(toolbars[0]);
        var leftZone = toolbarRoot?.Q("ToolbarZoneLeftAlign");
        if (leftZone == null)
            return;

        // Unity can preserve the visual tree across a domain reload. Remove the
        // previous container so it is also migrated from the old play-mode position.
        toolbarRoot.Q(ContainerName)?.RemoveFromHierarchy();

        var buttonContainer = new VisualElement
        {
            name = ContainerName,
            style =
            {
                flexDirection = FlexDirection.Row,
                alignItems = Align.Center,
                marginLeft = 4f
            }
        };

        buttonContainer.Add(CreateToolbarButton(
            "Package Manager",
            "打开 Package Manager",
            () => OpenEditorWindow(PackageManagerMenuPath)));

        buttonContainer.Add(CreateToolbarButton(
            "Asset Store",
            "打开 Asset Store",
            () => OpenEditorWindow(AssetStoreMenuPath)));

        // ToolbarZoneLeftAlign grows from the far-left edge. Appending keeps the
        // shortcuts beside Unity's existing account/cloud/tool controls.
        leftZone.Add(buttonContainer);

        buttonsInstalled = true;
        EditorApplication.update -= TryInstallButtons;
    }

    static Button CreateToolbarButton(string iconName, string tooltip, System.Action onClick)
    {
        var button = new Button(onClick)
        {
            tooltip = tooltip
        };

        button.AddToClassList("unity-toolbar-button");
        button.style.width = 32f;
        button.style.height = 20f;
        button.style.paddingLeft = 7f;
        button.style.paddingRight = 7f;
        button.style.paddingTop = 2f;
        button.style.paddingBottom = 2f;

        var icon = new Image
        {
            image = EditorGUIUtility.IconContent(iconName).image,
            scaleMode = ScaleMode.ScaleToFit,
            pickingMode = PickingMode.Ignore
        };

        icon.style.width = 16f;
        icon.style.height = 16f;
        button.Add(icon);
        return button;
    }
#endif

    /// <summary>
    /// Opens an editor window through its menu command and reports a warning only when Unity rejects that command.
    /// </summary>
    static void OpenEditorWindow(string menuPath)
    {
        if (!EditorApplication.ExecuteMenuItem(menuPath))
            Debug.LogWarning($"[ToolbarStoreButtons] 无法执行菜单：{menuPath}");
    }

#if !UNITY_6000_3_OR_NEWER
    /// <summary>
    /// Retrieves the root visual element from legacy toolbar implementations that expose it through an internal property or field.
    /// </summary>
    static VisualElement GetToolbarRoot(object toolbar)
    {
        // Unity 2021 exposes the top toolbar only through internal members, so
        // walk base types to support both the property and field implementations.
        for (var type = toolbar.GetType(); type != null; type = type.BaseType)
        {
            var property = type.GetProperty(
                "rootVisualElement",
                BindingFlags.Public | BindingFlags.NonPublic |
                BindingFlags.Instance | BindingFlags.DeclaredOnly);

            if (property?.GetValue(toolbar) is VisualElement propertyRoot)
                return propertyRoot;

            var field = type.GetField(
                "m_Root",
                BindingFlags.Public | BindingFlags.NonPublic |
                BindingFlags.Instance | BindingFlags.DeclaredOnly);

            if (field?.GetValue(toolbar) is VisualElement fieldRoot)
                return fieldRoot;
        }

        return null;
    }
#endif
}
