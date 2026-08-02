using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

namespace Xuan.Prometheus.Effects
{
    /// <summary>
    /// EffectTestConfigurationExtensions 仅在 Editor 测试程序集内提供定义写入能力，使正式运行时定义保持只读。
    /// 扩展方法通过反射写入 Unity 的私有序列化字段，因此无需为了测试扩大运行时类型的公开 API。
    /// </summary>
    internal static class EffectTestConfigurationExtensions
    {
        private const BindingFlags PrivateInstanceField = BindingFlags.Instance | BindingFlags.NonPublic;

        /// <summary>
        /// 为测试或示例资产生成器写入完整效果定义，并复制所有集合以隔离调用方后续修改。
        /// </summary>
        public static void ConfigureForTests(this EffectDefinition definition, string id, EffectTag effectTags, EffectDurationType effectDurationType, float effectDuration, float effectTickInterval, EffectStackPolicy effectStackPolicy, EffectStackKeyPolicy effectStackKeyPolicy, int effectMaxStacks, EffectExecutionPhase executionPhase, int executionPriority, IEnumerable<EffectOperation> applyOperations, IEnumerable<EffectOperation> stackOperations, IEnumerable<EffectOperation> tickOperations, IEnumerable<EffectOperation> removeOperations, IEnumerable<EffectTriggerDefinition> triggers = null)
        {
            if (definition == null) throw new ArgumentNullException(nameof(definition));
            SetPrivateField(definition, "effectId", id ?? string.Empty);
            SetPrivateField(definition, "tags", effectTags);
            SetPrivateField(definition, "durationType", effectDurationType);
            SetPrivateField(definition, "duration", Mathf.Max(0f, effectDuration));
            SetPrivateField(definition, "tickInterval", Mathf.Max(0f, effectTickInterval));
            SetPrivateField(definition, "stackPolicy", effectStackPolicy);
            SetPrivateField(definition, "stackKeyPolicy", effectStackKeyPolicy);
            SetPrivateField(definition, "maxStacks", Mathf.Max(1, effectMaxStacks));
            SetPrivateField(definition, "phase", executionPhase);
            SetPrivateField(definition, "priority", executionPriority);
            SetPrivateField(definition, "onApplyOperations", CopyToList(applyOperations));
            SetPrivateField(definition, "onStackOperations", CopyToList(stackOperations));
            SetPrivateField(definition, "onTickOperations", CopyToList(tickOperations));
            SetPrivateField(definition, "onRemoveOperations", CopyToList(removeOperations));
            SetPrivateField(definition, "grantedTriggers", CopyToList(triggers));
        }

        /// <summary>
        /// 为测试或示例资产生成器写入完整触发规则，并保持与 Inspector 相同的概率和冷却边界约束。
        /// </summary>
        public static void ConfigureForTests(this EffectTriggerDefinition definition, string id, EffectSignalType type, EffectListenScope scope, EffectTargetSelector selector, float triggerChance, float triggerCooldown, bool triggerOncePerSignalChain, int triggerPriority, IEnumerable<EffectConditionDefinition> triggerConditions, IEnumerable<EffectDefinition> triggeredEffects)
        {
            if (definition == null) throw new ArgumentNullException(nameof(definition));
            SetPrivateField(definition, "triggerId", id ?? string.Empty);
            SetPrivateField(definition, "signalType", type);
            SetPrivateField(definition, "listenScope", scope);
            SetPrivateField(definition, "targetSelector", selector);
            SetPrivateField(definition, "chance", Mathf.Clamp01(triggerChance));
            SetPrivateField(definition, "cooldown", Mathf.Max(0f, triggerCooldown));
            SetPrivateField(definition, "oncePerSignalChain", triggerOncePerSignalChain);
            SetPrivateField(definition, "priority", triggerPriority);
            SetPrivateField(definition, "conditions", CopyToList(triggerConditions));
            SetPrivateField(definition, "effects", CopyToList(triggeredEffects));
        }

        /// <summary>
        /// 为测试或示例资产生成器写入触发集合，并复制传入集合以避免共享可变列表。
        /// </summary>
        public static void ConfigureForTests(this EffectTriggerSet triggerSet, IEnumerable<EffectTriggerDefinition> definitions)
        {
            if (triggerSet == null) throw new ArgumentNullException(nameof(triggerSet));
            SetPrivateField(triggerSet, "triggers", CopyToList(definitions));
        }

        /// <summary>
        /// 将可空枚举复制为新的列表，保证测试配置与运行时定义原有的集合所有权语义一致。
        /// </summary>
        private static List<T> CopyToList<T>(IEnumerable<T> values)
        {
            return values == null ? new List<T>() : new List<T>(values);
        }

        /// <summary>
        /// 写入声明类型的私有实例字段；字段重命名时立即抛出明确异常，避免测试资产被静默写坏。
        /// </summary>
        private static void SetPrivateField<TTarget, TValue>(TTarget target, string fieldName, TValue value)
        {
            FieldInfo field = typeof(TTarget).GetField(fieldName, PrivateInstanceField);
            if (field == null) throw new MissingFieldException(typeof(TTarget).FullName, fieldName);
            field.SetValue(target, value);
        }
    }
}
