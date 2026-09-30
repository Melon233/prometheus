using System;
using System.Collections.Generic;
using UnityEngine;

namespace Xuan.Prometheus.Ai
{
    /// <summary>
    /// 标识 AI 状态动作使用的内置原子能力；资产只负责组合这些能力，具体运行时行为由 IEnemyAiAgent 实现。
    /// </summary>
    public enum EnemyAiActionType
    {
        None,
        PlayIdle,
        PlayMove,
        StopMovement,
        ResetIdleTimer,
        ChoosePatrolPoint,
        MoveToPatrolPoint,
        MoveToTarget,
        MoveHome,
        FaceTarget,
        StartAttack,
        ClearTarget
    }

    /// <summary>
    /// 标识状态转移可读取的通用条件；多个条件在同一转移中按逻辑与组合。
    /// </summary>
    public enum EnemyAiConditionType
    {
        Always,
        HasTarget,
        HasNoTarget,
        TargetDistanceLessOrEqual,
        TargetDistanceGreater,
        HomeDistanceLessOrEqual,
        HomeDistanceGreater,
        IdleElapsed,
        PatrolPointReached,
        AttackReady,
        AttackRunning,
        AttackNotRunning,
        CanAct,
        CanMove
    }

    /// <summary>
    /// 标识距离条件从根定义读取哪一个阈值，避免同一个半径在多条转移中重复填写。
    /// </summary>
    public enum EnemyAiValueSource
    {
        Constant,
        PerceptionRadius,
        ChaseRadius,
        AttackRadius,
        PatrolRadius,
        ArrivalDistance
    }

    /// <summary>
    /// 保存一个无运行时状态的原子动作定义；parameter 小于等于零时由动作使用根定义中的默认参数。
    /// </summary>
    [Serializable]
    public sealed class EnemyAiActionDefinition
    {
        [SerializeField] private EnemyAiActionType actionType;
        [SerializeField] private float parameter;

        /// <summary>获取需要执行的原子动作类型。</summary>
        public EnemyAiActionType ActionType => actionType;

        /// <summary>获取动作的可选覆盖参数；零表示使用 EnemyAiDefinition 默认值。</summary>
        public float Parameter => parameter;
    }

    /// <summary>
    /// 保存一个无运行时状态的转移条件定义。
    /// </summary>
    [Serializable]
    public sealed class EnemyAiConditionDefinition
    {
        [SerializeField] private EnemyAiConditionType conditionType;
        [SerializeField] private EnemyAiValueSource valueSource;
        [SerializeField] private float constantValue;
        [SerializeField] private bool negate;

        /// <summary>获取条件类型。</summary>
        public EnemyAiConditionType ConditionType => conditionType;

        /// <summary>获取条件阈值来源。</summary>
        public EnemyAiValueSource ValueSource => valueSource;

        /// <summary>获取 ValueSource 为 Constant 时使用的阈值。</summary>
        public float ConstantValue => constantValue;

        /// <summary>获取是否反转最终条件结果。</summary>
        public bool Negate => negate;
    }

    /// <summary>
    /// 描述一条从当前状态前往目标状态的有序转移；优先级越大越先被选择，相同优先级保持资产列表顺序。
    /// </summary>
    [Serializable]
    public sealed class EnemyAiTransitionDefinition
    {
        [SerializeField] private string targetStateId;
        [SerializeField] private int priority;
        [SerializeField] private List<EnemyAiConditionDefinition> conditions = new List<EnemyAiConditionDefinition>();

        /// <summary>获取目标状态稳定 ID。</summary>
        public string TargetStateId => targetStateId;

        /// <summary>获取转移优先级。</summary>
        public int Priority => priority;

        /// <summary>获取全部逻辑与条件。</summary>
        public IReadOnlyList<EnemyAiConditionDefinition> Conditions => conditions;
    }

    /// <summary>
    /// 描述一个可资产化 AI 状态及其进入、逐帧、退出动作和转移规则。
    /// </summary>
    [Serializable]
    public sealed class EnemyAiStateDefinition
    {
        [SerializeField] private string stateId;
        [SerializeField] private List<EnemyAiActionDefinition> enterActions = new List<EnemyAiActionDefinition>();
        [SerializeField] private List<EnemyAiActionDefinition> tickActions = new List<EnemyAiActionDefinition>();
        [SerializeField] private List<EnemyAiActionDefinition> exitActions = new List<EnemyAiActionDefinition>();
        [SerializeField] private List<EnemyAiTransitionDefinition> transitions = new List<EnemyAiTransitionDefinition>();

