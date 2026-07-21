using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using Xuan.Prometheus;

[CustomEditor(typeof(SignalTimelineAsset))]
public sealed class SignalTimelineAssetEditor : Editor
{
    private const float HeaderHeight = 22f;
    private const float TrackHeight = 38f;
    private const float TimelineBottomPadding = 8f;
    private const float MarkerWidth = 12f;
    private const float MarkerHeight = 22f;

    private SerializedProperty _durationProperty;
    private SerializedProperty _signalsProperty;
    private int _selectedSignalIndex = -1;
    private int _draggingSignalIndex = -1;
    private float _playhead;

    private SignalTimelineAsset Timeline => (SignalTimelineAsset)target;

    private void OnEnable()
    {
        _durationProperty = serializedObject.FindProperty("_duration");
        _signalsProperty = serializedObject.FindProperty("_signals");
    }

    public override void OnInspectorGUI()
    {
        serializedObject.Update();

        DrawDuration();
        var duration = Mathf.Max(0.01f, _durationProperty.floatValue);
        _playhead = EditorGUILayout.Slider("Add Signal At", _playhead, 0f, duration);

        EditorGUILayout.BeginHorizontal();
        if (GUILayout.Button("Add Signal"))
            ShowAddSignalMenu();

        if (GUILayout.Button("Sort By Time"))
            SortSignals();
        EditorGUILayout.EndHorizontal();

        EditorGUILayout.Space(5f);
        var signalTypes = GetSignalTypesInTimeline();
        var timelineHeight = HeaderHeight + TrackHeight * Mathf.Max(1, signalTypes.Count) + TimelineBottomPadding;
        var timelineRect = GUILayoutUtility.GetRect(1f, timelineHeight, GUILayout.ExpandWidth(true));
        DrawTimeline(timelineRect, duration, signalTypes);
        HandleTimelineInput(timelineRect, duration, signalTypes);

        DrawSelectedSignal(duration);

        if (serializedObject.ApplyModifiedProperties())
            EditorUtility.SetDirty(Timeline);
    }

    private void DrawDuration()
    {
        EditorGUI.BeginChangeCheck();
        var newDuration = EditorGUILayout.DelayedFloatField("Duration", _durationProperty.floatValue);
        if (!EditorGUI.EndChangeCheck())
            return;

        Undo.RecordObject(Timeline, "Change Signal Timeline Duration");
        _durationProperty.floatValue = Mathf.Max(0.01f, newDuration);
        ClampSignalTimes(_durationProperty.floatValue);
        _playhead = Mathf.Clamp(_playhead, 0f, _durationProperty.floatValue);
    }

    private void DrawTimeline(Rect rect, float duration, IReadOnlyList<Type> signalTypes)
    {
        EditorGUI.DrawRect(rect, new Color(0.13f, 0.13f, 0.13f));

        var rulerRect = new Rect(rect.x, rect.y, rect.width, HeaderHeight);
        for (var i = 0; i < Mathf.Max(1, signalTypes.Count); i++)
        {
            var trackRect = new Rect(rect.x, rect.y + HeaderHeight + i * TrackHeight, rect.width, TrackHeight);
            var shade = i % 2 == 0 ? 0.2f : 0.28f;
            EditorGUI.DrawRect(trackRect, new Color(0f, 0f, 0f, shade));

            var label = signalTypes.Count == 0
                ? "Signals"
                : GetSignalTypeLabel(signalTypes[i]);
            GUI.Label(new Rect(trackRect.x + 6f, trackRect.y + 10f, 150f, 18f), label, EditorStyles.miniBoldLabel);
        }

        const int divisions = 4;
        for (var i = 0; i <= divisions; i++)
        {
            var normalized = i / (float)divisions;
            var x = Mathf.Lerp(rect.xMin, rect.xMax, normalized);
            Handles.color = new Color(1f, 1f, 1f, 0.16f);
            Handles.DrawLine(new Vector3(x, rulerRect.yMin), new Vector3(x, rect.yMax));
            GUI.Label(new Rect(x + 3f, rulerRect.y + 3f, 45f, 16f),
                $"{duration * normalized:0.##}s", EditorStyles.miniLabel);
        }

        for (var i = 0; i < _signalsProperty.arraySize; i++)
        {
            var signalProperty = _signalsProperty.GetArrayElementAtIndex(i);
            var markerRect = GetMarkerRect(signalProperty, rect, duration, signalTypes);
            var signal = signalProperty.managedReferenceValue as Signal;
            var color = GetSignalColor(signal);
            if (i == _selectedSignalIndex)
                color = Color.white;

            EditorGUI.DrawRect(markerRect, color);
            GUI.Label(markerRect, "◆", EditorStyles.centeredGreyMiniLabel);
        }

        var playheadX = Mathf.Lerp(rect.xMin, rect.xMax, _playhead / duration);
        Handles.color = new Color(1f, 0.35f, 0.35f);
        Handles.DrawLine(new Vector3(playheadX, rect.yMin), new Vector3(playheadX, rect.yMax));
    }

