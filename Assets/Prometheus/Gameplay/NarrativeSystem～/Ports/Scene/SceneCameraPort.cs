using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;

namespace Xuan.Prometheus.Narrative
{
    /// <summary>场景中一台具名演出机位的登记项。</summary>
    [Serializable]
    public sealed class SceneCameraEntry
    {
        [SerializeField] [Tooltip("剧情中引用的机位名。")] private string cameraId;
        [SerializeField] [Tooltip("机位变换；相机会被摆到该位置与朝向。")] private Transform pose;
        [SerializeField] [Tooltip("该机位使用的视野角；小于等于零表示沿用当前值。")] private float fieldOfView;

        /// <summary>获取机位名。</summary>
        public string CameraId => cameraId;

        /// <summary>获取机位变换。</summary>
        public Transform Pose => pose;

        /// <summary>获取机位视野角。</summary>
        public float FieldOfView => fieldOfView;
    }

    /// <summary>
    /// 基于场景机位登记表的演出镜头端口。
    /// <para>
    /// 直接把目标相机摆到具名机位，因此结果完全由机位决定、可重复、可用于落终态。
    /// 正式流程应换成包装 <c>ICameraSystem</c> 与 Cinemachine 的实现；
    /// 本端口服务于不接入玩法镜头系统的测试场景，也可作为该实现的参考形态。
    /// </para>
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class SceneCameraPort : MonoBehaviour, INarrativeCameraPort
    {
        [SerializeField] [Tooltip("被演出接管的相机；留空则使用主相机。")] private Camera targetCamera;
        [SerializeField] [Tooltip("可供剧情引用的具名机位。")] private List<SceneCameraEntry> cameras = new List<SceneCameraEntry>();

        /// <summary>保存抖动期间的基准位置，使抖动结束后可以精确复位。</summary>
        private Vector3 shakeBasePosition;

        /// <summary>获取实际被接管的相机。</summary>
        private Camera Target => targetCamera != null ? targetCamera : Camera.main;

        /// <inheritdoc />
        public IDisposable AcquireControl(int priority)
        {
            Camera camera = Target;
            if (camera == null) return new CameraLease(null, Vector3.zero, Quaternion.identity, 0f);
            return new CameraLease(camera, camera.transform.position, camera.transform.rotation, camera.fieldOfView);
        }

        /// <inheritdoc />
        public void SnapTo(string cameraId)
        {
            if (!TryResolve(cameraId, out SceneCameraEntry entry)) return;
            Camera camera = Target;
            if (camera == null) return;
            camera.transform.SetPositionAndRotation(entry.Pose.position, entry.Pose.rotation);
            if (entry.FieldOfView > 0f) camera.fieldOfView = entry.FieldOfView;
        }

        /// <inheritdoc />
        public async UniTask BlendToAsync(string cameraId, float duration, CancellationToken cancellationToken)
        {
            if (!TryResolve(cameraId, out SceneCameraEntry entry)) return;
            Camera camera = Target;
            if (camera == null) return;
            if (duration <= 0f)
            {
                SnapTo(cameraId);
                return;
            }
            Vector3 startPosition = camera.transform.position;
            Quaternion startRotation = camera.transform.rotation;
            float startFov = camera.fieldOfView;
            float targetFov = entry.FieldOfView > 0f ? entry.FieldOfView : startFov;
            float elapsed = 0f;
            while (elapsed < duration)
            {
                cancellationToken.ThrowIfCancellationRequested();
                await UniTask.Yield(PlayerLoopTiming.Update, cancellationToken);
                elapsed += Time.deltaTime;
                float t = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(elapsed / duration));
                camera.transform.SetPositionAndRotation(Vector3.Lerp(startPosition, entry.Pose.position, t), Quaternion.Slerp(startRotation, entry.Pose.rotation, t));
                camera.fieldOfView = Mathf.Lerp(startFov, targetFov, t);
            }
            SnapTo(cameraId);
        }

        /// <inheritdoc />
        public async UniTask ShakeAsync(float amplitude, float duration, CancellationToken cancellationToken)
        {
            Camera camera = Target;
            if (camera == null || duration <= 0f || amplitude <= 0f) return;
            // 抖动是纯表现，必须自行复位；取消路径同样要还原，否则会残留一个偏移。
            shakeBasePosition = camera.transform.position;
            float elapsed = 0f;
            try
            {
                while (elapsed < duration)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    await UniTask.Yield(PlayerLoopTiming.Update, cancellationToken);
                    elapsed += Time.deltaTime;
                    float falloff = 1f - Mathf.Clamp01(elapsed / duration);
                    camera.transform.position = shakeBasePosition + UnityEngine.Random.insideUnitSphere * (amplitude * falloff);
                }
            }
            finally
            {
                if (camera != null) camera.transform.position = shakeBasePosition;
            }
        }

        /// <inheritdoc />
        public bool HasCamera(string cameraId)
        {
            return TryResolve(cameraId, out _);
        }

        /// <summary>按名称查找登记的机位。</summary>
        private bool TryResolve(string cameraId, out SceneCameraEntry entry)
        {
            for (int index = 0; index < cameras.Count; index++)
            {
                SceneCameraEntry candidate = cameras[index];
                if (candidate != null && string.Equals(candidate.CameraId, cameraId, StringComparison.Ordinal) && candidate.Pose != null)
                {
                    entry = candidate;
                    return true;
                }
            }
            Debug.LogWarning($"[Narrative] 场景中没有登记名为 '{cameraId}' 的演出机位。");
            entry = null;
            return false;
        }

        /// <summary>相机控制权租约；释放后把相机还原到接管前的位姿。</summary>
        private sealed class CameraLease : IDisposable
        {
            private readonly Camera camera;
            private readonly Vector3 position;
            private readonly Quaternion rotation;
            private readonly float fieldOfView;
            private bool released;

            /// <summary>记录接管前的相机状态。</summary>
            internal CameraLease(Camera camera, Vector3 position, Quaternion rotation, float fieldOfView)
            {
                this.camera = camera;
                this.position = position;
                this.rotation = rotation;
                this.fieldOfView = fieldOfView;
            }

            /// <inheritdoc />
            public void Dispose()
            {
                if (released || camera == null) return;
                released = true;
                camera.transform.SetPositionAndRotation(position, rotation);
                if (fieldOfView > 0f) camera.fieldOfView = fieldOfView;
            }
        }
    }
}
