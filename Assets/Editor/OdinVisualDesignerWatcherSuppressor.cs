using System;
using System.Reflection;
using UnityEditor;

/// <summary>
/// 临时补丁：屏蔽 Odin Visual Designer 文件监视器在 Unity 6.6 上每帧抛出的 NotImplementedException。
/// <para>
/// 成因：Unity 6000.6 把 <c>EntityId</c> 的 int 隐式转换（以及 <c>EditorUtility.InstanceIDToObject(int)</c>）
/// 改成了运行期直接抛 <c>NotImplementedException</c> 的存根。当前导入的 Odin 是 2026-04 的预编译 DLL，
/// 其 <c>Sirenix.OdinInspector.Editor.Internal.OVDFFileWatcher</c> 静态构造里会走到该调用，
/// 导致类型初始化失败，并由注册在 <see cref="EditorApplication.update"/> 上的两个回调每帧重复抛出。
/// </para>
/// <para>
/// 该监视器只服务 Odin Visual Designer；本项目未使用该功能（Visual Designer/Saved 为空），
/// 且其类型初始化已经失败、本就不可用，因此摘掉这两个回调不会丢失任何仍在工作的行为。
/// </para>
/// <para>
/// 这是权宜措施。Odin 官方已在 4.0.2.2 修复（"Made OdinEntityId independent of the inner workings of EntityId"），
/// 升级到 4.0.2.2 及以上后应当删除本文件。
/// </para>
/// </summary>
[InitializeOnLoad]
internal static class OdinVisualDesignerWatcherSuppressor
{
    private const string BrokenWatcherTypeFullName = "Sirenix.OdinInspector.Editor.Internal.OVDFFileWatcher";

    /// <summary>域重载后继续扫描的时长；Odin 的静态构造可能在本类之后才被触发。</summary>
    private const double SweepDurationSeconds = 30d;

    private static double sweepDeadline;

    static OdinVisualDesignerWatcherSuppressor()
    {
        // 立刻尝试一次，覆盖 Odin 已经完成注册的情况。
        RemoveBrokenWatcherCallbacks();

        // Odin 的注册可能晚于本类，因此在启动后的一段时间内持续复查。
        sweepDeadline = EditorApplication.timeSinceStartup + SweepDurationSeconds;
        EditorApplication.update -= Sweep;
        EditorApplication.update += Sweep;
    }

    /// <summary>在启动窗口期内反复摘除坏掉的回调，超时后自行退订以免持续付出反射开销。</summary>
    private static void Sweep()
    {
        RemoveBrokenWatcherCallbacks();
        if (EditorApplication.timeSinceStartup >= sweepDeadline)
        {
            EditorApplication.update -= Sweep;
        }
    }

    /// <summary>
    /// 按类型全名摘除 <see cref="EditorApplication.update"/> 上归属坏掉监视器的回调，
    /// 不与 Odin 建立编译期引用，Odin 被移除后本文件仍可编译。
    /// </summary>
    private static void RemoveBrokenWatcherCallbacks()
    {
        FieldInfo updateField = typeof(EditorApplication).GetField("update", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
        if (updateField == null)
        {
            return;
        }

        if (!(updateField.GetValue(null) is Delegate registeredCallbacks))
        {
            return;
        }

        foreach (Delegate callback in registeredCallbacks.GetInvocationList())
        {
            Type declaringType = callback.Method.DeclaringType;
            if (declaringType == null || !string.Equals(declaringType.FullName, BrokenWatcherTypeFullName, StringComparison.Ordinal))
            {
                continue;
            }

            if (callback is EditorApplication.CallbackFunction typedCallback)
            {
                EditorApplication.update -= typedCallback;
            }
        }
    }
}
