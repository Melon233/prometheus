using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using Unity.Cinemachine;
using UnityEngine;

namespace Xuan.Prometheus.Narrative
{
    /// <summary>
    /// 走 CameraSystem 的运行时演出镜头端口。
    ///
    /// 端口自己持有唯一一台演出虚拟机位，接管时把它交给 <c>ICameraSystem.AcquireCutsceneCamera</c>，
    /// 由玩法镜头系统负责与跟随镜头的优先级仲裁；释放租约即恢复玩法镜头。
    ///
    /// 具名机位复用 <see cref="IActorResolver.TryGetAnchor"/>，不另建一张机位登记表：
    /// 「剧情引用的具名位置」在角色摆位与机位两处是同一个概念，两套登记表必然漂移。
    /// 因此 <c>CameraTo("altar_wide")</c> 找的就是名为 <c>altar_wide</c> 的锚点。
    /// </summary>
    internal sealed class GameplayCameraPort : INarrativeCameraPort, IDisposable
    {
        /// <summary>承载演出虚拟机位的运行时节点名。</summary>
        private const string CameraName = "[NarrativeCutsceneCamera]";

        /// <summary>解析具名机位使用的锚点来源。</summary>
        private readonly IActorResolver actors;

        private GameObject host;
        private CinemachineCamera cutsceneCamera;

        /// <summary>在常驻根节点下创建唯一演出虚拟机位。</summary>
        /// <param name="actors">提供具名锚点的角色解析器。</param>
        internal GameplayCameraPort(IActorResolver actors)
        {
            this.actors = actors ?? throw new ArgumentNullException(nameof(actors));
            host = new GameObject(CameraName);
            host.transform.SetParent(PersistentRoot.Shared, false);
            cutsceneCamera = host.AddComponent<CinemachineCamera>();
            // 未接管时优先级压到最低，避免它在玩法期间抢走 Brain 的输出。
            cutsceneCamera.Priority = int.MinValue;
        }

        /// <inheritdoc />
        public IDisposable AcquireControl(int priority)
        {
            if (!Core.Gameplay.TryGetSystem(out ICameraSystem cameraSystem)) throw new InvalidOperationException($"{nameof(GameplayCameraPort)} requires {nameof(ICameraSystem)}.");
            return cameraSystem.AcquireCutsceneCamera(Require(), priority);
        }

        /// <inheritdoc />
        public void SnapTo(string cameraId)
        {
            Transform anchor = RequireAnchor(cameraId);
            Require().transform.SetPositionAndRotation(anchor.position, anchor.rotation);
        }

        /// <inheritdoc />
        public async UniTask BlendToAsync(string cameraId, float duration, CancellationToken cancellationToken)
        {
            Transform anchor = RequireAnchor(cameraId);
            if (duration <= 0f)
            {
                SnapTo(cameraId);
                return;
            }

            Transform camera = Require().transform;
            Vector3 startPosition = camera.position;
            Quaternion startRotation = camera.rotation;
            float elapsed = 0f;
            while (elapsed < duration)
            {
                cancellationToken.ThrowIfCancellationRequested();
                await UniTask.Yield(PlayerLoopTiming.Update, cancellationToken);
                elapsed += Time.deltaTime;
                float t = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(elapsed / duration));
                camera.SetPositionAndRotation(Vector3.Lerp(startPosition, anchor.position, t), Quaternion.Slerp(startRotation, anchor.rotation, t));
            }
            // 收尾一次精确对齐，避免累计误差让终态与锚点存在肉眼可见的偏差。
            SnapTo(cameraId);
        }

        /// <inheritdoc />
        public async UniTask ShakeAsync(float amplitude, float duration, CancellationToken cancellationToken)
        {
            if (duration <= 0f || amplitude <= 0f) return;
            Transform camera = Require().transform;
            // 抖动是纯表现，必须自行复位；取消路径同样要还原，否则会残留一个偏移。
            Vector3 basePosition = camera.position;
            float elapsed = 0f;
            try
            {
                while (elapsed < duration)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    await UniTask.Yield(PlayerLoopTiming.Update, cancellationToken);
                    elapsed += Time.deltaTime;
                    float falloff = 1f - Mathf.Clamp01(elapsed / duration);
                    camera.position = basePosition + UnityEngine.Random.insideUnitSphere * (amplitude * falloff);
                }
            }
            finally
            {
                if (cutsceneCamera != null) camera.position = basePosition;
            }
        }

        /// <inheritdoc />
        public bool HasCamera(string cameraId)
        {
            return !string.IsNullOrEmpty(cameraId) && actors.TryGetAnchor(cameraId, out _);
        }

        /// <summary>销毁演出虚拟机位；重复释放保持幂等。</summary>
        public void Dispose()
        {
            if (host == null) return;
            StageScope.DestroyObject(host);
            host = null;
            cutsceneCamera = null;
        }

        /// <summary>取用演出虚拟机位；已释放后继续使用属于调用方错误，不做兜底。</summary>
        private CinemachineCamera Require()
        {
            if (cutsceneCamera == null) throw new ObjectDisposedException(nameof(GameplayCameraPort));
            return cutsceneCamera;
        }

        /// <summary>解析具名机位对应的锚点；缺失是配置错误，直接报出机位名以便定位。</summary>
        private Transform RequireAnchor(string cameraId)
        {
            if (!actors.TryGetAnchor(cameraId, out Transform anchor)) throw new InvalidOperationException($"Narrative camera '{cameraId}' has no matching anchor. Register an anchor with the same id before referencing it.");
            return anchor;
        }
    }
}
