using System;
using System.Reflection;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Adds quick GameObject and component enable controls to the Hierarchy window.
/// </summary>
[InitializeOnLoad]
internal static class HierarchyQuickControls
{
    private const float ToggleWidth = 18f;
    private const float IconSize = 16f;
    private const float IconSpacing = 2f;
    private const float RightMargin = 2f;
    // Keeps the controls clear of Unity's prefab "Open" button and Spine's built-in
    // Hierarchy icon, which uses a slightly different right-edge coordinate.
    private const float RightSideBuiltInIconReserve = 10f;
    private const float DisabledIconBrightness = 0.38f;
    private const double HoverRepaintGracePeriod = 0.15d;

    private static readonly GUIContent ActiveToggleContent = new GUIContent("", "激活 / 禁用对象");
    private static double lastIconHoverTime;
    private static GUIStyle roundedTooltipStyle;

    static HierarchyQuickControls()
    {
        EditorApplication.hierarchyWindowItemOnGUI += DrawHierarchyControls;
        EditorApplication.update += RepaintWhileHoveringIcon;
    }

    private static void DrawHierarchyControls(int instanceId, Rect rowRect)
    {
        var gameObject = EditorUtility.InstanceIDToObject(instanceId) as GameObject;
        if (gameObject == null || Event.current.type == EventType.Layout)
        {
            return;
        }

        // The checkbox stays at the far right; component icons are placed immediately to its left.
        // Some Unity versions only pass the label area as rowRect. currentViewWidth keeps
        // the controls anchored to the real right edge of the Hierarchy window in both cases.
        var rightEdge = Mathf.Max(rowRect.xMax, EditorGUIUtility.currentViewWidth - RightMargin) - RightSideBuiltInIconReserve;
        var toggleRect = new Rect(rightEdge - ToggleWidth, rowRect.y + 1f, ToggleWidth, rowRect.height - 2f);
        EditorGUI.BeginChangeCheck();
        var active = GUI.Toggle(toggleRect, gameObject.activeSelf, ActiveToggleContent);
        if (EditorGUI.EndChangeCheck())
        {
            Undo.RecordObject(gameObject, active ? "Activate GameObject" : "Deactivate GameObject");
            gameObject.SetActive(active);
            EditorUtility.SetDirty(gameObject);
            EditorApplication.RepaintHierarchyWindow();
        }

        var components = gameObject.GetComponents<Component>();
        var iconRight = toggleRect.xMin - IconSpacing;

        // Draw in reverse so the Inspector component order still reads left-to-right.
        for (var i = components.Length - 1; i >= 1; i--)
        {
            var component = components[i];
            if (component == null)
            {
                continue;
            }

            var iconRect = new Rect(iconRight - IconSize, rowRect.y + (rowRect.height - IconSize) * 0.5f, IconSize, IconSize);
            if (iconRect.xMin < rowRect.xMin)
            {
                break;
            }

            DrawComponentIcon(component, iconRect);
            iconRight = iconRect.xMin - IconSpacing;
        }
    }

    private static void DrawComponentIcon(Component component, Rect iconRect)
    {
        var componentType = component.GetType();
        var tooltip = componentType.Name;
        Texture icon = EditorGUIUtility.GetIconForObject(component);
        if (icon == null)
        {
            icon = EditorGUIUtility.ObjectContent(null, componentType).image;
        }

        // Every component should still have a visible fallback, including custom scripts.
        if (icon == null)
        {
            icon = EditorGUIUtility.IconContent("cs Script Icon").image;
        }
        var enabledProperty = GetEnabledProperty(componentType);
        var canToggle = enabledProperty != null;
        var isEnabled = !canToggle || (bool)enabledProperty.GetValue(component, null);

        var previousColor = GUI.color;
        if (!isEnabled)
        {
            GUI.color = new Color(DisabledIconBrightness, DisabledIconBrightness, DisabledIconBrightness, previousColor.a);
        }

        // Do not attach tooltip to the GUIContent: Unity would show its delayed built-in
        // tooltip in addition to the immediate one drawn below.
        GUI.Label(iconRect, new GUIContent(icon));
        var clicked = GUI.Button(iconRect, GUIContent.none, GUIStyle.none);
        GUI.color = previousColor;

        if (iconRect.Contains(Event.current.mousePosition))
        {
            lastIconHoverTime = EditorApplication.timeSinceStartup;
            DrawImmediateTooltip(tooltip);
        }

        if (!clicked || !canToggle)
        {
            return;
        }

        Undo.RecordObject(component, isEnabled ? "Disable Component" : "Enable Component");
        enabledProperty.SetValue(component, !isEnabled, null);
        EditorUtility.SetDirty(component);
        EditorApplication.RepaintHierarchyWindow();
    }

