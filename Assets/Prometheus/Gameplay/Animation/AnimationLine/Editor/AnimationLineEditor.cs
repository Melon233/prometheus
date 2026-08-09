#if UNITY_EDITOR
using System.Collections.Generic;
using System.Reflection;
using Spine;
using Spine.Unity;
using UnityEditor;
using UnityEngine;

namespace Xuan.Prometheus.Editor
{
    /// <summary>
    /// Draws an editable time ruler for AnimationLine and provides explicit event insertion controls.
    /// </summary>
    [CustomEditor(typeof(AnimationLine))]
    public sealed class AnimationLineEditor : UnityEditor.Editor
    {
        private const float PreviewHeight = 240f;
        private const float DefaultPreviewFrameRate = 30f;
        private const float MinimumPreviewFrameRate = 1f;
        private const float MaximumPreviewFrameRate = 240f;
        private const float TimelineHeight = 96f;
        private const float MarkerWidth = 5f;
        private const int RulerSegmentCount = 4;
        private const BindingFlags PreviewMemberFlags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        private static readonly int TimelineControlHash = "AnimationLineTimelineControl".GetHashCode();
        private static readonly Color SourceEventColor = new Color(0.35f, 0.9f, 0.45f, 1f);
        private static readonly Color CustomEventColor = new Color(0.25f, 0.8f, 1f, 1f);

        private SerializedProperty semanticProperty;
        private SerializedProperty animationReferenceAssetProperty;
        private SerializedProperty eventsProperty;
        private UnityEditor.Editor spinePreviewEditor;
        private AnimationReferenceAsset previewAsset;
        private object spinePreview;
        private PropertyInfo activePreviewTrackProperty;
        private FieldInfo previewSkeletonAnimationField;
        private MethodInfo playPausePreviewMethod;
        private MethodInfo refreshPreviewMethod;
        private float insertionTime;
        private float previewFrameRate = DefaultPreviewFrameRate;
        private string insertionEventName = "event";
        private int insertionIntValue;
        private float insertionFloatValue;
        private string insertionStringValue = string.Empty;
        private bool showSourceEvents = true;

        /// <summary>
        /// Caches serialized property paths used by the custom Inspector.
        /// </summary>
        private void OnEnable()
        {
            semanticProperty = serializedObject.FindProperty("semantic");
            animationReferenceAssetProperty = serializedObject.FindProperty("animationReferenceAsset");
            eventsProperty = serializedObject.FindProperty("events");
            EditorApplication.update -= HandlePreviewUpdate;
            EditorApplication.update += HandlePreviewUpdate;
        }

        /// <summary>
        /// Releases the nested Spine preview and its hidden render objects when this Inspector closes.
        /// </summary>
        private void OnDisable()
        {
            EditorApplication.update -= HandlePreviewUpdate;
            DestroyPreviewEditor();
        }

        /// <summary>
        /// Draws source selection, timeline insertion controls and the editable marker list.
        /// </summary>
        public override void OnInspectorGUI()
        {
            AnimationLine animationLine = (AnimationLine)target;
            serializedObject.Update();
            EditorGUILayout.PropertyField(semanticProperty, new GUIContent("Animation Semantic"));
            EditorGUI.BeginChangeCheck();
            EditorGUILayout.PropertyField(animationReferenceAssetProperty, new GUIContent("Animation Reference"));
            if (EditorGUI.EndChangeCheck())
            {
                serializedObject.ApplyModifiedProperties();
                animationLine.NormalizeEvents();
                serializedObject.Update();
                insertionTime = 0f;
                DestroyPreviewEditor();
            }

            float duration = animationLine.Duration;
            List<Spine.Event> sourceEvents = CollectSourceEvents(animationLine);
            using (new EditorGUI.DisabledScope(true)) EditorGUILayout.FloatField("Duration (Seconds)", duration);
            if (animationLine.AnimationReferenceAsset == null || duration <= 0f) EditorGUILayout.HelpBox("Assign a valid AnimationReferenceAsset before inserting events.", MessageType.Info);
            DrawAnimationPreview(animationLine, duration);
            DrawTimeline(animationLine, sourceEvents, duration);
            DrawSourceEventList(sourceEvents, duration);
            DrawInsertionPanel(animationLine, duration);
            DrawEventList(animationLine, duration);
            if (serializedObject.ApplyModifiedProperties())
            {
                animationLine.NormalizeEvents();
                EditorUtility.SetDirty(animationLine);
            }
        }

