using System;
using UnityEngine;
using Xuan.Prometheus.Growth;

namespace Xuan.Prometheus.Component
{
    /// <summary>保存 CharaLevelLogic 启动时应用的角色累计总经验调试数据。</summary>
    [Serializable]
    public sealed class CharaLevelDebugData
    {
        /// <summary>配置启动时应用的非负角色累计总经验。</summary>
        [SerializeField, Min(0f)] private float totalExperience;

        /// <summary>获取非负调试累计总经验。</summary>
        public float TotalExperience => Mathf.Max(0f, totalExperience);
    }

    /// <summary>持有角色经验曲线配置、当前累计经验、当前等级及当前 Entity 独占的运行时副本。</summary>
    public sealed class CharaLevelComponent : MonoComponent
    {
        /// <summary>引用角色等级 Logic 的只读 ScriptableObject 配置。</summary>
        [SerializeField] private CharaLevelConfig config;
        /// <summary>配置启动时复制到运行时数据的 Debug 累计总经验。</summary>
        [SerializeField] private CharaLevelDebugData debugData = new CharaLevelDebugData();
        /// <summary>保存当前 Entity 生命周期独占的等级上限副本。</summary>
        private int runtimeMaximumLevel;
        /// <summary>保存当前 Entity 生命周期独占的最高等级攻击力副本。</summary>
        private float runtimeMaximumLevelAttack;
        /// <summary>保存当前 Entity 生命周期独占的经验曲线深拷贝。</summary>
        private AnimationCurve runtimeExperienceCurve;
        /// <summary>保存当前 Entity 生命周期独占的满级经验副本。</summary>
        private float runtimeMaximumExperience;
        /// <summary>保存角色当前已获得并限制在满级经验以内的累计总经验。</summary>
        private float currentTotalExperience;
        /// <summary>保存由累计经验曲线映射得到的当前整数等级。</summary>
        private int currentLevel = 1;
        /// <summary>标记运行时副本已经由 CharaLevelLogic 初始化。</summary>
        private bool initialized;
        /// <summary>向 UI 与存档观察者暴露等级脏通知。</summary>
        private readonly ModifiableProperty levelProperty = new ModifiableProperty();
        /// <summary>向 UI 与存档观察者暴露累计总经验脏通知。</summary>
        private readonly ModifiableProperty totalExperienceProperty = new ModifiableProperty();

        /// <summary>当累计经验发生变化时通知 CharaLevelLogic；布尔值表示映射后的整数等级是否改变。</summary>
        internal event Action<bool> Changed;

        /// <summary>获取运行时等级上限副本。</summary>
        public int MaximumLevel => initialized ? runtimeMaximumLevel : Config.MaximumLevel;

        /// <summary>获取运行时最高等级攻击力副本。</summary>
        public float MaximumLevelAttack => initialized ? runtimeMaximumLevelAttack : Config.MaximumLevelAttack;

        /// <summary>获取运行时满级累计经验副本。</summary>
        public float MaximumExperience => initialized ? runtimeMaximumExperience : Config.MaximumExperience;

        /// <summary>获取角色 Prefab 引用的只读等级配置；缺少引用时抛出明确异常。</summary>
        public CharaLevelConfig Config => config != null ? config : throw new InvalidOperationException("CharaLevelComponent requires a CharaLevelConfig reference.");

        /// <summary>获取角色当前等级。</summary>
        public int CurrentLevel => currentLevel;

        /// <summary>获取角色当前已获得的累计总经验。</summary>
        public float CurrentTotalExperience => currentTotalExperience;

        /// <summary>获取当前累计经验相对满级经验的归一化进度。</summary>
        public float NormalizedExperience => Mathf.Clamp01(currentTotalExperience / MaximumExperience);

        /// <summary>获取可监听的角色等级属性。</summary>
        public ModifiableProperty LevelProperty => levelProperty;

        /// <summary>获取可监听的累计总经验属性。</summary>
        public ModifiableProperty TotalExperienceProperty => totalExperienceProperty;

        /// <summary>获取运行时数据是否已经完成初始化。</summary>
        public bool IsInitialized => initialized;

        /// <summary>由 CharaLevelLogic 创建当前 Entity 独占的配置、曲线和 Debug 数据副本。</summary>
        internal void InitializeRuntimeData()
        {
            if (initialized) return;
            CharaLevelConfig runtimeSource = Config;
            runtimeMaximumLevel = runtimeSource.MaximumLevel;
            runtimeMaximumLevelAttack = runtimeSource.MaximumLevelAttack;
            runtimeExperienceCurve = GrowthCurveUtility.CloneOrLinear(runtimeSource.ExperienceCurve);
            runtimeMaximumExperience = runtimeSource.MaximumExperience;
            CharaLevelDebugData safeDebugData = debugData ?? new CharaLevelDebugData();
            currentTotalExperience = Mathf.Clamp(safeDebugData.TotalExperience, 0f, runtimeMaximumExperience);
            currentLevel = EvaluateLevel(currentTotalExperience);
            initialized = true;
            levelProperty.SetValue(currentLevel);
            totalExperienceProperty.SetValue(currentTotalExperience);
        }

        /// <summary>增加非负累计总经验并返回实际接受值，达到满级经验后不再继续累积溢出值。</summary>
        public float AddExperience(float requestedExperience)
        {
            EnsureInitialized();
            float safeExperience = Mathf.Max(0f, requestedExperience);
            float previousExperience = currentTotalExperience;
            int previousLevel = currentLevel;
            currentTotalExperience = Mathf.Clamp(currentTotalExperience + safeExperience, 0f, MaximumExperience);
            float acceptedExperience = currentTotalExperience - previousExperience;
            if (Mathf.Approximately(acceptedExperience, 0f)) return 0f;
            currentLevel = EvaluateLevel(currentTotalExperience);
            totalExperienceProperty.SetValue(currentTotalExperience);
            if (previousLevel != currentLevel) levelProperty.SetValue(currentLevel);
            Changed?.Invoke(previousLevel != currentLevel);
            return acceptedExperience;
        }

        /// <summary>按照最高等级攻击力除以等级跨度再乘当前等级跨度计算永久 Effect 的攻击力提升。</summary>
        public float GetCurrentAttackIncrease()
        {
            int levelSpan = MaximumLevel - 1;
            if (levelSpan <= 0) return 0f;
            return MaximumLevelAttack / levelSpan * Mathf.Max(0, currentLevel - 1);
        }

        /// <summary>按照新版 Spec 计算一加曲线等级进度乘等级跨度，并向下取整为稳定整数等级。</summary>
        private int EvaluateLevel(float totalExperience)
        {
            float normalizedExperience = Mathf.Clamp01(totalExperience / MaximumExperience);
            float levelProgress = GrowthCurveUtility.Evaluate01(runtimeExperienceCurve, normalizedExperience);
            float continuousLevel = 1f + levelProgress * (MaximumLevel - 1);
            return Mathf.Clamp(Mathf.FloorToInt(continuousLevel + 0.00001f), 1, MaximumLevel);
        }

        /// <summary>防止外部系统在 CharaLevelLogic 初始化前写入累计经验。</summary>
        private void EnsureInitialized()
        {
            if (!initialized) throw new InvalidOperationException("CharaLevelComponent runtime data has not been initialized by CharaLevelLogic.");
        }

        /// <summary>在 Inspector 修改时只补齐 Component 自己持有的 Debug 数据，静态配置由 CharaLevelConfig 校验。</summary>
        private void OnValidate()
        {
            if (debugData == null) debugData = new CharaLevelDebugData();
        }
    }
}
