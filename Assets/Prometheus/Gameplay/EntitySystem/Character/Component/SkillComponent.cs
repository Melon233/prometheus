using UnityEngine;
using Xuan.Prometheus.Component;

namespace Xuan.Prometheus.Logic.Talent
{
    /// <summary>保存技能使用的静态配置、命中代理和每个玩家实体独立拥有的冷却运行态。</summary>
    public sealed class SkillComponent : Component.Component, Component.IEntityBinderComponent, ITalentGrowthComponent
    {
        private TalentConfig talentConfig;
        private ColliderProxy colliderProxy;
        private string abilityId = "Player.Skill";
        /// <summary>保存技能自己的 Debug 等级配置、运行时等级副本和可修改增益系数。</summary>
        private TalentGrowthState talentGrowth = new TalentGrowthState();
        /// <summary>保存当前技能剩余冷却，并通过统一属性脏监听向 UI 暴露变化。</summary>
        private readonly ModifiableProperty cooldownRemaining = new ModifiableProperty();
        /// <summary>保存当前可用的充能层数，并通过统一属性脏监听向 UI 暴露变化。</summary>
        private readonly ModifiableProperty charges = new ModifiableProperty();
        /// <summary>保存属性面板引用，用于读取冷却缩减。</summary>
        private Xuan.Prometheus.Component.PropertyComponent propertyComponent;

        /// <summary>获取当前角色全部战斗能力共享的数值配置。</summary>
        public TalentConfig TalentConfig => talentConfig;

        /// <inheritdoc />
        public TalentAbilityType TalentAbilityType => TalentAbilityType.Skill;

        /// <inheritdoc />
        public int TalentLevel => talentGrowth.CurrentTalentLevel;

        /// <inheritdoc />
        public ModifiableProperty GainCoefficientProperty => talentGrowth.GainCoefficientProperty;

        /// <inheritdoc />
        public float GainCoefficient => talentGrowth.GainCoefficient;

        /// <inheritdoc />
        public float TalentScale => talentGrowth.TalentScale;

        /// <inheritdoc />
        public event System.Action TalentLevelChanged
        {
            add => talentGrowth.Changed += value;
            remove => talentGrowth.Changed -= value;
        }

        /// <inheritdoc />
        public void InitializeTalentGrowth(int maximumTalentLevel)
        {
            talentGrowth.InitializeRuntimeData(maximumTalentLevel);
        }

        /// <inheritdoc />
        public bool TrySetTalentLevel(int level)
        {
            return talentGrowth.TrySetTalentLevel(level);
        }

        /// <summary>获取技能碰撞代理。</summary>
        public ColliderProxy ColliderProxy => colliderProxy;

        /// <summary>获取写入 HitConfirmed 的技能能力编号。</summary>
        public string AbilityId => string.IsNullOrWhiteSpace(abilityId) ? "Player.Skill" : abilityId.Trim();

        /// <summary>
        /// 获取本次进入冷却时应当写入的秒数，已扣除冷却缩减。
        ///
        /// 缩减在**进入冷却的那一刻**结算一次，而不是每帧按当前缩减折算剩余时间：
        /// 后者会让冷却期间获得的缩减效果追溯性地缩短已经在走的冷却，
        /// 从而出现「切个装备冷却就好了」这类可被玩家利用的行为。
        /// </summary>
        public float CooldownDuration
        {
            get
            {
                if (talentConfig == null) return 0f;
                float reduction = propertyComponent == null ? 0f : propertyComponent.GetValue(Xuan.Prometheus.Component.PropertyType.CooldownReduction);
                if (reduction <= 0f) return talentConfig.SkillCooldown;
                // 05A 规定冷却缩减上限为 1；达到上限时技能没有冷却，但仍然逐层消耗充能。
                return reduction >= 1f ? 0f : talentConfig.SkillCooldown * (1f - reduction);
            }
        }

        /// <summary>获取技能的充能层数上限。</summary>
        public int MaxCharges => talentConfig == null ? 1 : talentConfig.SkillChargeCount;

        /// <summary>获取当前可用的充能层数。</summary>
        public int CurrentCharges => Mathf.Clamp(Mathf.RoundToInt(charges.Value), 0, MaxCharges);

        /// <summary>获取当前充能层数字段的可监听属性对象。</summary>
        public ModifiableProperty ChargesProperty => charges;

        /// <summary>获取当前非负技能剩余冷却秒数。</summary>
        public float CooldownRemaining => Mathf.Max(0f, cooldownRemaining.Value);

        /// <summary>获取当前技能剩余冷却字段的可监听属性对象。</summary>
        public ModifiableProperty CooldownRemainingProperty => cooldownRemaining;

        /// <summary>获取当前技能是否还有可用充能层，即是否允许新的释放请求。</summary>
        public bool IsCooldownReady => CurrentCharges > 0;

        /// <summary>从唯一根 PlayerBinder 复制技能配置和命中引用，并创建独立天赋运行态。</summary>
        public void Bind(Xuan.Prometheus.Logic.EntityBinder binder)
        {
            PlayerBinder playerBinder = binder as PlayerBinder ?? throw new System.InvalidOperationException($"SkillComponent requires PlayerBinder but received '{binder?.GetType().FullName}'.");
            talentConfig = playerBinder.SkillTalentConfig;
            colliderProxy = playerBinder.SkillCollider;
            abilityId = playerBinder.SkillAbilityId;
            talentGrowth = playerBinder.SkillTalentGrowth?.CloneTemplate() ?? new TalentGrowthState();
            Entity.TryGetComp(out propertyComponent);
        }

        /// <summary>解除技能配置和碰撞代理引用。</summary>
        public void Unbind()
        {
            talentConfig = null;
            colliderProxy = null;
            propertyComponent = null;
        }

        /// <summary>实体初始化时建立无冷却运行态，避免把 Prefab 或 ScriptableObject 当作运行时状态容器。</summary>
        public void InitializeRuntimeState()
        {
            cooldownRemaining.SetValue(0f);
            // 充能技能出场即满层：冷却计时只在层数不满时才有意义。
            charges.SetValue(MaxCharges);
        }

        /// <summary>
        /// 技能动画成功取得主轨所有权后消耗一层充能，并在必要时开始冷却计时。
        ///
        /// 已经在走的冷却**不会被重置**：多段充能技能的每一层独立回复，
        /// 重置会让连放两层的玩家比只放一层的玩家更晚拿回第一层。
        /// </summary>
        public void BeginCooldown()
        {
            if (CurrentCharges <= 0) return;
            bool wasFull = CurrentCharges >= MaxCharges;
            charges.SetValue(CurrentCharges - 1);
            if (wasFull) cooldownRemaining.SetValue(CooldownDuration);
        }

        /// <summary>使用非负帧时间推进冷却，并返回剩余时间是否实际改变，供 HUD 避免重复刷新。</summary>
        public bool AdvanceCooldown(float deltaTime)
        {
            if (CurrentCharges >= MaxCharges) return false;

            float previousRemaining = CooldownRemaining;
            cooldownRemaining.SetValue(Mathf.Max(0f, previousRemaining - Mathf.Max(0f, deltaTime)));
            if (CooldownRemaining <= 0f)
            {
                charges.SetValue(CurrentCharges + 1);
                // 层数仍不满时立刻开始下一层的计时，使多层回复是连续的而不是每层要等一帧。
                cooldownRemaining.SetValue(CurrentCharges >= MaxCharges ? 0f : CooldownDuration);
            }
            return !Mathf.Approximately(previousRemaining, CooldownRemaining);
        }
    }
}