        /// <summary>
        /// Draws the Spine animation preview and controls that share the timeline's current time.
        /// </summary>
        private void DrawAnimationPreview(AnimationLine animationLine, float duration)
        {
            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Animation Preview", EditorStyles.boldLabel);
            if (!EnsurePreviewEditor(animationLine))
            {
                EditorGUILayout.HelpBox("Assign a valid AnimationReferenceAsset to enable the animation preview.", MessageType.None);
                return;
            }
            Rect previewRect = GUILayoutUtility.GetRect(10f, PreviewHeight, GUILayout.ExpandWidth(true));
            spinePreviewEditor.OnInteractivePreviewGUI(previewRect, EditorStyles.helpBox);
            TrackEntry activeTrack = GetActivePreviewTrack();
            bool supportsPlaybackControls = spinePreview != null && activeTrack != null && duration > 0f;
            using (new EditorGUI.DisabledScope(!supportsPlaybackControls)) DrawPreviewControls(duration);
            if (spinePreview == null) EditorGUILayout.HelpBox("The installed Spine editor does not expose compatible preview controls.", MessageType.Warning);
        }

        /// <summary>
        /// Draws play, pause, next-frame and shared-time controls for the preview.
        /// </summary>
        private void DrawPreviewControls(float duration)
        {
            bool isPlaying = IsPreviewPlaying();
            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button(isPlaying ? "Pause" : "Play", GUILayout.Width(88f))) SetPreviewPlaying(!isPlaying);
            if (GUILayout.Button("Next Frame", GUILayout.Width(100f))) StepPreviewForward(duration);
            GUILayout.FlexibleSpace();
            EditorGUILayout.LabelField($"{insertionTime:0.000} / {duration:0.000} s", EditorStyles.miniLabel, GUILayout.Width(130f));
            EditorGUILayout.EndHorizontal();
            EditorGUI.BeginChangeCheck();
            float selectedTime = EditorGUILayout.Slider("Current Time", Mathf.Clamp(insertionTime, 0f, duration), 0f, duration);
            if (EditorGUI.EndChangeCheck()) SeekPreview(selectedTime, true);
            previewFrameRate = Mathf.Clamp(EditorGUILayout.FloatField("Preview FPS", previewFrameRate), MinimumPreviewFrameRate, MaximumPreviewFrameRate);
        }

        /// <summary>
        /// Creates the official Spine AnimationReferenceAsset editor and caches its preview bridge.
        /// </summary>
        private bool EnsurePreviewEditor(AnimationLine animationLine)
        {
            AnimationReferenceAsset sourceAsset = animationLine.AnimationReferenceAsset;
            if (spinePreviewEditor != null && previewAsset == sourceAsset) return true;
            DestroyPreviewEditor();
            if (sourceAsset == null || sourceAsset.Animation == null) return false;
            previewAsset = sourceAsset;
            spinePreviewEditor = UnityEditor.Editor.CreateEditor(sourceAsset);
            if (spinePreviewEditor == null) return false;
            FieldInfo previewField = spinePreviewEditor.GetType().GetField("preview", PreviewMemberFlags);
            spinePreview = previewField == null ? null : previewField.GetValue(spinePreviewEditor);
            if (spinePreview == null) return true;
            System.Type previewType = spinePreview.GetType();
            activePreviewTrackProperty = previewType.GetProperty("ActiveTrack", PreviewMemberFlags);
            previewSkeletonAnimationField = previewType.GetField("skeletonAnimation", PreviewMemberFlags);
            playPausePreviewMethod = previewType.GetMethod("PlayPauseAnimation", PreviewMemberFlags);
            refreshPreviewMethod = previewType.GetMethod("RefreshOnNextUpdate", PreviewMemberFlags);
            return true;
        }

