using System;
using System.Collections.Generic;
using UnityEngine;

namespace Xuan.Prometheus.Ai
{
    /// <summary>
    /// 定义通用 AI Brain 对宿主怪物能力的最小依赖，使决策运行时不直接依赖具体 Slime 组件或表现实现。
    /// </summary>
    public interface IEnemyAiAgent
    {
        /// <summary>获取宿主当前位置。</summary>
        Vector3 Position { get; }

        /// <summary>获取宿主当前是否允许执行普通行为。</summary>
        bool CanAct { get; }

        /// <summary>获取宿主当前是否允许移动。</summary>
        bool CanMove { get; }

        /// <summary>尝试在指定范围和层中选择一个有效目标。</summary>
        bool TryAcquireTarget(float radius, int layerMask, string requiredTag, out Transform target);

        /// <summary>判断已有目标是否仍可被当前宿主追踪。</summary>
        bool IsTargetValid(Transform target);

        /// <summary>按照世界方向移动宿主。</summary>
        void Move(Vector3 worldDirection, float speed, float deltaTime);

        /// <summary>停止宿主当前移动意图。</summary>
        void StopMovement();

        /// <summary>让宿主朝向指定世界方向。</summary>
        void Face(Vector3 worldDirection);

        /// <summary>播放待机表现。</summary>
        void PlayIdle();

        /// <summary>播放移动表现。</summary>
        void PlayMove();

        /// <summary>尝试启动一次攻击，并保证结束或打断时恰好回调一次。</summary>
        bool TryStartAttack(Action<bool> onFinished);

        /// <summary>取消当前攻击并关闭所有攻击窗口。</summary>
        void CancelAttack();
    }

    /// <summary>
    /// 保存单只敌人的全部可变 AI 数据；多个 Brain 共享同一资产时仍拥有完全隔离的黑板。
    /// </summary>
    public sealed class EnemyAiBlackboard
    {
        /// <summary>获取当前追踪目标。</summary>
        public Transform Target { get; internal set; }

        /// <summary>获取宿主创建时记录的出生点。</summary>
        public Vector3 HomePosition { get; internal set; }

        /// <summary>获取当前巡逻目标点。</summary>
        public Vector3 PatrolPoint { get; internal set; }

        /// <summary>获取当前是否已经选择巡逻点。</summary>
        public bool HasPatrolPoint { get; internal set; }

        /// <summary>获取当前状态已经持续的秒数。</summary>
        public float TimeInState { get; internal set; }

        /// <summary>获取剩余待机秒数。</summary>
        public float IdleRemaining { get; internal set; }

        /// <summary>获取剩余攻击冷却秒数。</summary>
        public float AttackCooldownRemaining { get; internal set; }

        /// <summary>获取当前是否正在播放不可立即重复触发的攻击。</summary>
        public bool AttackInProgress { get; internal set; }
    }

    /// <summary>
    /// 解释 EnemyAiDefinition 并驱动单只敌人的状态、感知、动作和生命周期；本类不持有任何测试专用入口。
    /// </summary>
    public sealed class EnemyAiBrain : IDisposable
    {
        private readonly EnemyAiDefinition definition;
        private readonly IEnemyAiAgent agent;
        private readonly Dictionary<string, EnemyAiStateDefinition> states = new Dictionary<string, EnemyAiStateDefinition>(StringComparer.Ordinal);
        private readonly System.Random random;
        private EnemyAiStateDefinition currentState;
        private float perceptionCountdown;
        private float decisionCountdown;
        private bool started;
        private bool running;
        private bool disposed;

        /// <summary>
        /// 创建一只敌人的独立 AI 运行时，并立即验证共享资产定义。
        /// </summary>
        public EnemyAiBrain(EnemyAiDefinition definition, IEnemyAiAgent agent, int randomSeed)
        {
            this.definition = definition != null ? definition : throw new ArgumentNullException(nameof(definition));
            this.agent = agent ?? throw new ArgumentNullException(nameof(agent));
            definition.ValidateOrThrow();
            random = new System.Random(randomSeed);
            Blackboard = new EnemyAiBlackboard { HomePosition = agent.Position };
            for (int index = 0; index < definition.States.Count; index++) states.Add(definition.States[index].StateId, definition.States[index]);
        }

        /// <summary>获取当前实例使用的只读资产定义。</summary>
        public EnemyAiDefinition Definition => definition;

        /// <summary>获取当前实例独占的运行时黑板。</summary>
        public EnemyAiBlackboard Blackboard { get; }

