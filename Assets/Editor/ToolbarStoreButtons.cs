using System.Reflection;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

/// <summary>
/// Adds Package Manager and Asset Store shortcuts to the left side of Unity's
/// main toolbar.
/// </summary>
[InitializeOnLoad]
public static class ToolbarStoreButtons
{
    const string ContainerName = "Prometheus.Toolbar.StoreButtons";
    const string PackageManagerMenuPath = "Window/Package Manager";
    const string AssetStoreMenuPath = "Window/Asset Store";

    static bool buttonsInstalled;

    static ToolbarStoreButtons()
    {
        // The main toolbar may not exist immediately after a domain reload.
        EditorApplication.update += TryInstallButtons;
    }

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

    static void OpenEditorWindow(string menuPath)
    {
        if (!EditorApplication.ExecuteMenuItem(menuPath))
            Debug.LogWarning($"[ToolbarStoreButtons] 无法执行菜单：{menuPath}");
    }

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
}
