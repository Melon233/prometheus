using UnityEngine;
using UnityEditor;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Reflection;

namespace EasyAudioCutter
{
    public class EasyAudioCutter : EditorWindow
    {
        private enum Tab { Trim, Merge, AdjustVolume }

        [SerializeField]
        private Tab currentTab = Tab.Trim;

        [SerializeField]
        private AudioClip sourceClip;
        [SerializeField]
        private float trimStart = 0f;
        [SerializeField]
        private float trimEnd = 1f;
        [SerializeField]
        private float fadeInDuration = 0f;
        [SerializeField]
        private float fadeOutDuration = 0f;
        [SerializeField]
        private AnimationCurve fadeInCurve = AnimationCurve.Linear(0, 0, 1, 1);
        [SerializeField]
        private AnimationCurve fadeOutCurve = AnimationCurve.Linear(0, 1, 1, 0);
        [SerializeField]
        private bool reverseAudio = false;

        [SerializeField]
        private List<AudioClip> mergeClips = new List<AudioClip>();

        [SerializeField]
        private AudioClip volumeClip;

        private AudioSource previewAudioSource;
        private AudioClip previewClip;

        private float[] waveformSamples;
        private const int waveformWidth = 400;
        private const float minWaveformHeight = 2f;

        [SerializeField]
        private bool loopPreview = false;
        private double previewStartTime = -1;

        [SerializeField]
        private float volumeIncrease = 0f;

        [SerializeField]
        private float previewVolume = 1f;

        private const string PREF_PREVIEW_VOLUME = "EasyAudioCutter_PreviewVolume";

        [MenuItem("Tools/Easy Audio Cutter")]
        public static void ShowWindow()
        {
            var window = GetWindow<EasyAudioCutter>("Easy Audio Cutter");
            window.minSize = new Vector2(400, 400);
            window.Show();
        }

        [MenuItem("Assets/Easy Audio Cutter", false, 20)]
        public static void EditSelectedAudioClip()
        {
            AudioClip selectedClip = Selection.activeObject as AudioClip;
            if (selectedClip != null)
            {
                OpenWithClip(selectedClip);
            }
        }

        [MenuItem("Assets/Easy Audio Cutter", true)]
        public static bool ValidateEditSelectedAudioClip()
        {
            return Selection.activeObject is AudioClip;
        }

        [MenuItem("CONTEXT/AudioClip/Easy Audio Cutter")]
        public static void EditContextAudioClip(MenuCommand command)
        {
            AudioClip clip = command.context as AudioClip;
            if (clip != null)
            {
                OpenWithClip(clip);
            }
        }

        public static void OpenWithClip(AudioClip clip)
        {
            var window = GetWindow<EasyAudioCutter>("Easy Audio Cutter");
            window.minSize = new Vector2(400, 400);
            window.Initialize(clip);
            window.Show();
            window.Focus();
        }

        public void Initialize(AudioClip clip)
        {
            sourceClip = clip;
            trimStart = 0f;
            trimEnd = sourceClip != null ? sourceClip.length : 1f;
            fadeInDuration = 0f;
            fadeOutDuration = 0f;
            reverseAudio = false;
            mergeClips.Clear();
            volumeClip = null;

            if (currentTab == Tab.Merge && mergeClips.Count == 0) mergeClips.Add(clip);
            if (currentTab == Tab.AdjustVolume) volumeClip = clip;

            previewVolume = EditorPrefs.GetFloat(PREF_PREVIEW_VOLUME, 1f);

            if (previewAudioSource != null)
            {
                previewAudioSource.Stop();
                DestroyPreviewClip();
                previewAudioSource.volume = previewVolume;
            }
            UpdateWaveform();
            Repaint();
        }

        private void OnEnable()
        {
            previewVolume = EditorPrefs.GetFloat(PREF_PREVIEW_VOLUME, 1f);

            GameObject go = new GameObject("AudioPreviewPlayer");
            go.hideFlags = HideFlags.HideAndDontSave;
            previewAudioSource = go.AddComponent<AudioSource>();
            previewAudioSource.playOnAwake = false;
            previewAudioSource.loop = false;
            previewAudioSource.volume = previewVolume;

            Undo.undoRedoPerformed += OnUndoRedo;
        }

