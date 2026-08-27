using System;
using System.Collections.Generic;
using UnityEngine;
using Xuan.Prometheus.Component;
using Xuan.Prometheus.Effects;
using Xuan.Prometheus.Growth;

namespace Xuan.Prometheus.Logic
{
    /// <summary>管理武器等级经验，并通过一个永久 tiersEffect 汇总当前武器全部固定与系数词条。</summary>
    public sealed class WeaponLogic : Logic
    {
        /// <summary>保存当前角色独占的武器数据组件。</summary>
        private WeaponComponent weaponComponent;
        /// <summary>保存当前武器全部词条的唯一永久 Effect 投影。</summary>
        private RuntimePermanentEffectProjection tiersProjection;
        /// <summary>复用武器 TierInstance 当前贡献解析缓冲区。</summary>
        private readonly List<ResolvedTierContribution> resolvedTiers = new List<ResolvedTierContribution>();
        /// <summary>标记武器跨过整数等级后需要在安全更新边界重建 Effect。</summary>
        private bool projectionDirty;

        /// <summary>武器属于常驻玩法数据，不受控制状态或上下场状态暂停。</summary>
        public WeaponLogic()
        {
            OrderTag = OrderTag.Gameplay;
            ControlRequirement = LogicControlRequirement.None;
        }

        /// <summary>初始化 Debug 武器数据副本，并立即建立汇总全部武器词条的永久 tiersEffect。</summary>
        public override void AfterNew()
        {
            if (!Entity.TryGetComp(out weaponComponent)) throw new InvalidOperationException("WeaponLogic requires WeaponComponent.");
            if (!Entity.TryGetComp(out EffectComponent effectComponent)) throw new InvalidOperationException("WeaponLogic requires EffectComponent.");
            weaponComponent.InitializeRuntimeData();
            weaponComponent.Changed += OnWeaponDataChanged;
            tiersProjection = new RuntimePermanentEffectProjection(effectComponent.Runtime, Entity, "Weapon.Tiers");
            RebuildTiersProjection();
        }

        /// <summary>武器系统在 Entity 存活期间始终允许启用。</summary>
        public override bool CanEnable()
        {
            return true;
        }

        /// <summary>武器系统只随 Entity 最终释放而禁用。</summary>
        public override bool CanDisable()
        {
            return false;
        }

        /// <summary>全部初始化工作已在 AfterNew 完成。</summary>
        public override void OnEnable()
        {
        }

        /// <summary>临时控制状态不会移除武器永久 Effect。</summary>
        public override void OnDisable()
        {
        }

        /// <summary>只在武器整数等级变化并刷新 TierInstance 后重建永久 tiersEffect。</summary>
        public override void OnUpdate(float dt)
        {
            if (!projectionDirty) return;
            projectionDirty = false;
            RebuildTiersProjection();
        }

        /// <summary>移除武器永久 tiersEffect，使全部汇总 Modifier 同时精确回滚。</summary>
        public override void OnDispose()
        {
            if (weaponComponent != null) weaponComponent.Changed -= OnWeaponDataChanged;
            tiersProjection?.Dispose();
            tiersProjection = null;
            resolvedTiers.Clear();
            weaponComponent = null;
        }

        /// <summary>武器经验变化时只在整数等级实际改变后标记 Effect 投影脏。</summary>
        private void OnWeaponDataChanged(bool levelChanged)
        {
            if (levelChanged) projectionDirty = true;
        }

        /// <summary>按 PropertyType 与模式汇总武器 Tier 数值，并用一个永久 Effect 一次性应用。</summary>
        private void RebuildTiersProjection()
        {
            if (!weaponComponent.TryResolveAll(resolvedTiers, out string error)) throw new InvalidOperationException(error);
            Dictionary<(PropertyType, PropertyModifierMode), float> totals = new Dictionary<(PropertyType, PropertyModifierMode), float>();
            for (int index = 0; index < resolvedTiers.Count; index++)
            {
                ResolvedTierContribution tier = resolvedTiers[index];
                AddTotal(totals, tier.PropertyType, PropertyModifierMode.Offset, tier.CurrentOffset);
                AddTotal(totals, tier.PropertyType, PropertyModifierMode.Boost, tier.CurrentCoefficient);
            }
            List<EffectOperation> operations = new List<EffectOperation>(totals.Count);
            foreach (KeyValuePair<(PropertyType, PropertyModifierMode), float> total in totals) operations.Add(new PropertyModifierOperation(total.Key.Item1, total.Key.Item2, EffectValueFormula.Constant(total.Value)));
            tiersProjection.Replace(operations);
        }

        /// <summary>只汇总非零词条通道，避免为禁用的固定值或系数值创建无意义 Modifier。</summary>
        private static void AddTotal(Dictionary<(PropertyType, PropertyModifierMode), float> totals, PropertyType propertyType, PropertyModifierMode modifierMode, float value)
        {
            if (Mathf.Approximately(value, 0f)) return;
            (PropertyType, PropertyModifierMode) key = (propertyType, modifierMode);
            totals.TryGetValue(key, out float previousValue);
            totals[key] = previousValue + value;
        }
    }
}
