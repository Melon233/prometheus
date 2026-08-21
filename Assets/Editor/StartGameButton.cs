using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEditor.Toolbars;
using UnityEngine;
using UnityEngine.UIElements;

/// <summary>
/// Adds a main-toolbar button that starts Play Mode from the project Entry scene without replacing the scene currently open for editing.
/// </summary>
[InitializeOnLoad]
public static class StartGameButton
{
    /// <summary>Identifies the toolbar element inside Unity's main-toolbar customization system.</summary>
    private const string ToolbarElementPath = "Prometheus/Start Game";

    /// <summary>Points to the scene that initializes YooAsset and loads the gameplay scene.</summary>
    private const string EntryScenePath = "Assets/Resources/Entry.unity";

    /// <summary>Tracks whether a temporary Play Mode start-scene override must be restored after a domain reload.</summary>
    private const string PendingRestoreKey = "Prometheus.StartGame.PendingRestore";

    /// <summary>Stores the previous Play Mode start-scene path across the domain reload performed when Play Mode starts.</summary>
    private const string PreviousStartScenePathKey = "Prometheus.StartGame.PreviousStartScenePath";

    /// <summary>Prevents later domain reloads from overriding a user's explicit decision to hide the button.</summary>
    private const string ToolbarVisibilityInitializedKey = "Prometheus.StartGame.ToolbarVisibilityInitialized";

    /// <summary>Limits toolbar discovery retries while Unity is still constructing its main window.</summary>
    private const int ToolbarVisibilityRetryLimit = 120;

    /// <summary>Counts editor updates spent waiting for Unity to construct the registered toolbar Overlay.</summary>
    private static int toolbarVisibilityRetryCount;

    /// <summary>Returns the normal gray measured from Unity's Play controls for the active editor theme.</summary>
    private static Color ToolbarNormalColor => EditorGUIUtility.isProSkin ? new Color32(80, 80, 80, 255) : new Color32(194, 194, 194, 255);

    /// <summary>Returns the slightly lighter gray used while the pointer hovers over Start Game.</summary>
    private static Color ToolbarHoverColor => EditorGUIUtility.isProSkin ? new Color32(94, 94, 94, 255) : new Color32(210, 210, 210, 255);

    /// <summary>Returns the darker gray used while Start Game is pressed.</summary>
    private static Color ToolbarPressedColor => EditorGUIUtility.isProSkin ? new Color32(64, 64, 64, 255) : new Color32(174, 174, 174, 255);

    /// <summary>
    /// Restores the temporary start-scene override even when entering Play Mode reloads the editor domain.
    /// </summary>
    static StartGameButton()
    {
        if (SessionState.GetBool(PendingRestoreKey, false)) SubscribeToPlayModeStateChanges();
        ScheduleToolbarInitialization();
    }

    /// <summary>
    /// Places the Start Game button beside Unity's central Play Mode controls.
    /// </summary>
    [MainToolbarElement(ToolbarElementPath, defaultDockPosition = MainToolbarDockPosition.Middle, defaultDockIndex = 1)]
    private static MainToolbarElement CreateStartGameButton()
    {
        var content = new MainToolbarContent("Start Game", "从 Entry 场景启动游戏，同时保留当前正在编辑的场景。");
        return new MainToolbarButton(content, StartGameFromEntry);
    }

    /// <summary>
    /// Begins a bounded search for the main toolbar because its window can be created after editor assemblies initialize.
    /// </summary>
    private static void ScheduleToolbarInitialization()
    {
        toolbarVisibilityRetryCount = 0;
        EditorApplication.update -= InitializeToolbarButton;
        EditorApplication.update += InitializeToolbarButton;
    }

    /// <summary>
    /// Expands the Start Game Overlay once for existing toolbar layouts that register new project elements as hidden.
    /// </summary>
    private static void InitializeToolbarButton()
    {
        EditorWindow toolbarWindow = Resources.FindObjectsOfTypeAll<EditorWindow>().FirstOrDefault(window => window.GetType().FullName == "UnityEditor.MainToolbarWindow");
        var startGameOverlay = toolbarWindow?.overlayCanvas.overlays.FirstOrDefault(overlay => overlay.id == ToolbarElementPath);
        if (startGameOverlay == null && ++toolbarVisibilityRetryCount < ToolbarVisibilityRetryLimit) return;
        EditorApplication.update -= InitializeToolbarButton;
        if (startGameOverlay == null)
        {
            Debug.LogWarning("[StartGameButton] Unity 主工具栏尚未创建，Start Game 可通过工具栏右键菜单手动显示。");
            return;
        }

        if (!EditorPrefs.GetBool(ToolbarVisibilityInitializedKey, false))
        {
            startGameOverlay.displayed = true;
            startGameOverlay.collapsed = false;
            EditorPrefs.SetBool(ToolbarVisibilityInitializedKey, true);
        }

        ApplyToolbarButtonStyle(startGameOverlay.rootVisualElement);
    }

