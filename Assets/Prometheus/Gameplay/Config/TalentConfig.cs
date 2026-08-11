using System;
using System.Collections.Generic;
using UnityEngine;
using Xuan.Prometheus.Effects;

namespace Xuan.Prometheus
{
    /// <summary>标记一个运行时以一倍为基准存储、但在 Inspector 中按百分比编辑和显示的浮点字段。</summary>
    [AttributeUsage(AttributeTargets.Field)]
    public sealed class PercentageAttribute : PropertyAttribute
    {
        /// <summary>创建百分比字段约束，并以运行时倍率单位保存允许的最小值。</summary>
        public PercentageAttribute(float minimumMultiplier = 0f)
        {
            MinimumMultiplier = minimumMultiplier;
        }

        /// <summary>获取转换回运行时倍率后允许的最小值。</summary>
        public float MinimumMultiplier { get; }
    }

    /// <summary>保存一次非普通攻击能力使用的伤害公式与动画速度数值。</summary>
    [Serializable]
    public sealed class TalentAbilityValues
    {
        [SerializeField, Percentage] private float damageMultiplier = 1f;
        [SerializeField] private float damageOffset;
        [SerializeField, Min(0f)] private float animationSpeed = 1f;

        /// <summary>获取能力伤害倍率。</summary>
        public float DamageMultiplier => Mathf.Max(0f, damageMultiplier);

        /// <summary>获取能力伤害固定偏移。</summary>
        public float DamageOffset => damageOffset;

        /// <summary>获取能力动画速度。</summary>
        public float AnimationSpeed => Mathf.Max(0f, animationSpeed);

        /// <summary>按照基础伤害乘倍率再加偏移的顺序计算非负请求伤害。</summary>
        public float CalculateDamage(float calculatedDamage)
        {
            return Mathf.Max(0f, Mathf.Max(0f, calculatedDamage) * DamageMultiplier + DamageOffset);
        }
    }

    /// <summary>保存一段普通攻击独立使用的伤害倍率、固定偏移和额外效果标签。</summary>
    [Serializable]
    public sealed class NormalAttackTalentStage
    {
        [SerializeField, Percentage] private float damageMultiplier = 1f;
        [SerializeField] private float damageOffset;
        [SerializeField] private EffectTag additionalTags;

        /// <summary>获取本段普通攻击伤害倍率。</summary>
        public float DamageMultiplier => Mathf.Max(0f, damageMultiplier);

        /// <summary>获取本段普通攻击伤害固定偏移。</summary>
        public float DamageOffset => damageOffset;

        /// <summary>获取本段在 Attack 与 NormalAttack 之外追加的效果标签。</summary>
        public EffectTag AdditionalTags => additionalTags;

        /// <summary>按照基础伤害乘倍率再加偏移的顺序计算非负请求伤害。</summary>
        public float CalculateDamage(float calculatedDamage)
        {
            return Mathf.Max(0f, Mathf.Max(0f, calculatedDamage) * DamageMultiplier + DamageOffset);
        }
    }

    /// <summary>集中保存普通攻击连段窗口和每一段的数值配置。</summary>
    [Serializable]
    public sealed class NormalAttackTalentValues
    {
        [SerializeField, Min(0f)] private float comboInterval = 2f;
        [SerializeField] private List<NormalAttackTalentStage> stages = new List<NormalAttackTalentStage>();

        /// <summary>获取普通攻击连段允许等待的最长时间。</summary>
        public float ComboInterval => Mathf.Max(0f, comboInterval);

        /// <summary>获取已配置的普通攻击数值段数。</summary>
        public int StageCount => stages == null ? 0 : stages.Count;

        /// <summary>按连段下标读取对应数值，缺少配置时明确返回失败。</summary>
        public bool TryGetStage(int stageIndex, out NormalAttackTalentStage stage)
        {
            if (stages != null && stageIndex >= 0 && stageIndex < stages.Count && stages[stageIndex] != null)
            {
                stage = stages[stageIndex];
                return true;
            }
            stage = null;
            return false;
        }
    }

    /// <summary>保存特殊攻击伤害、动画速度和蓄力时间。</summary>
    [Serializable]
    public sealed class SpecialAttackTalentValues
    {
        [SerializeField] private TalentAbilityValues ability = new TalentAbilityValues();
        [SerializeField, Min(0f)] private float chargeDuration = 0.5f;

        /// <summary>获取特殊攻击通用能力数值。</summary>
        public TalentAbilityValues Ability => ability ?? (ability = new TalentAbilityValues());

        /// <summary>获取长按普通攻击进入特殊攻击所需时间。</summary>
        public float ChargeDuration => Mathf.Max(0f, chargeDuration);
    }

    /// <summary>集中保存一个角色全部玩家战斗能力使用的纯数值配置。</summary>
    [CreateAssetMenu(menuName = "Prometheus/Talent Config", fileName = "TalentConfig")]
    public sealed class TalentConfig : ScriptableObject
    {
        [SerializeField] private NormalAttackTalentValues normalAttack = new NormalAttackTalentValues();
        [SerializeField] private SpecialAttackTalentValues specialAttack = new SpecialAttackTalentValues();
        [SerializeField] private TalentAbilityValues skill = new TalentAbilityValues();
        [SerializeField, Min(0f)] private float skillCooldown = 5f;
        [SerializeField] private TalentAbilityValues ultimate = new TalentAbilityValues();
        [SerializeField, Min(0f)] private float ultimateCooldown = 10f;

        /// <summary>获取普通攻击全部数值。</summary>
        public NormalAttackTalentValues NormalAttack => normalAttack ?? (normalAttack = new NormalAttackTalentValues());

        /// <summary>获取特殊攻击全部数值。</summary>
        public SpecialAttackTalentValues SpecialAttack => specialAttack ?? (specialAttack = new SpecialAttackTalentValues());

        /// <summary>获取技能全部数值。</summary>
        public TalentAbilityValues Skill => skill ?? (skill = new TalentAbilityValues());

        /// <summary>获取技能成功释放后进入的非负冷却秒数。</summary>
        public float SkillCooldown => Mathf.Max(0f, skillCooldown);

        /// <summary>获取大招全部数值。</summary>
        public TalentAbilityValues Ultimate => ultimate ?? (ultimate = new TalentAbilityValues());

        /// <summary>获取大招成功释放后进入的非负冷却秒数。</summary>
        public float UltimateCooldown => Mathf.Max(0f, ultimateCooldown);
    }
}
