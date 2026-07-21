// Place in: Assets/Editor/AnimationEffectTimelineEditor.cs

using UnityEditor;
using UnityEngine;

[CustomEditor(typeof(AnimationEffectTimeline))]
public sealed class AnimationEffectTimelineEditor : Editor
{
    const float HeaderHeight = 22f;
    const float TrackHeight = 40f;
    const float LabelWidth = 58f;

    int selectedEvent = -1;
    float playhead;

    AnimationEffectTimeline Timeline => (AnimationEffectTimeline)target;

    public override void OnInspectorGUI()
    {
        serializedObject.Update();

        EditorGUI.BeginChangeCheck();
        Timeline.animationClip = (AnimationClip)EditorGUILayout.ObjectField(
            "Animation Clip", Timeline.animationClip, typeof(AnimationClip), false);
        Timeline.animatorStateName = EditorGUILayout.TextField("Animator State", Timeline.animatorStateName);
        if (EditorGUI.EndChangeCheck())
            MarkDirty();

        if (Timeline.animationClip == null)
        {
            EditorGUILayout.HelpBox("Assign an AnimationClip to edit its event timeline.", MessageType.Info);
            return;
        }

        var duration = Timeline.animationClip.length;
        playhead = EditorGUILayout.Slider("Add Event At", playhead, 0f, duration);

        EditorGUILayout.BeginHorizontal();
        if (GUILayout.Button("+ Effect")) AddEvent(AnimationTimelineEventType.Effect, duration);
        if (GUILayout.Button("+ Audio")) AddEvent(AnimationTimelineEventType.Audio, duration);
        if (GUILayout.Button("Sort"))
        {
            Undo.RecordObject(Timeline, "Sort timeline events");
            Timeline.events.Sort((a, b) => a.normalizedTime.CompareTo(b.normalizedTime));
            selectedEvent = -1;
            MarkDirty();
        }
        EditorGUILayout.EndHorizontal();

        EditorGUILayout.Space(5);
        var timelineRect = GUILayoutUtility.GetRect(1f, HeaderHeight + TrackHeight * 2f + 10f,
            GUILayout.ExpandWidth(true));
        DrawTimeline(timelineRect, duration);
        HandleTimelineInput(timelineRect, duration);

        DrawSelectedEvent(duration);
        serializedObject.ApplyModifiedProperties();
    }

    void DrawTimeline(Rect rect, float duration)
    {
        var eventArea = new Rect(rect.x + LabelWidth, rect.y, rect.width - LabelWidth, rect.height);
        EditorGUI.DrawRect(rect, new Color(0.15f, 0.15f, 0.15f));

        DrawTrack(new Rect(rect.x, rect.y + HeaderHeight, rect.width, TrackHeight), "Effect");
        DrawTrack(new Rect(rect.x, rect.y + HeaderHeight + TrackHeight, rect.width, TrackHeight), "Audio");

        Handles.color = new Color(1f, 1f, 1f, 0.18f);
        const int divisions = 4;
        for (var i = 0; i <= divisions; i++)
        {
            var x = Mathf.Lerp(eventArea.xMin, eventArea.xMax, i / (float)divisions);
            Handles.DrawLine(new Vector3(x, rect.y), new Vector3(x, rect.yMax));
            var seconds = duration * i / divisions;
            GUI.Label(new Rect(x - 18f, rect.y + 2f, 42f, HeaderHeight),
                $"{seconds:0.##}s", EditorStyles.miniLabel);
        }

        for (var i = 0; i < Timeline.events.Count; i++)
        {
            var item = Timeline.events[i];
            var x = Mathf.Lerp(eventArea.xMin, eventArea.xMax, item.normalizedTime);
            var y = item.type == AnimationTimelineEventType.Effect
                ? rect.y + HeaderHeight + TrackHeight * 0.5f
                : rect.y + HeaderHeight + TrackHeight * 1.5f;
            var marker = new Rect(x - 7f, y - 11f, 14f, 22f);
            var color = item.type == AnimationTimelineEventType.Effect
                ? new Color(0.25f, 0.75f, 1f)
                : new Color(1f, 0.7f, 0.2f);
            if (i == selectedEvent) color = Color.white;
            EditorGUI.DrawRect(marker, color);
            GUI.Label(marker, item.type == AnimationTimelineEventType.Effect ? "V" : "S",
                EditorStyles.boldLabel);
        }

        var playheadX = Mathf.Lerp(eventArea.xMin, eventArea.xMax, Mathf.Clamp01(playhead / duration));
        Handles.color = new Color(1f, 0.3f, 0.3f);
        Handles.DrawLine(new Vector3(playheadX, rect.y), new Vector3(playheadX, rect.yMax));
    }