        /// <summary>
        /// Destroys the nested editor so Spine can unregister updates and clean its PreviewRenderUtility.
        /// </summary>
        private void DestroyPreviewEditor()
        {
            if (spinePreviewEditor != null) UnityEngine.Object.DestroyImmediate(spinePreviewEditor);
            spinePreviewEditor = null;
            previewAsset = null;
            spinePreview = null;
            activePreviewTrackProperty = null;
            previewSkeletonAnimationField = null;
            playPausePreviewMethod = null;
            refreshPreviewMethod = null;
        }

        /// <summary>
        /// Returns the track rendered by the official Spine preview.
        /// </summary>
        private TrackEntry GetActivePreviewTrack()
        {
            return spinePreview == null || activePreviewTrackProperty == null ? null : activePreviewTrackProperty.GetValue(spinePreview, null) as TrackEntry;
        }

        /// <summary>
        /// Returns the hidden SkeletonAnimation used to render the official Spine preview.
        /// </summary>
        private SkeletonAnimation GetPreviewSkeletonAnimation()
        {
            return spinePreview == null || previewSkeletonAnimationField == null ? null : previewSkeletonAnimationField.GetValue(spinePreview) as SkeletonAnimation;
        }

        /// <summary>
        /// Reports whether the preview's current track is advancing.
        /// </summary>
        private bool IsPreviewPlaying()
        {
            TrackEntry activeTrack = GetActivePreviewTrack();
            return activeTrack != null && activeTrack.TimeScale > 0f;
        }

        /// <summary>
        /// Starts or pauses the active preview track without affecting runtime scene objects.
        /// </summary>
        private void SetPreviewPlaying(bool shouldPlay)
        {
            TrackEntry activeTrack = GetActivePreviewTrack();
            if (activeTrack == null && shouldPlay && previewAsset != null && previewAsset.Animation != null && playPausePreviewMethod != null)
            {
                playPausePreviewMethod.Invoke(spinePreview, new object[] { previewAsset.Animation.Name, true });
                activeTrack = GetActivePreviewTrack();
            }
            if (activeTrack == null) return;
            activeTrack.TimeScale = shouldPlay ? 1f : 0f;
            RefreshPreview();
            Repaint();
        }

        /// <summary>
        /// Advances the shared preview time by one authored preview frame and wraps at the animation end.
        /// </summary>
        private void StepPreviewForward(float duration)
        {
            if (duration <= 0f) return;
            float nextTime = Mathf.Repeat(insertionTime + 1f / previewFrameRate, duration);
            SeekPreview(nextTime, true);
        }

        /// <summary>
        /// Seeks the hidden Spine track and synchronizes the timeline insertion cursor.
        /// </summary>
        private void SeekPreview(float time, bool pause)
        {
            AnimationLine animationLine = target as AnimationLine;
            float duration = animationLine == null ? 0f : animationLine.Duration;
            insertionTime = Mathf.Clamp(time, 0f, Mathf.Max(0f, duration));
            TrackEntry activeTrack = GetActivePreviewTrack();
            if (activeTrack == null || duration <= 0f)
            {
                Repaint();
                return;
            }
            if (pause) activeTrack.TimeScale = 0f;
            activeTrack.TrackTime = insertionTime >= duration ? Mathf.Max(0f, duration - 0.0001f) : insertionTime;
            SkeletonAnimation skeletonAnimation = GetPreviewSkeletonAnimation();
            if (skeletonAnimation != null)
            {
                skeletonAnimation.Update(0f);
                skeletonAnimation.LateUpdate();
            }
            RefreshPreview();
            Repaint();
        }

