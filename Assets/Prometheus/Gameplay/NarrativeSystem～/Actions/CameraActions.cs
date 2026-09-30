using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;

namespace Xuan.Prometheus.Narrative
{
    /// <summary>
    /// 镜头切换动作。
    /// 当前生效的镜头属于世界状态，因此落终态直接把镜头硬切到目标机位。
    /// </summary>
    public sealed class CameraBlendAction : StoryAction
    {
        private readonly string cameraId;
        private readonly float duration;

        /// <summary>创建一个镜头切换动作。</summary>
        /// <param name="cameraId">场景中登记的具名演出镜头。</param>
        /// <param name="duration">混合时长；小于等于零表示硬切。</param>
        public CameraBlendAction(string cameraId, float duration)
        {
            if (string.IsNullOrWhiteSpace(cameraId)) throw new ArgumentException("Camera id cannot be empty.", nameof(cameraId));
            this.cameraId = cameraId;
            this.duration = Mathf.Max(0f, duration);
        }

        /// <inheritdoc />
        public override UniTask PlayAsync(StoryContext context, CancellationToken cancellationToken)
        {
            INarrativeCameraPort camera = context.RequireServices().RequireCamera();
            if (duration <= 0f)
            {
                camera.SnapTo(cameraId);
                return UniTask.CompletedTask;
            }
            return camera.BlendToAsync(cameraId, duration, cancellationToken);
        }

        /// <inheritdoc />
        public override void Settle(StoryContext context)
        {
            context.RequireServices().RequireCamera().SnapTo(cameraId);
        }
    }

    /// <summary>
    /// 镜头抖动动作。
    /// 抖动结束后镜头回到原状，不留下任何世界状态，因此落终态为空实现。
    /// </summary>
    public sealed class CameraShakeAction : StoryAction
    {
        private readonly float amplitude;
        private readonly float duration;

        /// <summary>创建一个镜头抖动动作。</summary>
        public CameraShakeAction(float amplitude, float duration)
        {
            this.amplitude = Mathf.Max(0f, amplitude);
            this.duration = Mathf.Max(0f, duration);
        }

        /// <inheritdoc />
        public override UniTask PlayAsync(StoryContext context, CancellationToken cancellationToken)
        {
            return context.RequireServices().RequireCamera().ShakeAsync(amplitude, duration, cancellationToken);
        }

        /// <inheritdoc />
        public override void Settle(StoryContext context)
        {
            // 纯表现动作：跳过时整体略过。
        }
    }
}