        /// <summary>获取状态稳定 ID。</summary>
        public string StateId => stateId;

        /// <summary>获取状态进入动作。</summary>
        public IReadOnlyList<EnemyAiActionDefinition> EnterActions => enterActions;

        /// <summary>获取状态逐帧动作。</summary>
        public IReadOnlyList<EnemyAiActionDefinition> TickActions => tickActions;

        /// <summary>获取状态退出动作。</summary>
        public IReadOnlyList<EnemyAiActionDefinition> ExitActions => exitActions;

        /// <summary>获取状态转移集合。</summary>
        public IReadOnlyList<EnemyAiTransitionDefinition> Transitions => transitions;
    }

    /// <summary>
    /// 保存一类敌人的完整 AI 资产定义；该资产在运行时严格只读，可被任意数量的 EnemyAiBrain 实例安全共享。
    /// </summary>
    [CreateAssetMenu(menuName = "Prometheus/AI/Enemy AI Definition", fileName = "EnemyAiDefinition")]
    public sealed class EnemyAiDefinition : ScriptableObject
    {
        [SerializeField] private string definitionId = "Enemy.Default";
        [SerializeField] private string initialStateId = EnemyAiStateIds.Idle;
        [SerializeField, Min(0.01f)] private float perceptionInterval = 0.2f;
        [SerializeField, Min(0.01f)] private float decisionInterval = 0.1f;
        [SerializeField] private LayerMask targetLayers = 1 << 6;
        [SerializeField] private string targetTag = "Player";
        [SerializeField, Min(0f)] private float perceptionRadius = 4f;
        [SerializeField, Min(0f)] private float chaseRadius = 8f;
        [SerializeField, Min(0f)] private float attackRadius = 2f;
        [SerializeField, Min(0f)] private float patrolRadius = 5f;
        [SerializeField, Min(0f)] private float patrolStepDistance = 3f;
        [SerializeField, Min(0f)] private float patrolSpeed = 2f;
        [SerializeField, Min(0f)] private float chaseSpeed = 3f;
        [SerializeField, Min(0f)] private float returnSpeed = 3f;
        [SerializeField, Min(0f)] private float idleDuration = 2f;
        [SerializeField, Min(0f)] private float attackCooldown = 2f;
        [SerializeField, Min(0.001f)] private float arrivalDistance = 0.1f;
        [SerializeField] private string attackSignalId = "Enemy.NormalAttack";
        [SerializeField] private List<EnemyAiStateDefinition> states = new List<EnemyAiStateDefinition>();

        /// <summary>获取定义的稳定业务 ID。</summary>
        public string DefinitionId => definitionId;

        /// <summary>获取初始状态 ID。</summary>
        public string InitialStateId => initialStateId;

        /// <summary>获取感知刷新间隔。</summary>
        public float PerceptionInterval => perceptionInterval;

        /// <summary>获取状态决策刷新间隔。</summary>
        public float DecisionInterval => decisionInterval;

        /// <summary>获取目标物理层掩码。</summary>
        public int TargetLayerMask => targetLayers.value;

        /// <summary>获取目标标签；空值表示不额外校验标签。</summary>
        public string TargetTag => targetTag;

        /// <summary>获取发现目标半径。</summary>
        public float PerceptionRadius => perceptionRadius;

        /// <summary>获取相对出生点的最大追击半径。</summary>
        public float ChaseRadius => chaseRadius;

        /// <summary>获取攻击距离。</summary>
        public float AttackRadius => attackRadius;

        /// <summary>获取巡逻半径。</summary>
        public float PatrolRadius => patrolRadius;

        /// <summary>获取每次选择巡逻点使用的最大位移。</summary>
        public float PatrolStepDistance => patrolStepDistance;

        /// <summary>获取巡逻速度。</summary>
        public float PatrolSpeed => patrolSpeed;

