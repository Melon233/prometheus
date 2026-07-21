using UnityEditor;
using UnityEditor.Compilation;
using UnityEngine;

[InitializeOnLoad]
public static class EditorTimingLogger
{
    const string CompileStartKey = "EditorTiming.CompileStart";
    const string IsCompilingKey = "EditorTiming.IsCompiling";

    const string PlayStartKey = "EditorTiming.PlayStart";
    const string IsStartingPlayKey = "EditorTiming.IsStartingPlay";

    static EditorTimingLogger()
    {
        CompilationPipeline.compilationStarted -= OnCompilationStarted;
        CompilationPipeline.compilationStarted += OnCompilationStarted;

        CompilationPipeline.compilationFinished -= OnCompilationFinished;
        CompilationPipeline.compilationFinished += OnCompilationFinished;

        EditorApplication.playModeStateChanged -= OnPlayModeStateChanged;
        EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
    }

    static void OnCompilationStarted(object _)
    {
        SessionState.SetFloat(
            CompileStartKey,
            (float)EditorApplication.timeSinceStartup);

        SessionState.SetBool(IsCompilingKey, true);
    }

    static void OnCompilationFinished(object _)
    {
        if (!SessionState.GetBool(IsCompilingKey, false))
            return;

        var startedAt = SessionState.GetFloat(
            CompileStartKey,
            (float)EditorApplication.timeSinceStartup);

        var seconds = EditorApplication.timeSinceStartup - startedAt;

        Debug.Log($"[EditorTiming] 脚本编译耗时：{seconds:F3}s");

        SessionState.SetBool(IsCompilingKey, false);
    }

    static void OnPlayModeStateChanged(PlayModeStateChange state)
    {
        switch (state)
        {
            // 点击任何 Play 按钮后都会经过此状态
            case PlayModeStateChange.ExitingEditMode:
                SessionState.SetFloat(
                    PlayStartKey,
                    (float)EditorApplication.timeSinceStartup);

                SessionState.SetBool(IsStartingPlayKey, true);
                break;

            // Unity 完成加载并正式进入 Play Mode
            case PlayModeStateChange.EnteredPlayMode:
                if (!SessionState.GetBool(IsStartingPlayKey, false))
                    return;

                var startedAt = SessionState.GetFloat(
                    PlayStartKey,
                    (float)EditorApplication.timeSinceStartup);

                var seconds = EditorApplication.timeSinceStartup - startedAt;

                Debug.Log($"[EditorTiming] 游戏启动耗时：{seconds:F3}s");

                SessionState.SetBool(IsStartingPlayKey, false);
                break;

            // 启动被取消或因错误未进入 Play Mode 时清理状态
            case PlayModeStateChange.EnteredEditMode:
                SessionState.SetBool(IsStartingPlayKey, false);
                break;
        }
    }
}