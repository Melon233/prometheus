using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

namespace Xuan.Prometheus.Ai
{
    /// <summary>
    /// 仅在 Editor 测试程序集内提供 AI 资产私有字段写入能力，使正式 Runtime 定义继续保持只读。
    /// </summary>
    internal static class EnemyAiTestConfigurationExtensions
    {
        private const BindingFlags PrivateInstanceField = BindingFlags.Instance | BindingFlags.NonPublic;

        /// <summary>配置一个测试动作定义。</summary>
        public static EnemyAiActionDefinition ConfigureForTests(this EnemyAiActionDefinition action, EnemyAiActionType actionType, float parameter = 0f)
        {
            if (action == null) throw new ArgumentNullException(nameof(action));
            SetPrivateField(action, "actionType", actionType);
            SetPrivateField(action, "parameter", parameter);
            return action;
        }

        /// <summary>配置一个测试条件定义。</summary>
        public static EnemyAiConditionDefinition ConfigureForTests(this EnemyAiConditionDefinition condition, EnemyAiConditionType conditionType, EnemyAiValueSource valueSource = EnemyAiValueSource.Constant, float constantValue = 0f, bool negate = false)
        {
            if (condition == null) throw new ArgumentNullException(nameof(condition));
            SetPrivateField(condition, "conditionType", conditionType);
            SetPrivateField(condition, "valueSource", valueSource);
            SetPrivateField(condition, "constantValue", constantValue);
            SetPrivateField(condition, "negate", negate);
            return condition;
        }

        /// <summary>配置一条测试状态转移，并复制条件集合以隔离调用方后续修改。</summary>
        public static EnemyAiTransitionDefinition ConfigureForTests(this EnemyAiTransitionDefinition transition, string targetStateId, int priority, IEnumerable<EnemyAiConditionDefinition> conditions)
        {
            if (transition == null) throw new ArgumentNullException(nameof(transition));
            SetPrivateField(transition, "targetStateId", targetStateId ?? string.Empty);
            SetPrivateField(transition, "priority", priority);
            SetPrivateField(transition, "conditions", CopyToList(conditions));
            return transition;
        }

        /// <summary>配置一个测试状态及其完整生命周期动作和转移。</summary>
        public static EnemyAiStateDefinition ConfigureForTests(this EnemyAiStateDefinition state, string stateId, IEnumerable<EnemyAiActionDefinition> enterActions, IEnumerable<EnemyAiActionDefinition> tickActions, IEnumerable<EnemyAiActionDefinition> exitActions, IEnumerable<EnemyAiTransitionDefinition> transitions)
        {
            if (state == null) throw new ArgumentNullException(nameof(state));
            SetPrivateField(state, "stateId", stateId ?? string.Empty);
            SetPrivateField(state, "enterActions", CopyToList(enterActions));
            SetPrivateField(state, "tickActions", CopyToList(tickActions));
            SetPrivateField(state, "exitActions", CopyToList(exitActions));
            SetPrivateField(state, "transitions", CopyToList(transitions));
            return state;
        }

        /// <summary>配置一份完整测试 AI 定义，测试专用可写能力不会进入 Runtime 程序集。</summary>
        public static EnemyAiDefinition ConfigureForTests(this EnemyAiDefinition definition, string definitionId, string initialStateId, float perceptionInterval, float decisionInterval, int targetLayerMask, float perceptionRadius, float chaseRadius, float attackRadius, float patrolRadius, float patrolStepDistance, float patrolSpeed, float chaseSpeed, float returnSpeed, float idleDuration, float attackCooldown, float arrivalDistance, IEnumerable<EnemyAiStateDefinition> states)
        {
            if (definition == null) throw new ArgumentNullException(nameof(definition));
            LayerMask layers = targetLayerMask;
            SetPrivateField(definition, "definitionId", definitionId ?? string.Empty);
            SetPrivateField(definition, "initialStateId", initialStateId ?? string.Empty);
            SetPrivateField(definition, "perceptionInterval", perceptionInterval);
            SetPrivateField(definition, "decisionInterval", decisionInterval);
            SetPrivateField(definition, "targetLayers", layers);
            SetPrivateField(definition, "targetTag", string.Empty);
            SetPrivateField(definition, "perceptionRadius", perceptionRadius);
            SetPrivateField(definition, "chaseRadius", chaseRadius);
            SetPrivateField(definition, "attackRadius", attackRadius);
            SetPrivateField(definition, "patrolRadius", patrolRadius);
            SetPrivateField(definition, "patrolStepDistance", patrolStepDistance);
            SetPrivateField(definition, "patrolSpeed", patrolSpeed);
            SetPrivateField(definition, "chaseSpeed", chaseSpeed);
            SetPrivateField(definition, "returnSpeed", returnSpeed);
            SetPrivateField(definition, "idleDuration", idleDuration);
            SetPrivateField(definition, "attackCooldown", attackCooldown);
            SetPrivateField(definition, "arrivalDistance", arrivalDistance);
            SetPrivateField(definition, "attackSignalId", "Tests.EnemyAttack");
            SetPrivateField(definition, "states", CopyToList(states));
            return definition;
        }

        /// <summary>将可空枚举复制为新列表，防止测试定义与调用方共享可变集合。</summary>
        private static List<T> CopyToList<T>(IEnumerable<T> values)
        {
            return values == null ? new List<T>() : new List<T>(values);
        }

        /// <summary>按运行时真实声明类型写入私有实例字段，字段重命名时立即提供明确失败。</summary>
        private static void SetPrivateField(object target, string fieldName, object value)
        {
            FieldInfo field = target.GetType().GetField(fieldName, PrivateInstanceField);
            if (field == null) throw new MissingFieldException(target.GetType().FullName, fieldName);
            field.SetValue(target, value);
        }
    }
}