        /// <summary>获取当前状态稳定 ID；Brain 尚未启动时返回空字符串。</summary>
        public string CurrentStateId => currentState == null ? string.Empty : currentState.StateId;

        /// <summary>获取 Brain 是否已经开始运行且未被挂起。</summary>
        public bool IsRunning => running && !disposed;

        /// <summary>状态成功切换后发布旧状态和新状态 ID，供运行时调试面板或遥测订阅。</summary>
        public event Action<string, string> StateChanged;

        /// <summary>
        /// 首次启动 Brain 并进入资产指定的初始状态。
        /// </summary>
        public void Start()
        {
            ThrowIfDisposed();
            if (started) return;
            started = true;
            running = true;
            currentState = states[definition.InitialStateId];
            perceptionCountdown = 0f;
            decisionCountdown = 0f;
            Blackboard.TimeInState = 0f;
            ExecuteActions(currentState.EnterActions, 0f);
        }

        /// <summary>
        /// 在控制状态或受击表现结束后恢复当前状态；恢复时重新执行进入动作以重建动画和移动意图。
        /// </summary>
        public void Resume()
        {
            ThrowIfDisposed();
            if (!started)
            {
                Start();
                return;
            }

            if (running) return;
            running = true;
            perceptionCountdown = 0f;
            decisionCountdown = 0f;
            ExecuteActions(currentState.EnterActions, 0f);
        }

        /// <summary>
        /// 暂停行为执行并取消攻击窗口，但保留当前状态、目标和冷却数据供后续恢复。
        /// </summary>
        public void Suspend()
        {
            if (!running || disposed) return;
            running = false;
            agent.CancelAttack();
            agent.StopMovement();
            Blackboard.AttackInProgress = false;
        }

        /// <summary>
        /// 推进感知、决策和当前状态动作；deltaTime 为零时允许执行即时感知和转移但不会推进计时器。
        /// </summary>
        public void Tick(float deltaTime)
        {
            if (!running || disposed) return;
            float safeDeltaTime = Mathf.Max(0f, deltaTime);
            Blackboard.TimeInState += safeDeltaTime;
            Blackboard.IdleRemaining = Mathf.Max(0f, Blackboard.IdleRemaining - safeDeltaTime);
            Blackboard.AttackCooldownRemaining = Mathf.Max(0f, Blackboard.AttackCooldownRemaining - safeDeltaTime);
            perceptionCountdown -= safeDeltaTime;
            decisionCountdown -= safeDeltaTime;

            if (perceptionCountdown <= 0f)
            {
                UpdatePerception();
                perceptionCountdown = definition.PerceptionInterval;
            }

            if (decisionCountdown <= 0f)
            {
                EvaluateTransition();
                decisionCountdown = definition.DecisionInterval;
            }

            ExecuteActions(currentState.TickActions, safeDeltaTime);
        }

        /// <summary>
        /// 停止 Brain 并清理宿主动作引用；多次释放保持幂等。
        /// </summary>
        public void Dispose()
        {
            if (disposed) return;
            running = false;
            disposed = true;
            agent.CancelAttack();
            agent.StopMovement();
            Blackboard.Target = null;
            Blackboard.HasPatrolPoint = false;
            Blackboard.AttackInProgress = false;
            StateChanged = null;
        }

        /// <summary>
        /// 刷新当前目标；超过出生点追击半径时强制丢失目标并优先返回。
        /// </summary>
        private void UpdatePerception()
        {
            float homeDistance = HorizontalDistance(agent.Position, Blackboard.HomePosition);
            if (homeDistance > definition.ChaseRadius)
            {
                Blackboard.Target = null;
                return;
            }

            if (Blackboard.Target != null && !agent.IsTargetValid(Blackboard.Target)) Blackboard.Target = null;
            if (Blackboard.Target == null && agent.TryAcquireTarget(definition.PerceptionRadius, definition.TargetLayerMask, definition.TargetTag, out Transform target)) Blackboard.Target = target;
        }

        /// <summary>
        /// 从全部满足条件的转移中选择最高优先级项目，相同优先级保持资产列表顺序，并限制每次决策只切换一次。
        /// </summary>
        private void EvaluateTransition()
        {
            EnemyAiTransitionDefinition selected = null;
            int selectedPriority = int.MinValue;
            IReadOnlyList<EnemyAiTransitionDefinition> transitions = currentState.Transitions;
            for (int index = 0; index < transitions.Count; index++)
            {
                EnemyAiTransitionDefinition transition = transitions[index];
                if (transition.Priority <= selectedPriority || !AreConditionsMet(transition.Conditions)) continue;
                selected = transition;
                selectedPriority = transition.Priority;
            }

            if (selected != null && !string.Equals(selected.TargetStateId, currentState.StateId, StringComparison.Ordinal)) ChangeState(states[selected.TargetStateId]);
        }