    private void HandleTimelineInput(Rect rect, float duration, IReadOnlyList<Type> signalTypes)
    {
        var currentEvent = Event.current;

        if (currentEvent.type == UnityEngine.EventType.MouseUp && _draggingSignalIndex >= 0)
        {
            _draggingSignalIndex = -1;
            currentEvent.Use();
            return;
        }

        if (!rect.Contains(currentEvent.mousePosition))
            return;

        var time = Mathf.Clamp01(Mathf.InverseLerp(rect.xMin, rect.xMax, currentEvent.mousePosition.x)) * duration;

        if (currentEvent.type == UnityEngine.EventType.MouseDown && currentEvent.button == 0)
        {
            _selectedSignalIndex = FindSignalAt(currentEvent.mousePosition, rect, duration, signalTypes);
            if (_selectedSignalIndex >= 0)
            {
                Undo.RecordObject(Timeline, "Move Signal");
                _draggingSignalIndex = _selectedSignalIndex;
            }
            else
            {
                _playhead = time;
            }

            currentEvent.Use();
            Repaint();
            return;
        }

        if (currentEvent.type == UnityEngine.EventType.MouseDrag && _draggingSignalIndex >= 0)
        {
            var timeProperty = _signalsProperty.GetArrayElementAtIndex(_draggingSignalIndex)
                .FindPropertyRelative("_time");
            timeProperty.floatValue = time;
            currentEvent.Use();
            Repaint();
        }
    }

    private int FindSignalAt(Vector2 position, Rect timelineRect, float duration, IReadOnlyList<Type> signalTypes)
    {
        for (var i = _signalsProperty.arraySize - 1; i >= 0; i--)
        {
            var signalProperty = _signalsProperty.GetArrayElementAtIndex(i);
            if (GetMarkerRect(signalProperty, timelineRect, duration, signalTypes).Contains(position))
                return i;
        }

        return -1;
    }

    private Rect GetMarkerRect(
        SerializedProperty signalProperty,
        Rect timelineRect,
        float duration,
        IReadOnlyList<Type> signalTypes)
    {
        var timeProperty = signalProperty.FindPropertyRelative("_time");
        var normalizedTime = Mathf.Clamp01(timeProperty.floatValue / duration);
        var x = Mathf.Lerp(timelineRect.xMin, timelineRect.xMax, normalizedTime);
        var signal = signalProperty.managedReferenceValue as Signal;
        var trackIndex = signal != null ? IndexOfSignalType(signalTypes, signal.GetType()) : 0;
        var y = timelineRect.y + HeaderHeight + trackIndex * TrackHeight + (TrackHeight - MarkerHeight) * 0.5f;
        return new Rect(x - MarkerWidth * 0.5f, y, MarkerWidth, MarkerHeight);
    }

    private List<Type> GetSignalTypesInTimeline()
    {
        var types = new List<Type>();
        for (var i = 0; i < _signalsProperty.arraySize; i++)
        {
            var signal = _signalsProperty.GetArrayElementAtIndex(i).managedReferenceValue as Signal;
            if (signal != null && !types.Contains(signal.GetType()))
                types.Add(signal.GetType());
        }

        types.Sort((left, right) => string.Compare(GetMenuPath(left), GetMenuPath(right), StringComparison.Ordinal));
        return types;
    }

    private static int IndexOfSignalType(IReadOnlyList<Type> signalTypes, Type signalType)
    {
        for (var i = 0; i < signalTypes.Count; i++)
        {
            if (signalTypes[i] == signalType)
                return i;
        }

        return 0;
    }

