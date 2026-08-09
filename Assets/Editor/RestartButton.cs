using UnityEditor;
using UnityEditor.Compilation;
using UnityEditor.Toolbars;

/// <summary>
/// Adds a supported Unity 6.3 main-toolbar button that stops Play Mode, recompiles scripts, and starts Play Mode again after a successful compilation.
/// </summary>
[InitializeOnLoad]
public static class RestartButton
{
    private const string ToolbarElementPath = "Prometheus/Restart Play Mode";
    private const string PendingKey = "PlayModeRestart.Pending";
    private const string CompileSeenKey = "PlayModeRestart.CompileSeen";
    private const string CompileErrorKey = "PlayModeRestart.CompileError";

    static RestartButton()
    {
        // Track compiler diagnostics across the domain reload caused by a requested script compilation.
        CompilationPipeline.assemblyCompilationFinished += OnAssemblyCompilationFinished;

        // SessionState survives domain reloads, so an interrupted restart workflow can continue safely after scripts reload.
        if (SessionState.GetBool(PendingKey, false))
        {
            EditorApplication.update -= WaitForCompilation;
            EditorApplication.update += WaitForCompilation;
        }
    }

    /// <summary>
    /// Creates the restart control through Unity's supported main-toolbar extension API.
    /// </summary>
    [MainToolbarElement(ToolbarElementPath, defaultDockPosition = MainToolbarDockPosition.Middle)]
    private static MainToolbarElement CreateRestartButton()
    {
        var content = new MainToolbarContent("↻", "停止游戏、重新编译并启动");
        return new MainToolbarButton(content, Restart);
    }

    /// <summary>
    /// Starts a restart workflow, stopping Play Mode first when necessary.
    /// </summary>
    private static void Restart()
    {
        SessionState.SetBool(PendingKey, true);
        SessionState.SetBool(CompileSeenKey, false);
        SessionState.SetBool(CompileErrorKey, false);

        if (EditorApplication.isPlayingOrWillChangePlaymode)
        {
            EditorApplication.playModeStateChanged -= OnPlayModeStateChanged;
            EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
            EditorApplication.isPlaying = false;
            return;
        }

        RequestCompile();
    }

    /// <summary>
    /// Waits until Unity has fully returned to Edit Mode before requesting compilation.
    /// </summary>
    private static void OnPlayModeStateChanged(PlayModeStateChange state)
    {
        if (state != PlayModeStateChange.EnteredEditMode)
        {
            return;
        }

        EditorApplication.playModeStateChanged -= OnPlayModeStateChanged;
        RequestCompile();
    }

    /// <summary>
    /// Requests a script compilation and begins polling its completion state.
    /// </summary>
    private static void RequestCompile()
    {
        EditorApplication.update -= WaitForCompilation;
        EditorApplication.update += WaitForCompilation;
        CompilationPipeline.RequestScriptCompilation();
    }

    /// <summary>
    /// Records whether any assembly emitted a compiler error during the current restart workflow.
    /// </summary>
    private static void OnAssemblyCompilationFinished(string assemblyPath, CompilerMessage[] messages)
    {
        foreach (var message in messages)
        {
            if (message.type != CompilerMessageType.Error)
            {
                continue;
            }

            SessionState.SetBool(CompileErrorKey, true);
            break;
        }
    }

    /// <summary>
    /// Starts Play Mode only after Unity has observed and completed the requested compilation.
    /// </summary>
    private static void WaitForCompilation()
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
        {
            return;
        }

        EditorApplication.update -= WaitForCompilation;

        if (SessionState.GetBool(CompileErrorKey, false))
        {
            UnityEngine.Debug.LogWarning("重新编译失败，未自动启动游戏。请先修复 Console 中的编译错误。");
            SessionState.SetBool(PendingKey, false);
            return;
        }

        SessionState.SetBool(PendingKey, false);
        EditorApplication.delayCall += () => EditorApplication.isPlaying = true;
    }
}