    void DrawTrack(Rect rect, string name)
    {
        EditorGUI.DrawRect(rect, new Color(0f, 0f, 0f, 0.15f));
        GUI.Label(new Rect(rect.x + 5f, rect.y + 11f, LabelWidth - 10f, 18f), name, EditorStyles.miniBoldLabel);
    }

    void HandleTimelineInput(Rect rect, float duration)
    {
        var currentEvent = Event.current;
        if (!rect.Contains(currentEvent.mousePosition)) return;

        var eventArea = new Rect(rect.x + LabelWidth, rect.y, rect.width - LabelWidth, rect.height);
        var normalized = Mathf.Clamp01(Mathf.InverseLerp(eventArea.xMin, eventArea.xMax, currentEvent.mousePosition.x));

        if (currentEvent.type == EventType.MouseDown && currentEvent.button == 0)
        {
            selectedEvent = FindMarker(currentEvent.mousePosition, rect, eventArea);
            if (selectedEvent < 0)
            {
                playhead = normalized * duration;
                Repaint();
            }
            currentEvent.Use();
        }
        else if (currentEvent.type == EventType.MouseDrag && currentEvent.button == 0 && selectedEvent >= 0)
        {
            Undo.RecordObject(Timeline, "Move timeline event");
            Timeline.events[selectedEvent].normalizedTime = normalized;
            playhead = normalized * duration;
            MarkDirty();
            currentEvent.Use();
            Repaint();
        }
    }

    int FindMarker(Vector2 mousePosition, Rect timelineRect, Rect eventArea)
    {
        for (var i = Timeline.events.Count - 1; i >= 0; i--)
        {
            var item = Timeline.events[i];
            var x = Mathf.Lerp(eventArea.xMin, eventArea.xMax, item.normalizedTime);
            var y = item.type == AnimationTimelineEventType.Effect
                ? timelineRect.y + HeaderHeight + TrackHeight * 0.5f
                : timelineRect.y + HeaderHeight + TrackHeight * 1.5f;
            if (new Rect(x - 9f, y - 13f, 18f, 26f).Contains(mousePosition))
                return i;
        }
        return -1;
    }

    void DrawSelectedEvent(float duration)
    {
        if (selectedEvent < 0 || selectedEvent >= Timeline.events.Count)
        {
            EditorGUILayout.HelpBox("Click an event marker to edit it. Drag a marker to change its time.", MessageType.None);
            return;
        }

        var item = Timeline.events[selectedEvent];
        EditorGUILayout.Space(6);
        EditorGUILayout.LabelField("Selected Event", EditorStyles.boldLabel);

        EditorGUI.BeginChangeCheck();
        var seconds = EditorGUILayout.Slider("Time", item.normalizedTime * duration, 0f, duration);
        item.normalizedTime = duration > 0f ? seconds / duration : 0f;
        item.type = (AnimationTimelineEventType)EditorGUILayout.EnumPopup("Type", item.type);

        if (item.type == AnimationTimelineEventType.Effect)
        {
            item.effectPrefab = (GameObject)EditorGUILayout.ObjectField("Effect Prefab", item.effectPrefab,
                typeof(GameObject), false);
            item.socketName = EditorGUILayout.TextField("Socket Name", item.socketName);
            item.followSocket = EditorGUILayout.Toggle("Follow Socket", item.followSocket);
            item.localPosition = EditorGUILayout.Vector3Field("Local Position", item.localPosition);
            item.localEulerAngles = EditorGUILayout.Vector3Field("Local Rotation", item.localEulerAngles);
            item.destroyAfterSeconds = EditorGUILayout.FloatField("Destroy After", item.destroyAfterSeconds);
        }
        else
        {
            item.audioClip = (AudioClip)EditorGUILayout.ObjectField("Audio Clip", item.audioClip,
                typeof(AudioClip), false);
            item.volume = EditorGUILayout.Slider("Volume", item.volume, 0f, 1f);
        }

        if (EditorGUI.EndChangeCheck())
            MarkDirty();

        GUI.color = new Color(1f, 0.6f, 0.6f);
        if (GUILayout.Button("Delete Selected Event"))
        {
            Undo.RecordObject(Timeline, "Delete timeline event");
            Timeline.events.RemoveAt(selectedEvent);
            selectedEvent = -1;
            MarkDirty();
        }
        GUI.color = Color.white;
    }

    void AddEvent(AnimationTimelineEventType type, float duration)
    {
        Undo.RecordObject(Timeline, "Add timeline event");
        Timeline.events.Add(new AnimationTimelineEvent
        {
            type = type,
            normalizedTime = duration > 0f ? playhead / duration : 0f
        });
        selectedEvent = Timeline.events.Count - 1;
        MarkDirty();
    }

    void MarkDirty()
    {
        EditorUtility.SetDirty(Timeline);
    }
}
