using UnityEditor;
using UnityEditor.Callbacks;
using UnityEditor.Compilation;
using UnityEngine;

[InitializeOnLoad]
public static class EditorTimingLogger
{
    const string CompileStartKey = "EditorTiming.CompileStart";
    const string IsCompilingKey = "EditorTiming.IsCompiling";
    /// <summary>记录编译器阶段已经结束，供 Domain Reload 后继续等待 Editor 恢复。</summary>
    const string CompilePipelineFinishedKey = "EditorTiming.CompilePipelineFinished";
    /// <summary>记录本次编译是否产生错误；有错误时不会强制等待脚本重载回调。</summary>
    const string CompileErrorKey = "EditorTiming.CompileError";
    /// <summary>记录成功编译后的脚本重载已经结束。</summary>
    const string ScriptReloadFinishedKey = "EditorTiming.ScriptReloadFinished";
    /// <summary>记录纯编译器阶段耗时，便于和完整卡顿时间分别展示。</summary>
    const string CompilerSecondsKey = "EditorTiming.CompilerSeconds";
    /// <summary>要求 Editor 连续空闲的帧数，避免在重载收尾的瞬时空档过早结束计时。</summary>
    const int RequiredIdleFrames = 2;

    const string PlayStartKey = "EditorTiming.PlayStart";
    const string IsStartingPlayKey = "EditorTiming.IsStartingPlay";

    /// <summary>记录当前编译完成后已经连续观察到的 Editor 空闲帧数。</summary>
    static int idleFrameCount;

    static EditorTimingLogger()
    {
        CompilationPipeline.compilationStarted -= OnCompilationStarted;
        CompilationPipeline.compilationStarted += OnCompilationStarted;

        CompilationPipeline.compilationFinished -= OnCompilationFinished;
        CompilationPipeline.compilationFinished += OnCompilationFinished;

        CompilationPipeline.assemblyCompilationFinished -= OnAssemblyCompilationFinished;
        CompilationPipeline.assemblyCompilationFinished += OnAssemblyCompilationFinished;

        EditorApplication.playModeStateChanged -= OnPlayModeStateChanged;
        EditorApplication.playModeStateChanged += OnPlayModeStateChanged;

        EditorApplication.update -= WaitForEditorReady;
        if (SessionState.GetBool(IsCompilingKey, false))
            EditorApplication.update += WaitForEditorReady;
    }

    /// <summary>
    /// 从编译器开始工作时启动完整等待计时，并重置跨 Domain Reload 保存的状态。
    /// </summary>
    static void OnCompilationStarted(object _)
    {
        SessionState.SetFloat(
            CompileStartKey,
            (float)EditorApplication.timeSinceStartup);

        SessionState.SetBool(IsCompilingKey, true);
        SessionState.SetBool(CompilePipelineFinishedKey, false);
        SessionState.SetBool(CompileErrorKey, false);
        SessionState.SetBool(ScriptReloadFinishedKey, false);
        SessionState.SetFloat(CompilerSecondsKey, 0f);
        idleFrameCount = 0;
        EditorApplication.update -= WaitForEditorReady;
        EditorApplication.update += WaitForEditorReady;
    }

    /// <summary>
    /// 只记录编译器阶段完成；最终日志必须继续等待脚本重载和 Editor 恢复响应。
    /// </summary>
    static void OnCompilationFinished(object _)
    {
        if (!SessionState.GetBool(IsCompilingKey, false))
            return;

        var startedAt = SessionState.GetFloat(
            CompileStartKey,
            (float)EditorApplication.timeSinceStartup);

        var seconds = EditorApplication.timeSinceStartup - startedAt;

        SessionState.SetFloat(CompilerSecondsKey, (float)seconds);
        SessionState.SetBool(CompilePipelineFinishedKey, true);
        idleFrameCount = 0;
    }

    /// <summary>
    /// 汇总各程序集的编译错误，使失败编译可以在没有 Domain Reload 的情况下正常结束计时。
    /// </summary>
    static void OnAssemblyCompilationFinished(string _, CompilerMessage[] messages)
    {
        foreach (var message in messages)
        {
            if (message.type != CompilerMessageType.Error)
                continue;

            SessionState.SetBool(CompileErrorKey, true);
            return;
        }
    }

    /// <summary>
    /// 在新程序集完成加载后记录 Domain Reload 已结束；SessionState 会跨越本次重载保留计时数据。
    /// </summary>
    [DidReloadScripts]
    static void OnScriptsReloaded()
    {
        if (SessionState.GetBool(IsCompilingKey, false))
            SessionState.SetBool(ScriptReloadFinishedKey, true);
    }

    /// <summary>
    /// 等待编译、资源刷新和脚本重载全部结束，并在 Editor 稳定空闲后输出用户实际等待时间。
    /// </summary>
    static void WaitForEditorReady()
    {
        if (!SessionState.GetBool(IsCompilingKey, false))
            return;

        if (!SessionState.GetBool(CompilePipelineFinishedKey, false) || EditorApplication.isCompiling || EditorApplication.isUpdating)
        {
            idleFrameCount = 0;
            return;
        }

        var hasCompileError = SessionState.GetBool(CompileErrorKey, false);
        if (!hasCompileError && !SessionState.GetBool(ScriptReloadFinishedKey, false))
        {
            idleFrameCount = 0;
            return;
        }

        idleFrameCount++;
        if (idleFrameCount < RequiredIdleFrames)
            return;

        var startedAt = SessionState.GetFloat(CompileStartKey, (float)EditorApplication.timeSinceStartup);
        var compilerSeconds = SessionState.GetFloat(CompilerSecondsKey, 0f);
        var totalSeconds = EditorApplication.timeSinceStartup - startedAt;
        var reloadAndRefreshSeconds = Mathf.Max(0f, (float)totalSeconds - compilerSeconds);
        var result = hasCompileError ? "，编译失败" : string.Empty;

        Debug.Log($"[EditorTiming] 脚本刷新总耗时：{totalSeconds:F3}s（编译 {compilerSeconds:F3}s，重载/刷新 {reloadAndRefreshSeconds:F3}s{result}）");

        SessionState.SetBool(IsCompilingKey, false);
        SessionState.SetBool(CompilePipelineFinishedKey, false);
        SessionState.SetBool(CompileErrorKey, false);
        SessionState.SetBool(ScriptReloadFinishedKey, false);
        idleFrameCount = 0;
        EditorApplication.update -= WaitForEditorReady;
    }

    /// <summary>
    /// 记录 Play Mode 切换过程的起止状态并输出完整启动耗时。
    /// </summary>
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
