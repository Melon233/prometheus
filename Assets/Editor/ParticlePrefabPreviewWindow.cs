// Place this file in: Assets/Editor/ParticlePrefabPreviewWindow.cs
// Unity 2020.3+

using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Isolated preview window for particle-system prefabs.
/// The preview object lives only in PreviewRenderUtility's preview scene.
/// </summary>
public sealed class ParticlePrefabPreviewWindow : EditorWindow
{
    const float MinDistance = 0.05f;
    const float MaxDeltaTime = 1f / 20f;

    PreviewRenderUtility preview;
    GameObject prefab;
    GameObject instance;
    readonly List<ParticleSystem> rootParticleSystems = new List<ParticleSystem>();

    bool isPlaying = true;
    float previewTime;
    double lastUpdateTime;
    Vector2 orbit = new Vector2(25f, -35f);
    float distance = 3f;
    Vector3 target;
    Rect previewRect;

    [MenuItem("Prometheus/Effects/Particle Prefab Preview")]
    static void Open()
    {
        var window = GetWindow<ParticlePrefabPreviewWindow>();
        window.titleContent = new GUIContent("Particle Preview");
        window.minSize = new Vector2(360, 280);
        window.Show();
    }

    void OnEnable()
    {
        CreatePreviewUtility();
        lastUpdateTime = EditorApplication.timeSinceStartup;

        if (Selection.activeObject is GameObject selected)
            SetPrefab(selected);
    }

    void OnDisable()
    {
        DestroyPreviewInstance();

        if (preview != null)
        {
            preview.Cleanup();
            preview = null;
        }
    }

    void OnSelectionChange()
    {
        if (Selection.activeObject is GameObject selected)
        {
            SetPrefab(selected);
            Repaint();
        }
    }

    void Update()
    {
        var now = EditorApplication.timeSinceStartup;
        var deltaTime = Mathf.Clamp((float)(now - lastUpdateTime), 0f, MaxDeltaTime);
        lastUpdateTime = now;

        if (isPlaying && rootParticleSystems.Count > 0)
        {
            previewTime += deltaTime;
            foreach (var system in rootParticleSystems)
            {
                if (system != null)
                    system.Simulate(deltaTime, true, false, true);
            }
        }

        if (isPlaying)
            Repaint();
    }

    void OnGUI()
    {
        DrawToolbar();

        previewRect = GUILayoutUtility.GetRect(1, 100000, 1, 100000);
        EditorGUI.DrawRect(previewRect, new Color(0.12f, 0.12f, 0.12f));

        if (Event.current.type == EventType.Repaint && instance != null)
            DrawPreview(previewRect);
        else if (instance == null)
            DrawEmptyState(previewRect);

        HandlePreviewInput(previewRect);
    }

    void DrawToolbar()
    {
        EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);

        EditorGUI.BeginChangeCheck();
        var selectedPrefab = (GameObject)EditorGUILayout.ObjectField(
            prefab, typeof(GameObject), false, GUILayout.MinWidth(180));
        if (EditorGUI.EndChangeCheck())
            SetPrefab(selectedPrefab);

        using (new EditorGUI.DisabledScope(instance == null))
        {
            if (GUILayout.Button(isPlaying ? "Pause" : "Play", EditorStyles.toolbarButton))
                isPlaying = !isPlaying;

            if (GUILayout.Button("Restart", EditorStyles.toolbarButton))
                RestartPreview();

            if (GUILayout.Button("Frame", EditorStyles.toolbarButton))
                FramePreview();
        }

