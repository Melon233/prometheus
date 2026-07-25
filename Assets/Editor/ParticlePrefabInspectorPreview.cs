using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Replaces the built-in GameObject preview for particle-system prefabs while
/// delegating the rest of the Inspector to Unity's original GameObjectInspector.
/// </summary>
[CustomEditor(typeof(GameObject), true)]
[CanEditMultipleObjects]
public sealed class ParticlePrefabInspectorPreview : Editor
{
    const float MinDistance = 0.05f;
    const float InitialDistance = 1.5f;
    const float MaxDeltaTime = 1f / 20f;
    const float RepaintInterval = 1f / 30f;
    const float AxisLengthRelativeToView = 0.7f;

    readonly List<ParticleSystem> rootParticleSystems = new List<ParticleSystem>();

    Editor defaultInspector;
    PreviewRenderUtility preview;
    GameObject prefab;
    GameObject instance;
    bool isPlaying = true;
    double lastUpdateTime;
    double nextRepaintTime;
    Vector2 orbit = new Vector2(25f, -35f);
    float distance = InitialDistance;
    Vector3 targetPosition = Vector3.zero;

    void OnEnable()
    {
        var defaultInspectorType =
            typeof(Editor).Assembly.GetType("UnityEditor.GameObjectInspector");
        if (defaultInspectorType != null)
            defaultInspector = CreateEditor(targets, defaultInspectorType);

        lastUpdateTime = EditorApplication.timeSinceStartup;
        EditorApplication.update -= UpdatePreview;
        EditorApplication.update += UpdatePreview;
        SyncTarget();
    }

    void OnDisable()
    {
        EditorApplication.update -= UpdatePreview;
        DestroyPreviewInstance();

        if (preview != null)
        {
            preview.Cleanup();
            preview = null;
        }

        if (defaultInspector != null)
        {
            DestroyImmediate(defaultInspector);
            defaultInspector = null;
        }
    }

    public override void OnInspectorGUI()
    {
        if (defaultInspector != null)
            defaultInspector.OnInspectorGUI();
        else
            DrawDefaultInspector();
    }

    public override bool HasPreviewGUI()
    {
        return IsParticleTarget() ||
            (defaultInspector != null && defaultInspector.HasPreviewGUI());
    }

    public override GUIContent GetPreviewTitle()
    {
        if (IsParticleTarget())
            return new GUIContent(target != null ? target.name : "Preview");

        return defaultInspector != null
            ? defaultInspector.GetPreviewTitle()
            : base.GetPreviewTitle();
    }

    public override string GetInfoString()
    {
        if (IsParticleTarget())
            return "Drag to orbit • Scroll to zoom";

        return defaultInspector != null
            ? defaultInspector.GetInfoString()
            : base.GetInfoString();
    }

    public override bool RequiresConstantRepaint()
    {
        if (IsParticleTarget())
            return isPlaying;

        return defaultInspector != null &&
            defaultInspector.RequiresConstantRepaint();
    }

    public override void OnPreviewSettings()
    {
        SyncTarget();

        if (!IsParticleTarget())
        {
            if (defaultInspector != null)
                defaultInspector.OnPreviewSettings();
            return;
        }

        using (new EditorGUI.DisabledScope(instance == null))
        {
            if (GUILayout.Button(
                isPlaying ? "Pause" : "Play",
                EditorStyles.miniButton,
                GUILayout.Width(48f)))
            {
                isPlaying = !isPlaying;
                Repaint();
            }

            if (GUILayout.Button(
                "Restart",
                EditorStyles.miniButton,
                GUILayout.Width(52f)))
            {
                RestartPreview();
            }

            if (GUILayout.Button(
                "Origin",
                EditorStyles.miniButton,
                GUILayout.Width(44f)))
            {
                ResetViewToOrigin();
            }

            if (GUILayout.Button(
                "Frame",
                EditorStyles.miniButton,
                GUILayout.Width(42f)))
            {
                FramePreview();
                Repaint();
            }
        }
    }

    public override void OnPreviewGUI(Rect rect, GUIStyle background)
    {
        if (IsParticleTarget())
            DrawParticlePreview(rect, background);
        else if (defaultInspector != null)
            defaultInspector.OnPreviewGUI(rect, background);
    }

    public override void OnInteractivePreviewGUI(Rect rect, GUIStyle background)
    {
        if (IsParticleTarget())
            DrawParticlePreview(rect, background);
        else if (defaultInspector != null)
            defaultInspector.OnInteractivePreviewGUI(rect, background);
    }