        private void OnDisable()
        {
            if (previewAudioSource != null)
                DestroyImmediate(previewAudioSource.gameObject);
            DestroyPreviewClip();

            Undo.undoRedoPerformed -= OnUndoRedo;
        }

        private void OnUndoRedo()
        {
            if (currentTab == Tab.Trim)
            {
                UpdateWaveform();
            }
            Repaint();
        }

        private void OnGUI()
        {
            EasyAudioCutterTheme.EnsureStyles();

            if (previewAudioSource != null)
            {
                if (Mathf.Abs(previewAudioSource.volume - previewVolume) > 0.01f)
                {
                    previewAudioSource.volume = previewVolume;
                }

                if (previewAudioSource.isPlaying)
                {
                    Repaint();
                }
            }

            Color bgColor = EditorGUIUtility.isProSkin ? new Color(0.07f, 0.1f, 0.2f) : new Color(0.8f, 0.8f, 0.8f);
            EditorGUI.DrawRect(new Rect(0, 0, position.width, position.height), bgColor);

            EditorGUILayout.BeginHorizontal();
            GUILayout.FlexibleSpace();
            EditorGUILayout.EndHorizontal();
            EditorGUILayout.Space();

            if (GUILayout.Button("Help", EasyAudioCutterTheme.ButtonStyle, GUILayout.Width(100)))
            {
                DrawHelpDialog();
            }
            EditorGUILayout.Space(10);

            EditorGUI.BeginChangeCheck();
            Tab newTab = (Tab)GUILayout.Toolbar((int)currentTab, new string[] { "Trim", "Merge", "Adjust Volume" });
            if (EditorGUI.EndChangeCheck())
            {
                Undo.RecordObject(this, "Change Tab");
                currentTab = newTab;
            }

            EditorGUILayout.Space(5);

            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField("Preview Volume", EasyAudioCutterTheme.SliderLabelStyle, GUILayout.Width(100));
            EditorGUI.BeginChangeCheck();
            float newVol = EditorGUILayout.Slider(previewVolume, 0f, 1f);
            if (EditorGUI.EndChangeCheck())
            {
                Undo.RecordObject(this, "Change Preview Volume");
                previewVolume = newVol;
                EditorPrefs.SetFloat(PREF_PREVIEW_VOLUME, previewVolume);
                if (previewAudioSource != null) previewAudioSource.volume = previewVolume;
            }
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.Space();
            EasyAudioCutterTheme.DrawSeparator();
            EditorGUILayout.Space();

            switch (currentTab)
            {
                case Tab.Trim: DrawTrimTab(); break;
                case Tab.Merge: DrawMergeTab(); break;
                case Tab.AdjustVolume: DrawAdjustVolumeTab(); break;
            }
        }

        private void DrawHelpDialog()
        {
            string message = "Easy Audio Cutter - Help\n\n" +
                            "**Trim Tab**\n" +
                            "• Allows trimming a specific section with fade in/out effects.\n" +
                            "• Toggle 'Reverse Audio' to play the section backwards.\n" +
                            "• Click anywhere on the waveform to seek/jump to that time.\n" +
                            "• Supports adjustable start/end times and custom fade curves.\n" +
                            "• Real-time waveform visualization with a playhead tracker.\n" +
                            "• Tip: Use the curve editor for creative fade transitions.\n\n" +

                            "**Merge Tab**\n" +
                            "• Combines multiple audio files into a single clip.\n" +
                            "• Use '↑' and '↓' buttons to reorder clips in the list.\n" +
                            "• All audio files must have the same format (sample rate/channels).\n" +
                            "• Provides preview functionality for the merged sequence.\n" +
                            "• Tip: Verify formats beforehand to avoid merge errors.\n\n" +

                            "**Adjust Volume Tab**\n" +
                            "• Adjusts the volume of a single selected audio clip.\n" +
                            "• Use -1 to 1 range: negative decreases, positive increases volume.\n" +
                            "• Real-time preview of volume changes before saving.\n" +
                            "• Saves modified clip with '_Adjusted' suffix.\n" +
                            "• Tip: Avoid values close to -1 or 1 to prevent clipping.\n\n" +

                            "**General Information**\n" +
                            "• Shortcuts: Right-click any Audio Asset or use the '✂' button in the Inspector.\n" +
                            "• Supports Undo/Redo (Ctrl+Z) for all slider and toggle actions.\n" +
                            "• Global 'Preview Volume' slider allows adjusting playback level.\n" +
                            "• Saves all processed audio in WAV format.\n" +
                            "• Developed and updated as of July 05, 2025.\n";
            EditorUtility.DisplayDialog("Easy Audio Cutter - Help", message, "OK");
        }

