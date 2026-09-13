using System;
using UnityEngine;
using UnityEngine.Serialization;
using Xuan.Prometheus.Logic;
using Cfg = global::Prometheus.Config;

namespace Xuan.Prometheus.Effects
{
    /// <summary>
    /// EffectLibrary 集中保存默认战斗效果和两组触发规则，方便实体按职责注册并发出攻击信号。
    /// </summary>
    [CreateAssetMenu(menuName = "Prometheus/Effect System/Effect Library", fileName = "EffectLibrary")]
    public sealed class EffectLibrary : ScriptableObject
    {
        [SerializeField] private EffectDefinition directDamage;
        [SerializeField] private EffectDefinition burning;
        [SerializeField] private EffectDefinition combatFlow;
        [SerializeField, FormerlySerializedAs("stiffness")] private EffectDefinition stun;
        [SerializeField] private EffectTriggerSet attackTriggers;
        [SerializeField] private EffectTriggerSet combatFlowTriggers;

        /// <summary>
        /// 反应产物定义集合，按各自的 `EffectId` 被查找。
        ///
        /// 用集合而不是逐个具名字段：「哪一行反应产出哪个 Effect」已经写在 `ReactionMatrix`
        /// 的 `effectId` 列里，代码里再复述一遍就会有两个真相来源。
        /// </summary>
        [Header("反应产物")]
        [SerializeField] private EffectDefinition[] reactionProducts = System.Array.Empty<EffectDefinition>();

        /// <summary>获取单次攻击伤害定义。</summary>
        public EffectDefinition DirectDamage => directDamage;

        /// <summary>获取燃烧 DOT 定义。</summary>
        public EffectDefinition Burning => burning;

        /// <summary>获取可叠层属性增益定义。</summary>
        public EffectDefinition CombatFlow => combatFlow;

        /// <summary>获取禁止目标执行主动行为的眩晕定义。</summary>
        public EffectDefinition Stun => stun;

        /// <summary>获取普通攻击和燃烧附加触发规则。</summary>
        public EffectTriggerSet AttackTriggers => attackTriggers;

        /// <summary>获取造成非 DOT 攻击伤害时叠加属性增益的规则。</summary>
        public EffectTriggerSet CombatFlowTriggers => combatFlowTriggers;

        /// <summary>
        /// 按配表的 `effectId` 取出反应产物定义；未配置该产物时返回空。
        ///
        /// 产物是**可选**的：原激化即使没有配 Effect 也照常提供激化反应，
        /// 因为激化的数值全部来自反应矩阵，产物只承载表现。
        /// </summary>
        public EffectDefinition GetReactionProduct(string effectId)
        {
            if (string.IsNullOrEmpty(effectId)) return null;
            for (int index = 0; index < reactionProducts.Length; index++)
                if (reactionProducts[index] != null && reactionProducts[index].EffectId == effectId) return reactionProducts[index];
            return null;
        }

        /// <summary>
        /// 为实体注册命中伤害、燃烧和眩晕规则，并返回统一注销句柄。
        /// </summary>
        public IDisposable RegisterAttackTriggers(EffectRuntime runtime, Entity attacker)
        {
            if (runtime == null) throw new ArgumentNullException(nameof(runtime));
            if (attacker == null) throw new ArgumentNullException(nameof(attacker));
            return runtime.RegisterTriggerSet(attacker, attackTriggers);
        }

        /// <summary>
        /// 为实体注册由非 DOT 攻击伤害驱动的战意规则，并返回注销句柄。
        /// </summary>
        public IDisposable RegisterCombatFlowTriggers(EffectRuntime runtime, Entity attacker)
        {
            if (runtime == null) throw new ArgumentNullException(nameof(runtime));
            if (attacker == null) throw new ArgumentNullException(nameof(attacker));
            return runtime.RegisterTriggerSet(attacker, combatFlowTriggers);
        }

    }
}
