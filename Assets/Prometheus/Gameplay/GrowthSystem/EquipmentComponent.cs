using System;
using System.Collections.Generic;
using UnityEngine;
using Xuan.Prometheus.Growth;

namespace Xuan.Prometheus.Component
{
    /// <summary>持有装备槽位、成长曲线、档位预设、Debug 配置和每个 PlayerEntity 独占的 EquipmentInstance。</summary>
    public sealed class EquipmentComponent : MonoComponent
    {
        /// <summary>引用装备 Logic 的只读 ScriptableObject 配置。</summary>
        [SerializeField] private EquipmentConfig config;
        /// <summary>配置启动时按列表下标应用到装备槽位的 Debug Definition 与累计经验。</summary>
        [SerializeField] private List<EquipmentDebugData> debugEquipment = new List<EquipmentDebugData>();
        /// <summary>保存当前 Entity 生命周期独占的装备槽位数量副本。</summary>
        private int runtimeEquipmentSlotCount;
        /// <summary>保存当前 Entity 生命周期独占的副词条槽位数量副本。</summary>
        private int runtimeSubTierSlotCount;
        /// <summary>保存当前 Entity 生命周期独占的装备等级上限副本。</summary>
        private int runtimeMaximumLevel;
        /// <summary>保存当前 Entity 生命周期独占的装备经验曲线深拷贝。</summary>
        private AnimationCurve runtimeExperienceCurve;
        /// <summary>保存当前 Entity 生命周期独占的装备满级经验副本。</summary>
        private float runtimeMaximumExperience;
        /// <summary>保存当前 Entity 生命周期独占的档位预设深拷贝。</summary>
        private List<TierPreset> runtimeTierPresets = new List<TierPreset>();
        /// <summary>保存当前装备实例列表，下标固定对应装备槽位，空槽位保存 null。</summary>
        private List<EquipmentInstance> currentEquipment = new List<EquipmentInstance>();
        /// <summary>标记运行时装备数据已经由 EquipmentLogic 初始化。</summary>
        private bool initialized;
        /// <summary>用可监听修订号向 UI 和存档层暴露装备集合、经验、等级和词条值变化。</summary>
        private readonly ModifiableProperty revisionProperty = new ModifiableProperty();

        /// <summary>当前装备实例或词条数值发生变化时通知 EquipmentLogic 重建唯一 tiersEffect。</summary>
        internal event Action<bool> Changed;

        /// <summary>获取运行时装备槽位数量副本。</summary>
        public int EquipmentSlotCount => initialized ? runtimeEquipmentSlotCount : Config.EquipmentSlotCount;

        /// <summary>获取运行时副词条槽位数量副本。</summary>
        public int SubTierSlotCount => initialized ? runtimeSubTierSlotCount : Config.SubTierSlotCount;

        /// <summary>获取运行时装备等级上限副本。</summary>
        public int MaximumLevel => initialized ? runtimeMaximumLevel : Config.MaximumLevel;

        /// <summary>获取运行时单件装备满级累计经验副本。</summary>
        public float MaximumExperience => initialized ? runtimeMaximumExperience : Config.MaximumExperience;

        /// <summary>获取角色 Prefab 引用的只读装备配置；缺少引用时抛出明确异常。</summary>
        public EquipmentConfig Config => config != null ? config : throw new InvalidOperationException("EquipmentComponent requires an EquipmentConfig reference.");

        /// <summary>获取当前装备集合和词条数值的可监听修订号。</summary>
        public ModifiableProperty RevisionProperty => revisionProperty;

        /// <summary>获取固定槽位顺序的当前装备实例只读列表，空槽位以 null 保留其下标。</summary>
        public IReadOnlyList<EquipmentInstance> CurrentEquipment => currentEquipment;

        /// <summary>获取运行时数据是否已经完成初始化。</summary>
        public bool IsInitialized => initialized;

