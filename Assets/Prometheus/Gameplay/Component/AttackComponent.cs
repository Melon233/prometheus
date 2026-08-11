using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Serialization;
using Xuan.Prometheus.Effects;

namespace Xuan.Prometheus.Component
{
    /// <summary>只保存一段普通攻击在角色预制体上的碰撞体引用和稳定能力编号。</summary>
    [Serializable]
    public sealed class NormalAttackHitBinding
    {
        [SerializeField] private ColliderProxy colliderProxy;
        [SerializeField] private string abilityId;

        /// <summary>获取本段命中窗口启用的独立碰撞代理。</summary>
        public ColliderProxy ColliderProxy => colliderProxy;

        /// <summary>按配置值或稳定连段下标生成本段命中能力编号。</summary>
        public string ResolveAbilityId(int stageIndex)
        {
            return string.IsNullOrWhiteSpace(abilityId) ? $"Player.NormalAttack.{stageIndex + 1}" : abilityId.Trim();
        }
    }

    /// <summary>表示普通攻击 Logic 合并场景绑定与 TalentConfig 数值后得到的只读命中配置。</summary>
    public readonly struct NormalAttackHitSelection
    {
        /// <summary>创建一份不会再依赖可变序列化列表的普通攻击命中快照。</summary>
        public NormalAttackHitSelection(ColliderProxy colliderProxy, float damageMultiplier, float damageOffset, EffectTag additionalTags, string abilityId)
        {
            ColliderProxy = colliderProxy;
            DamageMultiplier = Mathf.Max(0f, damageMultiplier);
            DamageOffset = damageOffset;
            AdditionalTags = additionalTags;
            AbilityId = abilityId ?? string.Empty;
        }

        /// <summary>获取本段使用的碰撞代理。</summary>
        public ColliderProxy ColliderProxy { get; }

        /// <summary>获取本段伤害倍率。</summary>
        public float DamageMultiplier { get; }

        /// <summary>获取本段伤害固定偏移。</summary>
        public float DamageOffset { get; }

        /// <summary>获取本段追加的效果标签。</summary>
        public EffectTag AdditionalTags { get; }

        /// <summary>获取本段写入 HitConfirmed 的能力编号。</summary>
        public string AbilityId { get; }
    }

    /// <summary>保存普通攻击场景引用和运行态；所有可调数值统一从角色 TalentConfig 读取。</summary>
    public class AttackComponent : MonoComponent
    {
        [SerializeField] private TalentConfig talentConfig;
        [SerializeField] private List<NormalAttackHitBinding> attackHits = new List<NormalAttackHitBinding>();
        [SerializeField, HideInInspector, FormerlySerializedAs("atkCollider")] private ColliderProxy legacyAttackCollider;
        [NonSerialized] public bool canCombo = true;
        [NonSerialized] public float elapsedComboTime;
        [NonSerialized] public int nextComboIndex;
        [NonSerialized] public AnimationPlayback currentAnimation;

        /// <summary>获取当前角色全部战斗能力共享的数值配置。</summary>
        public TalentConfig TalentConfig => talentConfig;

        /// <summary>获取当前显式配置的普通攻击命中段数。</summary>
        public int ConfiguredHitCount => attackHits == null ? 0 : attackHits.Count;

        /// <summary>获取敌人旧配置或单段攻击系统使用的首个碰撞代理。</summary>
        public ColliderProxy PrimaryHitbox => ConfiguredHitCount > 0 && attackHits[0] != null && attackHits[0].ColliderProxy != null ? attackHits[0].ColliderProxy : legacyAttackCollider;

        /// <summary>获取指定配置段的碰撞代理，供普通攻击 Logic 在初始化时集中绑定和关闭全部命中盒。</summary>
        public ColliderProxy GetConfiguredHitbox(int stageIndex)
        {
            if (attackHits == null || stageIndex < 0 || stageIndex >= attackHits.Count) return null;
            return attackHits[stageIndex]?.ColliderProxy;
        }

        /// <summary>按连段下标合并碰撞体与 AbilityId 绑定、TalentConfig 倍率、偏移和标签。</summary>
        public bool TryGetHitSelection(int stageIndex, out NormalAttackHitSelection selection)
        {
            if (attackHits != null && attackHits.Count > 0)
            {
                if (talentConfig == null || stageIndex < 0 || stageIndex >= attackHits.Count || attackHits[stageIndex] == null || attackHits[stageIndex].ColliderProxy == null || !talentConfig.NormalAttack.TryGetStage(stageIndex, out NormalAttackTalentStage values))
                {
                    selection = default;
                    return false;
                }
                NormalAttackHitBinding binding = attackHits[stageIndex];
                selection = new NormalAttackHitSelection(binding.ColliderProxy, values.DamageMultiplier, values.DamageOffset, values.AdditionalTags, binding.ResolveAbilityId(stageIndex));
                return true;
            }
            if (stageIndex < 0 || legacyAttackCollider == null)
            {
                selection = default;
                return false;
            }
            selection = new NormalAttackHitSelection(legacyAttackCollider, 1f, 0f, EffectTag.None, $"Player.NormalAttack.{stageIndex + 1}");
            return true;
        }
    }
}