        /// <summary>获取追击速度。</summary>
        public float ChaseSpeed => chaseSpeed;

        /// <summary>获取返回出生点速度。</summary>
        public float ReturnSpeed => returnSpeed;

        /// <summary>获取默认待机持续时间。</summary>
        public float IdleDuration => idleDuration;

        /// <summary>获取攻击完成后的冷却时间。</summary>
        public float AttackCooldown => attackCooldown;

        /// <summary>获取移动到达判定距离。</summary>
        public float ArrivalDistance => arrivalDistance;

        /// <summary>获取攻击命中时发布的 EffectSignal 稳定来源 ID。</summary>
        public string AttackSignalId => attackSignalId;

        /// <summary>获取全部状态定义。</summary>
        public IReadOnlyList<EnemyAiStateDefinition> States => states;

        /// <summary>
        /// 查找指定稳定 ID 对应的状态。
        /// </summary>
        public bool TryGetState(string stateId, out EnemyAiStateDefinition state)
        {
            for (int index = 0; index < states.Count; index++)
            {
                EnemyAiStateDefinition candidate = states[index];
                if (candidate != null && string.Equals(candidate.StateId, stateId, StringComparison.Ordinal))
                {
                    state = candidate;
                    return true;
                }
            }

            state = null;
            return false;
        }

        /// <summary>
        /// 在运行时创建 Brain 前完整校验资产，避免缺失状态或错误引用在战斗中静默退化。
        /// </summary>
        public void ValidateOrThrow()
        {
            if (string.IsNullOrWhiteSpace(definitionId)) throw new InvalidOperationException($"Enemy AI definition '{name}' has an empty definition ID.");
            if (string.IsNullOrWhiteSpace(initialStateId)) throw new InvalidOperationException($"Enemy AI definition '{definitionId}' has an empty initial state ID.");
            if (perceptionInterval <= 0f || decisionInterval <= 0f) throw new InvalidOperationException($"Enemy AI definition '{definitionId}' requires positive perception and decision intervals.");
            if (perceptionRadius < 0f || chaseRadius < perceptionRadius || attackRadius < 0f || attackRadius > chaseRadius) throw new InvalidOperationException($"Enemy AI definition '{definitionId}' has inconsistent perception, attack, or chase radii.");
            if (patrolRadius < 0f || patrolStepDistance < 0f || patrolSpeed < 0f || chaseSpeed < 0f || returnSpeed < 0f || idleDuration < 0f || attackCooldown < 0f || arrivalDistance <= 0f) throw new InvalidOperationException($"Enemy AI definition '{definitionId}' contains an invalid movement or timing value.");
            if (targetLayers.value == 0) throw new InvalidOperationException($"Enemy AI definition '{definitionId}' has an empty target layer mask.");
            if (states == null || states.Count == 0) throw new InvalidOperationException($"Enemy AI definition '{definitionId}' does not contain any states.");

            HashSet<string> stateIds = new HashSet<string>(StringComparer.Ordinal);
            for (int stateIndex = 0; stateIndex < states.Count; stateIndex++)
            {
                EnemyAiStateDefinition state = states[stateIndex] ?? throw new InvalidOperationException($"Enemy AI definition '{definitionId}' contains a null state at index {stateIndex}.");
                if (string.IsNullOrWhiteSpace(state.StateId)) throw new InvalidOperationException($"Enemy AI definition '{definitionId}' contains a state with an empty ID at index {stateIndex}.");
                if (!stateIds.Add(state.StateId)) throw new InvalidOperationException($"Enemy AI definition '{definitionId}' contains duplicate state ID '{state.StateId}'.");
                ValidateActions(state.EnterActions, state.StateId, "enter");
                ValidateActions(state.TickActions, state.StateId, "tick");
                ValidateActions(state.ExitActions, state.StateId, "exit");
            }

            if (!stateIds.Contains(initialStateId)) throw new InvalidOperationException($"Enemy AI definition '{definitionId}' cannot find initial state '{initialStateId}'.");
            for (int stateIndex = 0; stateIndex < states.Count; stateIndex++) ValidateTransitions(states[stateIndex], stateIds);
        }

