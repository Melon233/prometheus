// Place this file in: Assets/Editor/ComponentHeaderRemoveButton.cs
// Unity 2018.4+

using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Adds a small remove button to the right side of each removable component header.
/// The action supports Undo. Transform and non-editable/persistent components are excluded.
/// </summary>
[InitializeOnLoad]
public static class ComponentHeaderRemoveButton
{
    const float ButtonWidth = 18f;
    const float RightInset = 42f; // Leave room for Unity's context menu button.

    static readonly GUIContent RemoveContent = new GUIContent("×", "Remove Component (Undo: Ctrl/Cmd + Z)");
    static GUIStyle removeButtonStyle;

    static ComponentHeaderRemoveButton()
    {
        // Editor.finishedDefaultHeaderGUI -= DrawRemoveButton;
        Editor.finishedDefaultHeaderGUI += DrawRemoveButton;
        Debug.Log("[ComponentRemove] Header callback registered.");
    }

    static void DrawRemoveButton(Editor editor)
    {
        var removableTargets = GetRemovableTargets(editor.targets);
        if (removableTargets.Count == 0)
            return;

        GUILayout.BeginHorizontal();
        GUILayout.FlexibleSpace();

        var oldColor = GUI.color;
        GUI.color = new Color(1f, 0.55f, 0.55f);

        if (GUILayout.Button(
                RemoveContent,
                EditorStyles.miniButton,
                GUILayout.Width(22f),
                GUILayout.Height(18f)))
        {
            foreach (var component in removableTargets)
                Undo.DestroyObjectImmediate(component);

            GUIUtility.ExitGUI();
        }

        GUI.color = oldColor;
        GUILayout.EndHorizontal();
    }

    static List<Component> GetRemovableTargets(Object[] targets)
    {
        var result = new List<Component>();

        foreach (var target in targets)
        {
            var component = target as Component;
            if (component == null || component is Transform)
                continue;

            // Do not offer an invalid delete action for imported/persistent assets or locked objects.
            if (EditorUtility.IsPersistent(component) ||
                (component.hideFlags & HideFlags.NotEditable) != 0)
                continue;

            result.Add(component);
        }

        // Multi-object editing must be all-or-nothing; never delete only part of a selection.
        if (result.Count != targets.Length)
            result.Clear();

        return result;
    }

    static GUIStyle GetRemoveButtonStyle()
    {
        if (removeButtonStyle != null)
            return removeButtonStyle;

        removeButtonStyle = new GUIStyle(EditorStyles.miniButton)
        {
            fontSize = 15,
            fontStyle = FontStyle.Bold,
            alignment = TextAnchor.MiddleCenter,
            padding = new RectOffset(0, 0, -1, 1)
        };
        return removeButtonStyle;
    }
}
