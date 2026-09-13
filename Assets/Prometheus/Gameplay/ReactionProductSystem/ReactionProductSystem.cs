using System.Collections.Generic;
using UnityEngine;
using Xuan.Prometheus.Combat;
using Xuan.Prometheus.Component;
using Xuan.Prometheus.Effects;
using Xuan.Prometheus.Elements;
using Xuan.Prometheus.Logic;
using Cfg = global::Prometheus.Config;

namespace Xuan.Prometheus.Reactions
{
    /// <summary>
    /// 把伪元素状态变成可见后果：冻结让目标动不了，草原核到期炸出草伤。
    ///
    /// 三条职责线在这里汇合，各自仍然只做自己那一件事：
    /// - `IElementSystem` 拥有状态与它的时钟，只广播事实；
    /// - `EffectDefinition` 资产承载表现（Buff 图标、控制状态），不持有时长；
    /// - `DamagePipeline` / `DamageSettlement` 负责数值与落地。
    ///
    /// 产物 Effect 一律配成 `Permanent`：它的存活时间由元素系统的状态决定，
    /// 在资产上再写一份时长会造成两个时钟，一旦不同步就会出现「状态没了但还冻着」。
    /// </summary>
    internal sealed class ReactionProductSystem : XSystem, IReactionProductSystem
    {
        /// <summary>状态的权威来源。</summary>
        private readonly IElementSystem elementSystem;
        /// <summary>按编号取回实体，用于把状态事件落到具体的属性面板上。</summary>
        private readonly IEntitySystem entitySystem;
        /// <summary>产物 Effect 的施加与撤销入口。</summary>
        private readonly IEffectSystem effectSystem;

        /// <summary>记录每个 (目标, 状态) 当前挂着的产物 Effect，供状态消失时精确撤下。</summary>
        private readonly Dictionary<(int TargetEntityId, Cfg.AuraKey Key), EffectDefinition> activeProducts = new Dictionary<(int, Cfg.AuraKey), EffectDefinition>();

        /// <inheritdoc />
        public int ActiveProductCount => activeProducts.Count;

        /// <summary>创建反应产物系统；三个依赖都由组合根构造注入，顺序因此被强制。</summary>
        public ReactionProductSystem(IElementSystem elements, IEntitySystem entities, IEffectSystem effects)
        {
            elementSystem = elements ?? throw new System.ArgumentNullException(nameof(elements));
            entitySystem = entities ?? throw new System.ArgumentNullException(nameof(entities));
            effectSystem = effects ?? throw new System.ArgumentNullException(nameof(effects));
        }

        /// <summary>订阅状态变化。</summary>
        public override void AfterNew()
        {
            elementSystem.PseudoStateChanged += OnPseudoStateChanged;
        }

        /// <summary>退订并撤下全部仍挂着的产物。</summary>
        public override void Dispose()
        {
            elementSystem.PseudoStateChanged -= OnPseudoStateChanged;
            activeProducts.Clear();
        }

        /// <summary>把一次状态变化落实为产物的施加、撤销或引爆。</summary>
        private void OnPseudoStateChanged(PseudoStateChangedEvent change)
        {
            switch (change.Change)
            {
                case PseudoStateChange.Applied:
                    ApplyProduct(in change);
                    break;

                case PseudoStateChange.Expired:
                    RemoveProduct(in change);
                    // 自然到期才引爆：被超绽放或烈绽放消耗时，伤害由那次反应自己结算，
                    // 在这里再炸一次会让同一个草原核打出两笔伤害。
                    Detonate(in change);
                    break;

                case PseudoStateChange.Consumed:
                    RemoveProduct(in change);
                    break;
            }
        }

        /// <summary>施加状态对应的产物 Effect；该状态没有配产物时什么都不做。</summary>
        private void ApplyProduct(in PseudoStateChangedEvent change)
        {
            if (!TryResolveProduct(change.Reaction, out EffectDefinition definition)) return;
            if (!entitySystem.TryGetEntity(change.TargetEntityId, out Entity target)) return;
            entitySystem.TryGetEntity(change.SourceEntityId, out Entity source);

            effectSystem.Runtime.ApplyEffect(definition, source, target, source);
            activeProducts[(change.TargetEntityId, change.Key)] = definition;
        }

        /// <summary>撤下状态对应的产物 Effect。</summary>
        private void RemoveProduct(in PseudoStateChangedEvent change)
        {
            (int, Cfg.AuraKey) key = (change.TargetEntityId, change.Key);
            if (!activeProducts.TryGetValue(key, out EffectDefinition definition)) return;
            activeProducts.Remove(key);

            if (!entitySystem.TryGetEntity(change.TargetEntityId, out Entity target)) return;
            IReadOnlyList<EffectInstance> active = effectSystem.Runtime.GetActiveEffects(target);
            for (int index = active.Count - 1; index >= 0; index--)
                if (ReferenceEquals(active[index].Definition, definition))
                    effectSystem.Runtime.RemoveEffect(active[index], EffectRemovalReason.Dispelled);
        }

        /// <summary>
        /// 草原核到期时引爆，对承载它的目标结算一笔剧变伤害。
        ///
        /// 只有配了伤害元素的状态才会引爆：冻结与原激化的反应行 `damageElement` 为 `None`，
        /// 到期时静静消失。这是配表决定的，不是按状态键写死的分支。
        /// </summary>
        private void Detonate(in PseudoStateChangedEvent change)
        {
            Cfg.ReactionMatrixRow row = change.Reaction;
            if (row == null || row.DamageElement == Cfg.ElementType.None || row.Multiplier <= 0f) return;
            if (!entitySystem.TryGetEntity(change.TargetEntityId, out Entity target)) return;
            if (!target.TryGetComp(out PropertyComponent targetProperty) || targetProperty.IsDead) return;

            entitySystem.TryGetEntity(change.SourceEntityId, out Entity source);
            PropertyComponent sourceProperty = source != null && source.TryGetComp(out PropertyComponent resolved) ? resolved : null;
            int sourceLevel = source != null && source.TryGetComp(out CharaLevelComponent level) ? level.CurrentLevel : DamagePipeline.DefaultLevel;

            float damage = DamagePipeline.ResolveTransformative(row, sourceProperty, targetProperty, sourceLevel);
            if (damage <= 0f) return;

            Vector3 position = target.bindGo != null ? target.bindGo.transform.position : Vector3.zero;
            // 引爆自成一条因果链：它发生在命中之后很久，挂到当初那次命中下面会让因果深度无限增长。
            DamageSettlementContext settlement = new DamageSettlementContext(source, target, source, row.ReactionId, 0L, position, null);
            // 引爆不打断、不暴击：打断由触发它的那一击负责，剧变伤害本身不能暴击（05 第 5.3 节）。
            DamageFacts facts = new DamageFacts(row.DamageElement, DamageActionType.Effect, row.ReactionId, 0, false, false);
            DamageSettlement.Settle(effectSystem.Runtime, targetProperty, damage, EffectTag.Periodic, in facts, in settlement);
        }

        /// <summary>按写入该状态的反应行取出产物 Effect 定义；效果库尚未加载或未配置该产物时返回 false。</summary>
        private bool TryResolveProduct(Cfg.ReactionMatrixRow row, out EffectDefinition definition)
        {
            definition = null;
            EffectLibrary library = effectSystem.DefaultLibrary;
            if (library == null || row == null) return false;
            definition = library.GetReactionProduct(row.EffectId);
            return definition != null;
        }
    }
}
