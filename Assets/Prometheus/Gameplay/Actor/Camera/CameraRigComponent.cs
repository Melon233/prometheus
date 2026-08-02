using System;
using UnityEngine;

namespace Xuan.Prometheus.Actor
{
    /// <summary>标识由客户端镜头系统独占管理的 CameraRig，并集中暴露唯一输出相机与唯一音频监听器。</summary>
    [DisallowMultipleComponent]
    public sealed class CameraRigComponent : MonoBehaviour
    {
        /// <summary>由 CameraDirectorSystem 驱动的最终渲染相机。</summary>
        [SerializeField] private Camera outputCamera;

        /// <summary>由 CameraRigRuntime 保证全局独占启用的音频监听器。</summary>
        [SerializeField] private AudioListener audioListener;

        /// <summary>获取当前 Rig 的最终渲染相机。</summary>
        public Camera OutputCamera => outputCamera;

        /// <summary>获取当前 Rig 独占使用的音频监听器。</summary>
        public AudioListener AudioListener => audioListener;

        /// <summary>获取当前 Rig 是否仍包含可用的相机与音频监听器。</summary>
        public bool IsOperational => outputCamera != null && audioListener != null;

        /// <summary>由 CameraRigRuntime 在接管或创建 Rig 时写入唯一组件引用。</summary>
        /// <param name="camera">与当前 GameObject 绑定的最终渲染相机。</param>
        /// <param name="listener">与当前 GameObject 绑定的唯一音频监听器。</param>
        internal void Initialize(Camera camera, AudioListener listener)
        {
            if (camera == null) throw new ArgumentNullException(nameof(camera));
            if (listener == null) throw new ArgumentNullException(nameof(listener));
            if (camera.gameObject != gameObject) throw new ArgumentException("CameraRig output camera must be attached to the CameraRig GameObject.", nameof(camera));
            if (listener.gameObject != gameObject) throw new ArgumentException("CameraRig audio listener must be attached to the CameraRig GameObject.", nameof(listener));
            outputCamera = camera;
            audioListener = listener;
        }
    }
}
