using System;
using System.Collections.Generic;
using UnityEngine;
using Xuan.Prometheus.Component;
using Xuan.Prometheus.Effects;
using Xuan.Prometheus.Logic.Talent;

namespace Xuan.Prometheus.Logic
{
    /// <summary>只负责战斗心流被动初始化及跨动作的天赋组合，不再拥有任何具体攻击输入、动画或碰撞体。</summary>
    public sealed class TalentLogic : Logic
    {
        private CoreTalentComponent coreTalentComponent;
        private EffectComponent effectComponent;
        /// <summary>保存四种技能 Component 的统一成长接口，等级数据仍分别归属各自 Component。</summary>
        private readonly List<ITalentGrowthComponent> talentComponents = new List<ITalentGrowthComponent>(4);
        /// <summary>保存四种天赋增益系数的唯一永久 Effect 投影。</summary>
        private RuntimePermanentEffectProjection talentProjection;
        /// <summary>标记任一技能等级变化后需要在安全更新边界重建永久 Effect。</summary>
        private bool projectionDirty;
        /// <summary>保存当前 Entity 从 TalentConfig 读取的天赋成长系数。</summary>
        private float talentGrowthCoefficient;

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
            talentGrowthCoefficient = talentConfig.TalentGrowthCoefficient;
            RegisterTalentComponent(attackComponent, talentConfig.MaximumTalentLevel);
            RegisterTalentComponent(specialAttackComponent, talentConfig.MaximumTalentLevel);
            RegisterTalentComponent(skillComponent, talentConfig.MaximumTalentLevel);
            RegisterTalentComponent(ultimateComponent, talentConfig.MaximumTalentLevel);
            talentProjection = new RuntimePermanentEffectProjection(effectComponent.Runtime, Entity, "Talent");
            RebuildTalentProjection();
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

        /// <summary>在任一技能等级变化后的安全边界重建唯一永久天赋 Effect。</summary>
        public override void OnUpdate(float dt)
        {
            if (!projectionDirty) return;
            projectionDirty = false;
            RebuildTalentProjection();
        }

        /// <summary>注销等级监听并移除永久天赋 Effect，EffectComponent 继续统一释放战斗触发注册。</summary>
        public override void OnDispose()
        {
            for (int index = 0; index < talentComponents.Count; index++) talentComponents[index].TalentLevelChanged -= OnTalentLevelChanged;
            talentComponents.Clear();
            talentProjection?.Dispose();
            talentProjection = null;
            coreTalentComponent = null;
            effectComponent = null;
        }

        /// <summary>初始化一个技能 Component 的 Debug 等级副本并订阅后续等级变化。</summary>
        private void RegisterTalentComponent(ITalentGrowthComponent talentComponent, int maximumTalentLevel)
        {
            if (talentComponent == null) throw new ArgumentNullException(nameof(talentComponent));
            talentComponent.InitializeTalentGrowth(maximumTalentLevel);
            talentComponent.TalentLevelChanged += OnTalentLevelChanged;
            talentComponents.Add(talentComponent);
        }

        /// <summary>等级变化时只标记投影脏，避免在外部升级调用栈内重入 EffectRuntime。</summary>
        private void OnTalentLevelChanged()
        {
            projectionDirty = true;
        }

        /// <summary>按基础值乘以一加等级成长系数的 Spec，向四个 Component 投影各自增益系数。</summary>
        private void RebuildTalentProjection()
        {
            List<EffectOperation> operations = new List<EffectOperation>(talentComponents.Count);
            for (int index = 0; index < talentComponents.Count; index++)
            {
                ITalentGrowthComponent talentComponent = talentComponents[index];
                float gainCoefficient = Mathf.Max(0f, talentComponent.TalentLevel - 1) * talentGrowthCoefficient;
                operations.Add(new TalentGainModifierOperation(talentComponent.TalentAbilityType, EffectValueFormula.Constant(gainCoefficient)));
            }
            talentProjection.Replace(operations);
        }
    }
}
