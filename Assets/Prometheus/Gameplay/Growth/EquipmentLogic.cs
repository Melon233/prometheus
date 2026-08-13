using System;
using System.Collections.Generic;
using UnityEngine;
using Xuan.Prometheus.Component;
using Xuan.Prometheus.Effects;
using Xuan.Prometheus.Growth;

namespace Xuan.Prometheus.Logic
{
    /// <summary>汇总全部当前装备 Tier，并通过一个可替换的永久 tiersEffect 修改角色战斗属性。</summary>
    public sealed class EquipmentLogic : Logic
    {
        /// <summary>保存当前角色独占的装备数据组件。</summary>
        private EquipmentComponent equipmentComponent;
        /// <summary>保存全部装备汇总后的永久 Effect 投影。</summary>
        private RuntimePermanentEffectProjection tiersProjection;
        /// <summary>复用全部装备 TierInstance 当前贡献解析缓冲区。</summary>
        private readonly List<ResolvedTierContribution> resolvedTiers = new List<ResolvedTierContribution>();
        /// <summary>标记装备变化后需要在安全更新边界重建 Effect。</summary>
        private bool projectionDirty;

        /// <summary>装备属于常驻玩法数据，不受控制状态或上下场状态暂停。</summary>
        public EquipmentLogic()
        {
            OrderTag = OrderTag.Gameplay;
            ControlRequirement = LogicControlRequirement.None;
        }

        /// <summary>初始化 Debug 当前装备副本，并立即建立汇总全部装备词条的永久 tiersEffect。</summary>
        public override void AfterNew()
        {
            if (!Entity.TryGetComp(out equipmentComponent)) throw new InvalidOperationException("EquipmentLogic requires EquipmentComponent.");
            if (!Entity.TryGetComp(out EffectComponent effectComponent)) throw new InvalidOperationException("EquipmentLogic requires EffectComponent.");
            equipmentComponent.InitializeRuntimeData();
            equipmentComponent.Changed += OnEquipmentChanged;
            tiersProjection = new RuntimePermanentEffectProjection(effectComponent.Runtime, Entity, "Equipment.Tiers");
            RebuildTiersProjection();
        }

        /// <summary>装备系统在 Entity 存活期间始终允许启用。</summary>
        public override bool CanEnable()
        {
            return true;
        }

        /// <summary>装备系统只随 Entity 最终释放而禁用。</summary>
        public override bool CanDisable()
        {
            return false;
        }

        /// <summary>全部初始化工作已在 AfterNew 完成。</summary>
        public override void OnEnable()
        {
        }

        /// <summary>临时状态不会移除装备永久 Effect。</summary>
        public override void OnDisable()
        {
        }

        /// <summary>在装备列表变化后的安全边界重建唯一 tiersEffect。</summary>
        public override void OnUpdate(float dt)
        {
            if (!projectionDirty) return;
            projectionDirty = false;
            RebuildTiersProjection();
        }

        /// <summary>注销装备监听并移除唯一 tiersEffect，使全部汇总 Modifier 同时精确回滚。</summary>
        public override void OnDispose()
        {
            if (equipmentComponent != null) equipmentComponent.Changed -= OnEquipmentChanged;
            tiersProjection?.Dispose();
            tiersProjection = null;
            resolvedTiers.Clear();
            equipmentComponent = null;
        }

        /// <summary>装备变化时只标记投影脏，避免在外部换装调用栈中重入 EffectRuntime。</summary>
        private void OnEquipmentChanged(bool projectionChanged)
        {
            if (projectionChanged) projectionDirty = true;
        }

        /// <summary>按 PropertyType 与模式汇总全部 Tier 数值，并用一个永久 Effect 一次性应用。</summary>
        private void RebuildTiersProjection()
        {
            if (!equipmentComponent.TryResolveAll(resolvedTiers, out string error)) throw new InvalidOperationException(error);
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
