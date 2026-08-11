using System;
using Xuan.Prometheus.Component;
using Xuan.Prometheus.Logic.Talent;

namespace Xuan.Prometheus.Logic
{
    /// <summary>只负责战斗心流被动初始化及跨动作的天赋组合，不再拥有任何具体攻击输入、动画或碰撞体。</summary>
    public sealed class TalentLogic : Logic
    {
        private CoreTalentComponent coreTalentComponent;
        private EffectComponent effectComponent;

        /// <summary>天赋组合属于常驻被动，不应被 Root、Silence、Stun 或受击动画暂停。</summary>
        public TalentLogic()
        {
            OrderTag = OrderTag.Gameplay;
            ControlRequirement = LogicControlRequirement.None;
        }

        /// <summary>缓存天赋数据并安装由实际伤害驱动的战斗心流触发规则。</summary>
        public override void AfterNew()
        {
            if (!Entity.TryGetComp(out coreTalentComponent)) throw new InvalidOperationException("TalentLogic requires CoreTalentComponent.");
            if (!Entity.TryGetComp(out effectComponent)) throw new InvalidOperationException("TalentLogic requires EffectComponent.");
            if (!Entity.TryGetComp(out AttackComponent attackComponent)) throw new InvalidOperationException("TalentLogic requires AttackComponent.");
            if (!Entity.TryGetComp(out SpecialAttackComponent specialAttackComponent)) throw new InvalidOperationException("TalentLogic requires SpecialAttackComponent.");
            if (!Entity.TryGetComp(out SkillComponent skillComponent)) throw new InvalidOperationException("TalentLogic requires SkillComponent.");
            if (!Entity.TryGetComp(out UltimateComponent ultimateComponent)) throw new InvalidOperationException("TalentLogic requires UltimateComponent.");
            TalentConfig talentConfig = attackComponent.TalentConfig;
            if (talentConfig == null) throw new InvalidOperationException("TalentLogic requires a shared TalentConfig.");
            if (!ReferenceEquals(talentConfig, specialAttackComponent.TalentConfig) || !ReferenceEquals(talentConfig, skillComponent.TalentConfig) || !ReferenceEquals(talentConfig, ultimateComponent.TalentConfig)) throw new InvalidOperationException("All player combat components must reference the same TalentConfig.");
            coreTalentComponent.BindConfig(talentConfig);
            effectComponent.RegisterCombatFlowTriggers(Entity);
        }

        /// <summary>实体存活期间始终启用天赋组合。</summary>
        public override bool CanEnable()
        {
            return true;
        }

        /// <summary>天赋组合只随实体生命周期释放。</summary>
        public override bool CanDisable()
        {
            return false;
        }

        /// <summary>战斗心流已经在初始化阶段注册，启用时无需重复安装。</summary>
        public override void OnEnable()
        {
        }

        /// <summary>临时状态不移除天赋规则。</summary>
        public override void OnDisable()
        {
        }

        /// <summary>当前没有需要逐帧执行的跨动作天赋组合。</summary>
        public override void OnUpdate(float dt)
        {
        }

        /// <summary>触发注册由 EffectComponent 统一释放，此处只清空组合逻辑引用。</summary>
        public override void OnDispose()
        {
            coreTalentComponent = null;
            effectComponent = null;
        }
    }
}