    private static PropertyInfo GetEnabledProperty(Type componentType)
    {
        var property = componentType.GetProperty("enabled", BindingFlags.Instance | BindingFlags.Public);
        return property != null && property.PropertyType == typeof(bool) && property.CanRead && property.CanWrite
            ? property
            : null;
    }

    private static void DrawImmediateTooltip(string tooltip)
    {
        if (Event.current.type != EventType.Repaint)
        {
            return;
        }

        var content = new GUIContent(tooltip);
        var textSize = EditorStyles.whiteLabel.CalcSize(content);
        var size = new Vector2(textSize.x + 12f, Mathf.Max(textSize.y + 4f, 20f));

        var mousePosition = Event.current.mousePosition;
        // Hierarchy rows render from top to bottom. Showing the label above the icon keeps
        // later rows from painting over it.
        var tooltipRect = new Rect(mousePosition.x + 14f, mousePosition.y - size.y - 6f, size.x, size.y);
        tooltipRect.x = Mathf.Min(tooltipRect.x, EditorGUIUtility.currentViewWidth - tooltipRect.width - RightMargin);
        tooltipRect.y = Mathf.Max(0f, tooltipRect.y);
        var previousDepth = GUI.depth;
        GUI.depth = -100;
        GUI.Box(tooltipRect, content, RoundedTooltipStyle);
        GUI.depth = previousDepth;
    }

    private static void RepaintWhileHoveringIcon()
    {
        var mouseOverWindow = EditorWindow.mouseOverWindow;
        var mouseIsOverHierarchy = mouseOverWindow != null && mouseOverWindow.GetType().Name == "SceneHierarchyWindow";
        if (mouseIsOverHierarchy || EditorApplication.timeSinceStartup - lastIconHoverTime < HoverRepaintGracePeriod)
        {
            EditorApplication.RepaintHierarchyWindow();
        }
    }

    private static GUIStyle RoundedTooltipStyle
    {
        get
        {
            if (roundedTooltipStyle != null)
            {
                return roundedTooltipStyle;
            }

            var background = CreateRoundedBackground(16, 5f, new Color(0.08f, 0.08f, 0.08f, 1f));
            roundedTooltipStyle = new GUIStyle(EditorStyles.whiteLabel)
            {
                alignment = TextAnchor.MiddleCenter,
                padding = new RectOffset(6, 6, 2, 2),
                border = new RectOffset(5, 5, 5, 5),
                normal = { background = background }
            };
            return roundedTooltipStyle;
        }
    }

    private static Texture2D CreateRoundedBackground(int size, float radius, Color color)
    {
        var texture = new Texture2D(size, size, TextureFormat.RGBA32, false)
        {
            hideFlags = HideFlags.HideAndDontSave,
            filterMode = FilterMode.Bilinear
        };

        for (var y = 0; y < size; y++)
        {
            for (var x = 0; x < size; x++)
            {
                var distanceX = Mathf.Max(radius - x - 0.5f, x - (size - radius - 0.5f), 0f);
                var distanceY = Mathf.Max(radius - y - 0.5f, y - (size - radius - 0.5f), 0f);
                var edgeAlpha = Mathf.Clamp01(radius - Mathf.Sqrt(distanceX * distanceX + distanceY * distanceY) + 0.5f);
                texture.SetPixel(x, y, new Color(color.r, color.g, color.b, color.a * edgeAlpha));
            }
        }

        texture.Apply();
        return texture;
    }
}