    void SyncTarget()
    {
        var selectedPrefab = target as GameObject;
        if (prefab == selectedPrefab)
            return;

        SetPrefab(selectedPrefab);
    }

    void SetPrefab(GameObject value)
    {
        prefab = value;
        DestroyPreviewInstance();

        if (prefab == null ||
            prefab.GetComponentInChildren<ParticleSystem>(true) == null)
        {
            return;
        }

        CreatePreviewUtility();
        instance = Object.Instantiate(prefab);
        instance.hideFlags = HideFlags.HideAndDontSave;
        preview.AddSingleGO(instance);

        var allSystems = instance.GetComponentsInChildren<ParticleSystem>(true);
        foreach (var system in allSystems)
        {
            if (!HasParticleSystemAncestor(system.transform))
                rootParticleSystems.Add(system);
        }

        RestartPreview();
        ResetViewToOrigin();
    }

    static bool HasParticleSystemAncestor(Transform child)
    {
        for (var parent = child.parent; parent != null; parent = parent.parent)
        {
            if (parent.GetComponent<ParticleSystem>() != null)
                return true;
        }

        return false;
    }

    void UpdatePreview()
    {
        var now = EditorApplication.timeSinceStartup;
        var deltaTime = Mathf.Clamp(
            (float)(now - lastUpdateTime),
            0f,
            MaxDeltaTime);
        lastUpdateTime = now;

        if (!isPlaying || rootParticleSystems.Count == 0)
            return;

        foreach (var system in rootParticleSystems)
        {
            if (system != null)
                system.Simulate(deltaTime, true, false, true);
        }

        if (now >= nextRepaintTime)
        {
            nextRepaintTime = now + RepaintInterval;
            Repaint();
        }
    }

    void RestartPreview()
    {
        isPlaying = true;
        lastUpdateTime = EditorApplication.timeSinceStartup;

        foreach (var system in rootParticleSystems)
        {
            if (system == null)
                continue;

            system.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            system.Simulate(0f, true, true, true);
            system.Play(true);
        }

        Repaint();
    }

    void ResetViewToOrigin()
    {
        targetPosition = Vector3.zero;
        orbit = new Vector2(25f, -35f);
        distance = InitialDistance;
        Repaint();
    }

    void FramePreview()
    {
        if (instance == null)
            return;

        var renderers = instance.GetComponentsInChildren<Renderer>(true);
        if (renderers.Length == 0)
        {
            targetPosition = Vector3.zero;
            distance = InitialDistance;
            return;
        }

        var bounds = renderers[0].bounds;
        for (var i = 1; i < renderers.Length; i++)
            bounds.Encapsulate(renderers[i].bounds);

        targetPosition = bounds.center;
        var radius = Mathf.Max(bounds.extents.magnitude, 0.25f);
        distance = radius / Mathf.Tan(15f * Mathf.Deg2Rad) * 1.25f;
    }

    void DrawParticlePreview(Rect rect, GUIStyle background)
    {
        SyncTarget();

        if (instance == null)
        {
            GUI.Label(rect, "No ParticleSystem found.", EditorStyles.centeredGreyMiniLabel);
            return;
        }

        if (Event.current.type == EventType.Repaint)
        {
            CreatePreviewUtility();
            ConfigureCamera();

            preview.BeginPreview(rect, background ?? GUIStyle.none);
            preview.camera.Render();
            var texture = preview.EndPreview();
            GUI.DrawTexture(rect, texture, ScaleMode.StretchToFill, false);
            DrawXZAxes(rect);
        }

        HandlePreviewInput(rect);
    }

    void ConfigureCamera()
    {
        var rotation = Quaternion.Euler(orbit.x, orbit.y, 0f);
        preview.camera.transform.position =
            targetPosition - rotation * Vector3.forward * distance;
        preview.camera.transform.rotation = rotation;
        preview.camera.nearClipPlane = 0.01f;
        preview.camera.farClipPlane = 1000f;
        preview.camera.fieldOfView = 30f;
        preview.camera.clearFlags = CameraClearFlags.SolidColor;
        preview.camera.backgroundColor = new Color(0.08f, 0.08f, 0.08f, 1f);
    }

