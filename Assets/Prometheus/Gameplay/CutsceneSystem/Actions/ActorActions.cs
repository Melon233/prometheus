using System;
using System.Collections.Generic;
using UnityEngine;

namespace Xuan.Prometheus.Cutscene
{
    /// <summary>
    /// 演员移动到相对锚点的目标点。时长为 0 时瞬移（玩家只允许瞬移：玩家的行走动画由实体逻辑控制）。
    /// 非玩家演员移动时可指定移动动画与到达后的动画，并自动朝向移动方向。
    /// </summary>
    [Serializable]
    public sealed class ActorMoveAction : CutsceneAction
    {
        [SerializeField] [Tooltip("演员键。")]
        private string actor;

        [SerializeField] [Tooltip("目标位置（相对锚点）。")]
        private Vector3 targetPosition;

        [SerializeField] [Tooltip("移动时长（秒）；0 表示瞬移。")]
        private float duration;

        [SerializeField] [Tooltip("移动期间循环播放的动画；为空则不切换。仅非玩家演员。")]
        private string moveAnimation = "walk";

        [SerializeField] [Tooltip("到达后循环播放的动画；为空则不切换。仅非玩家演员。")]
        private string arriveAnimation = "idle";

        /// <inheritdoc />
        public override string Channel => $"actor:{actor}:move";

        /// <inheritdoc />
        public override CutsceneActionRun Begin(CutsceneContext context)
        {
            CutsceneActor target = context.GetActor(actor);
            Vector3 end = context.ToWorld(targetPosition);
            if (duration <= 0f)
            {
                target.SetPosition(end);
                return null;
            }
            // 横向位移决定朝向；纯纵深移动保持原朝向。
            float dx = end.x - target.Position.x;
            if (Mathf.Abs(dx) > 0.01f) target.SetFaceRight(dx > 0f);
            if (!string.IsNullOrEmpty(moveAnimation)) target.PlayAnimation(moveAnimation, true, null);
            return new Run(this, target, target.Position, end);
        }

        /// <inheritdoc />
        public override void ApplyFinalState(CutsceneContext context)
        {
            CutsceneActor target = context.GetActor(actor);
            target.SetPosition(context.ToWorld(targetPosition));
            if (duration > 0f && !string.IsNullOrEmpty(arriveAnimation)) target.PlayAnimation(arriveAnimation, true, null);
        }

        /// <inheritdoc />
        public override void Validate(CutsceneDefinition definition, string where, List<string> errors)
        {
            // 只有带时长的移动会切动画，因此瞬移允许玩家，平滑移动不允许。
            ValidateActor(definition, actor, duration <= 0f, where, errors);
        }

        /// <summary>一次平滑移动的运行体。</summary>
        private sealed class Run : CutsceneActionRun
        {
            private readonly ActorMoveAction action;
            private readonly CutsceneActor target;
            private readonly Vector3 start;
            private readonly Vector3 end;
            private float elapsed;

            public Run(ActorMoveAction action, CutsceneActor target, Vector3 start, Vector3 end)
            {
                this.action = action;
                this.target = target;
                this.start = start;
                this.end = end;
            }

            /// <inheritdoc />
            public override bool Tick(float dt)
            {
                elapsed += dt;
                float t = Mathf.Clamp01(elapsed / action.duration);
                target.SetPosition(Vector3.Lerp(start, end, t));
                return t >= 1f;
            }

            /// <inheritdoc />
            public override void Finish()
            {
                target.SetPosition(end);
                if (!string.IsNullOrEmpty(action.arriveAnimation)) target.PlayAnimation(action.arriveAnimation, true, null);
            }
        }
    }

    /// <summary>设置演员左右朝向，瞬时生效。</summary>
    [Serializable]
    public sealed class ActorFaceAction : CutsceneAction
    {
        [SerializeField] [Tooltip("演员键。")]
        private string actor;

        [SerializeField] [Tooltip("是否面朝右。")]
        private bool faceRight = true;

        /// <inheritdoc />
        public override CutsceneActionRun Begin(CutsceneContext context)
        {
            ApplyFinalState(context);
            return null;
        }

        /// <inheritdoc />
        public override void ApplyFinalState(CutsceneContext context) => context.GetActor(actor).SetFaceRight(faceRight);

        /// <inheritdoc />
        public override void Validate(CutsceneDefinition definition, string where, List<string> errors) => ValidateActor(definition, actor, true, where, errors);
    }

    /// <summary>设置演员显隐，瞬时生效。玩家被隐藏后会在演出结束时自动恢复可见。</summary>
    [Serializable]
    public sealed class ActorVisibleAction : CutsceneAction
    {
        [SerializeField] [Tooltip("演员键。")]
        private string actor;

        [SerializeField] [Tooltip("是否可见。")]
        private bool visible = true;

        /// <inheritdoc />
        public override CutsceneActionRun Begin(CutsceneContext context)
        {
            ApplyFinalState(context);
            return null;
        }

        /// <inheritdoc />
        public override void ApplyFinalState(CutsceneContext context) => context.GetActor(actor).SetVisible(visible);

        /// <inheritdoc />
        public override void Validate(CutsceneDefinition definition, string where, List<string> errors) => ValidateActor(definition, actor, true, where, errors);
    }

    /// <summary>
    /// 播放演员的 Spine 动画，瞬时生效（动画本身由 Spine 驱动）。
    /// 非循环动画可接续一个循环动画，例如 talk1 播完回到 idle。不支持玩家演员。
    /// </summary>
    [Serializable]
    public sealed class ActorAnimationAction : CutsceneAction
    {
        [SerializeField] [Tooltip("演员键（不能是玩家）。")]
        private string actor;

        [SerializeField] [Tooltip("Spine 动画名。")]
        private string animation = "idle";

        [SerializeField] [Tooltip("是否循环。")]
        private bool loop;

        [SerializeField] [Tooltip("非循环动画播完后接续的循环动画；为空表示停在最后一帧。")]
        private string thenLoopAnimation = "idle";

        /// <inheritdoc />
        public override CutsceneActionRun Begin(CutsceneContext context)
        {
            context.GetActor(actor).PlayAnimation(animation, loop, thenLoopAnimation);
            return null;
        }

        /// <inheritdoc />
        public override void ApplyFinalState(CutsceneContext context)
        {
            // 跳过时只保留会持续存在的结果：循环动画本身，或非循环动画之后接续的循环动画。
            CutsceneActor target = context.GetActor(actor);
            if (loop) target.PlayAnimation(animation, true, null);
            else if (!string.IsNullOrEmpty(thenLoopAnimation)) target.PlayAnimation(thenLoopAnimation, true, null);
        }

        /// <inheritdoc />
        public override void Validate(CutsceneDefinition definition, string where, List<string> errors)
        {
            ValidateActor(definition, actor, false, where, errors);
            if (string.IsNullOrWhiteSpace(animation)) errors.Add($"{where}：未配置动画名。");
        }
    }
}