        /// <summary>
        /// Marks the official Spine render texture dirty after playback or seeking changes.
        /// </summary>
        private void RefreshPreview()
        {
            if (spinePreview != null && refreshPreviewMethod != null) refreshPreviewMethod.Invoke(spinePreview, null);
        }

        /// <summary>
        /// Pulls the advancing Spine track time back into the shared timeline cursor.
        /// </summary>
        private void HandlePreviewUpdate()
        {
            AnimationLine animationLine = target as AnimationLine;
            TrackEntry activeTrack = GetActivePreviewTrack();
            if (animationLine == null || activeTrack == null || activeTrack.TimeScale <= 0f || animationLine.Duration <= 0f) return;
            insertionTime = Mathf.Repeat(Mathf.Max(0f, activeTrack.TrackTime), animationLine.Duration);
            Repaint();
        }

        /// <summary>
        /// Draws a clickable seconds ruler and all current event markers.
        /// </summary>
        private void DrawTimeline(AnimationLine animationLine, List<Spine.Event> sourceEvents, float duration)
        {
            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Event Timeline", EditorStyles.boldLabel);
            DrawTimelineLegend(sourceEvents.Count, animationLine.Events.Count);
            Rect timelineRect = GUILayoutUtility.GetRect(10f, TimelineHeight, GUILayout.ExpandWidth(true));
            EditorGUI.DrawRect(timelineRect, new Color(0.12f, 0.12f, 0.12f, 1f));
            DrawRuler(timelineRect, duration);
            DrawSourceEventMarkers(sourceEvents, timelineRect, duration);
            DrawCustomEventMarkers(animationLine, timelineRect, duration);
            DrawInsertionCursor(timelineRect, duration);
            HandleTimelineInput(timelineRect, duration);
        }

        /// <summary>
        /// Draws the color legend that distinguishes imported Spine events from editable custom events.
        /// </summary>
        private static void DrawTimelineLegend(int sourceEventCount, int customEventCount)
        {
            EditorGUILayout.BeginHorizontal();
            DrawLegendItem(SourceEventColor, $"Spine Events ({sourceEventCount})");
            GUILayout.Space(16f);
            DrawLegendItem(CustomEventColor, $"Custom Events ({customEventCount})");
            EditorGUILayout.EndHorizontal();
        }

        /// <summary>
        /// Draws one colored timeline legend item.
        /// </summary>
        private static void DrawLegendItem(Color color, string label)
        {
            Rect colorRect = GUILayoutUtility.GetRect(10f, 10f, GUILayout.Width(10f));
            EditorGUI.DrawRect(colorRect, color);
            GUILayout.Label(label, EditorStyles.miniLabel);
        }

        /// <summary>
        /// Draws fixed ruler divisions so marker placement can be read in seconds.
        /// </summary>
        private static void DrawRuler(Rect timelineRect, float duration)
        {
            for (int i = 0; i <= RulerSegmentCount; i++)
            {
                float normalizedTime = i / (float)RulerSegmentCount;
                float x = Mathf.Lerp(timelineRect.xMin, timelineRect.xMax, normalizedTime);
                EditorGUI.DrawRect(new Rect(x, timelineRect.yMin, 1f, timelineRect.height), new Color(1f, 1f, 1f, 0.12f));
                GUI.Label(new Rect(x + 3f, timelineRect.yMin + 2f, 64f, 18f), $"{duration * normalizedTime:0.###}s", EditorStyles.miniLabel);
            }
        }