        private void DrawTrimTab()
        {
            EditorGUILayout.LabelField("Trim AudioClip", EasyAudioCutterTheme.HeaderStyle);
            EditorGUILayout.Space();

            EditorGUI.BeginChangeCheck();
            AudioClip newSource = (AudioClip)EditorGUILayout.ObjectField("Source Clip", sourceClip, typeof(AudioClip), false);
            if (EditorGUI.EndChangeCheck())
            {
                Undo.RecordObject(this, "Change Source Clip");
                Initialize(newSource);
            }

            if (sourceClip == null)
            {
                EditorGUILayout.HelpBox("Please assign an AudioClip to trim.", MessageType.Info);
                return;
            }

            EditorGUILayout.LabelField($"Length: {sourceClip.length:F2} seconds", EasyAudioCutterTheme.LabelStyle);
            EditorGUILayout.Space();

            float oldTrimStart = trimStart;
            float oldTrimEnd = trimEnd;
            float oldFadeIn = fadeInDuration;
            float oldFadeOut = fadeOutDuration;
            bool oldReverse = reverseAudio;

            EditorGUI.BeginChangeCheck();
            float newTrimStart = EditorGUILayout.Slider("Start Time", trimStart, 0f, sourceClip.length);
            if (EditorGUI.EndChangeCheck())
            {
                Undo.RecordObject(this, "Change Trim Start");
                trimStart = newTrimStart;
            }

            EditorGUI.BeginChangeCheck();
            float newTrimEnd = EditorGUILayout.Slider("End Time", trimEnd, 0f, sourceClip.length);
            if (EditorGUI.EndChangeCheck())
            {
                Undo.RecordObject(this, "Change Trim End");
                trimEnd = newTrimEnd;
            }
            trimEnd = Mathf.Max(trimStart, trimEnd);

            EditorGUI.BeginChangeCheck();
            float newFadeIn = EditorGUILayout.Slider("Fade In Duration", fadeInDuration, 0f, trimEnd - trimStart);
            if (EditorGUI.EndChangeCheck())
            {
                Undo.RecordObject(this, "Change Fade In");
                fadeInDuration = newFadeIn;
            }

            EditorGUI.BeginChangeCheck();
            float newFadeOut = EditorGUILayout.Slider("Fade Out Duration", fadeOutDuration, 0f, trimEnd - trimStart);
            if (EditorGUI.EndChangeCheck())
            {
                Undo.RecordObject(this, "Change Fade Out");
                fadeOutDuration = newFadeOut;
            }

            EditorGUI.BeginChangeCheck();
            bool newReverse = EditorGUILayout.Toggle("Reverse Audio", reverseAudio);
            if (EditorGUI.EndChangeCheck())
            {
                Undo.RecordObject(this, "Toggle Reverse");
                reverseAudio = newReverse;
            }

            if (!Mathf.Approximately(oldTrimStart, trimStart) ||
                !Mathf.Approximately(oldTrimEnd, trimEnd) ||
                !Mathf.Approximately(oldFadeIn, fadeInDuration) ||
                !Mathf.Approximately(oldFadeOut, fadeOutDuration) ||
                oldReverse != reverseAudio)
            {
                UpdateWaveform();
            }

            EditorGUILayout.Space();

            Rect waveformRect = GUILayoutUtility.GetRect(position.width - 20, 100);
            waveformRect.x += 10;
            waveformRect.width -= 20;
            DrawWaveform(waveformRect);

            if (Event.current.type == EventType.MouseDown && waveformRect.Contains(Event.current.mousePosition))
            {
                if (previewAudioSource != null && previewClip != null)
                {
                    float clickPercent = (Event.current.mousePosition.x - waveformRect.x) / waveformRect.width;
                    clickPercent = Mathf.Clamp01(clickPercent);

                    if (!previewAudioSource.isPlaying)
                    {
                        CreatePreviewClipAndPlay();
                    }

                    previewAudioSource.time = clickPercent * previewClip.length;
                    Repaint();
                }
            }

            EditorGUILayout.Space();

            EditorGUI.BeginChangeCheck();
            bool newLoop = EditorGUILayout.Toggle("Loop Preview", loopPreview);
            if (EditorGUI.EndChangeCheck())
            {
                Undo.RecordObject(this, "Toggle Loop");
                loopPreview = newLoop;
            }

            if (GUILayout.Button(previewAudioSource.isPlaying ? "Stop Preview" : "Play Preview", EasyAudioCutterTheme.ButtonStyle))
            {
                if (previewAudioSource.isPlaying)
                {
                    previewAudioSource.Stop();
                    DestroyPreviewClip();
                }
                else
                {
                    CreatePreviewClipAndPlay();
                }
            }

            EditorGUILayout.Space();
            if (GUILayout.Button("Process and Save Trimmed Clip", EasyAudioCutterTheme.ButtonStyle))
            {
                ProcessAndSaveTrimmedClip();
            }
        }

