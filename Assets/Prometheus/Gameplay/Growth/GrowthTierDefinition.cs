using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Serialization;
using Xuan.Prometheus.Component;

namespace Xuan.Prometheus.Growth
{
    /// <summary>限制装备和武器词条只能选择新版 Spec 指定的五种战斗属性。</summary>
    public enum TierType
    {
        /// <summary>攻击力。</summary>
        Attack = 0,
        /// <summary>防御力。</summary>
        Defence = 1,
        /// <summary>暴击率。</summary>
        CriticalRate = 2,
        /// <summary>暴击伤害。</summary>
        CriticalDamage = 3,
        /// <summary>生命值上限。</summary>
        MaximumHealth = 4
    }

    /// <summary>保存一个整数档位在满级时允许同时提供的固定增益与系数增益最大值。</summary>
    [Serializable]
    public sealed class TierValuePreset
    {
        /// <summary>配置稳定的正整数档位编号。</summary>
        [SerializeField, Min(1)] private int tier = 1;
        /// <summary>配置该档位满级时写入 Offset 通道的最大值。</summary>
        [SerializeField, FormerlySerializedAs("fixedValue")] private float maximumOffset;
        /// <summary>配置该档位满级时写入 Boost 通道的最大系数，例如 0.1 表示百分之十。</summary>
        [SerializeField, FormerlySerializedAs("coefficientValue")] private float maximumCoefficient;

        /// <summary>创建默认档位预设供 Unity 序列化器使用。</summary>
        public TierValuePreset()
        {
        }

        /// <summary>创建同时包含满级固定值和满级系数值的档位预设。</summary>
        public TierValuePreset(int tierIndex, float offsetAtMaximumLevel, float coefficientAtMaximumLevel)
        {
            tier = Mathf.Max(1, tierIndex);
            maximumOffset = offsetAtMaximumLevel;
            maximumCoefficient = coefficientAtMaximumLevel;
        }

        /// <summary>获取至少为一的档位编号。</summary>
        public int Tier => Mathf.Max(1, tier);

        /// <summary>获取该档位满级时的固定增益最大值。</summary>
        public float MaximumOffset => Mathf.Max(0f, maximumOffset);

        /// <summary>获取该档位满级时的系数增益最大值。</summary>
        public float MaximumCoefficient => Mathf.Max(0f, maximumCoefficient);

        /// <summary>创建当前档位不可变的满级数值快照。</summary>
        public TierMaximumValues ToMaximumValues()
        {
            return new TierMaximumValues(maximumOffset, maximumCoefficient);
        }
    }

    /// <summary>保存一种允许词条属性的全部整数档位预设，等价于可序列化的 TierType 到数值列表映射条目。</summary>
    [Serializable]
    public sealed class TierPreset
    {
        /// <summary>配置该预设对应的允许词条类型。</summary>
        [SerializeField] private TierType tierType;
        /// <summary>配置该属性各档位在满级时的固定值和系数值。</summary>
        [SerializeField] private List<TierValuePreset> values = new List<TierValuePreset>();

        /// <summary>创建默认属性预设供 Unity 序列化器使用。</summary>
        public TierPreset()
        {
        }

        /// <summary>创建指定属性的档位预设，并复制调用方提供的列表容器。</summary>
        public TierPreset(TierType type, IEnumerable<TierValuePreset> tierValues)
        {
            tierType = type;
            values = tierValues == null ? new List<TierValuePreset>() : new List<TierValuePreset>(tierValues);
        }

        /// <summary>获取该预设对应的词条类型。</summary>
        public TierType TierType => tierType;

        /// <summary>获取只读档位预设列表。</summary>
        public IReadOnlyList<TierValuePreset> Values => values ?? (values = new List<TierValuePreset>());

        /// <summary>按整数档位解析唯一满级数值；未配置或重复配置该档位时返回失败。</summary>
        public bool TryResolve(int tierIndex, out TierMaximumValues maximumValues)
        {
            TierValuePreset matchedValue = null;
            int matchCount = 0;
            if (values != null)
            {
                for (int index = 0; index < values.Count; index++)
                {
                    TierValuePreset candidate = values[index];
                    if (candidate == null || candidate.Tier != tierIndex) continue;
                    matchedValue = candidate;
                    matchCount++;
                }
            }
            if (matchCount != 1)
            {
                maximumValues = default;
                return false;
            }
            maximumValues = matchedValue.ToMaximumValues();
            return true;
        }

        /// <summary>创建包含全部档位值副本的运行时预设。</summary>
        public TierPreset Clone()
        {
            List<TierValuePreset> clonedValues = new List<TierValuePreset>();
            if (values != null)
            {
                for (int index = 0; index < values.Count; index++)
                {
                    TierValuePreset value = values[index];
                    if (value != null) clonedValues.Add(new TierValuePreset(value.Tier, value.MaximumOffset, value.MaximumCoefficient));
                }
            }
            return new TierPreset(tierType, clonedValues);
        }
    }

