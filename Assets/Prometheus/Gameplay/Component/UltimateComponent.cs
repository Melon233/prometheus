using UnityEngine;
using Xuan.Prometheus.Component;

namespace Xuan.Prometheus.Logic.Talent
{
    /// <summary>只保存大招使用的 TalentConfig、碰撞体和稳定能力编号。</summary>
    public class UltimateComponent : Component.MonoComponent
    {
        [SerializeField] private TalentConfig talentConfig;
        [SerializeField] private ColliderProxy colliderProxy;
        [SerializeField] private string abilityId = "Player.Ultimate";

        /// <summary>获取当前角色全部战斗能力共享的数值配置。</summary>
        public TalentConfig TalentConfig => talentConfig;

        /// <summary>获取大招碰撞代理。</summary>
        public ColliderProxy ColliderProxy => colliderProxy;

        /// <summary>获取写入 HitConfirmed 的大招能力编号。</summary>
        public string AbilityId => string.IsNullOrWhiteSpace(abilityId) ? "Player.Ultimate" : abilityId.Trim();
    }
}