    /// <summary>
    /// Applies the measured Play-control gray and matching pointer feedback to Unity's generated toolbar button element.
    /// </summary>
    private static void ApplyToolbarButtonStyle(VisualElement overlayRoot)
    {
        VisualElement toolbarButton = FindToolbarButton(overlayRoot);
        if (toolbarButton == null)
        {
            Debug.LogWarning("[StartGameButton] Unity 未生成 Start Game 的工具栏按钮元素。");
            return;
        }

        toolbarButton.style.backgroundColor = ToolbarNormalColor;
        toolbarButton.UnregisterCallback<PointerEnterEvent>(OnToolbarButtonPointerEnter);
        toolbarButton.UnregisterCallback<PointerLeaveEvent>(OnToolbarButtonPointerLeave);
        toolbarButton.UnregisterCallback<PointerDownEvent>(OnToolbarButtonPointerDown);
        toolbarButton.UnregisterCallback<PointerUpEvent>(OnToolbarButtonPointerUp);
        toolbarButton.RegisterCallback<PointerEnterEvent>(OnToolbarButtonPointerEnter);
        toolbarButton.RegisterCallback<PointerLeaveEvent>(OnToolbarButtonPointerLeave);
        toolbarButton.RegisterCallback<PointerDownEvent>(OnToolbarButtonPointerDown);
        toolbarButton.RegisterCallback<PointerUpEvent>(OnToolbarButtonPointerUp);
    }

    /// <summary>Applies the hover gray when the pointer enters Start Game.</summary>
    private static void OnToolbarButtonPointerEnter(PointerEnterEvent pointerEvent)
    {
        ((VisualElement)pointerEvent.currentTarget).style.backgroundColor = ToolbarHoverColor;
    }

    /// <summary>Restores the normal gray when the pointer leaves Start Game.</summary>
    private static void OnToolbarButtonPointerLeave(PointerLeaveEvent pointerEvent)
    {
        ((VisualElement)pointerEvent.currentTarget).style.backgroundColor = ToolbarNormalColor;
    }

    /// <summary>Applies the pressed gray when the pointer presses Start Game.</summary>
    private static void OnToolbarButtonPointerDown(PointerDownEvent pointerEvent)
    {
        ((VisualElement)pointerEvent.currentTarget).style.backgroundColor = ToolbarPressedColor;
    }

    /// <summary>Returns to the hover gray when the pointer releases Start Game.</summary>
    private static void OnToolbarButtonPointerUp(PointerUpEvent pointerEvent)
    {
        ((VisualElement)pointerEvent.currentTarget).style.backgroundColor = ToolbarHoverColor;
    }

    /// <summary>
    /// Finds Unity's generated toolbar button without relying on internal editor element types.
    /// </summary>
    private static VisualElement FindToolbarButton(VisualElement element)
    {
        if (element.ClassListContains("unity-toolbar-button")) return element;
        foreach (VisualElement child in element.Children())
        {
            VisualElement toolbarButton = FindToolbarButton(child);
            if (toolbarButton != null) return toolbarButton;
        }

        return null;
    }

    /// <summary>
    /// Temporarily assigns Entry as Unity's Play Mode start scene and starts the game.
    /// </summary>
    private static void StartGameFromEntry()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
        {
            Debug.LogWarning("[StartGameButton] Unity 已处于 Play Mode 或正在切换状态，未重复启动游戏。");
            return;
        }

        SceneAsset entryScene = AssetDatabase.LoadAssetAtPath<SceneAsset>(EntryScenePath);
        if (entryScene == null)
        {
            Debug.LogError($"[StartGameButton] 找不到启动场景：{EntryScenePath}");
            return;
        }

        string previousStartScenePath = AssetDatabase.GetAssetPath(EditorSceneManager.playModeStartScene);
        SessionState.SetString(PreviousStartScenePathKey, previousStartScenePath);
        SessionState.SetBool(PendingRestoreKey, true);
        SubscribeToPlayModeStateChanges();
        EditorSceneManager.playModeStartScene = entryScene;
        EditorApplication.isPlaying = true;
    }

    /// <summary>
    /// Ensures only one callback observes the Play Mode transition for this start request.
    /// </summary>
    private static void SubscribeToPlayModeStateChanges()
    {
        EditorApplication.playModeStateChanged -= OnPlayModeStateChanged;
        EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
    }

    /// <summary>
    /// Restores the previous start-scene override after Entry has been selected, or after Unity cancels the transition back in Edit Mode.
    /// </summary>
    private static void OnPlayModeStateChanged(PlayModeStateChange state)
    {
        if (state != PlayModeStateChange.EnteredPlayMode && state != PlayModeStateChange.EnteredEditMode) return;
        RestorePreviousStartScene();
    }

    /// <summary>
    /// Restores the user's previous Play Mode start scene and clears the domain-reload-safe request state.
    /// </summary>
    private static void RestorePreviousStartScene()
    {
        string previousStartScenePath = SessionState.GetString(PreviousStartScenePathKey, string.Empty);
        EditorSceneManager.playModeStartScene = string.IsNullOrEmpty(previousStartScenePath) ? null : AssetDatabase.LoadAssetAtPath<SceneAsset>(previousStartScenePath);
        SessionState.SetString(PreviousStartScenePathKey, string.Empty);
        SessionState.SetBool(PendingRestoreKey, false);
        EditorApplication.playModeStateChanged -= OnPlayModeStateChanged;
    }
}
