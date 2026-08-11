using UnityEngine;
using Xuan.Prometheus.Component;

namespace Xuan.Prometheus.Logic.Talent
{
    /// <summary>保存技能使用的静态配置、命中代理和每个玩家实体独立拥有的冷却运行态。</summary>
    public sealed class SkillComponent : Component.MonoComponent
    {
        [SerializeField] private TalentConfig talentConfig;
        [SerializeField] private ColliderProxy colliderProxy;
        [SerializeField] private string abilityId = "Player.Skill";
        /// <summary>保存当前技能剩余冷却，并通过统一属性脏监听向 UI 暴露变化。</summary>
        private readonly ModifiableProperty cooldownRemaining = new ModifiableProperty();

        /// <summary>获取当前角色全部战斗能力共享的数值配置。</summary>
        public TalentConfig TalentConfig => talentConfig;

        /// <summary>获取技能碰撞代理。</summary>
        public ColliderProxy ColliderProxy => colliderProxy;

        /// <summary>获取写入 HitConfirmed 的技能能力编号。</summary>
        public string AbilityId => string.IsNullOrWhiteSpace(abilityId) ? "Player.Skill" : abilityId.Trim();

        /// <summary>获取 TalentConfig 配置的技能完整冷却秒数。</summary>
        public float CooldownDuration => talentConfig == null ? 0f : talentConfig.SkillCooldown;

        /// <summary>获取当前非负技能剩余冷却秒数。</summary>
        public float CooldownRemaining => Mathf.Max(0f, cooldownRemaining.Value);

        /// <summary>获取当前技能剩余冷却字段的可监听属性对象。</summary>
        public ModifiableProperty CooldownRemainingProperty => cooldownRemaining;

        /// <summary>获取当前技能是否已经结束冷却并允许新的释放请求。</summary>
        public bool IsCooldownReady => CooldownRemaining <= 0f;

        /// <summary>实体初始化时建立无冷却运行态，避免把 Prefab 或 ScriptableObject 当作运行时状态容器。</summary>
        public void InitializeRuntimeState()
        {
            cooldownRemaining.SetValue(0f);
        }

        /// <summary>技能动画成功取得主轨所有权后按当前配置写入完整冷却时间。</summary>
        public void BeginCooldown()
        {
            cooldownRemaining.SetValue(CooldownDuration);
        }

        /// <summary>使用非负帧时间推进冷却，并返回剩余时间是否实际改变，供 HUD 避免重复刷新。</summary>
        public bool AdvanceCooldown(float deltaTime)
        {
            float previousRemaining = CooldownRemaining;
            cooldownRemaining.SetValue(Mathf.Max(0f, previousRemaining - Mathf.Max(0f, deltaTime)));
            return !Mathf.Approximately(previousRemaining, CooldownRemaining);
        }
    }
}
