using System;
using System.Collections.Generic;
using UnityEngine;
using Xuan.Prometheus.Growth;

namespace Xuan.Prometheus.Component
{
    /// <summary>保存 WeaponLogic 启动时应用的武器累计总经验调试数据。</summary>
    [Serializable]
    public sealed class WeaponDebugData
    {
        /// <summary>配置启动时应用的非负武器累计总经验。</summary>
        [SerializeField, Min(0f)] private float totalExperience;

        /// <summary>获取非负调试累计总经验。</summary>
        public float TotalExperience => Mathf.Max(0f, totalExperience);
    }

    /// <summary>持有武器成长配置、当前等级经验、TierInstance 及每个 PlayerEntity 独占的运行时副本。</summary>
    public sealed class WeaponComponent : MonoComponent
    {
        /// <summary>引用武器 Logic 的只读 ScriptableObject 配置。</summary>
        [SerializeField] private WeaponConfig config;
        /// <summary>配置启动时复制到运行时数据的 Debug 武器累计总经验。</summary>
        [SerializeField] private WeaponDebugData debugData = new WeaponDebugData();
        /// <summary>保存当前 Entity 生命周期独占的武器等级上限副本。</summary>
        private int runtimeMaximumLevel;
        /// <summary>保存当前 Entity 生命周期独占的武器经验曲线深拷贝。</summary>
        private AnimationCurve runtimeExperienceCurve;
        /// <summary>保存当前 Entity 生命周期独占的武器满级经验副本。</summary>
        private float runtimeMaximumExperience;
        /// <summary>保存当前 Entity 生命周期独占的档位预设深拷贝。</summary>
        private List<TierPreset> runtimeTierPresets = new List<TierPreset>();
        /// <summary>保存当前武器与配置定义一一对应的运行时词条实例。</summary>
        private List<TierInstance> runtimeTierInstances = new List<TierInstance>();
        /// <summary>保存由累计经验曲线映射得到的当前整数等级。</summary>
        private int currentLevel = 1;
        /// <summary>保存武器当前已获得并限制在满级经验以内的累计总经验。</summary>
        private float currentTotalExperience;
        /// <summary>标记运行时副本已经由 WeaponLogic 初始化。</summary>
        private bool initialized;
        /// <summary>向 UI 与存档观察者暴露武器等级脏通知。</summary>
        private readonly ModifiableProperty levelProperty = new ModifiableProperty();
        /// <summary>向 UI 与存档观察者暴露武器累计总经验脏通知。</summary>
        private readonly ModifiableProperty totalExperienceProperty = new ModifiableProperty();

        /// <summary>武器累计经验发生变化时通知 WeaponLogic；布尔值表示映射后的整数等级是否改变。</summary>
        internal event Action<bool> Changed;

        /// <summary>获取运行时武器等级上限副本。</summary>
        public int MaximumLevel => initialized ? runtimeMaximumLevel : Config.MaximumLevel;

        /// <summary>获取运行时武器满级累计经验副本。</summary>
        public float MaximumExperience => initialized ? runtimeMaximumExperience : Config.MaximumExperience;

        /// <summary>获取角色 Prefab 引用的只读武器配置；缺少引用时抛出明确异常。</summary>
        public WeaponConfig Config => config != null ? config : throw new InvalidOperationException("WeaponComponent requires a WeaponConfig reference.");

        /// <summary>获取当前武器等级。</summary>
        public int CurrentLevel => currentLevel;

        /// <summary>获取当前武器累计总经验。</summary>
        public float CurrentTotalExperience => currentTotalExperience;

        /// <summary>获取当前武器运行时词条实例只读列表。</summary>
        public IReadOnlyList<TierInstance> Tiers => runtimeTierInstances;

        /// <summary>获取可监听的武器等级属性。</summary>
        public ModifiableProperty LevelProperty => levelProperty;

        /// <summary>获取可监听的武器累计总经验属性。</summary>
        public ModifiableProperty TotalExperienceProperty => totalExperienceProperty;

        /// <summary>获取运行时数据是否已经完成初始化。</summary>
        public bool IsInitialized => initialized;

        /// <summary>由 WeaponLogic 创建配置、曲线、档位预设、词条定义和 Debug 数据的当前 Entity 独占副本。</summary>
        internal void InitializeRuntimeData()
        {
            if (initialized) return;
            WeaponConfig runtimeSource = Config;
            runtimeMaximumLevel = runtimeSource.MaximumLevel;
            runtimeExperienceCurve = GrowthCurveUtility.CloneOrLinear(runtimeSource.ExperienceCurve);
            runtimeMaximumExperience = runtimeSource.MaximumExperience;
            runtimeTierPresets = TierRules.ClonePresets(runtimeSource.TierPresets);
            ValidateDefinitions(runtimeSource.WeaponTiers);
            runtimeTierInstances = new List<TierInstance>();
            for (int index = 0; index < runtimeSource.WeaponTiers.Count; index++) runtimeTierInstances.Add(new TierInstance(runtimeSource.WeaponTiers[index]));
            WeaponDebugData safeDebugData = debugData ?? new WeaponDebugData();
            currentTotalExperience = Mathf.Clamp(safeDebugData.TotalExperience, 0f, runtimeMaximumExperience);
            currentLevel = EvaluateLevel(currentTotalExperience);
            RefreshTierInstances();
            initialized = true;
            levelProperty.SetValue(currentLevel);
            totalExperienceProperty.SetValue(currentTotalExperience);
        }