        /// <summary>
        /// 将指定值来源解析为当前根定义中的实际阈值。
        /// </summary>
        public float ResolveValue(EnemyAiValueSource source, float constantValue)
        {
            switch (source)
            {
                case EnemyAiValueSource.PerceptionRadius: return perceptionRadius;
                case EnemyAiValueSource.ChaseRadius: return chaseRadius;
                case EnemyAiValueSource.AttackRadius: return attackRadius;
                case EnemyAiValueSource.PatrolRadius: return patrolRadius;
                case EnemyAiValueSource.ArrivalDistance: return arrivalDistance;
                default: return constantValue;
            }
        }

        /// <summary>
        /// 校验动作集合没有空元素。
        /// </summary>
        private void ValidateActions(IReadOnlyList<EnemyAiActionDefinition> actions, string stateId, string phase)
        {
            if (actions == null) throw new InvalidOperationException($"Enemy AI state '{stateId}' in definition '{definitionId}' has a null {phase} action list.");
            for (int index = 0; index < actions.Count; index++)
            {
                if (actions[index] == null) throw new InvalidOperationException($"Enemy AI state '{stateId}' in definition '{definitionId}' has a null {phase} action at index {index}.");
            }
        }

        /// <summary>
        /// 校验状态的全部转移引用和条件元素。
        /// </summary>
        private void ValidateTransitions(EnemyAiStateDefinition state, HashSet<string> stateIds)
        {
            if (state.Transitions == null) throw new InvalidOperationException($"Enemy AI state '{state.StateId}' in definition '{definitionId}' has a null transition list.");
            for (int transitionIndex = 0; transitionIndex < state.Transitions.Count; transitionIndex++)
            {
                EnemyAiTransitionDefinition transition = state.Transitions[transitionIndex] ?? throw new InvalidOperationException($"Enemy AI state '{state.StateId}' in definition '{definitionId}' has a null transition at index {transitionIndex}.");
                if (!stateIds.Contains(transition.TargetStateId)) throw new InvalidOperationException($"Enemy AI state '{state.StateId}' in definition '{definitionId}' references missing target state '{transition.TargetStateId}'.");
                if (transition.Conditions == null) throw new InvalidOperationException($"Enemy AI transition '{state.StateId}' to '{transition.TargetStateId}' has a null condition list.");
                for (int conditionIndex = 0; conditionIndex < transition.Conditions.Count; conditionIndex++)
                {
                    if (transition.Conditions[conditionIndex] == null) throw new InvalidOperationException($"Enemy AI transition '{state.StateId}' to '{transition.TargetStateId}' has a null condition at index {conditionIndex}.");
                }
            }
        }

        /// <summary>
        /// 在 Inspector 修改资产时约束纯数值边界；跨字段和图引用错误仍由 ValidateOrThrow 提供明确诊断。
        /// </summary>
        private void OnValidate()
        {
            perceptionInterval = Mathf.Max(0.01f, perceptionInterval);
            decisionInterval = Mathf.Max(0.01f, decisionInterval);
            perceptionRadius = Mathf.Max(0f, perceptionRadius);
            chaseRadius = Mathf.Max(perceptionRadius, chaseRadius);
            attackRadius = Mathf.Clamp(attackRadius, 0f, chaseRadius);
            patrolRadius = Mathf.Max(0f, patrolRadius);
            patrolStepDistance = Mathf.Clamp(patrolStepDistance, 0f, patrolRadius);
            patrolSpeed = Mathf.Max(0f, patrolSpeed);
            chaseSpeed = Mathf.Max(0f, chaseSpeed);
            returnSpeed = Mathf.Max(0f, returnSpeed);
            idleDuration = Mathf.Max(0f, idleDuration);
            attackCooldown = Mathf.Max(0f, attackCooldown);
            arrivalDistance = Mathf.Max(0.001f, arrivalDistance);
        }
    }

    /// <summary>
    /// 提供项目默认敌人状态的稳定 ID；资产仍可声明这些常量之外的自定义状态。
    /// </summary>
    public static class EnemyAiStateIds
    {
        public const string Idle = "Idle";
        public const string Patrol = "Patrol";
        public const string Chase = "Chase";
        public const string Attack = "Attack";
        public const string ReturnHome = "ReturnHome";
    }
}