        /// <summary>
        /// Draws imported Spine event markers in the upper read-only lane.
        /// </summary>
        private static void DrawSourceEventMarkers(List<Spine.Event> sourceEvents, Rect timelineRect, float duration)
        {
            if (duration <= 0f) return;
            float laneTop = timelineRect.yMin + 20f;
            float laneHeight = (timelineRect.height - 20f) * 0.5f;
            EditorGUI.DrawRect(new Rect(timelineRect.xMin, laneTop + laneHeight, timelineRect.width, 1f), new Color(1f, 1f, 1f, 0.12f));
            for (int i = 0; i < sourceEvents.Count; i++)
            {
                Spine.Event sourceEvent = sourceEvents[i];
                float normalizedTime = Mathf.Clamp01(sourceEvent.Time / duration);
                float x = Mathf.Lerp(timelineRect.xMin, timelineRect.xMax, normalizedTime);
                EditorGUI.DrawRect(new Rect(x - MarkerWidth * 0.5f, laneTop, MarkerWidth, laneHeight), SourceEventColor);
                GUI.Label(new Rect(x + 4f, laneTop, 100f, 18f), sourceEvent.Data.Name, EditorStyles.miniLabel);
            }
        }

        /// <summary>
        /// Draws editable custom event markers in the lower lane.
        /// </summary>
        private static void DrawCustomEventMarkers(AnimationLine animationLine, Rect timelineRect, float duration)
        {
            if (duration <= 0f) return;
            float laneTop = timelineRect.yMin + 20f + (timelineRect.height - 20f) * 0.5f + 1f;
            float laneHeight = timelineRect.yMax - laneTop;
            for (int i = 0; i < animationLine.Events.Count; i++)
            {
                AnimationLineEvent marker = animationLine.Events[i];
                float normalizedTime = Mathf.Clamp01(marker.Time / duration);
                float x = Mathf.Lerp(timelineRect.xMin, timelineRect.xMax, normalizedTime);
                EditorGUI.DrawRect(new Rect(x - MarkerWidth * 0.5f, laneTop, MarkerWidth, laneHeight), CustomEventColor);
                GUI.Label(new Rect(x + 4f, laneTop, 100f, 18f), marker.EventName, EditorStyles.miniLabel);
            }
        }

        /// <summary>
        /// Reads every imported EventTimeline without changing the wrapped Spine animation.
        /// </summary>
        private static List<Spine.Event> CollectSourceEvents(AnimationLine animationLine)
        {
            List<Spine.Event> sourceEvents = new List<Spine.Event>();
            if (animationLine.AnimationReferenceAsset == null || animationLine.AnimationReferenceAsset.Animation == null) return sourceEvents;
            ExposedList<Timeline> timelines = animationLine.AnimationReferenceAsset.Animation.Timelines;
            for (int timelineIndex = 0; timelineIndex < timelines.Count; timelineIndex++)
            {
                EventTimeline eventTimeline = timelines.Items[timelineIndex] as EventTimeline;
                if (eventTimeline == null) continue;
                for (int eventIndex = 0; eventIndex < eventTimeline.Events.Length; eventIndex++)
                {
                    Spine.Event sourceEvent = eventTimeline.Events[eventIndex];
                    if (sourceEvent != null) sourceEvents.Add(sourceEvent);
                }
            }
            sourceEvents.Sort(CompareSourceEventsByTime);
            return sourceEvents;
        }

        /// <summary>
        /// Orders imported events for deterministic timeline and list rendering.
        /// </summary>
        private static int CompareSourceEventsByTime(Spine.Event left, Spine.Event right)
        {
            return left.Time.CompareTo(right.Time);
        }

        /// <summary>
        /// Draws the pending insertion position in yellow.
        /// </summary>
        private void DrawInsertionCursor(Rect timelineRect, float duration)
        {
            if (duration <= 0f) return;
            float normalizedTime = Mathf.Clamp01(insertionTime / duration);
            float x = Mathf.Lerp(timelineRect.xMin, timelineRect.xMax, normalizedTime);
            EditorGUI.DrawRect(new Rect(x - 1f, timelineRect.yMin, 2f, timelineRect.height), new Color(1f, 0.75f, 0.15f, 1f));
        }