    /// <summary>描述装备或武器上的一个主词条或副词条及其属性档位。</summary>
    [Serializable]
    public sealed class TierDefinition
    {
        /// <summary>配置当前词条是否为唯一主词条。</summary>
        [SerializeField, FormerlySerializedAs("mainTier")] private bool isMainTier;
        /// <summary>配置当前词条的允许属性类型。</summary>
        [SerializeField] private TierType tierType;
        /// <summary>配置当前词条引用的正整数档位。</summary>
        [SerializeField, Min(1)] private int tier = 1;

        /// <summary>创建默认词条定义供 Unity 序列化器使用。</summary>
        public TierDefinition()
        {
        }

        /// <summary>创建指定主副属性、词条类型和档位的定义。</summary>
        public TierDefinition(bool mainTier, TierType type, int tierIndex)
        {
            isMainTier = mainTier;
            tierType = type;
            tier = Mathf.Max(1, tierIndex);
        }

        /// <summary>获取当前词条是否为主词条。</summary>
        public bool IsMainTier => isMainTier;

        /// <summary>获取当前词条属性类型。</summary>
        public TierType TierType => tierType;

        /// <summary>获取至少为一的档位编号。</summary>
        public int Tier => Mathf.Max(1, tier);

        /// <summary>创建脱离 Prefab 或调用方对象的词条定义副本。</summary>
        public TierDefinition Clone()
        {
            return new TierDefinition(isMainTier, tierType, Tier);
        }
    }

    /// <summary>保存一条运行时词条引用的定义副本及其当前系数和当前固定偏移。</summary>
    [Serializable]
    public sealed class TierInstance
    {
        /// <summary>保存当前实例独占的词条定义副本。</summary>
        [SerializeField] private TierDefinition definition;
        /// <summary>保存随装备或武器等级成长得到的当前 Boost 系数。</summary>
        [SerializeField] private float currentCoefficient;
        /// <summary>保存随装备或武器等级成长得到的当前 Offset 固定值。</summary>
        [SerializeField] private float currentOffset;

        /// <summary>创建默认实例供 Unity 序列化器使用。</summary>
        public TierInstance()
        {
        }

        /// <summary>从词条定义创建运行时独占实例，当前值由所属装备或武器刷新。</summary>
        public TierInstance(TierDefinition tierDefinition)
        {
            definition = tierDefinition?.Clone() ?? throw new ArgumentNullException(nameof(tierDefinition));
        }

        /// <summary>获取当前实例独占的词条定义。</summary>
        public TierDefinition Definition => definition;

        /// <summary>获取当前 Boost 系数。</summary>
        public float CurrentCoefficient => currentCoefficient;

        /// <summary>获取当前 Offset 固定值。</summary>
        public float CurrentOffset => currentOffset;

        /// <summary>按照离散等级进度同步两个属性通道；档位最大值即满级值。</summary>
        internal void Refresh(TierMaximumValues maximumValues, float levelProgress)
        {
            float safeProgress = Mathf.Clamp01(levelProgress);
            currentCoefficient = maximumValues.MaximumCoefficient * safeProgress;
            currentOffset = maximumValues.MaximumOffset * safeProgress;
        }
    }

    /// <summary>保存一件已装备物品的只读定义资产引用、运行时词条实例、当前累计总经验和当前等级。</summary>
    [Serializable]
    public sealed class EquipmentInstance
    {
        /// <summary>保存当前实例引用的只读装备定义资产，运行时可变词条值由 tiers 独占持有。</summary>
        [SerializeField] private EquipmentDefinition definition;
        /// <summary>保存与定义词条一一对应的运行时词条实例。</summary>
        [SerializeField] private List<TierInstance> tiers = new List<TierInstance>();
        /// <summary>保存装备当前获得并限制在满级经验以内的累计总经验。</summary>
        [SerializeField, Min(0f)] private float currentTotalExperience;
        /// <summary>保存由装备经验曲线映射得到的当前等级，装备初始等级固定为零。</summary>
        [SerializeField, Min(0)] private int currentLevel;

        /// <summary>创建默认实例供 Unity 序列化器使用。</summary>
        public EquipmentInstance()
        {
        }

        /// <summary>从装备定义和累计经验创建运行时实例及词条实例。</summary>
        public EquipmentInstance(EquipmentDefinition equipmentDefinition, float totalExperience)
        {
            definition = equipmentDefinition != null ? equipmentDefinition : throw new ArgumentNullException(nameof(equipmentDefinition));
            currentTotalExperience = Mathf.Max(0f, totalExperience);
            tiers = new List<TierInstance>(definition.Tiers.Count);
            for (int index = 0; index < definition.Tiers.Count; index++) tiers.Add(new TierInstance(definition.Tiers[index]));
        }

        /// <summary>获取当前装备引用的只读定义资产。</summary>
        public EquipmentDefinition Definition => definition;

