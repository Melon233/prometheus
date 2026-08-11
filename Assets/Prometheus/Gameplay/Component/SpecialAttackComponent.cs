using System;
using UnityEngine;
using Xuan.Prometheus.Component;

namespace Xuan.Prometheus.Logic.Talent
{
    /// <summary>保存特殊攻击的 TalentConfig、碰撞体、能力编号和非序列化蓄力运行态。</summary>
    public class SpecialAttackComponent : Component.MonoComponent
    {
        [SerializeField] private TalentConfig talentConfig;
        [SerializeField] private ColliderProxy colliderProxy;
        [SerializeField] private string abilityId = "Player.SpecialAttack";
        [NonSerialized] public Timer specialTimer;
        [NonSerialized] public bool canSpecial = true;

        /// <summary>获取当前角色全部战斗能力共享的数值配置。</summary>
        public TalentConfig TalentConfig => talentConfig;

        /// <summary>获取特殊攻击碰撞代理。</summary>
        public ColliderProxy ColliderProxy => colliderProxy;

        /// <summary>获取写入 HitConfirmed 的特殊攻击能力编号。</summary>
        public string AbilityId => string.IsNullOrWhiteSpace(abilityId) ? "Player.SpecialAttack" : abilityId.Trim();

        /// <summary>按 TalentConfig 蓄力时间创建新的运行时计时器。</summary>
        public void InitializeRuntimeTimer()
        {
            specialTimer = new Timer(talentConfig == null ? 0f : talentConfig.SpecialAttack.ChargeDuration);
        }
    }
}
