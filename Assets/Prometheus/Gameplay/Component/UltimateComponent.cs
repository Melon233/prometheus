using UnityEngine;
using Xuan.Prometheus.Component;

namespace Xuan.Prometheus.Logic.Talent
{
    /// <summary>保存大招使用的静态配置、命中代理和每个玩家实体独立拥有的冷却运行态。</summary>
    public sealed class UltimateComponent : Component.MonoComponent, ITalentGrowthComponent
    {
        [SerializeField] private TalentConfig talentConfig;
        [SerializeField] private ColliderProxy colliderProxy;
        [SerializeField] private string abilityId = "Player.Ultimate";
        /// <summary>保存大招自己的 Debug 等级配置、运行时等级副本和可修改增益系数。</summary>
        [SerializeField] private TalentGrowthState talentGrowth = new TalentGrowthState();
        /// <summary>保存当前大招剩余冷却，并通过统一属性脏监听向 UI 暴露变化。</summary>
        private readonly ModifiableProperty cooldownRemaining = new ModifiableProperty();

        /// <summary>获取当前角色全部战斗能力共享的数值配置。</summary>
        public TalentConfig TalentConfig => talentConfig;

        /// <inheritdoc />
        public TalentAbilityType TalentAbilityType => TalentAbilityType.Ultimate;

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

        /// <summary>获取大招碰撞代理。</summary>
        public ColliderProxy ColliderProxy => colliderProxy;

        /// <summary>获取写入 HitConfirmed 的大招能力编号。</summary>
        public string AbilityId => string.IsNullOrWhiteSpace(abilityId) ? "Player.Ultimate" : abilityId.Trim();

        /// <summary>获取 TalentConfig 配置的大招完整冷却秒数。</summary>
        public float CooldownDuration => talentConfig == null ? 0f : talentConfig.UltimateCooldown;

        /// <summary>获取当前非负大招剩余冷却秒数。</summary>
        public float CooldownRemaining => Mathf.Max(0f, cooldownRemaining.Value);

        /// <summary>获取当前大招剩余冷却字段的可监听属性对象。</summary>
        public ModifiableProperty CooldownRemainingProperty => cooldownRemaining;

        /// <summary>获取当前是否已经结束冷却并允许能量门禁继续判断。</summary>
        public bool IsCooldownReady => CooldownRemaining <= 0f;

        /// <summary>同时检查正数满能量与冷却完成条件，作为 UltimateLogic 唯一释放门禁。</summary>
        public bool CanRelease(PropertyComponent property)
        {
            return property != null && property.IsUltEnergyFull && IsCooldownReady;
        }

        /// <summary>实体初始化时建立无冷却运行态，避免把 Prefab 或 ScriptableObject 当作运行时状态容器。</summary>
        public void InitializeRuntimeState()
        {
            cooldownRemaining.SetValue(0f);
        }

        /// <summary>大招成功开始后按当前配置写入完整冷却时间。</summary>
        public void BeginCooldown()
        {
            cooldownRemaining.SetValue(CooldownDuration);
        }

        /// <summary>使用非负帧时间推进冷却，并返回剩余时间是否实际改变，供 HUD 避免重复事件。</summary>
        public bool AdvanceCooldown(float deltaTime)
        {
            float previousRemaining = CooldownRemaining;
            cooldownRemaining.SetValue(Mathf.Max(0f, previousRemaining - Mathf.Max(0f, deltaTime)));
            return !Mathf.Approximately(previousRemaining, CooldownRemaining);
        }
    }
}
