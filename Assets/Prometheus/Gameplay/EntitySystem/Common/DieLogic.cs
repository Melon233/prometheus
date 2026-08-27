using Xuan.Prometheus.Component;
using Xuan.Prometheus.Logic;

namespace Xuan.Prometheus
{
    /// <summary>把唯一死亡事实转换为最高优先级死亡 AnimationLine 和 Entity 延迟回收请求。</summary>
    public sealed class DieLogic : Logic.Logic
    {
        private SpineComponent spineComponent;
        private EventComponent eventComponent;
        private bool deathHandled;

        public override void AfterNew()
        {
            ControlRequirement = LogicControlRequirement.None;
            Entity.TryGetComp(out spineComponent);
            Entity.TryGetComp(out eventComponent);
            eventComponent.AddListener<DieEvent>(OnDie);
        }

        /// <summary>死亡动画拥有不可被其他现有动画抢占的最高优先级，并按最终片段时长保留场景对象。</summary>
        private void OnDie(DieEvent evt)
        {
            if (deathHandled) return;
            deathHandled = true;
            DieExecutor configuration = spineComponent.animationLib.dieExecutor;
            AnimationPlayback playback = spineComponent.TryPlay(configuration.Semantic, AnimationOwner.Death, AnimationPriority.Death, false, 1f, true);
            float animationDuration = playback == null ? 0f : playback.Duration;
            Entity.RequestDispose(animationDuration + 1f);
        }

        public override bool CanDisable()
        {
            return false;
        }

        public override bool CanEnable()
        {
            return true;
        }

        public override void OnDisable()
        {
        }

        public override void OnDispose()
        {
            if (eventComponent != null) eventComponent.RemoveListener<DieEvent>(OnDie);
            eventComponent = null;
            spineComponent = null;
        }

        public override void OnEnable()
        {
        }

        public override void OnUpdate(float dt)
        {
        }
    }
}