        /// <summary>由 EquipmentLogic 创建配置、曲线、档位预设和 Debug EquipmentInstance 的独立运行时副本。</summary>
        internal void InitializeRuntimeData()
        {
            if (initialized) return;
            EquipmentConfig runtimeSource = Config;
            runtimeEquipmentSlotCount = runtimeSource.EquipmentSlotCount;
            runtimeSubTierSlotCount = runtimeSource.SubTierSlotCount;
            runtimeMaximumLevel = runtimeSource.MaximumLevel;
            runtimeExperienceCurve = GrowthCurveUtility.CloneOrLinear(runtimeSource.ExperienceCurve);
            runtimeMaximumExperience = runtimeSource.MaximumExperience;
            runtimeTierPresets = TierRules.ClonePresets(runtimeSource.TierPresets);
            currentEquipment = new List<EquipmentInstance>(runtimeEquipmentSlotCount);
            for (int slotIndex = 0; slotIndex < runtimeEquipmentSlotCount; slotIndex++) currentEquipment.Add(null);
            int debugCount = debugEquipment == null ? 0 : Mathf.Min(debugEquipment.Count, currentEquipment.Count);
            for (int slotIndex = 0; slotIndex < debugCount; slotIndex++)
            {
                EquipmentDebugData debugData = debugEquipment[slotIndex];
                if (debugData == null) continue;
                if (!TryCreateInstance(debugData.Definition, debugData.TotalExperience, out EquipmentInstance instance, out string error)) throw new InvalidOperationException($"Debug equipment slot {slotIndex} is invalid: {error}");
                currentEquipment[slotIndex] = instance;
            }
            initialized = true;
            revisionProperty.SetValue(1f);
        }

        /// <summary>获取指定零基槽位的当前 EquipmentInstance，无效下标抛出明确异常。</summary>
        public EquipmentInstance GetEquipment(int slotIndex)
        {
            EnsureInitialized();
            ValidateSlotIndex(slotIndex);
            return currentEquipment[slotIndex];
        }

        /// <summary>验证并从 Definition 与累计经验创建新实例放入指定槽位，调用方对象后续变化不会污染运行态。</summary>
        public bool TryEquip(int slotIndex, EquipmentDefinition definition, float totalExperience = 0f)
        {
            EnsureInitialized();
            ValidateSlotIndex(slotIndex);
            if (!TryCreateInstance(definition, totalExperience, out EquipmentInstance instance, out _)) return false;
            currentEquipment[slotIndex] = instance;
            NotifyChanged(true);
            return true;
        }

        /// <summary>卸下指定槽位装备，空槽位保持幂等并返回失败。</summary>
        public bool TryUnequip(int slotIndex)
        {
            EnsureInitialized();
            ValidateSlotIndex(slotIndex);
            if (currentEquipment[slotIndex] == null) return false;
            currentEquipment[slotIndex] = null;
            NotifyChanged(true);
            return true;
        }

        /// <summary>为指定槽位装备增加非负累计经验，刷新离散等级和全部 TierInstance 当前值并返回实际接受经验。</summary>
        public float AddExperience(int slotIndex, float requestedExperience)
        {
            EnsureInitialized();
            ValidateSlotIndex(slotIndex);
            EquipmentInstance instance = currentEquipment[slotIndex];
            if (instance == null) return 0f;
            float previousExperience = instance.CurrentTotalExperience;
            int previousLevel = instance.CurrentLevel;
            float nextExperience = Mathf.Clamp(previousExperience + Mathf.Max(0f, requestedExperience), 0f, MaximumExperience);
            float acceptedExperience = nextExperience - previousExperience;
            if (Mathf.Approximately(acceptedExperience, 0f)) return 0f;
            RefreshInstance(instance, nextExperience);
            NotifyChanged(previousLevel != instance.CurrentLevel);
            return acceptedExperience;
        }

        /// <summary>把全部当前 TierInstance 解析为 PropertyType 与两个 Modifier 通道当前值。</summary>
        internal bool TryResolveAll(List<ResolvedTierContribution> destination, out string error)
        {
            EnsureInitialized();
            if (destination == null) throw new ArgumentNullException(nameof(destination));
            destination.Clear();
            for (int slotIndex = 0; slotIndex < currentEquipment.Count; slotIndex++)
            {
                EquipmentInstance equipment = currentEquipment[slotIndex];
                if (equipment == null) continue;
                IReadOnlyList<TierInstance> tiers = equipment.Tiers;
                for (int tierIndex = 0; tierIndex < tiers.Count; tierIndex++)
                {
                    TierInstance tier = tiers[tierIndex];
                    if (tier == null || tier.Definition == null)
                    {
                        destination.Clear();
                        error = $"Equipment slot {slotIndex} contains an invalid TierInstance at index {tierIndex}.";
                        return false;
                    }
                    destination.Add(new ResolvedTierContribution(TierRules.ToPropertyType(tier.Definition.TierType), tier.CurrentOffset, tier.CurrentCoefficient));
                }
            }
            error = string.Empty;
            return true;
        }

