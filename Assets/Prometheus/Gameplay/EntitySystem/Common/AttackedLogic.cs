using Xuan.Prometheus.Component;
using Xuan.Prometheus.Logic;

namespace Xuan.Prometheus
{
    /// <summary>为全部可受击实体把 StaggeredEvent 映射为可重播的受击动画，并让 ControlState.Attacked 严格跟随动画会话生命周期。</summary>
    public sealed class AttackedLogic : Logic.Logic
    {
        private EventComponent eventComponent;
        private PropertyComponent propertyComponent;
        private SpineComponent spineComponent;
        /// <summary>保存可选的小队成员状态，使后台成员继续结算伤害与 Effect 时不会启动无法推进的受击动画。</summary>
        private TeamMemberComponent teamMemberComponent;
        /// <summary>保存当前受击动画会话，连续受击只替换会话而不提前退出受击状态。</summary>
        private AnimationPlayback playback;
        /// <summary>保存当前受击动画贡献的状态句柄，保证完成、中断、死亡和回收时精确释放。</summary>
        private ControlStateModifier attackedStateModifier;
        /// <summary>标记当前是否正在用新受击会话替换旧会话，避免旧会话的 Interrupted 回调产生瞬时状态退出。</summary>
        private bool replacingPlayback;
        /// <summary>记录死亡事实，阻止致死链路之后重新提交受击表现。</summary>
        private bool dead;

        /// <summary>获取当前 Logic 是否持有由受击动画创建的受击状态。</summary>
        public bool IsAttacked => attackedStateModifier != null;

        /// <summary>缓存属性与动画组件，并订阅成功打断和死亡事实。</summary>
        public override void AfterNew()
        {
            ControlRequirement = LogicControlRequirement.None;
            Entity.TryGetComp(out eventComponent);
            Entity.TryGetComp(out propertyComponent);
            Entity.TryGetComp(out spineComponent);
            Entity.TryGetComp(out teamMemberComponent);
            eventComponent.AddListener<StaggeredEvent>(OnStaggered);
            eventComponent.AddListener<DieEvent>(OnDie);
        }

        /// <summary>每次打断能力严格超过韧性的伤害都重播受击表现，受击状态由成功创建的播放会话决定。</summary>
        private void OnStaggered(StaggeredEvent evt)
        {
            if (dead || (teamMemberComponent != null && !teamMemberComponent.IsOnField)) return;
            PlayHitReaction();
        }

        /// <summary>以高于普通动作的优先级播放单段或双段受击序列，并在会话成功后进入受击状态。</summary>
        private void PlayHitReaction()
        {
            if (spineComponent == null || spineComponent.animationLib == null || spineComponent.animationLib.attackedExecutor == null) return;
            AttackedExecutor configuration = spineComponent.animationLib.attackedExecutor;
            replacingPlayback = playback != null;
            AnimationPlayback newPlayback;
            try
            {
                newPlayback = configuration.HasRecoveryAnimation ? spineComponent.TryPlaySequence(configuration.Semantic, configuration.RecoverySemantic, AnimationOwner.HitReaction, AnimationPriority.HitReaction, false, 1f, true) : spineComponent.TryPlay(configuration.Semantic, AnimationOwner.HitReaction, AnimationPriority.HitReaction, false, 1f, true);
            }
            finally
            {
                replacingPlayback = false;
            }
            if (newPlayback == null) return;
            playback = newPlayback;
            playback.Finished += OnAnimationFinished;
            EnterAttackedState();
        }

        /// <summary>只处理当前会话的结束；连续受击替换产生的 Interrupted 回调保留原有受击状态。</summary>
        private void OnAnimationFinished(AnimationPlayback source, AnimationEndReason reason)
        {
            if (!ReferenceEquals(source, playback)) return;
            playback = null;
            if (replacingPlayback && reason == AnimationEndReason.Interrupted) return;
            ExitAttackedState();
        }

        /// <summary>首次成功播放受击动画时添加唯一一份 Attacked 控制状态贡献。</summary>
        private void EnterAttackedState()
        {
            if (attackedStateModifier == null && propertyComponent != null) attackedStateModifier = propertyComponent.AddControlStateModifier(ControlState.Attacked);
        }

        /// <summary>受击动画最终结束时精确移除当前会话持有的 Attacked 状态贡献。</summary>
        private void ExitAttackedState()
        {
            if (attackedStateModifier == null) return;
            if (propertyComponent != null) propertyComponent.RemoveControlStateModifier(attackedStateModifier);
            attackedStateModifier = null;
        }

        /// <summary>死亡表现开始前停止当前受击轨、释放受击状态，并永久拒绝后续受击事件。</summary>
        private void OnDie(DieEvent evt)
        {
            dead = true;
            if (spineComponent != null) spineComponent.Stop(AnimationOwner.HitReaction);
            ExitAttackedState();
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

        /// <summary>回收时对称注销全部实体事件并清理组件引用。</summary>
        public override void OnDispose()
        {
            if (eventComponent != null) eventComponent.RemoveListener<StaggeredEvent>(OnStaggered);
            if (eventComponent != null) eventComponent.RemoveListener<DieEvent>(OnDie);
            if (playback != null) playback.Finished -= OnAnimationFinished;
            playback = null;
            replacingPlayback = false;
            ExitAttackedState();
            eventComponent = null;
            propertyComponent = null;
            spineComponent = null;
            teamMemberComponent = null;
            dead = false;
        }

        public override void OnEnable()
        {
        }

        public override void OnUpdate(float dt)
        {
        }
    }
}