        /// <summary>
        /// 判断一条转移的全部条件是否成立。
        /// </summary>
        private bool AreConditionsMet(IReadOnlyList<EnemyAiConditionDefinition> conditions)
        {
            for (int index = 0; index < conditions.Count; index++)
            {
                EnemyAiConditionDefinition condition = conditions[index];
                bool result = EvaluateCondition(condition);
                if (condition.Negate) result = !result;
                if (!result) return false;
            }

            return true;
        }

        /// <summary>
        /// 解释单个资产条件并读取当前实例黑板或宿主能力。
        /// </summary>
        private bool EvaluateCondition(EnemyAiConditionDefinition condition)
        {
            float threshold = definition.ResolveValue(condition.ValueSource, condition.ConstantValue);
            switch (condition.ConditionType)
            {
                case EnemyAiConditionType.HasTarget: return Blackboard.Target != null && agent.IsTargetValid(Blackboard.Target);
                case EnemyAiConditionType.HasNoTarget: return Blackboard.Target == null || !agent.IsTargetValid(Blackboard.Target);
                case EnemyAiConditionType.TargetDistanceLessOrEqual: return Blackboard.Target != null && HorizontalDistance(agent.Position, Blackboard.Target.position) <= threshold;
                case EnemyAiConditionType.TargetDistanceGreater: return Blackboard.Target == null || HorizontalDistance(agent.Position, Blackboard.Target.position) > threshold;
                case EnemyAiConditionType.HomeDistanceLessOrEqual: return HorizontalDistance(agent.Position, Blackboard.HomePosition) <= threshold;
                case EnemyAiConditionType.HomeDistanceGreater: return HorizontalDistance(agent.Position, Blackboard.HomePosition) > threshold;
                case EnemyAiConditionType.IdleElapsed: return Blackboard.IdleRemaining <= 0f;
                case EnemyAiConditionType.PatrolPointReached: return !Blackboard.HasPatrolPoint || HorizontalDistance(agent.Position, Blackboard.PatrolPoint) <= threshold;
                case EnemyAiConditionType.AttackReady: return Blackboard.AttackCooldownRemaining <= 0f;
                case EnemyAiConditionType.AttackRunning: return Blackboard.AttackInProgress;
                case EnemyAiConditionType.AttackNotRunning: return !Blackboard.AttackInProgress;
                case EnemyAiConditionType.CanAct: return agent.CanAct;
                case EnemyAiConditionType.CanMove: return agent.CanMove;
                default: return true;
            }
        }

        /// <summary>
        /// 对称执行旧状态退出和新状态进入动作，并重置状态计时。
        /// </summary>
        private void ChangeState(EnemyAiStateDefinition nextState)
        {
            string previousStateId = currentState.StateId;
            ExecuteActions(currentState.ExitActions, 0f);
            currentState = nextState;
            Blackboard.TimeInState = 0f;
            ExecuteActions(currentState.EnterActions, 0f);
            StateChanged?.Invoke(previousStateId, currentState.StateId);
        }

        /// <summary>
        /// 按资产顺序执行动作集合，保证进入和退出清理具有稳定顺序。
        /// </summary>
        private void ExecuteActions(IReadOnlyList<EnemyAiActionDefinition> actions, float deltaTime)
        {
            for (int index = 0; index < actions.Count; index++) ExecuteAction(actions[index], deltaTime);
        }