        /// <summary>
        /// Captures timeline pointer input so clicking and holding the left mouse button continuously scrubs the preview.
        /// </summary>
        private void HandleTimelineInput(Rect timelineRect, float duration)
        {
            UnityEngine.Event currentEvent = UnityEngine.Event.current;
            if (duration <= 0f || currentEvent == null) return;
            EditorGUIUtility.AddCursorRect(timelineRect, MouseCursor.SlideArrow);
            int controlId = GUIUtility.GetControlID(TimelineControlHash, FocusType.Passive, timelineRect);
            UnityEngine.EventType controlEventType = currentEvent.GetTypeForControl(controlId);
            if (controlEventType == UnityEngine.EventType.MouseDown && currentEvent.button == 0 && timelineRect.Contains(currentEvent.mousePosition))
            {
                GUIUtility.hotControl = controlId;
                SeekTimelineAtPointer(timelineRect, duration, currentEvent.mousePosition);
                currentEvent.Use();
                return;
            }
            if (controlEventType == UnityEngine.EventType.MouseDrag && GUIUtility.hotControl == controlId)
            {
                SeekTimelineAtPointer(timelineRect, duration, currentEvent.mousePosition);
                currentEvent.Use();
                return;
            }
            if (controlEventType == UnityEngine.EventType.MouseUp && currentEvent.button == 0 && GUIUtility.hotControl == controlId)
            {
                SeekTimelineAtPointer(timelineRect, duration, currentEvent.mousePosition);
                GUIUtility.hotControl = 0;
                currentEvent.Use();
            }
        }

        /// <summary>
        /// Converts the current pointer X coordinate into a clamped shared preview and timeline time.
        /// </summary>
        private void SeekTimelineAtPointer(Rect timelineRect, float duration, Vector2 mousePosition)
        {
            float selectedTime = Mathf.Clamp01((mousePosition.x - timelineRect.xMin) / timelineRect.width) * duration;
            SeekPreview(selectedTime, true);
        }

        /// <summary>
        /// Draws the new marker payload and inserts it with Unity Undo support.
        /// </summary>
        private void DrawInsertionPanel(AnimationLine animationLine, float duration)
        {
            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Insert Event", EditorStyles.boldLabel);
            using (new EditorGUI.DisabledScope(duration <= 0f))
            {
                EditorGUI.BeginChangeCheck();
                float selectedTime = EditorGUILayout.Slider("Time (Seconds)", Mathf.Clamp(insertionTime, 0f, Mathf.Max(0f, duration)), 0f, Mathf.Max(0f, duration));
                if (EditorGUI.EndChangeCheck()) SeekPreview(selectedTime, true);
                insertionEventName = EditorGUILayout.TextField("Event Name", insertionEventName);
                insertionIntValue = EditorGUILayout.IntField("Int", insertionIntValue);
                insertionFloatValue = EditorGUILayout.FloatField("Float", insertionFloatValue);
                insertionStringValue = EditorGUILayout.TextField("String", insertionStringValue);
                using (new EditorGUI.DisabledScope(string.IsNullOrWhiteSpace(insertionEventName)))
                {
                    if (GUILayout.Button("Insert Event At Time")) InsertEvent(animationLine);
                }
            }
        }

        /// <summary>
        /// Writes the pending marker into the asset and refreshes the serialized list.
        /// </summary>
        private void InsertEvent(AnimationLine animationLine)
        {
            Undo.RecordObject(animationLine, "Insert Animation Line Event");
            animationLine.InsertEvent(insertionTime, insertionEventName, insertionIntValue, insertionFloatValue, insertionStringValue);
            EditorUtility.SetDirty(animationLine);
            serializedObject.Update();
        }

        /// <summary>
        /// Draws the imported Spine events as a read-only foldout with time and payload details.
        /// </summary>
        private void DrawSourceEventList(List<Spine.Event> sourceEvents, float duration)
        {
            EditorGUILayout.Space();
            showSourceEvents = EditorGUILayout.Foldout(showSourceEvents, $"Spine Events ({sourceEvents.Count})", true);
            if (!showSourceEvents) return;
            if (sourceEvents.Count == 0)
            {
                EditorGUILayout.HelpBox("The selected Spine animation does not contain imported events.", MessageType.None);
                return;
            }
            using (new EditorGUI.DisabledScope(true))
            {
                for (int i = 0; i < sourceEvents.Count; i++) DrawSourceEvent(sourceEvents[i], i, duration);
            }
        }

