using UnityEditor;
using UnityEditor.Compilation;
using UnityEngine;
#if UNITY_6000_3_OR_NEWER
using UnityEditor.Toolbars;
#else
using System.Reflection;
using UnityEngine.UIElements;
#endif

/// <summary>
/// Provides a toolbar action that exits Play Mode, recompiles project scripts, and starts Play Mode again when compilation succeeds.
/// </summary>
[InitializeOnLoad]
public static class RestartButton
{
    const string PendingKey = "PlayModeRestart.Pending";
    const string CompileSeenKey = "PlayModeRestart.CompileSeen";
    const string CompileErrorKey = "PlayModeRestart.CompileError";
#if UNITY_6000_3_OR_NEWER
    const string ToolbarElementPath = "Prometheus/Restart Play Mode";
#else

    static bool buttonInstalled;
#endif

    static RestartButton()
    {
#if !UNITY_6000_3_OR_NEWER
        // Unity versions before 6.3 do not provide the supported MainToolbar registration API, so they retain the legacy visual-tree integration.
        EditorApplication.update += InstallToolbarButton;
#endif
        CompilationPipeline.assemblyCompilationFinished += OnAssemblyCompilationFinished;

        // 编译会导致 Domain Reload；重新加载后继续等待并启动游戏。
        if (SessionState.GetBool(PendingKey, false))
        {
            EditorApplication.update -= WaitForCompilation;
            EditorApplication.update += WaitForCompilation;
        }
    }

#if UNITY_6000_3_OR_NEWER
    /// <summary>
    /// Registers the restart action in the middle section through Unity 6.3's supported main-toolbar API.
    /// </summary>
    /// <returns>The button descriptor Unity uses to build and refresh the toolbar element.</returns>
    [MainToolbarElement(ToolbarElementPath, defaultDockPosition = MainToolbarDockPosition.Middle, defaultDockIndex = 0)]
    public static MainToolbarElement CreateRestartToolbarButton()
    {
        return new MainToolbarButton(new MainToolbarContent("↻", "停止游戏、重新编译并启动"), Restart);
    }
#else
    /// <summary>
    /// Installs the restart button into the legacy toolbar visual tree after that tree becomes available.
    /// </summary>
    static void InstallToolbarButton()
    {
        if (buttonInstalled) return;

        var toolbarType = typeof(Editor).Assembly.GetType("UnityEditor.Toolbar");
        if (toolbarType == null)
        {
            Debug.LogError("[RestartButton] 找不到 UnityEditor.Toolbar。");
            return;
        }

        var toolbars = Resources.FindObjectsOfTypeAll(toolbarType);
        if (toolbars.Length == 0)
        {
            Debug.Log("[RestartButton] Toolbar 尚未创建，继续等待。");
            return;
        }

        var root = GetToolbarRoot(toolbars[0]);
        if (root == null)
        {
            Debug.LogError("[RestartButton] 找到 Toolbar，但无法取得其 UI 根节点。");
            return;
        }

        var playZone = root.Q("ToolbarZonePlayMode");
        if (playZone == null)
        {
            Debug.LogError(
                "[RestartButton] 找不到 ToolbarZonePlayMode。此 Unity 版本的工具栏结构可能已变化。");
            return;
        }

        var restartButton = new Button(Restart)
        {
            text = "↻",
            tooltip = "停止游戏、重新编译并启动"
        };
        // restartButton.style.fontSize = 3f;
        // 使用 Unity 顶部工具栏按钮的内置样式
        restartButton.AddToClassList("unity-toolbar-button");
        restartButton.style.marginLeft = -3.2f;
        restartButton.style.marginTop = 0.5f;
        playZone.Add(restartButton);
        restartButton.schedule.Execute(() =>
        {
            Button referenceButton = null;

            // 不依赖内部元素名称，找该区域已有的原生按钮
            var buttons = playZone.Query<Button>().ToList();
            foreach (var button in buttons)
            {
                if (button != restartButton &&
                    button.resolvedStyle.width > 0 &&
                    button.resolvedStyle.height > 0)
                {
                    referenceButton = button;
                    break;
                }
            }

            if (referenceButton == null)
            {
                Debug.LogWarning("[RestartButton] 未找到可用于复制尺寸的原生工具栏按钮。");
                return;
            }

            restartButton.style.width = referenceButton.resolvedStyle.width - 1.5f;
            restartButton.style.height = referenceButton.resolvedStyle.height - 0.9f;
        });
        buttonInstalled = true;
    }