    private static string GetSignalTypeLabel(Type signalType)
    {
        var name = ObjectNames.NicifyVariableName(signalType.Name);
        return name.EndsWith(" Signal", StringComparison.Ordinal)
            ? name.Substring(0, name.Length - " Signal".Length)
            : name;
    }

    private void DrawSelectedSignal(float duration)
    {
        if (_selectedSignalIndex < 0 || _selectedSignalIndex >= _signalsProperty.arraySize)
        {
            EditorGUILayout.HelpBox("Choose Add Signal, then drag its marker to place it on the timeline.", MessageType.Info);
            return;
        }

        var signalProperty = _signalsProperty.GetArrayElementAtIndex(_selectedSignalIndex);
        var signal = signalProperty.managedReferenceValue as Signal;
        EditorGUILayout.Space(6f);
        EditorGUILayout.LabelField(signal != null ? signal.DisplayName : "Missing Signal", EditorStyles.boldLabel);

        EditorGUI.BeginChangeCheck();
        EditorGUILayout.PropertyField(signalProperty, GUIContent.none, true);
        if (EditorGUI.EndChangeCheck())
        {
            var timeProperty = signalProperty.FindPropertyRelative("_time");
            timeProperty.floatValue = Mathf.Clamp(timeProperty.floatValue, 0f, duration);
        }

        GUI.color = new Color(1f, 0.62f, 0.62f);
        if (GUILayout.Button("Delete Selected Signal"))
        {
            Undo.RecordObject(Timeline, "Delete Signal");
            _signalsProperty.DeleteArrayElementAtIndex(_selectedSignalIndex);
            _selectedSignalIndex = -1;
        }
        GUI.color = Color.white;
    }

    private void ShowAddSignalMenu()
    {
        var signalTypes = TypeCache.GetTypesDerivedFrom<Signal>()
            .Where(type => !type.IsAbstract && !type.IsGenericType && type.GetConstructor(Type.EmptyTypes) != null)
            .OrderBy(GetMenuPath)
            .ToList();

        var menu = new GenericMenu();
        if (signalTypes.Count == 0)
        {
            menu.AddDisabledItem(new GUIContent("No Signal subclasses found"));
        }
        else
        {
            foreach (var signalType in signalTypes)
            {
                var capturedType = signalType;
                menu.AddItem(new GUIContent(GetMenuPath(capturedType)), false, () => AddSignal(capturedType));
            }
        }

        menu.ShowAsContext();
    }

    private void AddSignal(Type signalType)
    {
        Undo.RecordObject(Timeline, "Add Signal");
        serializedObject.Update();

        var signal = (Signal)Activator.CreateInstance(signalType);
        signal.Time = _playhead;

        _signalsProperty.arraySize++;
        var signalProperty = _signalsProperty.GetArrayElementAtIndex(_signalsProperty.arraySize - 1);
        signalProperty.managedReferenceValue = signal;
        _selectedSignalIndex = _signalsProperty.arraySize - 1;

        serializedObject.ApplyModifiedProperties();
        EditorUtility.SetDirty(Timeline);
        Repaint();
    }

    private void SortSignals()
    {
        serializedObject.ApplyModifiedProperties();
        Undo.RecordObject(Timeline, "Sort Signals");
        Timeline.SortSignals();
        EditorUtility.SetDirty(Timeline);
        serializedObject.Update();
        _selectedSignalIndex = -1;
    }

    private void ClampSignalTimes(float duration)
    {
        for (var i = 0; i < _signalsProperty.arraySize; i++)
        {
            var timeProperty = _signalsProperty.GetArrayElementAtIndex(i).FindPropertyRelative("_time");
            timeProperty.floatValue = Mathf.Clamp(timeProperty.floatValue, 0f, duration);
        }
    }

    private static string GetMenuPath(Type signalType)
    {
        var attribute = (SignalMenuAttribute)Attribute.GetCustomAttribute(signalType, typeof(SignalMenuAttribute));
        return attribute != null && !string.IsNullOrWhiteSpace(attribute.Path)
            ? attribute.Path
            : "Other/" + ObjectNames.NicifyVariableName(signalType.Name);
    }

    private static Color GetSignalColor(Signal signal)
    {
        if (signal == null)
            return new Color(0.8f, 0.2f, 0.2f);

        var hash = signal.GetType().FullName.GetHashCode();
        var hue = Mathf.Abs(hash % 360) / 360f;
        return Color.HSVToRGB(hue, 0.6f, 0.9f);
    }
}