        private void DrawMergeTab()
        {
            EditorGUILayout.LabelField("Merge AudioClips", EasyAudioCutterTheme.HeaderStyle);
            EditorGUILayout.Space();

            for (int i = 0; i < mergeClips.Count; i++)
            {
                EditorGUILayout.BeginHorizontal();

                if (GUILayout.Button("↑", GUILayout.Width(20)))
                {
                    if (i > 0)
                    {
                        Undo.RecordObject(this, "Move Clip Up");
                        var temp = mergeClips[i];
                        mergeClips[i] = mergeClips[i - 1];
                        mergeClips[i - 1] = temp;
                    }
                }
                if (GUILayout.Button("↓", GUILayout.Width(20)))
                {
                    if (i < mergeClips.Count - 1)
                    {
                        Undo.RecordObject(this, "Move Clip Down");
                        var temp = mergeClips[i];
                        mergeClips[i] = mergeClips[i + 1];
                        mergeClips[i + 1] = temp;
                    }
                }

                EditorGUI.BeginChangeCheck();
                AudioClip newClip = (AudioClip)EditorGUILayout.ObjectField(mergeClips[i], typeof(AudioClip), false);
                if (EditorGUI.EndChangeCheck())
                {
                    Undo.RecordObject(this, "Change Merge Clip");
                    mergeClips[i] = newClip;
                }

                if (GUILayout.Button("X", EasyAudioCutterTheme.ButtonStyle, GUILayout.Width(20)))
                {
                    Undo.RecordObject(this, "Remove Merge Clip");
                    mergeClips.RemoveAt(i);
                    i--;
                }
                EditorGUILayout.EndHorizontal();
            }

            if (GUILayout.Button("+ Add AudioClip", EasyAudioCutterTheme.ButtonStyle))
            {
                Undo.RecordObject(this, "Add Merge Clip");
                mergeClips.Add(null);
            }

            EditorGUILayout.Space();

            if (mergeClips.Count >= 2)
            {
                if (GUILayout.Button(previewAudioSource.isPlaying ? "Stop Preview" : "Play Merged Preview", EasyAudioCutterTheme.ButtonStyle))
                {
                    if (previewAudioSource.isPlaying)
                    {
                        previewAudioSource.Stop();
                        DestroyPreviewClip();
                    }
                    else
                    {
                        CreateMergedPreviewClipAndPlay();
                    }
                }

                EditorGUILayout.Space();

                if (GUILayout.Button("Process and Save Merged Clip", EasyAudioCutterTheme.ButtonStyle))
                {
                    ProcessAndSaveMergedClip();
                }
            }
            else
            {
                EditorGUILayout.HelpBox("Add at least two AudioClips to merge.", MessageType.Info);
            }
        }

