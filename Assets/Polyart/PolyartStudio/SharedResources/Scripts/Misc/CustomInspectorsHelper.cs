#if UNITY_EDITOR

using UnityEngine;
using UnityEditor;

// This static class holds reusable GUIStyles and helper methods for Editor Windows.
public static class CustomInspectorsHelper
{
    // --- GUIStyle Definitions ---
    // Styles are initialized lazily to ensure GUI.skin is available.
    public static GUIStyle LargeLabelStyle => _largeLabelStyle ?? (_largeLabelStyle = new GUIStyle(EditorStyles.label)
    {
        fontSize = 36,
        fontStyle = FontStyle.Bold,
        normal = { textColor = UnityEngine.Color.white },
        alignment = TextAnchor.MiddleCenter
    });
    private static GUIStyle _largeLabelStyle;

    public static GUIStyle MediumLabelStyle => _medLabelStyle ?? (_medLabelStyle = new GUIStyle(EditorStyles.label)
    {
        fontSize = 16,
        fontStyle = FontStyle.Normal,
        normal = { textColor = UnityEngine.Color.white },
        alignment = TextAnchor.MiddleCenter
    });
    private static GUIStyle _medLabelStyle;

    public static GUIStyle SmallLabelStyle => _smallLabelStyle ?? (_smallLabelStyle = new GUIStyle(EditorStyles.label)
    {
        fontSize = 12,
        fontStyle = FontStyle.Normal,
        wordWrap = true, // Enable text wrapping
        normal = { textColor = new UnityEngine.Color(0.85f, 0.85f, 0.85f) },
        alignment = TextAnchor.UpperLeft // Better alignment for wrapped text
    });
    private static GUIStyle _smallLabelStyle;

    public static GUIStyle SmallLabelStyleCenter => _smallLabelStyleCenter ?? (_smallLabelStyleCenter = new GUIStyle(EditorStyles.label)
    {
        fontSize = 12,
        fontStyle = FontStyle.Bold,
        wordWrap = true, // Enable text wrapping
        normal = { textColor = new UnityEngine.Color(1f, 1f, 1f) },
        alignment = TextAnchor.MiddleCenter
    });
    private static GUIStyle _smallLabelStyleCenter;

    public static GUIStyle SmallBoldLabelStyle => _smallBoldLabelStyle ?? (_smallBoldLabelStyle = new GUIStyle(EditorStyles.label)
    {
        fontSize = 12,
        fontStyle = FontStyle.Bold,
        wordWrap = true, // Enable text wrapping
        normal = { textColor = new UnityEngine.Color(0.95f, 0.95f, 0.95f) },
        alignment = TextAnchor.UpperLeft // Better alignment for wrapped text
    });
    private static GUIStyle _smallBoldLabelStyle;

    // Custom styles for enabled and disabled buttons
    public static GUIStyle EnabledButtonStyle => _enabledButtonStyle ?? (_enabledButtonStyle = new GUIStyle(GUI.skin.button)
    {
        fontSize = 12,
        normal = { textColor = Color.white, background = MakeTex(2, 2, new Color(0f, 0.4f, 0f)) } 
    });
    private static GUIStyle _enabledButtonStyle;

    public static GUIStyle DisabledButtonStyle => _disabledButtonStyle ?? (_disabledButtonStyle = new GUIStyle(GUI.skin.button)
    {
        normal = { textColor = Color.white, background = MakeTex(2, 2, new Color(0.4f, 0f, 0f)) } 
    });
    private static GUIStyle _disabledButtonStyle;

    // Custom styles for toggled and untoggled buttons
    public static GUIStyle ToggledButtonStyle => _toggledButtonStyle ?? (_toggledButtonStyle = new GUIStyle(GUI.skin.button)
    {
        fontSize = 12,
        normal = { textColor = new Color(0.1f, 0.1f, 0.1f), background = MakeTex(2, 2, new Color(0.35f, 0.9f, 0.95f)) } 
    });
    private static GUIStyle _toggledButtonStyle;

    public static GUIStyle UntoggledButtonStyle => _untoggledButtonStyle ?? (_untoggledButtonStyle = new GUIStyle(GUI.skin.button)
    {
        normal = { textColor = Color.white}
    });
    private static GUIStyle _untoggledButtonStyle;

    // --- Helper Methods ---

    // Helper to create a solid color texture for GUIStyle backgrounds
    public static Texture2D MakeTex(int width, int height, Color col)
    {
        Color[] pix = new Color[width * height];
        for (int i = 0; i < pix.Length; i++)
            pix[i] = col;

        Texture2D result = new Texture2D(width, height);
        result.SetPixels(pix);
        result.Apply();
        return result;
    }

    // Helper for a centered foldout with a custom label style
    public static bool CenteredFoldout(bool isExpanded, string label, GUIStyle labelStyle, float height = 24f)
    {
        Rect foldoutRect = GUILayoutUtility.GetRect(0, height, GUILayout.ExpandWidth(true));
        Rect arrowRect = new Rect(foldoutRect.x, foldoutRect.y, 20, foldoutRect.height);

        // Draw arrow (no label)
        isExpanded = EditorGUI.Foldout(arrowRect, isExpanded, GUIContent.none, true);

        // Handle full-width click
        Event e = Event.current;
        if (e.type == EventType.MouseDown && foldoutRect.Contains(e.mousePosition))
        {
            if (!arrowRect.Contains(e.mousePosition))
            {
                isExpanded = !isExpanded;
                e.Use();
            }
        }

        // Draw centered label
        GUI.Label(foldoutRect, label, labelStyle);

        return isExpanded;
    }
}


#endif