        /// <summary>
        /// 将一个原子动作定义路由到通用宿主能力。
        /// </summary>
        private void ExecuteAction(EnemyAiActionDefinition action, float deltaTime)
        {
            switch (action.ActionType)
            {
                case EnemyAiActionType.PlayIdle: agent.PlayIdle(); break;
                case EnemyAiActionType.PlayMove: agent.PlayMove(); break;
                case EnemyAiActionType.StopMovement: agent.StopMovement(); break;
                case EnemyAiActionType.ResetIdleTimer: Blackboard.IdleRemaining = action.Parameter > 0f ? action.Parameter : definition.IdleDuration; break;
                case EnemyAiActionType.ChoosePatrolPoint: ChoosePatrolPoint(action.Parameter); break;
                case EnemyAiActionType.MoveToPatrolPoint: MoveTowards(Blackboard.PatrolPoint, action.Parameter > 0f ? action.Parameter : definition.PatrolSpeed, deltaTime); break;
                case EnemyAiActionType.MoveToTarget: MoveToTarget(action.Parameter, deltaTime); break;
                case EnemyAiActionType.MoveHome: MoveTowards(Blackboard.HomePosition, action.Parameter > 0f ? action.Parameter : definition.ReturnSpeed, deltaTime); break;
                case EnemyAiActionType.FaceTarget: FaceTarget(); break;
                case EnemyAiActionType.StartAttack: StartAttack(); break;
                case EnemyAiActionType.ClearTarget: Blackboard.Target = null; break;
            }
        }

        /// <summary>
        /// 在出生点周围确定可复现的随机巡逻点，随机源属于当前 Brain 而不是 Unity 全局随机状态。
        /// </summary>
        private void ChoosePatrolPoint(float distanceOverride)
        {
            float distance = distanceOverride > 0f ? distanceOverride : definition.PatrolStepDistance;
            distance = Mathf.Min(distance, definition.PatrolRadius);
            double angle = random.NextDouble() * Math.PI * 2d;
            Blackboard.PatrolPoint = Blackboard.HomePosition + new Vector3((float)Math.Cos(angle), 0f, (float)Math.Sin(angle)) * distance;
            Blackboard.HasPatrolPoint = true;
        }

        /// <summary>
        /// 朝当前有效目标移动；目标丢失时不产生移动。
        /// </summary>
        private void MoveToTarget(float speedOverride, float deltaTime)
        {
            if (Blackboard.Target == null || !agent.IsTargetValid(Blackboard.Target)) return;
            MoveTowards(Blackboard.Target.position, speedOverride > 0f ? speedOverride : definition.ChaseSpeed, deltaTime);
        }

        /// <summary>
        /// 朝世界位置移动并同步朝向，移动能力被控制状态禁用时立即停止移动意图。
        /// </summary>
        private void MoveTowards(Vector3 destination, float speed, float deltaTime)
        {
            if (!agent.CanMove)
            {
                agent.StopMovement();
                return;
            }

            Vector3 direction = destination - agent.Position;
            direction.y = 0f;
            if (direction.sqrMagnitude <= definition.ArrivalDistance * definition.ArrivalDistance)
            {
                agent.StopMovement();
                return;
            }

            Vector3 normalizedDirection = direction.normalized;
            agent.Face(normalizedDirection);
            agent.Move(normalizedDirection, speed, deltaTime);
        }

        /// <summary>
        /// 朝向当前有效目标但不产生位移。
        /// </summary>
        private void FaceTarget()
        {
            if (Blackboard.Target == null || !agent.IsTargetValid(Blackboard.Target)) return;
            Vector3 direction = Blackboard.Target.position - agent.Position;
            direction.y = 0f;
            if (direction.sqrMagnitude > 0.000001f) agent.Face(direction.normalized);
        }

        /// <summary>
        /// 在目标有效、攻击就绪且宿主允许行动时启动攻击；完成后进入资产配置冷却，打断不消耗完整冷却。
        /// </summary>
        private void StartAttack()
        {
            if (!agent.CanAct || Blackboard.AttackInProgress || Blackboard.AttackCooldownRemaining > 0f || Blackboard.Target == null || !agent.IsTargetValid(Blackboard.Target)) return;
            FaceTarget();
            Blackboard.AttackInProgress = agent.TryStartAttack(OnAttackFinished);
        }

        /// <summary>
        /// 接收宿主攻击表现的唯一完成回调并更新独立冷却状态。
        /// </summary>
        private void OnAttackFinished(bool completed)
        {
            if (disposed) return;
            Blackboard.AttackInProgress = false;
            if (completed) Blackboard.AttackCooldownRemaining = definition.AttackCooldown;
        }

        /// <summary>
        /// 计算忽略高度差的平面距离。
        /// </summary>
        private static float HorizontalDistance(Vector3 from, Vector3 to)
        {
            from.y = 0f;
            to.y = 0f;
            return Vector3.Distance(from, to);
        }

        /// <summary>
        /// 阻止已经释放的 Brain 被重新启动。
        /// </summary>
        private void ThrowIfDisposed()
        {
            if (disposed) throw new ObjectDisposedException(nameof(EnemyAiBrain));
        }
    }
}
