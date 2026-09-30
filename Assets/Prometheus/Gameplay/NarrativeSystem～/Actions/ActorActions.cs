using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;

namespace Xuan.Prometheus.Narrative
{
    /// <summary>
    /// 角色或场景物体的位移动作。
    /// 终点位置属于世界状态，因此落终态直接把目标放到终点，跳过之后的场景与完整演绎一致。
    /// </summary>
    public sealed class ActorMoveAction : StoryAction
    {
        private readonly ActorRef actor;
        private readonly Anchor destination;
        private readonly float duration;
        private readonly bool faceMoveDirection;

        /// <summary>创建一个位移动作。</summary>
        /// <param name="actor">被移动的角色或场景物体。</param>
        /// <param name="destination">终点。</param>
        /// <param name="duration">移动时长；小于等于零表示瞬移。</param>
        /// <param name="faceMoveDirection">移动过程中是否朝向前进方向。</param>
        public ActorMoveAction(ActorRef actor, Anchor destination, float duration, bool faceMoveDirection = false)
        {
            this.actor = actor;
            this.destination = destination;
            this.duration = Mathf.Max(0f, duration);
            this.faceMoveDirection = faceMoveDirection;
        }

        /// <inheritdoc />
        public override async UniTask PlayAsync(StoryContext context, CancellationToken cancellationToken)
        {
            StageScope stage = context.RequireStage();
            ActorHandle handle = stage.RequireActor(actor);
            Vector3 target = destination.Resolve(stage.Services.Actors);
            Vector3 start = handle.Transform.position;
            if (faceMoveDirection && (target - start).sqrMagnitude > 0.0001f) handle.Transform.rotation = Quaternion.LookRotation((target - start).normalized, Vector3.up);
            if (duration <= 0f)
            {
                handle.Transform.position = target;
                return;
            }
            float elapsed = 0f;
            while (elapsed < duration)
            {
                cancellationToken.ThrowIfCancellationRequested();
                await UniTask.Yield(PlayerLoopTiming.Update, cancellationToken);
                elapsed += Time.deltaTime;
                if (!handle.IsAlive) return;
                handle.Transform.position = Vector3.Lerp(start, target, Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(elapsed / duration)));
            }
            handle.Transform.position = target;
        }

        /// <inheritdoc />
        public override void Settle(StoryContext context)
        {
            StageScope stage = context.RequireStage();
            if (!stage.TryGetActor(actor, out ActorHandle handle)) return;
            handle.Transform.position = destination.Resolve(stage.Services.Actors);
        }
    }

    /// <summary>角色或场景物体的朝向动作；朝向属于世界状态。</summary>
    public sealed class ActorFaceAction : StoryAction
    {
        private readonly ActorRef actor;
        private readonly ActorRef lookAt;
        private readonly Quaternion rotation;
        private readonly bool looksAtActor;

        /// <summary>创建一个朝向指定角色的动作。</summary>
        public ActorFaceAction(ActorRef actor, ActorRef lookAt)
        {
            this.actor = actor;
            this.lookAt = lookAt;
            looksAtActor = true;
        }

        /// <summary>创建一个朝向指定欧拉角的动作。</summary>
        public ActorFaceAction(ActorRef actor, Vector3 eulerAngles)
        {
            this.actor = actor;
            rotation = Quaternion.Euler(eulerAngles);
            looksAtActor = false;
        }

        /// <inheritdoc />
        public override UniTask PlayAsync(StoryContext context, CancellationToken cancellationToken)
        {
            Settle(context);
            return UniTask.CompletedTask;
        }

        /// <inheritdoc />
        public override void Settle(StoryContext context)
        {
            StageScope stage = context.RequireStage();
            if (!stage.TryGetActor(actor, out ActorHandle handle)) return;
            if (!looksAtActor)
            {
                handle.Transform.rotation = rotation;
                return;
            }
            if (!stage.TryGetActor(lookAt, out ActorHandle target)) return;
            Vector3 direction = target.Transform.position - handle.Transform.position;
            direction.y = 0f;
            if (direction.sqrMagnitude > 0.0001f) handle.Transform.rotation = Quaternion.LookRotation(direction.normalized, Vector3.up);
        }
    }

    /// <summary>角色或场景物体的显隐动作；显隐属于世界状态。</summary>
    public sealed class ActorVisibilityAction : StoryAction
    {
        private readonly ActorRef actor;
        private readonly bool visible;

        /// <summary>创建一个显隐动作。</summary>
        public ActorVisibilityAction(ActorRef actor, bool visible)
        {
            this.actor = actor;
            this.visible = visible;
        }

        /// <inheritdoc />
        public override UniTask PlayAsync(StoryContext context, CancellationToken cancellationToken)
        {
            Settle(context);
            return UniTask.CompletedTask;
        }

        /// <inheritdoc />
        public override void Settle(StoryContext context)
        {
            StageScope stage = context.RequireStage();
            if (stage.TryGetActor(actor, out ActorHandle handle)) handle.GameObject.SetActive(visible);
        }
    }
}
