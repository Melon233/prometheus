using Spine;
using Xuan.Prometheus.Component;
using Xuan.Prometheus.Logic;

namespace Xuan.Prometheus
{
    /// <summary>把唯一死亡事实转换为死亡动画和 Entity 回收请求，不参与伤害数值或胜负结算。</summary>
    public sealed class DieLogic : Logic.Logic
    {
        private SpineComponent spineComponent;
        private DieExecutor dieExecutor;
        private EventComponent eventComponent;
        private bool deathHandled;

        /// <summary>缓存死亡表现依赖并订阅实体局部死亡事件。</summary>
        public override void AfterNew()
        {
            ControlRequirement = LogicControlRequirement.None;
            Entity.TryGetComp(out spineComponent);
            Entity.TryGetComp(out eventComponent);
            dieExecutor = spineComponent.animationLib.dieExecutor;
            eventComponent.AddListener<DieEvent>(OnDie);
        }

        /// <summary>只处理首次死亡事件，并让 Entity 立即停止更新但保留 GameObject 完成死亡动画。</summary>
        private void OnDie(DieEvent evt)
        {
            if (deathHandled) return;
            deathHandled = true;
            TrackEntry entry = dieExecutor.Execute();
            float animationDuration = entry == null || entry.Animation == null ? 0f : entry.Animation.Duration;
            Entity.RequestDispose(animationDuration + 1f);
        }

        /// <inheritdoc />
        public override bool CanDisable()
        {
            return false;
        }

        /// <inheritdoc />
        public override bool CanEnable()
        {
            return true;
        }

        /// <inheritdoc />
        public override void OnDisable()
        {
        }

        /// <summary>对称注销死亡监听器，防止保留死亡动画的 GameObject 持续引用已经释放的 Logic。</summary>
        public override void OnDispose()
        {
            if (eventComponent != null) eventComponent.RemoveListener<DieEvent>(OnDie);
            eventComponent = null;
            spineComponent = null;
            dieExecutor = null;
        }

        /// <inheritdoc />
        public override void OnEnable()
        {
        }

        /// <inheritdoc />
        public override void OnUpdate(float dt)
        {
        }
    }
}
