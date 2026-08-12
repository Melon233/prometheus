using Xuan.Prometheus.Component;

namespace Xuan.Prometheus.Logic
{
    /// <summary>负责闪避玩法状态与 Logic 阻塞，完整动画会话结束前始终保持 Dodge 优先级所有权。</summary>
    public sealed class DodgeLogic : Logic
    {
        private AnimationPlayback playback;
        private InputComponent inputComponent;
        private SpineComponent spineComponent;
        private DodgeComponent dodgeComponent;
        /// <summary>保存水平速度运行态；闪避开始时只清除水平分量，竖直分量继续由 GravityLogic 更新。</summary>
        private MotionComponent motionComponent;

        public override void AfterNew()
        {
            OrderTag = OrderTag.Controller;
            ControlRequirement = LogicControlRequirement.Move;
            Entity.TryGetComp(out inputComponent);
            Entity.TryGetComp(out spineComponent);
            Entity.TryGetComp(out dodgeComponent);
            Entity.TryGetComp(out motionComponent);
        }

        public override bool CanEnable()
        {
            return inputComponent.wasDodgePressedThisFrame && !dodgeComponent.isDodging;
        }

        public override bool CanDisable()
        {
            return !dodgeComponent.isDodging;
        }

        public override void OnEnable()
        {
            dodgeComponent.isDodging = true;
            Entity.BlockLogic<RotateLogic>();
            Entity.BlockLogic<GroundMoveLogic>();
            Entity.BlockLogic<AirMoveLogic>();
            motionComponent.curVelo.x = 0f;
            motionComponent.curVelo.z = 0f;
            DodgeExecutor configuration = spineComponent.animationLib.dodgeExecutor;
            playback = spineComponent.TryPlay(configuration.GetSemantic(inputComponent.moveDir.x != 0f), AnimationOwner.Dodge, AnimationPriority.Dodge, false);
            if (playback == null)
            {
                dodgeComponent.isDodging = false;
                return;
            }
            playback.Finished += OnAnimationFinished;
        }

        /// <summary>仅以动画会话的自然完成或高优先级抢占作为闪避结束条件，避免命中窗口事件提前释放 Idle 播放权。</summary>
        private void OnAnimationFinished(AnimationPlayback source, AnimationEndReason reason)
        {
            if (!ReferenceEquals(source, playback)) return;
            dodgeComponent.isDodging = false;
        }

        public override void OnDisable()
        {
            if (playback != null)
            {
                playback.Finished -= OnAnimationFinished;
            }
            spineComponent.Stop(AnimationOwner.Dodge);
            playback = null;
            dodgeComponent.isDodging = false;
            Entity.UnBlockLogic<GroundMoveLogic>();
            Entity.UnBlockLogic<RotateLogic>();
            Entity.UnBlockLogic<AirMoveLogic>();
        }

        public override void OnUpdate(float dt)
        {
        }

        public override void OnDispose()
        {
            if (playback != null)
            {
                playback.Finished -= OnAnimationFinished;
            }
            playback = null;
            motionComponent = null;
        }
    }
}
