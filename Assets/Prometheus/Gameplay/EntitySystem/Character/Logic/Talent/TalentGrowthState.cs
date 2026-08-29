using System;
using UnityEngine;
using Xuan.Prometheus.Component;

namespace Xuan.Prometheus.Logic.Talent
{
    /// <summary>标识永久天赋 Effect 需要修改的四种玩家能力通道。</summary>
    public enum TalentAbilityType
    {
        /// <summary>普通攻击天赋。</summary>
        NormalAttack = 0,
        /// <summary>特殊攻击天赋。</summary>
        SpecialAttack = 1,
        /// <summary>技能天赋。</summary>
        Skill = 2,
        /// <summary>大招天赋。</summary>
        Ultimate = 3
    }

    /// <summary>统一暴露每个技能 Component 自己持有的等级数据和可修改增益系数。</summary>
    public interface ITalentGrowthComponent
    {
        /// <summary>获取该 Component 对应的稳定能力类型。</summary>
        TalentAbilityType TalentAbilityType { get; }
        /// <summary>获取当前技能等级。</summary>
        int TalentLevel { get; }
        /// <summary>获取由永久 Effect 修改的增益系数属性。</summary>
        ModifiableProperty GainCoefficientProperty { get; }
        /// <summary>获取当前天赋增益系数。</summary>
        float GainCoefficient { get; }
        /// <summary>获取应用到基础倍率或基础增益值上的最终缩放系数。</summary>
        float TalentScale { get; }
        /// <summary>技能等级变化时通知 TalentLogic 在安全更新边界重建永久 Effect。</summary>
        event Action TalentLevelChanged;
        /// <summary>使用 TalentConfig 等级上限和本 Component 的 Debug 等级初始化独立运行时数据。</summary>
        void InitializeTalentGrowth(int maximumTalentLevel);
        /// <summary>尝试把当前技能等级修改到配置上限范围内。</summary>
        bool TrySetTalentLevel(int level);
    }

    /// <summary>作为每个技能 Component 的可序列化字段，保存 Debug 等级配置与 Entity 独占运行时副本。</summary>
    [Serializable]
    public sealed class TalentGrowthState
    {
        /// <summary>配置启动时应用到所属技能 Component 的 Debug 等级。</summary>
        [SerializeField, Min(1)] private int debugTalentLevel = 1;
        /// <summary>保存当前 Entity 独占的技能等级。</summary>
        private int currentTalentLevel = 1;
        /// <summary>保存当前 Entity 从 TalentConfig 复制的技能等级上限。</summary>
        private int runtimeMaximumTalentLevel = 1;
        /// <summary>标记该状态已经由 TalentLogic 初始化。</summary>
        private bool initialized;
        /// <summary>保存由永久 Effect 修改并可被 UI 监听的增益系数。</summary>
        private readonly ModifiableProperty gainCoefficientProperty = new ModifiableProperty();

        /// <summary>技能等级变化时通知所属 Component。</summary>
        public event Action Changed;

        /// <summary>获取当前技能等级；初始化前返回约束后的 Debug 配置。</summary>
        public int CurrentTalentLevel => initialized ? currentTalentLevel : Mathf.Max(1, debugTalentLevel);

        /// <summary>获取当前运行时技能等级上限。</summary>
        public int MaximumTalentLevel => initialized ? runtimeMaximumTalentLevel : 1;

        /// <summary>获取当前技能可修改增益系数。</summary>
        public ModifiableProperty GainCoefficientProperty => gainCoefficientProperty;

        /// <summary>获取当前技能增益系数。</summary>
        public float GainCoefficient => gainCoefficientProperty.Value;

        /// <summary>获取基础倍率或基础增益值需要乘算的最终非负系数。</summary>
        public float TalentScale => Mathf.Max(0f, 1f + GainCoefficient);

        /// <summary>从配置上限和 Debug 等级创建运行时数据副本，重复调用保持幂等。</summary>
        public void InitializeRuntimeData(int maximumTalentLevel)
        {
            if (initialized) return;
            runtimeMaximumTalentLevel = Mathf.Max(1, maximumTalentLevel);
            currentTalentLevel = Mathf.Clamp(debugTalentLevel, 1, runtimeMaximumTalentLevel);
            gainCoefficientProperty.SetBaseValue(0f);
            initialized = true;
        }

        /// <summary>在运行时上限内修改等级；非法等级和无变化请求都不会污染当前数据。</summary>
        public bool TrySetTalentLevel(int level)
        {
            if (!initialized) throw new InvalidOperationException("TalentGrowthState has not been initialized by TalentLogic.");
            if (level < 1 || level > runtimeMaximumTalentLevel || level == currentTalentLevel) return false;
            currentTalentLevel = level;
            Changed?.Invoke();
            return true;
        }
    }
}