        /// <summary>验证 Definition 后创建运行时 EquipmentInstance，并按经验曲线刷新等级与词条值。</summary>
        private bool TryCreateInstance(EquipmentDefinition definition, float totalExperience, out EquipmentInstance instance, out string error)
        {
            if (!ValidateDefinition(definition, out error))
            {
                instance = null;
                return false;
            }
            instance = new EquipmentInstance(definition, Mathf.Clamp(totalExperience, 0f, MaximumExperience));
            RefreshInstance(instance, instance.CurrentTotalExperience);
            return true;
        }

        /// <summary>验证装备恰好一个主词条、副词条不超上限，并确保每条定义都能解析唯一档位最大值。</summary>
        private bool ValidateDefinition(EquipmentDefinition definition, out string error)
        {
            if (definition == null)
            {
                error = "EquipmentDefinition is null.";
                return false;
            }
            int mainTierCount = 0;
            int subTierCount = 0;
            for (int index = 0; index < definition.Tiers.Count; index++)
            {
                TierDefinition tier = definition.Tiers[index];
                if (tier == null)
                {
                    error = $"EquipmentDefinition '{definition.DefinitionId}' contains a null tier at index {index}.";
                    return false;
                }
                if (tier.IsMainTier) mainTierCount++;
                else subTierCount++;
                if (!TierRules.TryResolve(runtimeTierPresets, tier, out _))
                {
                    error = $"EquipmentDefinition '{definition.DefinitionId}' cannot resolve {tier.TierType} tier {tier.Tier}.";
                    return false;
                }
            }
            if (mainTierCount != 1 || subTierCount > runtimeSubTierSlotCount)
            {
                error = $"EquipmentDefinition '{definition.DefinitionId}' requires exactly one main tier and at most {runtimeSubTierSlotCount} sub tiers.";
                return false;
            }
            error = string.Empty;
            return true;
        }

        /// <summary>用累计经验曲线映射零起始装备等级，再以离散等级比例刷新全部词条当前值。</summary>
        private void RefreshInstance(EquipmentInstance instance, float totalExperience)
        {
            float normalizedExperience = Mathf.Clamp01(totalExperience / MaximumExperience);
            float curveProgress = GrowthCurveUtility.Evaluate01(runtimeExperienceCurve, normalizedExperience);
            int currentLevel = Mathf.Clamp(Mathf.FloorToInt(curveProgress * MaximumLevel + 0.00001f), 0, MaximumLevel);
            float levelProgress = currentLevel / (float)MaximumLevel;
            instance.SetProgress(Mathf.Clamp(totalExperience, 0f, MaximumExperience), currentLevel);
            for (int index = 0; index < instance.Tiers.Count; index++)
            {
                TierInstance tier = instance.Tiers[index];
                if (!TierRules.TryResolve(runtimeTierPresets, tier.Definition, out TierMaximumValues maximumValues)) throw new InvalidOperationException($"Equipment tier {tier.Definition.TierType} level {tier.Definition.Tier} lost its runtime preset.");
                tier.Refresh(maximumValues, levelProgress);
            }
        }

        /// <summary>发布装备变化并推进可监听修订号。</summary>
        private void NotifyChanged(bool projectionChanged)
        {
            revisionProperty.SetValue(revisionProperty.Value >= 1000000f ? 1f : revisionProperty.Value + 1f);
            Changed?.Invoke(projectionChanged);
        }

        /// <summary>校验装备槽下标处于当前运行时配置范围。</summary>
        private void ValidateSlotIndex(int slotIndex)
        {
            if (slotIndex < 0 || slotIndex >= currentEquipment.Count) throw new ArgumentOutOfRangeException(nameof(slotIndex), slotIndex, $"Equipment slot index must be between 0 and {currentEquipment.Count - 1}.");
        }

        /// <summary>防止装备 API 在 EquipmentLogic 初始化运行时副本前被调用。</summary>
        private void EnsureInitialized()
        {
            if (!initialized) throw new InvalidOperationException("EquipmentComponent runtime data has not been initialized by EquipmentLogic.");
        }

        /// <summary>在 Inspector 修改时只维护 Debug 数据，静态槽位、曲线和预设由 EquipmentConfig 校验。</summary>
        private void OnValidate()
        {
            if (debugEquipment == null) debugEquipment = new List<EquipmentDebugData>();
            int configuredSlotCount = config == null ? debugEquipment.Count : config.EquipmentSlotCount;
            if (debugEquipment.Count > configuredSlotCount) debugEquipment.RemoveRange(configuredSlotCount, debugEquipment.Count - configuredSlotCount);
        }
    }
}