        EditorGUILayout.EndHorizontal();
    }

    void SetPrefab(GameObject value)
    {
        if (prefab == value)
            return;

        prefab = value;
        DestroyPreviewInstance();

        if (prefab == null)
            return;

        instance = Instantiate(prefab);
        instance.hideFlags = HideFlags.HideAndDontSave;
        preview.AddSingleGO(instance);

        var allSystems = instance.GetComponentsInChildren<ParticleSystem>(true);
        foreach (var system in allSystems)
        {
            // Simulate only roots. Simulate(..., withChildren: true) handles descendants.
            if (system.transform.parent == null ||
                system.transform.parent.GetComponentInParent<ParticleSystem>() == null)
            {
                rootParticleSystems.Add(system);
            }
        }

        if (rootParticleSystems.Count == 0)
        {
            Debug.LogWarning("[Particle Preview] The selected prefab has no ParticleSystem.", prefab);
            DestroyPreviewInstance();
            return;
        }

        RestartPreview();
        FramePreview();
    }

    void RestartPreview()
    {
        previewTime = 0f;
        isPlaying = true;

        foreach (var system in rootParticleSystems)
        {
            if (system == null) continue;
            system.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            system.Simulate(0f, true, true, true);
            system.Play(true);
        }
    }

    void DrawPreview(Rect rect)
    {
        CreatePreviewUtility();
        ConfigureCamera();

        preview.BeginPreview(rect, GUIStyle.none);
        preview.camera.Render();
        var texture = preview.EndPreview();
        GUI.DrawTexture(rect, texture, ScaleMode.StretchToFill, false);
    }

    void ConfigureCamera()
    {
        var rotation = Quaternion.Euler(orbit.x, orbit.y, 0f);
        preview.camera.transform.position = target - rotation * Vector3.forward * distance;
        preview.camera.transform.rotation = rotation;
        preview.camera.nearClipPlane = 0.01f;
        preview.camera.farClipPlane = 1000f;
        preview.camera.fieldOfView = 30f;
        preview.camera.clearFlags = CameraClearFlags.SolidColor;
        preview.camera.backgroundColor = new Color(0.08f, 0.08f, 0.08f, 1f);
    }

    void FramePreview()
    {
        if (instance == null)
            return;

        var renderers = instance.GetComponentsInChildren<Renderer>(true);
        if (renderers.Length == 0)
        {
            target = instance.transform.position;
            distance = 3f;
            return;
        }

        var bounds = renderers[0].bounds;
        for (var i = 1; i < renderers.Length; i++)
            bounds.Encapsulate(renderers[i].bounds);

        target = bounds.center;
        var radius = Mathf.Max(bounds.extents.magnitude, 0.25f);
        distance = radius / Mathf.Tan(15f * Mathf.Deg2Rad) * 1.25f;
    }

    void HandlePreviewInput(Rect rect)
    {
        var currentEvent = Event.current;
        if (!rect.Contains(currentEvent.mousePosition))
            return;

        if (currentEvent.type == EventType.ScrollWheel)
        {
            distance = Mathf.Max(MinDistance, distance * (1f + currentEvent.delta.y * 0.08f));
            currentEvent.Use();
            Repaint();
        }
        else if (currentEvent.type == EventType.MouseDrag && currentEvent.button == 0)
        {
            orbit.y += currentEvent.delta.x;
            orbit.x = Mathf.Clamp(orbit.x - currentEvent.delta.y, -89f, 89f);
            currentEvent.Use();
            Repaint();
        }
    }

    void DrawEmptyState(Rect rect)
    {
        var message = prefab == null
            ? "Select or drag a ParticleSystem prefab here."
            : "The selected prefab does not contain a ParticleSystem.";

        GUI.Label(rect, message, new GUIStyle(EditorStyles.centeredGreyMiniLabel)
        {
            alignment = TextAnchor.MiddleCenter
        });
    }

    void CreatePreviewUtility()
    {
        if (preview != null)
            return;

        preview = new PreviewRenderUtility();
        preview.cameraFieldOfView = 30f;
        preview.ambientColor = new Color(0.55f, 0.55f, 0.55f);
        preview.lights[0].intensity = 1.2f;
        preview.lights[0].transform.rotation = Quaternion.Euler(35f, -35f, 0f);
        preview.lights[1].intensity = 0.7f;
    }

    void DestroyPreviewInstance()
    {
        rootParticleSystems.Clear();

        if (instance != null)
        {
            DestroyImmediate(instance);
            instance = null;
        }
    }
}
