using System;
using Xuan.Prometheus.Logic.Talent;

namespace Xuan.Prometheus.Logic
{
    /// <summary>独立推进玩家技能冷却，使离场、Stun、Silence、跳跃或动作阻塞都不会暂停冷却。</summary>
    public sealed class SkillCooldownLogic : Logic
    {
        private SkillComponent skillComponent;

        /// <summary>缓存技能组件并初始化每个玩家实体独立拥有的冷却运行态。</summary>
        public override void AfterNew()
        {
            OrderTag = OrderTag.Gameplay;
            ControlRequirement = LogicControlRequirement.None;
            if (!Entity.TryGetComp(out skillComponent)) throw new InvalidOperationException("SkillCooldownLogic requires SkillComponent.");
            skillComponent.InitializeRuntimeState();
        }

        /// <summary>玩家实体存活期间始终启用冷却推进。</summary>
        public override bool CanEnable()
        {
            return true;
        }

        /// <summary>冷却 Logic 只随实体生命周期退出。</summary>
        public override bool CanDisable()
        {
            return false;
        }

        /// <summary>首次启用不需要发布快照，EntitySystem 注册监听时会立即同步当前冷却值。</summary>
        public override void OnEnable()
        {
        }

        /// <summary>外部控制状态不会停用当前 Logic，因此无需额外处理暂停恢复。</summary>
        public override void OnDisable()
        {
        }

        /// <summary>按非负帧时间推进冷却，ModifiableProperty 会在最终值变化时通知监听方。</summary>
        public override void OnUpdate(float dt)
        {
            skillComponent.AdvanceCooldown(dt);
        }

        /// <summary>实体回收时清除组件引用，不再发布失效玩家的 HUD 状态。</summary>
        public override void OnDispose()
        {
            skillComponent = null;
        }
    }
}