        private void DrawAdjustVolumeTab()
        {
            EditorGUILayout.LabelField("Adjust Volume", EasyAudioCutterTheme.HeaderStyle);
            EditorGUILayout.Space();

            EditorGUI.BeginChangeCheck();
            AudioClip newVolumeClip = (AudioClip)EditorGUILayout.ObjectField("Audio Clip", volumeClip, typeof(AudioClip), false);
            if (EditorGUI.EndChangeCheck())
            {
                Undo.RecordObject(this, "Change Volume Clip");
                volumeClip = newVolumeClip;
            }

            EditorGUILayout.Space();

            EditorGUI.BeginChangeCheck();
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField("Volume Adjustment", EasyAudioCutterTheme.SliderLabelStyle);
            float newVolume = EditorGUILayout.Slider(volumeIncrease, -1f, 1f);
            EditorGUILayout.EndHorizontal();
            if (EditorGUI.EndChangeCheck())
            {
                Undo.RecordObject(this, "Change Volume Amount");
                volumeIncrease = newVolume;
            }

            EditorGUILayout.LabelField("Note: -1 to 1 range.", EditorStyles.helpBox);

            if (volumeClip != null)
            {
                if (GUILayout.Button("Apply Volume Adjustment (Preview)", EasyAudioCutterTheme.ButtonStyle))
                {
                    ApplyVolumeIncrease();
                }

                EditorGUILayout.Space();

                if (GUILayout.Button("Process and Save Adjusted Clip", EasyAudioCutterTheme.ButtonStyle))
                {
                    ProcessAndSaveAdjustedClips();
                }
            }
            else
            {
                EditorGUILayout.HelpBox("Add an AudioClip to adjust volume.", MessageType.Info);
            }
        }

        private void DestroyPreviewClip()
        {
            if (previewClip != null)
            {
                DestroyImmediate(previewClip);
                previewClip = null;
                previewStartTime = -1;
            }
        }

        private void CreatePreviewClipAndPlay()
        {
            if (sourceClip == null || trimEnd <= trimStart) return;

            previewStartTime = EditorApplication.timeSinceStartup;

            int freq = sourceClip.frequency;
            int channels = sourceClip.channels;

            int startSample = Mathf.FloorToInt(trimStart * freq) * channels;
            int sampleLength = Mathf.FloorToInt((trimEnd - trimStart) * freq) * channels;

            float[] allData = new float[sourceClip.samples * channels];
            sourceClip.GetData(allData, 0);

            float[] trimmedData = new float[sampleLength];
            System.Array.Copy(allData, startSample, trimmedData, 0, sampleLength);

            if (reverseAudio)
            {
                System.Array.Reverse(trimmedData);
            }

            ApplyFades(trimmedData, freq, channels);

            previewClip = AudioClip.Create("PreviewTrim", sampleLength / channels, channels, freq, false);
            previewClip.SetData(trimmedData, 0);
            previewAudioSource.clip = previewClip;
            previewAudioSource.volume = previewVolume;
            previewAudioSource.loop = loopPreview;
            previewAudioSource.Play();
        }

        private void ProcessAndSaveTrimmedClip()
        {
            if (sourceClip == null) return;

            int freq = sourceClip.frequency;
            int channels = sourceClip.channels;

            int startSample = Mathf.FloorToInt(trimStart * freq) * channels;
            int sampleLength = Mathf.FloorToInt((trimEnd - trimStart) * freq) * channels;

            float[] srcData = new float[sourceClip.samples * channels];
            sourceClip.GetData(srcData, 0);

            float[] trimmedData = new float[sampleLength];
            System.Array.Copy(srcData, startSample, trimmedData, 0, sampleLength);

            if (reverseAudio)
            {
                System.Array.Reverse(trimmedData);
            }

            ApplyFades(trimmedData, freq, channels);

            SaveWav(trimmedData, freq, channels, "Trimmed_" + sourceClip.name + (reverseAudio ? "_Reverse" : ""));
        }

