using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Xuan.Prometheus.Actor
{
    /// <summary>接管 Prefab 中的旧相机并把它提升为 Pawn 生命周期之外的独立客户端 CameraRig。</summary>
    internal sealed class CameraRigRuntime : IDisposable
    {
        /// <summary>运行时层级中统一使用的 CameraRig 名称。</summary>
        internal const string RuntimeName = "ClientCameraRig";

        /// <summary>记录为保证唯一 AudioListener 而被当前 Rig 暂时禁用的外部监听器。</summary>
        private readonly List<AudioListener> suppressedAudioListeners = new List<AudioListener>();

        /// <summary>当前 Rig 根对象。</summary>
        private GameObject rigObject;

        /// <summary>当前 Rig 输出相机。</summary>
        private Camera outputCamera;

        /// <summary>当前 Rig 独占音频监听器。</summary>
        private AudioListener audioListener;

        /// <summary>当前 Rig 标记组件。</summary>
        private CameraRigComponent component;

        /// <summary>记录当前 Rig 是否已经释放。</summary>
        private bool disposed;

        /// <summary>禁止外部直接构造，确保所有实例都完成层级和音频独占初始化。</summary>
        private CameraRigRuntime()
        {
        }

        /// <summary>获取当前 Rig 输出相机；Rig 被释放或遭到外部销毁后返回空。</summary>
        internal Camera OutputCamera => outputCamera;

        /// <summary>获取当前 Rig 标记组件；Rig 被释放或遭到外部销毁后返回空。</summary>
        internal CameraRigComponent Component => component;

        /// <summary>把来源相机接管为独立 Rig；相机与 Pawn 共用根对象时会复制相机，避免错误地把整个 Pawn 脱离实体生命周期。</summary>
        /// <param name="sourceCamera">Prefab 提供的来源相机。</param>
        /// <param name="runtimeRoot">承载独立客户端 Rig 的常驻运行时根节点。</param>
        /// <param name="sourceOwnerRoot">来源 Pawn 根节点，用于识别相机是否与 Pawn 共用 GameObject。</param>
        /// <returns>已经完成层级提升和音频独占的 CameraRigRuntime。</returns>
        internal static CameraRigRuntime Adopt(Camera sourceCamera, Transform runtimeRoot, Transform sourceOwnerRoot)
        {
            if (sourceCamera == null) throw new ArgumentNullException(nameof(sourceCamera));
            if (runtimeRoot == null) throw new ArgumentNullException(nameof(runtimeRoot));
            if (runtimeRoot == sourceCamera.transform || runtimeRoot.IsChildOf(sourceCamera.transform)) throw new ArgumentException("CameraRig runtime root cannot be the source camera or its descendant.", nameof(runtimeRoot));
            CameraRigRuntime runtime = new CameraRigRuntime();
            try
            {
                runtime.Initialize(sourceCamera, runtimeRoot, sourceOwnerRoot);
                return runtime;
            }
            catch
            {
                runtime.Dispose();
                throw;
            }
        }

        /// <summary>持续压制运行期间新出现的外部 AudioListener，保证跨场景和动态加载后仍然只有 Rig 监听器启用。</summary>
        internal void EnsureExclusiveAudioListener()
        {
            if (disposed || audioListener == null) return;
            AudioListener[] listeners = UnityEngine.Object.FindObjectsOfType<AudioListener>(true);
            for (int index = 0; index < listeners.Length; index++)
            {
                AudioListener listener = listeners[index];
                if (listener == null || listener == audioListener || !listener.enabled) continue;
                if (!suppressedAudioListeners.Contains(listener)) suppressedAudioListeners.Add(listener);
                listener.enabled = false;
            }
            audioListener.enabled = true;
        }

        /// <summary>禁用并销毁独立 Rig，同时恢复仍然存活且曾被当前 Rig 压制的外部 AudioListener。</summary>
        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            SceneManager.sceneLoaded -= OnSceneLoaded;
            if (outputCamera != null) outputCamera.enabled = false;
            if (audioListener != null) audioListener.enabled = false;
            for (int index = 0; index < suppressedAudioListeners.Count; index++)
            {
                AudioListener listener = suppressedAudioListeners[index];
                if (listener != null) listener.enabled = true;
            }
            suppressedAudioListeners.Clear();
            GameObject objectToDestroy = rigObject;
            rigObject = null;
            outputCamera = null;
            audioListener = null;
            component = null;
            if (objectToDestroy == null) return;
            if (Application.isPlaying) UnityEngine.Object.Destroy(objectToDestroy);
            else UnityEngine.Object.DestroyImmediate(objectToDestroy);
        }

        /// <summary>根据来源相机所在层级选择直接脱离或安全复制，并完成统一 CameraRig 配置。</summary>
        private void Initialize(Camera sourceCamera, Transform runtimeRoot, Transform sourceOwnerRoot)
        {
            bool sharesOwnerRoot = sourceOwnerRoot != null && sourceCamera.transform == sourceOwnerRoot;
            if (sharesOwnerRoot) CreateRigFromSharedOwnerCamera(sourceCamera, runtimeRoot);
            else AdoptDedicatedCameraObject(sourceCamera, runtimeRoot);
            rigObject.name = RuntimeName;
            rigObject.tag = "MainCamera";
            rigObject.SetActive(true);
            outputCamera.enabled = true;
            audioListener = rigObject.GetComponent<AudioListener>();
            if (audioListener == null) audioListener = rigObject.AddComponent<AudioListener>();
            component = rigObject.GetComponent<CameraRigComponent>();
            if (component == null) component = rigObject.AddComponent<CameraRigComponent>();
            component.Initialize(outputCamera, audioListener);
            EnsureExclusiveAudioListener();
            SceneManager.sceneLoaded += OnSceneLoaded;
        }

        /// <summary>新场景载入完成后重新执行监听器仲裁，避免场景自带 AudioListener 与常驻 Rig 同时启用。</summary>
        private void OnSceneLoaded(Scene scene, LoadSceneMode loadSceneMode)
        {
            EnsureExclusiveAudioListener();
        }

        /// <summary>直接把专用相机对象脱离 Pawn，同时保持世界姿态以避免初始化瞬间画面跳变。</summary>
        private void AdoptDedicatedCameraObject(Camera sourceCamera, Transform runtimeRoot)
        {
            rigObject = sourceCamera.gameObject;
            outputCamera = sourceCamera;
            rigObject.transform.SetParent(runtimeRoot, true);
        }

        /// <summary>复制与 Pawn 共用根对象的相机设置，并禁用来源组件，避免提升层级时带走整个 Pawn。</summary>
        private void CreateRigFromSharedOwnerCamera(Camera sourceCamera, Transform runtimeRoot)
        {
            rigObject = new GameObject(RuntimeName);
            rigObject.layer = sourceCamera.gameObject.layer;
            rigObject.transform.SetPositionAndRotation(sourceCamera.transform.position, sourceCamera.transform.rotation);
            rigObject.transform.localScale = Vector3.one;
            rigObject.transform.SetParent(runtimeRoot, true);
            outputCamera = rigObject.AddComponent<Camera>();
            outputCamera.CopyFrom(sourceCamera);
            sourceCamera.enabled = false;
            AudioListener sourceListener = sourceCamera.GetComponent<AudioListener>();
            if (sourceListener != null) sourceListener.enabled = false;
        }
    }
}