    static VisualElement GetToolbarRoot(object toolbar)
    {
        for (var type = toolbar.GetType(); type != null; type = type.BaseType)
        {
            // 部分版本使用 rootVisualElement 属性
            var property = type.GetProperty(
                "rootVisualElement",
                BindingFlags.Public | BindingFlags.NonPublic |
                BindingFlags.Instance | BindingFlags.DeclaredOnly);

            if (property?.GetValue(toolbar) is VisualElement propertyRoot)
                return propertyRoot;

            // Unity 2021 / 2022 的常见内部字段
            var field = type.GetField(
                "m_Root",
                BindingFlags.Public | BindingFlags.NonPublic |
                BindingFlags.Instance | BindingFlags.DeclaredOnly);

            if (field?.GetValue(toolbar) is VisualElement fieldRoot)
                return fieldRoot;
        }

        return null;
    }
#endif

    /// <summary>
    /// Records the pending restart request and either exits Play Mode first or immediately requests compilation.
    /// </summary>
    static void Restart()
    {
        SessionState.SetBool(PendingKey, true);
        SessionState.SetBool(CompileSeenKey, false);
        SessionState.SetBool(CompileErrorKey, false);

        if (EditorApplication.isPlayingOrWillChangePlaymode)
        {
            EditorApplication.playModeStateChanged -= OnPlayModeStateChanged;
            EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
            EditorApplication.isPlaying = false;
        }
        else
        {
            RequestCompile();
        }
    }

    /// <summary>
    /// Continues the restart workflow after Unity has fully returned to Edit Mode.
    /// </summary>
    static void OnPlayModeStateChanged(PlayModeStateChange state)
    {
        if (state != PlayModeStateChange.EnteredEditMode) return;

        EditorApplication.playModeStateChanged -= OnPlayModeStateChanged;
        RequestCompile();
    }

    /// <summary>
    /// Requests script compilation and starts polling for its completion across a possible domain reload.
    /// </summary>
    static void RequestCompile()
    {
        EditorApplication.update -= WaitForCompilation;
        EditorApplication.update += WaitForCompilation;

        // Unity 2019.4+：请求项目脚本重新编译
        CompilationPipeline.RequestScriptCompilation();
    }

    /// <summary>
    /// Remembers whether any compiled assembly reported an error during the pending restart operation.
    /// </summary>
    static void OnAssemblyCompilationFinished(string _, CompilerMessage[] messages)
    {
        foreach (var message in messages)
        {
            if (message.type == CompilerMessageType.Error)
            {
                SessionState.SetBool(CompileErrorKey, true);
                break;
            }
        }
    }

    /// <summary>
    /// Waits until compilation has actually started and finished, then resumes Play Mode only when compilation succeeded.
    /// </summary>
    static void WaitForCompilation()
    {
        if (!SessionState.GetBool(PendingKey, false))
        {
            EditorApplication.update -= WaitForCompilation;
            return;
        }

        if (EditorApplication.isCompiling)
        {
            SessionState.SetBool(CompileSeenKey, true);
            return;
        }

        if (!SessionState.GetBool(CompileSeenKey, false))
            return;

        EditorApplication.update -= WaitForCompilation;

        if (SessionState.GetBool(CompileErrorKey, false))
        {
            Debug.LogWarning("重新编译失败，未自动启动游戏。请先修复 Console 中的编译错误。");
            SessionState.SetBool(PendingKey, false);
            return;
        }

        SessionState.SetBool(PendingKey, false);
        EditorApplication.delayCall += () => EditorApplication.isPlaying = true;
    }
}
