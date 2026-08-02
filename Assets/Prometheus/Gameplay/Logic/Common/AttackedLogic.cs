using Spine;
using Xuan.Prometheus.Component;
using Xuan.Prometheus.Logic;

namespace Xuan.Prometheus
{
    /// <summary>负责受击动画的开始与结束通知，并保证完成、打断和回收路径只结束同一次受击表现。</summary>
    public sealed class AttackedLogic : Logic.Logic
    {
        private EventComponent eventComponent;
        private SpineComponent spineComponent;
        private AttackedExecutor attackedExecutor;
        private TrackEntry trackEntry;
        private bool presentationActive;

        /// <summary>缓存受击表现依赖并订阅实体局部受击事件。</summary>
        public override void AfterNew()
        {
            ControlRequirement = LogicControlRequirement.None;
            Entity.TryGetComp(out eventComponent);
            Entity.TryGetComp(out spineComponent);
            attackedExecutor = spineComponent.animationLib.attackedExecutor;
            eventComponent.AddListener<AttackedEvent>(OnAttacked);
        }

        /// <summary>开始新的受击表现前先断开旧 TrackEntry 回调，避免连续受击累积匿名委托。</summary>
        private void OnAttacked(AttackedEvent evt)
        {
            DetachTrackCallbacks();
            if (presentationActive) eventComponent.Invoke(new AttackedEndEvent());
            eventComponent.Invoke(new AttackedStartEvent());
            presentationActive = true;
            trackEntry = attackedExecutor.Execute();
            if (trackEntry == null)
            {
                FinishPresentation(null);
                return;
            }
            trackEntry.Complete += OnTrackFinished;
            trackEntry.Interrupt += OnTrackFinished;
        }

        /// <summary>完成和打断共享同一个幂等结束入口，忽略已经被后续受击替换的旧动画回调。</summary>
        private void OnTrackFinished(TrackEntry entry)
        {
            FinishPresentation(entry);
        }

        /// <summary>结束当前受击表现并恰好发布一次 AttackedEndEvent。</summary>
        private void FinishPresentation(TrackEntry completedEntry)
        {
            if (completedEntry != null && !ReferenceEquals(completedEntry, trackEntry)) return;
            DetachTrackCallbacks();
            trackEntry = null;
            if (!presentationActive) return;
            presentationActive = false;
            eventComponent.Invoke(new AttackedEndEvent());
        }

        /// <summary>从当前 Spine TrackEntry 对称移除具名回调，使死亡和回收后不会触发失效 EventComponent。</summary>
        private void DetachTrackCallbacks()
        {
            if (trackEntry == null) return;
            trackEntry.Complete -= OnTrackFinished;
            trackEntry.Interrupt -= OnTrackFinished;
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

        /// <summary>对称注销受击监听和 Spine 回调，不在 Entity 回收阶段重新发布受击结束事件。</summary>
        public override void OnDispose()
        {
            if (eventComponent != null) eventComponent.RemoveListener<AttackedEvent>(OnAttacked);
            DetachTrackCallbacks();
            trackEntry = null;
            presentationActive = false;
            eventComponent = null;
            spineComponent = null;
            attackedExecutor = null;
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