        /// <summary>增加非负累计总经验并返回实际接受值，跨过整数等级时同步刷新全部 TierInstance。</summary>
        public float AddExperience(float requestedExperience)
        {
            EnsureInitialized();
            float previousExperience = currentTotalExperience;
            int previousLevel = currentLevel;
            currentTotalExperience = Mathf.Clamp(currentTotalExperience + Mathf.Max(0f, requestedExperience), 0f, MaximumExperience);
            float acceptedExperience = currentTotalExperience - previousExperience;
            if (Mathf.Approximately(acceptedExperience, 0f)) return 0f;
            currentLevel = EvaluateLevel(currentTotalExperience);
            totalExperienceProperty.SetValue(currentTotalExperience);
            bool levelChanged = previousLevel != currentLevel;
            if (levelChanged)
            {
                levelProperty.SetValue(currentLevel);
                RefreshTierInstances();
            }
            Changed?.Invoke(levelChanged);
            return acceptedExperience;
        }

        /// <summary>把当前武器全部 TierInstance 解析为 PropertyType 与两个 Modifier 通道当前值。</summary>
        internal bool TryResolveAll(List<ResolvedTierContribution> destination, out string error)
        {
            EnsureInitialized();
            if (destination == null) throw new ArgumentNullException(nameof(destination));
            destination.Clear();
            for (int index = 0; index < runtimeTierInstances.Count; index++)
            {
                TierInstance tier = runtimeTierInstances[index];
                if (tier == null || tier.Definition == null)
                {
                    destination.Clear();
                    error = $"Weapon contains an invalid TierInstance at index {index}.";
                    return false;
                }
                destination.Add(new ResolvedTierContribution(TierRules.ToPropertyType(tier.Definition.TierType), tier.CurrentOffset, tier.CurrentCoefficient));
            }
            error = string.Empty;
            return true;
        }

        /// <summary>验证非空武器词条定义恰好一个主词条，并确保每条定义都能解析唯一档位最大值。</summary>
        private void ValidateDefinitions(IReadOnlyList<TierDefinition> definitions)
        {
            int definitionCount = definitions == null ? 0 : definitions.Count;
            int mainTierCount = 0;
            for (int index = 0; index < definitionCount; index++)
            {
                TierDefinition definition = definitions[index];
                if (definition == null) throw new InvalidOperationException($"Weapon contains a null TierDefinition at index {index}.");
                if (definition.IsMainTier) mainTierCount++;
                if (!TierRules.TryResolve(runtimeTierPresets, definition, out _)) throw new InvalidOperationException($"Weapon cannot resolve {definition.TierType} tier {definition.Tier}.");
            }
            if (definitionCount > 0 && mainTierCount != 1) throw new InvalidOperationException("A configured weapon requires exactly one main tier.");
        }

        /// <summary>按照一加曲线等级进度乘等级跨度计算武器整数等级，并向下取整保持阈值稳定。</summary>
        private int EvaluateLevel(float totalExperience)
        {
            float normalizedExperience = Mathf.Clamp01(totalExperience / MaximumExperience);
            float curveProgress = GrowthCurveUtility.Evaluate01(runtimeExperienceCurve, normalizedExperience);
            float continuousLevel = 1f + curveProgress * (MaximumLevel - 1);
            return Mathf.Clamp(Mathf.FloorToInt(continuousLevel + 0.00001f), 1, MaximumLevel);
        }

        /// <summary>以当前离散等级在一到满级区间的进度刷新全部词条当前系数与固定值。</summary>
        private void RefreshTierInstances()
        {
            int levelSpan = MaximumLevel - 1;
            float levelProgress = levelSpan <= 0 ? 1f : Mathf.Clamp01((currentLevel - 1f) / levelSpan);
            for (int index = 0; index < runtimeTierInstances.Count; index++)
            {
                TierInstance tier = runtimeTierInstances[index];
                if (!TierRules.TryResolve(runtimeTierPresets, tier.Definition, out TierMaximumValues maximumValues)) throw new InvalidOperationException($"Weapon tier {tier.Definition.TierType} level {tier.Definition.Tier} lost its runtime preset.");
                tier.Refresh(maximumValues, levelProgress);
            }
        }

        /// <summary>防止外部系统在 WeaponLogic 初始化运行时副本前写入累计经验。</summary>
        private void EnsureInitialized()
        {
            if (!initialized) throw new InvalidOperationException("WeaponComponent runtime data has not been initialized by WeaponLogic.");
        }

        /// <summary>在 Inspector 修改时只补齐 Component 自己持有的 Debug 数据，静态配置由 WeaponConfig 校验。</summary>
        private void OnValidate()
        {
            if (debugData == null) debugData = new WeaponDebugData();
        }
    }
}