        private void ApplyFades(float[] data, int freq, int channels)
        {
            int fadeInSamples = Mathf.FloorToInt(fadeInDuration * freq) * channels;
            for (int i = 0; i < fadeInSamples && i < data.Length; i++)
            {
                float t = (float)i / fadeInSamples;
                data[i] *= fadeInCurve.Evaluate(t);
            }

            int fadeOutSamples = Mathf.FloorToInt(fadeOutDuration * freq) * channels;
            int startFadeOut = data.Length - fadeOutSamples;
            for (int i = 0; i < fadeOutSamples && (startFadeOut + i) < data.Length; i++)
            {
                float t = (float)i / fadeOutSamples;
                data[startFadeOut + i] *= fadeOutCurve.Evaluate(t);
            }
        }

        private void CreateMergedPreviewClipAndPlay()
        {
            if (mergeClips.Count < 2) return;

            int freq = mergeClips[0].frequency;
            int channels = mergeClips[0].channels;

            List<float> mergedSamples = new List<float>();

            foreach (var clip in mergeClips)
            {
                if (clip == null || clip.frequency != freq || clip.channels != channels)
                {
                    EditorUtility.DisplayDialog("Error", "All clips must have same format.", "OK");
                    return;
                }

                float[] data = new float[clip.samples * channels];
                clip.GetData(data, 0);
                mergedSamples.AddRange(data);
            }

            float[] mergedData = mergedSamples.ToArray();
            int samples = mergedData.Length / channels;

            DestroyPreviewClip();
            previewClip = AudioClip.Create("PreviewMerged", samples, channels, freq, false);
            previewClip.SetData(mergedData, 0);
            previewAudioSource.clip = previewClip;
            previewAudioSource.volume = previewVolume;
            previewAudioSource.loop = loopPreview;
            previewAudioSource.Play();
        }

        private void ProcessAndSaveMergedClip()
        {
            if (mergeClips.Count < 2) return;

            int freq = mergeClips[0].frequency;
            int channels = mergeClips[0].channels;

            List<float> mergedSamples = new List<float>();

            foreach (var clip in mergeClips)
            {
                if (clip == null || clip.frequency != freq || clip.channels != channels)
                {
                    EditorUtility.DisplayDialog("Error", "All clips must have same format.", "OK");
                    return;
                }

                float[] data = new float[clip.samples * channels];
                clip.GetData(data, 0);
                mergedSamples.AddRange(data);
            }

            SaveWav(mergedSamples.ToArray(), freq, channels, "MergedAudio");
        }

        private void ApplyVolumeIncrease()
        {
            if (previewAudioSource.isPlaying)
            {
                previewAudioSource.Stop();
                DestroyPreviewClip();
            }

            if (volumeClip != null)
            {
                float[] data = new float[volumeClip.samples * volumeClip.channels];
                volumeClip.GetData(data, 0);

                for (int i = 0; i < data.Length; i++)
                {
                    data[i] += data[i] * volumeIncrease;
                    if (data[i] > 1f) data[i] = 1f;
                    if (data[i] < -1f) data[i] = -1f;
                }

                int samples = data.Length / volumeClip.channels;
                AudioClip tempClip = AudioClip.Create(volumeClip.name + "_Temp", samples, volumeClip.channels, volumeClip.frequency, false);
                tempClip.SetData(data, 0);
                previewAudioSource.clip = tempClip;
                previewAudioSource.volume = previewVolume;
                previewAudioSource.loop = false;
                previewAudioSource.Play();
            }
        }

        private void ProcessAndSaveAdjustedClips()
        {
            if (volumeClip != null)
            {
                float[] data = new float[volumeClip.samples * volumeClip.channels];
                volumeClip.GetData(data, 0);

                for (int i = 0; i < data.Length; i++)
                {
                    data[i] += data[i] * volumeIncrease;
                    if (data[i] > 1f) data[i] = 1f;
                    if (data[i] < -1f) data[i] = -1f;
                }

                SaveWav(data, volumeClip.frequency, volumeClip.channels, volumeClip.name + "_Adjusted");
                EditorUtility.DisplayDialog("Done", "Adjusted clip saved!", "OK");
            }
        }

