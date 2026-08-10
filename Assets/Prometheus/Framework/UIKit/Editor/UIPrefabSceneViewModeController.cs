using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Xuan.Prometheus.Editor
{
    /// <summary>在 Unity 6 的 UI Prefab Stage 出现 Canvas (Environment) 时临时切换 Scene 视图为 2D，并在离开后恢复进入前的维度。</summary>
    [InitializeOnLoad]
    public static class UIPrefabSceneViewModeController
    {
        /// <summary>Unity 为 UI Prefab Stage 自动创建的环境 Canvas 名称。</summary>
        private const string EnvironmentCanvasName = "Canvas (Environment)";

        /// <summary>Prefab Stage 或 SceneView 在域重载后延迟恢复时允许等待的最大编辑器帧数。</summary>
        private const int MaximumSynchronizationRetryCount = 5;

        /// <summary>记录当前编辑器会话是否正在由本控制器接管 Scene 视图维度。</summary>
        private const string IsControllingSessionKey = "Prometheus.UIKit.UIPrefabSceneViewMode.IsControlling";

        /// <summary>记录进入 UI Prefab Stage 时被切换的 SceneView 实例编号。</summary>
        private const string SceneViewInstanceIdSessionKey = "Prometheus.UIKit.UIPrefabSceneViewMode.SceneViewInstanceId";

        /// <summary>记录进入 UI Prefab Stage 前 Scene 视图是否已经处于 2D 模式。</summary>
        private const string Original2DModeSessionKey = "Prometheus.UIKit.UIPrefabSceneViewMode.Original2DMode";

        /// <summary>记录当前编辑器会话是否已经取得一份进入 UI Prefab Stage 前的 Scene 视图快照。</summary>
        private const string HasRecordedModeSessionKey = "Prometheus.UIKit.UIPrefabSceneViewMode.HasRecordedMode";

        /// <summary>记录当前 Stage 切换尚可等待环境 Canvas 和 SceneView 就绪的剩余次数。</summary>
        private static int remainingSynchronizationRetryCount;

        /// <summary>注册 Prefab Stage 生命周期监听，并在脚本重载后同步当前已经打开的 Stage。</summary>
        static UIPrefabSceneViewModeController()
        {
            PrefabStage.prefabStageOpened -= OnPrefabStageChanged;
            PrefabStage.prefabStageOpened += OnPrefabStageChanged;
            PrefabStage.prefabStageClosing -= OnPrefabStageChanged;
            PrefabStage.prefabStageClosing += OnPrefabStageChanged;
            EditorApplication.update -= TrackSceneViewMode;
            EditorApplication.update += TrackSceneViewMode;
            QueueSynchronization();
        }

        /// <summary>Prefab Stage 打开或关闭时把判断延迟到 Unity 完成 Stage 层级切换之后。</summary>
        private static void OnPrefabStageChanged(PrefabStage _)
        {
            QueueSynchronization();
        }

        /// <summary>合并同一编辑器帧内的多次 Stage 变化，只执行一次最终状态同步。</summary>
        private static void QueueSynchronization()
        {
            remainingSynchronizationRetryCount = MaximumSynchronizationRetryCount;
            ScheduleSynchronization();
        }

        /// <summary>安排下一编辑器帧执行同步，但不重置当前 Stage 切换的剩余重试次数。</summary>
        private static void ScheduleSynchronization()
        {
            EditorApplication.delayCall -= SynchronizeCurrentPrefabStage;
            EditorApplication.delayCall += SynchronizeCurrentPrefabStage;
        }

        /// <summary>根据当前 Prefab Stage 是否具有环境 Canvas 决定进入 2D 模式或恢复原有维度。</summary>
        private static void SynchronizeCurrentPrefabStage()
        {
            PrefabStage prefabStage = PrefabStageUtility.GetCurrentPrefabStage();
            if (HasEnvironmentCanvas(prefabStage))
            {
                if (Enter2DMode()) remainingSynchronizationRetryCount = 0;
                else RetrySynchronization();
                return;
            }

            if (prefabStage != null && remainingSynchronizationRetryCount > 0)
            {
                RetrySynchronization();
                return;
            }

            remainingSynchronizationRetryCount = 0;
            RestoreOriginalMode();
        }

        /// <summary>消耗一次有限重试并等待下一编辑器帧，避免域重载阶段环境对象尚未恢复时漏判 UI Prefab Stage。</summary>
        private static void RetrySynchronization()
        {
            if (remainingSynchronizationRetryCount <= 0) return;
            remainingSynchronizationRetryCount--;
            ScheduleSynchronization();
        }

        /// <summary>在非 UI Stage 中持续缓存最近活动 SceneView 的维度，并在域重载后补偿可能错过的 UI Stage 打开事件。</summary>
        private static void TrackSceneViewMode()
        {
            PrefabStage prefabStage = PrefabStageUtility.GetCurrentPrefabStage();
            if (HasEnvironmentCanvas(prefabStage))
            {
                if (!SessionState.GetBool(IsControllingSessionKey, false)) Enter2DMode();
                return;
            }

            if (SessionState.GetBool(IsControllingSessionKey, false))
            {
                if (prefabStage == null) RestoreOriginalMode();
                return;
            }

            if (prefabStage != null && (remainingSynchronizationRetryCount > 0 || prefabStage.prefabContentsRoot == null || prefabStage.prefabContentsRoot.transform is RectTransform)) return;
            RecordActiveSceneViewMode();
        }

        /// <summary>判断当前 Stage 的真实预制体根节点外是否存在 Unity 自动创建的 Canvas (Environment)。</summary>
        private static bool HasEnvironmentCanvas(PrefabStage prefabStage)
        {
            if (prefabStage == null || prefabStage.prefabContentsRoot == null) return false;
            Transform prefabRoot = prefabStage.prefabContentsRoot.transform;
            Transform hierarchyRoot = prefabRoot.root;
            if (hierarchyRoot == prefabRoot || hierarchyRoot.name != EnvironmentCanvasName) return false;
            return hierarchyRoot.TryGetComponent(out Canvas _);
        }

        /// <summary>首次进入 UI Prefab Stage 时保存活动 Scene 视图及原始维度，然后将其切换为 2D。</summary>
        private static bool Enter2DMode()
        {
            bool isControlling = SessionState.GetBool(IsControllingSessionKey, false);
            if (!isControlling && !SessionState.GetBool(HasRecordedModeSessionKey, false)) RecordActiveSceneViewMode();
            SceneView sceneView = ResolveRecordedSceneView();
            if (sceneView == null) sceneView = ResolveActiveSceneView();
            if (sceneView == null) return false;
            if (!isControlling)
            {
                SessionState.SetBool(IsControllingSessionKey, true);
            }

            if (!sceneView.in2DMode)
            {
                sceneView.in2DMode = true;
                sceneView.Repaint();
            }

            return true;
        }

        /// <summary>离开带环境 Canvas 的 Prefab Stage 后，把进入时记录的 Scene 视图恢复为原始维度。</summary>
        private static void RestoreOriginalMode()
        {
            if (!SessionState.GetBool(IsControllingSessionKey, false)) return;
            SceneView sceneView = ResolveRecordedSceneView();
            bool original2DMode = SessionState.GetBool(Original2DModeSessionKey, false);
            if (sceneView != null && sceneView.in2DMode != original2DMode)
            {
                sceneView.in2DMode = original2DMode;
                sceneView.Repaint();
            }

            SessionState.SetBool(IsControllingSessionKey, false);
        }

        /// <summary>把最近活动 SceneView 的实例编号和当前维度写入会话快照，供下一次 UI Prefab Stage 恢复使用。</summary>
        private static void RecordActiveSceneViewMode()
        {
            SceneView sceneView = ResolveActiveSceneView();
            if (sceneView == null) return;
            int recordedInstanceId = SessionState.GetInt(SceneViewInstanceIdSessionKey, 0);
            bool recorded2DMode = SessionState.GetBool(Original2DModeSessionKey, false);
            if (SessionState.GetBool(HasRecordedModeSessionKey, false) && recordedInstanceId == sceneView.GetInstanceID() && recorded2DMode == sceneView.in2DMode) return;
            SessionState.SetInt(SceneViewInstanceIdSessionKey, sceneView.GetInstanceID());
            SessionState.SetBool(Original2DModeSessionKey, sceneView.in2DMode);
            SessionState.SetBool(HasRecordedModeSessionKey, true);
        }

        /// <summary>优先取得最近活动的 Scene 视图，并在其不存在时回退到第一个已打开的 Scene 视图。</summary>
        private static SceneView ResolveActiveSceneView()
        {
            if (SceneView.lastActiveSceneView != null) return SceneView.lastActiveSceneView;
            foreach (object candidate in SceneView.sceneViews)
            {
                if (candidate is SceneView sceneView) return sceneView;
            }

            return null;
        }

        /// <summary>通过编辑器实例编号找回进入 UI Prefab Stage 时实际切换的 Scene 视图。</summary>
        private static SceneView ResolveRecordedSceneView()
        {
            int instanceId = SessionState.GetInt(SceneViewInstanceIdSessionKey, 0);
            return instanceId == 0 ? null : EditorUtility.InstanceIDToObject(instanceId) as SceneView;
        }
    }
}
