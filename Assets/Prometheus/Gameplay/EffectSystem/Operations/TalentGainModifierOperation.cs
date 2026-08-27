using System;
using UnityEngine;
using Xuan.Prometheus.Component;
using Xuan.Prometheus.Logic.Talent;

namespace Xuan.Prometheus.Effects
{
    /// <summary>通过持续 Effect 修改指定技能 Component 自己持有的可修改天赋增益系数。</summary>
    [Serializable]
    public sealed class TalentGainModifierOperation : EffectOperation
    {
        /// <summary>配置该操作影响的玩家能力通道。</summary>
        [SerializeField] private TalentAbilityType abilityType;
        /// <summary>配置该 Effect 实例每层贡献的天赋增益系数。</summary>
        [SerializeField] private EffectValueFormula valuePerStack = EffectValueFormula.Constant(0f);

        /// <summary>创建默认操作供 Unity 序列化器使用。</summary>
        public TalentGainModifierOperation()
        {
        }

        /// <summary>创建影响指定技能通道和增益系数的运行时操作。</summary>
        public TalentGainModifierOperation(TalentAbilityType targetAbilityType, EffectValueFormula perStackValue)
        {
            abilityType = targetAbilityType;
            valuePerStack = perStackValue ?? EffectValueFormula.Constant(0f);
        }

        /// <summary>为持续 Effect 目标的技能增益属性安装可精确回滚的 Offset Modifier。</summary>
        public override void Execute(EffectOperationContext context)
        {
            if (context.Instance == null || context.Target == null) return;
            ITalentGrowthComponent talentComponent = ResolveTalentComponent(context.Target, abilityType);
            if (talentComponent == null) return;
            float value = valuePerStack.Evaluate(context) * context.Stacks;
            context.Instance.SetResource(BuildResourceKey(abilityType), new EffectTalentGainModifierHandle(talentComponent.GainCoefficientProperty, value));
        }

        /// <summary>根据能力类型生成同一 EffectInstance 内稳定且唯一的资源键。</summary>
        public static string BuildResourceKey(TalentAbilityType targetAbilityType)
        {
            return $"TalentGainModifier:{targetAbilityType}";
        }

        /// <summary>从 Entity 的具体组件字典解析对应技能 Component，避免接口类型无法作为组件键查询。</summary>
        private static ITalentGrowthComponent ResolveTalentComponent(Xuan.Prometheus.Logic.Entity target, TalentAbilityType targetAbilityType)
        {
            switch (targetAbilityType)
            {
                case TalentAbilityType.NormalAttack: return target.TryGetComp(out AttackComponent attackComponent) ? attackComponent : null;
                case TalentAbilityType.SpecialAttack: return target.TryGetComp(out SpecialAttackComponent specialAttackComponent) ? specialAttackComponent : null;
                case TalentAbilityType.Skill: return target.TryGetComp(out SkillComponent skillComponent) ? skillComponent : null;
                case TalentAbilityType.Ultimate: return target.TryGetComp(out UltimateComponent ultimateComponent) ? ultimateComponent : null;
                default: throw new ArgumentOutOfRangeException(nameof(targetAbilityType), targetAbilityType, "Unsupported talent ability type.");
            }
        }
    }

    /// <summary>精确拥有一份技能增益系数 Modifier，并在永久 Effect 被替换或移除时只回滚自身贡献。</summary>
    internal sealed class EffectTalentGainModifierHandle : IDisposable
    {
        /// <summary>保存目标技能 Component 持有的增益属性。</summary>
        private readonly ModifiableProperty property;
        /// <summary>保存当前句柄添加的 Modifier 对象身份。</summary>
        private readonly ModifiableValueModifier modifier;
        /// <summary>保证释放操作幂等。</summary>
        private bool disposed;

        /// <summary>创建句柄时立即向增益属性 Offset 通道登记数值。</summary>
        public EffectTalentGainModifierHandle(ModifiableProperty targetProperty, float value)
        {
            property = targetProperty;
            modifier = property?.AddValueModifier(PropertyModifierMode.Offset, value);
        }

        /// <summary>首次释放时按对象身份移除当前 Effect 添加的 Modifier。</summary>
        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            if (property == null || modifier == null) return;
            property.RemoveValueModifier(modifier);
        }
    }
}