        private void SaveWav(float[] data, int frequency, int channels, string defaultName)
        {
            int samples = data.Length / channels;
            AudioClip clip = AudioClip.Create(defaultName, samples, channels, frequency, false);
            clip.SetData(data, 0);

            string path = EditorUtility.SaveFilePanelInProject("Save Audio", defaultName + ".wav", "wav", "Choose location");

            if (!string.IsNullOrEmpty(path))
            {
                byte[] bytes = WavUtility.FromAudioClip(clip);
                File.WriteAllBytes(path, bytes);
                AssetDatabase.ImportAsset(path);
                EditorUtility.DisplayDialog("Done", "Audio saved!", "OK");
            }
        }

        private void UpdateWaveform()
        {
            if (sourceClip == null)
            {
                waveformSamples = null;
                return;
            }

            int freq = sourceClip.frequency;
            int channels = sourceClip.channels;

            int startSample = Mathf.FloorToInt(trimStart * freq) * channels;
            int lengthSamples = Mathf.FloorToInt((trimEnd - trimStart) * freq) * channels;

            float[] allData = new float[sourceClip.samples * channels];
            sourceClip.GetData(allData, 0);

            float[] trimmedData = new float[lengthSamples];
            System.Array.Copy(allData, startSample, trimmedData, 0, lengthSamples);

            if (reverseAudio)
            {
                System.Array.Reverse(trimmedData);
            }

            ApplyFades(trimmedData, freq, channels);

            int samplesPerPixel = Mathf.Max(1, trimmedData.Length / waveformWidth);
            waveformSamples = new float[waveformWidth];
            for (int i = 0; i < waveformWidth; i++)
            {
                float max = 0f;
                int start = i * samplesPerPixel;
                int end = Mathf.Min(start + samplesPerPixel, trimmedData.Length);
                for (int j = start; j < end; j += channels)
                {
                    float val = Mathf.Abs(trimmedData[j]);
                    if (val > max) max = val;
                }
                waveformSamples[i] = max;
            }

            Repaint();
        }

        private void DrawWaveform(Rect rect)
        {
            if (waveformSamples == null || waveformSamples.Length == 0) return;

            Color waveBgColor = EditorGUIUtility.isProSkin ? new Color(0.05f, 0.08f, 0.15f) : new Color(0.9f, 0.9f, 0.9f);
            EditorGUI.DrawRect(rect, waveBgColor);
            float midY = rect.y + rect.height / 2f;

            Handles.BeginGUI();
            Handles.color = new Color(1f, 0.6f, 0f);

            float totalWaveformWidth = waveformSamples.Length;
            float xOffset = (rect.width - totalWaveformWidth) / 2f;

            for (int i = 0; i < waveformSamples.Length; i++)
            {
                float x = rect.x + xOffset + i;
                float height = Mathf.Max(minWaveformHeight, waveformSamples[i] * rect.height);
                Handles.DrawLine(
                    new Vector3(x, midY - height / 2),
                    new Vector3(x, midY + height / 2)
                );
            }

            float startX = rect.x + xOffset;
            float endX = rect.x + xOffset + waveformSamples.Length;

            Handles.color = EditorGUIUtility.isProSkin ? Color.white : Color.black;
            Handles.DrawLine(new Vector3(startX, rect.y), new Vector3(startX, rect.y + rect.height));
            Handles.DrawLine(new Vector3(endX, rect.y), new Vector3(endX, rect.y + rect.height));

            if (previewAudioSource != null && previewAudioSource.isPlaying && previewClip != null)
            {
                float progress = previewAudioSource.time / previewClip.length;
                float playheadX = rect.x + (progress * rect.width);

                Handles.color = Color.red;
                Handles.DrawLine(new Vector3(playheadX, rect.y), new Vector3(playheadX, rect.y + rect.height));
            }

            Handles.EndGUI();
        }
    }

    [CustomEditor(typeof(AudioClip))]
    public class AudioClipInspectorOverride : Editor
    {
        private Editor _defaultEditor;

        private void OnEnable()
        {
            var type = typeof(Editor).Assembly.GetType("UnityEditor.AudioClipInspector");
            if (type != null)
                _defaultEditor = CreateEditor(target, type);
        }

        private void OnDisable()
        {
            if (_defaultEditor != null) DestroyImmediate(_defaultEditor);
        }