        /// <summary>
        /// Draws one imported Spine event and its immutable payload.
        /// </summary>
        private static void DrawSourceEvent(Spine.Event sourceEvent, int index, float duration)
        {
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUILayout.LabelField($"{index}: {sourceEvent.Data.Name}", EditorStyles.boldLabel);
            EditorGUILayout.FloatField("Time (Seconds)", sourceEvent.Time);
            EditorGUILayout.FloatField("Normalized Time", duration <= 0f ? 0f : Mathf.Clamp01(sourceEvent.Time / duration));
            EditorGUILayout.IntField("Int", sourceEvent.Int);
            EditorGUILayout.FloatField("Float", sourceEvent.Float);
            EditorGUILayout.TextField("String", sourceEvent.String ?? string.Empty);
            EditorGUILayout.EndVertical();
        }

        /// <summary>
        /// Draws all marker fields and supports deleting individual entries.
        /// </summary>
        private void DrawEventList(AnimationLine animationLine, float duration)
        {
            EditorGUILayout.Space();
            EditorGUILayout.LabelField($"Events ({eventsProperty.arraySize})", EditorStyles.boldLabel);
            int removeIndex = -1;
            for (int i = 0; i < eventsProperty.arraySize; i++)
            {
                SerializedProperty markerProperty = eventsProperty.GetArrayElementAtIndex(i);
                SerializedProperty timeProperty = markerProperty.FindPropertyRelative("time");
                SerializedProperty eventNameProperty = markerProperty.FindPropertyRelative("eventName");
                SerializedProperty intValueProperty = markerProperty.FindPropertyRelative("intValue");
                SerializedProperty floatValueProperty = markerProperty.FindPropertyRelative("floatValue");
                SerializedProperty stringValueProperty = markerProperty.FindPropertyRelative("stringValue");
                EditorGUILayout.BeginVertical(EditorStyles.helpBox);
                EditorGUILayout.BeginHorizontal();
                EditorGUILayout.LabelField($"{i}: {eventNameProperty.stringValue}", EditorStyles.boldLabel);
                if (GUILayout.Button("Remove", GUILayout.Width(72f))) removeIndex = i;
                EditorGUILayout.EndHorizontal();
                timeProperty.floatValue = EditorGUILayout.Slider("Time (Seconds)", timeProperty.floatValue, 0f, Mathf.Max(0f, duration));
                EditorGUILayout.PropertyField(eventNameProperty, new GUIContent("Event Name"));
                EditorGUILayout.PropertyField(intValueProperty, new GUIContent("Int"));
                EditorGUILayout.PropertyField(floatValueProperty, new GUIContent("Float"));
                EditorGUILayout.PropertyField(stringValueProperty, new GUIContent("String"));
                float normalizedTime = duration <= 0f ? 0f : Mathf.Clamp01(timeProperty.floatValue / duration);
                using (new EditorGUI.DisabledScope(true)) EditorGUILayout.FloatField("Normalized Time", normalizedTime);
                EditorGUILayout.EndVertical();
            }
            if (removeIndex >= 0) RemoveEvent(animationLine, removeIndex);
        }

        /// <summary>
        /// Removes a marker through the asset API so ordering and the runtime cache remain valid.
        /// </summary>
        private void RemoveEvent(AnimationLine animationLine, int index)
        {
            serializedObject.ApplyModifiedProperties();
            Undo.RecordObject(animationLine, "Remove Animation Line Event");
            animationLine.RemoveEventAt(index);
            EditorUtility.SetDirty(animationLine);
            serializedObject.Update();
        }
    }
}
#endif