    void HandlePreviewInput(Rect rect)
    {
        var currentEvent = Event.current;
        if (!rect.Contains(currentEvent.mousePosition))
            return;

        if (currentEvent.type == EventType.ScrollWheel)
        {
            distance = Mathf.Max(
                MinDistance,
                distance * (1f + currentEvent.delta.y * 0.08f));
            currentEvent.Use();
            Repaint();
        }
        else if (currentEvent.type == EventType.MouseDrag &&
            currentEvent.button == 0)
        {
            orbit.y += currentEvent.delta.x;
            orbit.x = Mathf.Clamp(
                orbit.x - currentEvent.delta.y,
                -89f,
                89f);
            currentEvent.Use();
            Repaint();
        }
    }

    void DrawXZAxes(Rect rect)
    {
        var halfViewHeight = distance *
            Mathf.Tan(preview.camera.fieldOfView * 0.5f * Mathf.Deg2Rad);
        var axisLength = Mathf.Max(
            MinDistance,
            halfViewHeight * AxisLengthRelativeToView);
        var origin = WorldToPreviewPoint(Vector3.zero, rect);
        var xNegative = WorldToPreviewPoint(Vector3.left * axisLength, rect);
        var xPositive = WorldToPreviewPoint(Vector3.right * axisLength, rect);
        var zNegative = WorldToPreviewPoint(Vector3.back * axisLength, rect);
        var zPositive = WorldToPreviewPoint(Vector3.forward * axisLength, rect);

        var previousHandlesColor = Handles.color;
        Handles.BeginGUI();

        DrawAxisHalf(origin, xNegative, new Color(0.8f, 0.15f, 0.15f, 0.4f));
        DrawAxisHalf(origin, xPositive, new Color(1f, 0.2f, 0.2f, 0.95f));
        DrawAxisHalf(origin, zNegative, new Color(0.15f, 0.4f, 0.9f, 0.4f));
        DrawAxisHalf(origin, zPositive, new Color(0.2f, 0.55f, 1f, 0.95f));

        if (origin.z > 0f)
        {
            EditorGUI.DrawRect(
                new Rect(origin.x - 2f, origin.y - 2f, 4f, 4f),
                new Color(1f, 1f, 1f, 0.9f));
        }

        DrawAxisLabel(xPositive, "X", new Color(1f, 0.35f, 0.35f));
        DrawAxisLabel(zPositive, "Z", new Color(0.35f, 0.65f, 1f));

        Handles.color = previousHandlesColor;
        Handles.EndGUI();
    }

    Vector3 WorldToPreviewPoint(Vector3 worldPosition, Rect rect)
    {
        var viewportPoint = preview.camera.WorldToViewportPoint(worldPosition);
        return new Vector3(
            rect.x + viewportPoint.x * rect.width,
            rect.y + (1f - viewportPoint.y) * rect.height,
            viewportPoint.z);
    }

    static void DrawAxisHalf(Vector3 origin, Vector3 end, Color color)
    {
        if (origin.z <= 0f || end.z <= 0f)
            return;

        var origin2D = new Vector3(origin.x, origin.y, 0f);
        var end2D = new Vector3(end.x, end.y, 0f);
        Handles.color = new Color(0f, 0f, 0f, 0.75f);
        Handles.DrawAAPolyLine(5f, origin2D, end2D);
        Handles.color = color;
        Handles.DrawAAPolyLine(3f, origin2D, end2D);
    }

    static void DrawAxisLabel(Vector3 position, string text, Color color)
    {
        if (position.z <= 0f)
            return;

        var previousContentColor = GUI.contentColor;
        GUI.contentColor = color;
        GUI.Label(
            new Rect(position.x + 4f, position.y - 8f, 16f, 16f),
            text,
            EditorStyles.miniBoldLabel);
        GUI.contentColor = previousContentColor;
    }

    void CreatePreviewUtility()
    {
        if (preview != null)
            return;

        preview = new PreviewRenderUtility();
        preview.cameraFieldOfView = 30f;
        preview.ambientColor = new Color(0.55f, 0.55f, 0.55f);
        preview.lights[0].intensity = 1.2f;
        preview.lights[0].transform.rotation =
            Quaternion.Euler(35f, -35f, 0f);
        preview.lights[1].intensity = 0.7f;
    }

    void DestroyPreviewInstance()
    {
        rootParticleSystems.Clear();

        if (instance != null)
        {
            Object.DestroyImmediate(instance);
            instance = null;
        }
    }

    bool IsParticleTarget()
    {
        var gameObject = target as GameObject;
        return gameObject != null &&
            gameObject.GetComponentInChildren<ParticleSystem>(true) != null;
    }
}