        public override void OnInspectorGUI()
        {
            if (_defaultEditor != null)
            {
                _defaultEditor.OnInspectorGUI();
            }
            else
            {
                base.OnInspectorGUI();
            }
        }

        public override bool HasPreviewGUI()
        {
            return _defaultEditor != null && _defaultEditor.HasPreviewGUI();
        }

        public override void OnPreviewGUI(Rect r, GUIStyle background)
        {
            if (_defaultEditor != null) _defaultEditor.OnPreviewGUI(r, background);
        }

        public override void OnInteractivePreviewGUI(Rect r, GUIStyle background)
        {
            if (_defaultEditor != null) _defaultEditor.OnInteractivePreviewGUI(r, background);
        }

        public override void OnPreviewSettings()
        {
            if (_defaultEditor != null)
            {
                _defaultEditor.OnPreviewSettings();
            }

            if (GUILayout.Button(new GUIContent("✂", "Open in Easy Audio Cutter"), EditorStyles.toolbarButton, GUILayout.Width(30)))
            {
                EasyAudioCutter.OpenWithClip((AudioClip)target);
            }
        }
    }

    public static class EasyAudioCutterTheme
    {
        public static GUIStyle HeaderStyle { get; private set; }
        public static GUIStyle ButtonStyle { get; private set; }
        public static GUIStyle SliderLabelStyle { get; private set; }
        public static GUIStyle LabelStyle { get; private set; }

        private static bool isInitialized = false;
        private static bool wasProSkin = false;

        public static void EnsureStyles()
        {
            if (!isInitialized || wasProSkin != EditorGUIUtility.isProSkin)
            {
                SetupStyles();
            }
        }

        public static void DrawSeparator()
        {
            var rect = GUILayoutUtility.GetRect(1f, 1f);
            EditorGUI.DrawRect(rect, new Color(0.5f, 0.5f, 0.5f, 0.5f));
        }

        private static void SetupStyles()
        {
            wasProSkin = EditorGUIUtility.isProSkin;
            bool isDark = wasProSkin;

            HeaderStyle = new GUIStyle(EditorStyles.boldLabel);
            HeaderStyle.normal.textColor = isDark ? new Color(0.4f, 0.8f, 1f) : new Color(0.1f, 0.3f, 0.5f);
            HeaderStyle.fontSize = 14;

            ButtonStyle = new GUIStyle(GUI.skin.button);
            ButtonStyle.normal.textColor = isDark ? Color.white : Color.black;
            ButtonStyle.fontSize = 12;
            ButtonStyle.padding = new RectOffset(6, 6, 4, 4);

            SliderLabelStyle = new GUIStyle(EditorStyles.label);
            SliderLabelStyle.normal.textColor = isDark ? Color.cyan : new Color(0.0f, 0.4f, 0.7f);

            LabelStyle = new GUIStyle(EditorStyles.label);
            LabelStyle.normal.textColor = isDark ? Color.white : Color.black;

            isInitialized = true;
        }
    }

    public static class WavUtility
    {
        public static byte[] FromAudioClip(AudioClip clip)
        {
            using (var memoryStream = new MemoryStream())
            {
                using (var writer = new BinaryWriter(memoryStream))
                {
                    var hz = clip.frequency;
                    var channels = clip.channels;
                    var samples = clip.samples;
                    writer.Write(Encoding.ASCII.GetBytes("RIFF"));
                    writer.Write(36 + samples * channels * 2);
                    writer.Write(Encoding.ASCII.GetBytes("WAVE"));
                    writer.Write(Encoding.ASCII.GetBytes("fmt "));
                    writer.Write(16);
                    writer.Write((ushort)1);
                    writer.Write((ushort)channels);
                    writer.Write(hz);
                    writer.Write(hz * channels * 2);
                    writer.Write((ushort)(channels * 2));
                    writer.Write((ushort)16);
                    writer.Write(Encoding.ASCII.GetBytes("data"));
                    writer.Write(samples * channels * 2);

                    float[] data = new float[samples * channels];
                    clip.GetData(data, 0);

                    foreach (var sample in data)
                    {
                        writer.Write((short)(sample * 32767f));
                    }
                }
                return memoryStream.ToArray();
            }
        }
    }
}