        /// <summary>获取运行时词条实例只读列表。</summary>
        public IReadOnlyList<TierInstance> Tiers => tiers ?? (tiers = new List<TierInstance>());

        /// <summary>获取装备当前累计总经验。</summary>
        public float CurrentTotalExperience => currentTotalExperience;

        /// <summary>获取装备当前等级。</summary>
        public int CurrentLevel => currentLevel;

        /// <summary>更新装备当前经验与曲线映射等级。</summary>
        internal void SetProgress(float totalExperience, int level)
        {
            currentTotalExperience = Mathf.Max(0f, totalExperience);
            currentLevel = Mathf.Max(0, level);
        }
    }

    /// <summary>保存启动时创建一件装备实例所需的 Definition 和累计总经验调试数据。</summary>
    [Serializable]
    public sealed class EquipmentDebugData
    {
        /// <summary>配置启动时装备到对应列表下标槽位的静态定义。</summary>
        [SerializeField] private EquipmentDefinition definition;
        /// <summary>配置该装备启动时拥有的累计总经验。</summary>
        [SerializeField, Min(0f)] private float totalExperience;

        /// <summary>获取调试装备定义。</summary>
        public EquipmentDefinition Definition => definition;

        /// <summary>获取非负调试累计总经验。</summary>
        public float TotalExperience => Mathf.Max(0f, totalExperience);
    }

    /// <summary>保存一个档位在满级时两个属性通道的不可变最大值。</summary>
    public readonly struct TierMaximumValues
    {
        /// <summary>创建满级固定值和满级系数值快照。</summary>
        public TierMaximumValues(float maximumOffset, float maximumCoefficient)
        {
            MaximumOffset = maximumOffset;
            MaximumCoefficient = maximumCoefficient;
        }

        /// <summary>获取满级 Offset 固定值。</summary>
        public float MaximumOffset { get; }

        /// <summary>获取满级 Boost 系数。</summary>
        public float MaximumCoefficient { get; }
    }

    /// <summary>保存一个运行时词条已经解析出的属性类型、当前固定值和当前系数值。</summary>
    public readonly struct ResolvedTierContribution
    {
        /// <summary>创建同时包含两个 Modifier 通道当前值的不可变贡献。</summary>
        public ResolvedTierContribution(PropertyType propertyType, float currentOffset, float currentCoefficient)
        {
            PropertyType = propertyType;
            CurrentOffset = currentOffset;
            CurrentCoefficient = currentCoefficient;
        }

        /// <summary>获取词条影响的现有战斗属性。</summary>
        public PropertyType PropertyType { get; }

        /// <summary>获取当前 Offset 固定值。</summary>
        public float CurrentOffset { get; }

        /// <summary>获取当前 Boost 系数。</summary>
        public float CurrentCoefficient { get; }
    }

    /// <summary>集中维护 TierType 到现有 PropertyType 的映射与唯一档位预设解析。</summary>
    public static class TierRules
    {
        /// <summary>把受限词条类型转换为 PropertyModifierOperation 使用的 PropertyType。</summary>
        public static PropertyType ToPropertyType(TierType tierType)
        {
            switch (tierType)
            {
                case TierType.Attack: return PropertyType.Atk;
                case TierType.Defence: return PropertyType.Def;
                case TierType.CriticalRate: return PropertyType.CritRate;
                case TierType.CriticalDamage: return PropertyType.CritDmg;
                case TierType.MaximumHealth: return PropertyType.MaxHp;
                default: throw new ArgumentOutOfRangeException(nameof(tierType), tierType, "Unsupported tier type.");
            }
        }

        /// <summary>从预设列表查找唯一属性预设并解析档位，缺失或重复属性预设都会明确失败。</summary>
        public static bool TryResolve(IReadOnlyList<TierPreset> presets, TierDefinition definition, out TierMaximumValues maximumValues)
        {
            if (presets == null || definition == null)
            {
                maximumValues = default;
                return false;
            }
            TierPreset matchedPreset = null;
            int matchCount = 0;
            for (int index = 0; index < presets.Count; index++)
            {
                TierPreset preset = presets[index];
                if (preset == null || preset.TierType != definition.TierType) continue;
                matchedPreset = preset;
                matchCount++;
            }
            if (matchCount != 1 || !matchedPreset.TryResolve(definition.Tier, out maximumValues))
            {
                maximumValues = default;
                return false;
            }
            return true;
        }

        /// <summary>深拷贝属性档位预设列表，使每个 Component 运行时数据不共享可变集合。</summary>
        public static List<TierPreset> ClonePresets(IReadOnlyList<TierPreset> presets)
        {
            List<TierPreset> clones = new List<TierPreset>();
            if (presets == null) return clones;
            for (int index = 0; index < presets.Count; index++) if (presets[index] != null) clones.Add(presets[index].Clone());
            return clones;
        }
    }
}
