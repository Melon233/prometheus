using System;
using System.Collections.Generic;
using UnityEngine;
using Xuan.Prometheus.Logic;

namespace Xuan.Prometheus.Effects
{
    /// <summary>
    /// EffectRequest 是 TriggerRule 产生的待执行命令，所有请求都必须经过统一排序和执行队列。
    /// </summary>
    internal sealed class EffectRequest
    {
        /// <summary>获取请求的效果定义。</summary>
        public EffectDefinition Definition { get; }

        /// <summary>获取直接释放当前效果的实体。</summary>
        public Entity Caster { get; }

        /// <summary>获取效果目标实体。</summary>
        public Entity Target { get; }

        /// <summary>获取当前效果因果链的实际源头实体。</summary>
        public Entity Source { get; }

        /// <summary>获取产生请求的因果信号。</summary>
        public EffectSignal Signal { get; }

        /// <summary>获取请求执行阶段。</summary>
        public EffectExecutionPhase Phase { get; }

        /// <summary>获取同阶段内的执行优先级。</summary>
        public int Priority { get; }

        /// <summary>获取保证稳定排序的插入序号。</summary>
        public long Sequence { get; }

        /// <summary>
        /// 创建完整效果请求。
        /// </summary>
        public EffectRequest(EffectDefinition definition, Entity caster, Entity target, Entity source, EffectSignal signal, int priority, long sequence)
        {
            Definition = definition;
            Caster = caster;
            Target = target;
            Source = source;
            Signal = signal;
            Phase = definition.Phase;
            Priority = priority;
            Sequence = sequence;
        }
    }

    /// <summary>
    /// EffectReapplyResult 分别记录重复施加是否刷新持续时间和改变层数，避免用单一 changed 混淆两个生命周期分支。
    /// </summary>
    internal readonly struct EffectReapplyResult
    {
        /// <summary>获取有限持续时间是否已经刷新。</summary>
        public bool DurationRefreshed { get; }

        /// <summary>获取效果层数是否实际改变。</summary>
        public bool StacksChanged { get; }

        /// <summary>获取本次重复施加是否产生任何有效状态变化。</summary>
        public bool HasChanges => DurationRefreshed || StacksChanged;

        /// <summary>
        /// 创建一次重复施加的独立状态变化结果。
        /// </summary>
        public EffectReapplyResult(bool durationRefreshed, bool stacksChanged)
        {
            DurationRefreshed = durationRefreshed;
            StacksChanged = stacksChanged;
        }
    }

    /// <summary>
    /// EffectInstance 保存持续效果的全部运行状态，任何状态都不会回写共享 EffectDefinition。
    /// </summary>
    public sealed class EffectInstance
    {
        private readonly Dictionary<string, IDisposable> resources = new Dictionary<string, IDisposable>();
        private readonly List<IDisposable> triggerRegistrations = new List<IDisposable>();

        /// <summary>获取运行时唯一实例编号。</summary>
        public long InstanceId { get; }

        /// <summary>获取实例使用的只读定义。</summary>
        public EffectDefinition Definition { get; }

        /// <summary>获取最初直接释放效果的实体。</summary>
        public Entity Caster { get; }

        /// <summary>获取持有效果的目标实体。</summary>
        public Entity Owner { get; }

        /// <summary>获取当前效果因果链的实际源头实体。</summary>
        public Entity Source { get; }

        /// <summary>获取当前已经存在的时间。</summary>
        public float ElapsedTime { get; private set; }

        /// <summary>获取距离上一次周期执行已经经过的时间。</summary>
        public float TickElapsedTime { get; private set; }

        /// <summary>获取当前层数。</summary>
        public int Stacks { get; private set; } = 1;

        /// <summary>获取实例是否仍在容器中生效。</summary>
        public bool IsActive { get; private set; } = true;

        /// <summary>获取最近一次成功应用、叠层或刷新该实例的信号。</summary>
        public EffectSignal LastSignal { get; private set; }

        /// <summary>
        /// 创建一个新的持续效果实例。
        /// </summary>
        internal EffectInstance(long instanceId, EffectDefinition definition, Entity caster, Entity owner, Entity source, EffectSignal signal)
        {
            InstanceId = instanceId;
            Definition = definition;
            Caster = caster;
            Owner = owner;
            Source = source;
            LastSignal = signal;
        }

        /// <summary>
        /// 判断当前实例是否与指定定义、直接释放者和实际源头关系属于同一个堆叠组。
        /// </summary>
        internal bool Matches(EffectDefinition definition, Entity caster, Entity source)
        {
            if (!IsActive || definition == null || Definition.EffectId != definition.EffectId) return false;
            switch (definition.StackKeyPolicy)
            {
                case EffectStackKeyPolicy.Definition: return true;
                case EffectStackKeyPolicy.DefinitionAndSource: return ReferenceEquals(Source, source);
                case EffectStackKeyPolicy.DefinitionAndCaster: return ReferenceEquals(Caster, caster);
                default: return false;
            }
        }

        /// <summary>
        /// 根据定义的重复施加策略分别更新层数和有限持续时间，并保留周期 Tick 的既有进度。
        /// </summary>
        internal EffectReapplyResult Reapply(EffectStackPolicy policy, EffectSignal signal)
        {
            bool durationRefreshed = Definition.DurationType == EffectDurationType.Duration && (policy == EffectStackPolicy.RefreshDuration || policy == EffectStackPolicy.AddStackAndRefreshDuration);
            bool stacksChanged = false;
            if (durationRefreshed)
            {
                ElapsedTime = 0f;
            }
            if (policy == EffectStackPolicy.AddStack || policy == EffectStackPolicy.AddStackAndRefreshDuration)
            {
                int newStacks = Mathf.Min(Definition.MaxStacks, Stacks + 1);
                stacksChanged = newStacks != Stacks;
                Stacks = newStacks;
            }
            EffectReapplyResult result = new EffectReapplyResult(durationRefreshed, stacksChanged);
            if (result.HasChanges) LastSignal = signal;
            return result;
        }

        /// <summary>
        /// 推进实例计时并计算本帧需要执行的 Tick 数量和是否到期。
        /// </summary>
        internal int Advance(float deltaTime, int maxTicks, out bool expired)
        {
            if (!IsActive)
            {
                expired = false;
                return 0;
            }
            float safeDeltaTime = Mathf.Max(0f, deltaTime);
            if (Definition.DurationType == EffectDurationType.Duration) ElapsedTime += safeDeltaTime;
            int tickCount = 0;
            if (Definition.TickInterval > 0f)
            {
                TickElapsedTime += safeDeltaTime;
                int totalTicks = Mathf.FloorToInt(TickElapsedTime / Definition.TickInterval);
                tickCount = Mathf.Min(Mathf.Max(0, maxTicks), totalTicks);
                if (totalTicks > 0) TickElapsedTime -= totalTicks * Definition.TickInterval;
            }
            expired = Definition.DurationType == EffectDurationType.Duration && ElapsedTime >= Definition.Duration;
            return tickCount;
        }

        /// <summary>
        /// 设置实例拥有的可释放资源；相同键的旧资源会先被安全释放。
        /// </summary>
        internal void SetResource(string key, IDisposable resource)
        {
            string safeKey = string.IsNullOrWhiteSpace(key) ? resource.GetType().FullName : key;
            if (resources.TryGetValue(safeKey, out IDisposable previous)) previous.Dispose();
            resources[safeKey] = resource;
        }

        /// <summary>
        /// 保存效果存续期间授予的触发规则注册句柄。
        /// </summary>
        internal void AddTriggerRegistration(IDisposable registration)
        {
            if (registration != null) triggerRegistrations.Add(registration);
        }

        /// <summary>
        /// 停用实例并释放所有触发注册和属性修改句柄；重复调用不会产生副作用。
        /// </summary>
        internal void Deactivate()
        {
            if (!IsActive) return;
            IsActive = false;
            for (int i = triggerRegistrations.Count - 1; i >= 0; i--) triggerRegistrations[i].Dispose();
            triggerRegistrations.Clear();
            foreach (IDisposable resource in resources.Values) resource.Dispose();
            resources.Clear();
        }
    }

    /// <summary>
    /// EffectRuntime 负责信号路由、条件判断、请求排序、持续效果生命周期和递归保护。
    /// </summary>
    public sealed class EffectRuntime : IDisposable
    {
        private const int DefaultMaxChainDepth = 16;
        private const int DefaultMaxCommandsPerTransaction = 1024;
        private const int DefaultMaxTicksPerUpdate = 32;
        private readonly Queue<EffectSignal> signalQueue = new Queue<EffectSignal>();
        private readonly List<EffectRequest> requestQueue = new List<EffectRequest>();
        private readonly List<EffectTriggerRuntime> triggers = new List<EffectTriggerRuntime>();
        private readonly Dictionary<Entity, EffectContainer> containers = new Dictionary<Entity, EffectContainer>();
        private readonly System.Random random;
        private long nextSignalChainId = 1L;
        private long nextInstanceId = 1L;
        private long nextSequence = 1L;
        private bool isProcessing;
        private bool disposed;
        private int processedCommands;

        /// <summary>获取或设置单条因果链允许的最大深度。</summary>
        public int MaxChainDepth { get; set; } = DefaultMaxChainDepth;

        /// <summary>获取或设置单次事务允许执行的最大信号和请求数量。</summary>
        public int MaxCommandsPerTransaction { get; set; } = DefaultMaxCommandsPerTransaction;

        /// <summary>获取或设置单次 Update 为一个实例补算的最大 Tick 数量。</summary>
        public int MaxTicksPerUpdate { get; set; } = DefaultMaxTicksPerUpdate;

        /// <summary>当运行时产生诊断信息时触发，调用方可以连接到 Unity 日志或自定义调试面板。</summary>
        public event Action<string> Trace;

        /// <summary>
        /// 使用确定性随机种子创建效果运行时，便于测试和战斗回放。
        /// </summary>
        public EffectRuntime(int randomSeed = 1977)
        {
            random = new System.Random(randomSeed);
        }

        /// <summary>
        /// 注册一条由指定实体拥有的触发规则，并返回可释放注册句柄。
        /// </summary>
        public IDisposable RegisterTrigger(Entity owner, EffectTriggerDefinition definition)
        {
            ThrowIfDisposed();
            if (owner == null) throw new ArgumentNullException(nameof(owner));
            if (definition == null) throw new ArgumentNullException(nameof(definition));
            EffectTriggerRuntime runtime = new EffectTriggerRuntime(owner, definition);
            triggers.Add(runtime);
            return new EffectRegistration(() => runtime.Disable());
        }

        /// <summary>
        /// 注册触发集合中的全部规则，并返回统一释放句柄。
        /// </summary>
        public IDisposable RegisterTriggerSet(Entity owner, EffectTriggerSet triggerSet)
        {
            ThrowIfDisposed();
            if (triggerSet == null) throw new ArgumentNullException(nameof(triggerSet));
            List<IDisposable> registrations = new List<IDisposable>();
            foreach (EffectTriggerDefinition definition in triggerSet.Triggers) registrations.Add(RegisterTrigger(owner, definition));
            return new CompositeEffectRegistration(registrations);
        }

        /// <summary>
        /// 发布一条根信号；如果当前正在执行另一事务，该信号会安全追加到当前队列末尾。
        /// </summary>
        public void Publish(EffectSignal signal)
        {
            ThrowIfDisposed();
            if (signal == null) throw new ArgumentNullException(nameof(signal));
            RunTransaction(() => EnqueueSignal(signal));
        }

        /// <summary>
        /// 不经过 Trigger 直接请求一个效果，但仍然使用同一请求队列、堆叠规则和生命周期。
        /// </summary>
        public void ApplyEffect(EffectDefinition definition, Entity caster, Entity target, Entity source = null)
        {
            ThrowIfDisposed();
            if (definition == null) throw new ArgumentNullException(nameof(definition));
            EffectSignal signal = new EffectSignal(EffectSignalType.Manual, caster, target, source ?? caster);
            RunTransaction(() => EnqueueEffect(definition, caster, target, source ?? caster, signal, 0));
        }

        /// <summary>
        /// 推进全部触发冷却和持续效果；周期操作各自开启独立根事务。
        /// </summary>
        public void Tick(float deltaTime)
        {
            ThrowIfDisposed();
            float safeDeltaTime = Mathf.Max(0f, deltaTime);
            EffectTriggerRuntime[] triggerSnapshot = triggers.ToArray();
            foreach (EffectTriggerRuntime trigger in triggerSnapshot) trigger.Tick(safeDeltaTime);
            List<EffectInstance> instanceSnapshot = GetAllActiveInstances();
            foreach (EffectInstance instance in instanceSnapshot) TickInstance(instance, safeDeltaTime);
            triggers.RemoveAll(trigger => !trigger.IsEnabled);
        }

        /// <summary>
        /// 主动移除目标身上的指定效果实例。
        /// </summary>
        public void RemoveEffect(EffectInstance instance, EffectRemovalReason reason = EffectRemovalReason.Dispelled)
        {
            ThrowIfDisposed();
            if (instance == null || !instance.IsActive) return;
            EffectSignal signal = new EffectSignal(EffectSignalType.Manual, instance.Caster, instance.Owner, instance.Source, originEffectInstanceId: instance.InstanceId);
            RunTransaction(() => RemoveInstance(instance, reason, signal));
        }

        /// <summary>
        /// 移除指定实体持有的全部持续效果。
        /// </summary>
        public void RemoveAll(Entity owner, EffectRemovalReason reason = EffectRemovalReason.OwnerDisposed)
        {
            ThrowIfDisposed();
            if (owner == null || !containers.TryGetValue(owner, out EffectContainer container)) return;
            EffectInstance[] snapshot = container.Instances.ToArray();
            foreach (EffectInstance instance in snapshot) RemoveEffect(instance, reason);
        }

        /// <summary>
        /// 获取目标当前持有的指定效果层数；不存在时返回零。
        /// </summary>
        public int GetStackCount(Entity owner, string effectId)
        {
            if (owner == null || string.IsNullOrWhiteSpace(effectId)) return 0;
            if (!containers.TryGetValue(owner, out EffectContainer container)) return 0;
            EffectInstance instance = container.FindByEffectId(effectId);
            return instance == null ? 0 : instance.Stacks;
        }

        /// <summary>
        /// 获取目标当前全部活动效果的只读快照。
        /// </summary>
        public IReadOnlyList<EffectInstance> GetActiveEffects(Entity owner)
        {
            if (owner == null || !containers.TryGetValue(owner, out EffectContainer container)) return Array.Empty<EffectInstance>();
            return container.Instances.ToArray();
        }

        /// <summary>
        /// 追加子信号并应用深度保护；该方法只供效果操作和运行时内部调用。
        /// </summary>
        internal void EnqueueSignal(EffectSignal signal)
        {
            if (signal == null) return;
            if (signal.SignalChainId == 0L) signal.AssignTransaction(nextSignalChainId++, 0);
            if (signal.ChainDepth > MaxChainDepth)
            {
                EmitTrace($"EffectRuntime rejected signal {signal.Type}: chain depth {signal.ChainDepth} exceeded limit {MaxChainDepth}.");
                return;
            }
            signalQueue.Enqueue(signal);
        }

        /// <summary>
        /// 追加效果请求并赋予稳定序号；该方法不会立即执行效果。
        /// </summary>
        internal void EnqueueEffect(EffectDefinition definition, Entity caster, Entity target, Entity source, EffectSignal signal, int priorityOffset)
        {
            if (definition == null || target == null || signal == null) return;
            if (signal.SignalChainId == 0L) signal.AssignTransaction(nextSignalChainId++, 0);
            requestQueue.Add(new EffectRequest(definition, caster, target, source, signal, definition.Priority + priorityOffset, nextSequence++));
        }

        /// <summary>
        /// 按选择器从信号中取得目标实体，供 Trigger 和 ApplyEffectOperation 共享。
        /// </summary>
        internal static Entity SelectTarget(EffectSignal signal, EffectTargetSelector selector)
        {
            switch (selector)
            {
                case EffectTargetSelector.Caster: return signal.Caster;
                case EffectTargetSelector.Target: return signal.Target;
                case EffectTargetSelector.Source: return signal.Source;
                default: return null;
            }
        }

        /// <summary>
        /// 释放全部实例、触发规则和等待队列。
        /// </summary>
        public void Dispose()
        {
            if (disposed) return;
            Entity[] owners = new Entity[containers.Keys.Count];
            containers.Keys.CopyTo(owners, 0);
            foreach (Entity owner in owners) RemoveAll(owner, EffectRemovalReason.OwnerDisposed);
            foreach (EffectTriggerRuntime trigger in triggers) trigger.Disable();
            triggers.Clear();
            signalQueue.Clear();
            requestQueue.Clear();
            disposed = true;
        }

        /// <summary>
        /// 在同一同步事务中执行种子动作并持续排空信号和效果请求队列。
        /// </summary>
        private void RunTransaction(Action seedAction)
        {
            if (isProcessing)
            {
                seedAction();
                return;
            }
            isProcessing = true;
            processedCommands = 0;
            try
            {
                seedAction();
                DrainQueues();
            }
            finally
            {
                isProcessing = false;
            }
        }

        /// <summary>
        /// 优先处理新信号，再从按照阶段、优先级和序号排序的请求队列中执行一个效果。
        /// </summary>
        private void DrainQueues()
        {
            while (signalQueue.Count > 0 || requestQueue.Count > 0)
            {
                processedCommands++;
                if (processedCommands > MaxCommandsPerTransaction)
                {
                    EmitTrace($"EffectRuntime aborted transaction after exceeding {MaxCommandsPerTransaction} commands.");
                    signalQueue.Clear();
                    requestQueue.Clear();
                    return;
                }
                if (signalQueue.Count > 0)
                {
                    ProcessSignal(signalQueue.Dequeue());
                    continue;
                }
                requestQueue.Sort(CompareRequests);
                EffectRequest request = requestQueue[0];
                requestQueue.RemoveAt(0);
                ApplyRequest(request);
            }
        }

        /// <summary>
        /// 对信号匹配全部活动触发规则，并把成功规则的效果转换为请求。
        /// </summary>
        private void ProcessSignal(EffectSignal signal)
        {
            EffectTriggerRuntime[] snapshot = triggers.ToArray();
            foreach (EffectTriggerRuntime trigger in snapshot)
            {
                if (!trigger.CanTrigger(signal)) continue;
                if (random.NextDouble() > trigger.Definition.Probability) continue;
                Entity target = SelectTarget(signal, trigger.Definition.TargetSelector);
                if (target == null) continue;
                trigger.MarkTriggered(signal.SignalChainId);
                foreach (EffectDefinition definition in trigger.Definition.Effects) EnqueueEffect(definition, signal.Caster, target, signal.Source, signal, trigger.Definition.Priority);
                EmitTrace($"Signal {signal.Type} matched trigger {trigger.Definition.TriggerId} for signal chain {signal.SignalChainId}.");
            }
        }

        /// <summary>
        /// 根据持续时间类型和堆叠策略执行一个效果请求。
        /// </summary>
        private void ApplyRequest(EffectRequest request)
        {
            if (request.Definition.DurationType == EffectDurationType.Instant)
            {
                ExecuteOperations(request.Definition.OnApplyOperations, request.Definition, null, request.Signal, request.Caster, request.Target, request.Source);
                return;
            }
            EffectContainer container = GetOrCreateContainer(request.Target);
            EffectInstance existing = request.Definition.StackPolicy == EffectStackPolicy.Independent ? null : container.FindMatching(request.Definition, request.Caster, request.Source);
            if (existing == null)
            {
                CreateInstance(container, request);
                return;
            }
            switch (request.Definition.StackPolicy)
            {
                case EffectStackPolicy.Reject: return;
                case EffectStackPolicy.Replace:
                    RemoveInstance(existing, EffectRemovalReason.Replaced, request.Signal);
                    CreateInstance(GetOrCreateContainer(request.Target), request);
                    return;
                case EffectStackPolicy.RefreshDuration:
                case EffectStackPolicy.AddStack:
                case EffectStackPolicy.AddStackAndRefreshDuration:
                    ReapplyExisting(existing, request);
                    return;
                default: return;
            }
        }

        /// <summary>
        /// 创建实例、先加入容器、注册授予 Trigger，再执行 OnApply，以保证重入时可以找到该实例。
        /// </summary>
        private void CreateInstance(EffectContainer container, EffectRequest request)
        {
            EffectInstance instance = new EffectInstance(nextInstanceId++, request.Definition, request.Caster, request.Target, request.Source, request.Signal);
            container.Add(instance);
            foreach (EffectTriggerDefinition trigger in request.Definition.GrantedTriggers) instance.AddTriggerRegistration(RegisterTrigger(instance.Owner, trigger));
            ExecuteOperations(request.Definition.OnApplyOperations, request.Definition, instance, request.Signal, request.Caster, request.Target, request.Source);
            EffectSignal appliedSignal = request.Signal.CreateChild(EffectSignalType.EffectApplied, request.Caster, request.Target, request.Source, 1f, 1f, request.Signal.Tags | request.Definition.Tags, request.Signal.AbilityId, instance.InstanceId, request.Signal.Position);
            EnqueueSignal(appliedSignal);
            EmitTrace($"Applied effect {request.Definition.EffectId} as instance {instance.InstanceId}.");
        }

        /// <summary>
        /// 更新已有实例，并只为实际发生的层数变化和有限时长刷新执行各自的操作与信号。
        /// </summary>
        private void ReapplyExisting(EffectInstance instance, EffectRequest request)
        {
            EffectReapplyResult result = instance.Reapply(request.Definition.StackPolicy, request.Signal);
            if (result.StacksChanged)
            {
                ExecuteOperations(request.Definition.OnStackOperations, request.Definition, instance, request.Signal, instance.Caster, instance.Owner, instance.Source);
                EffectSignal stackedSignal = request.Signal.CreateChild(EffectSignalType.EffectStacked, instance.Caster, instance.Owner, instance.Source, instance.Stacks, instance.Stacks, request.Signal.Tags | request.Definition.Tags, request.Signal.AbilityId, instance.InstanceId, request.Signal.Position);
                EnqueueSignal(stackedSignal);
                EmitTrace($"Stacked effect {request.Definition.EffectId} to {instance.Stacks} stack(s).");
            }
            if (result.DurationRefreshed)
            {
                ExecuteOperations(request.Definition.OnRefreshOperations, request.Definition, instance, request.Signal, instance.Caster, instance.Owner, instance.Source);
                EffectSignal refreshedSignal = request.Signal.CreateChild(EffectSignalType.EffectRefreshed, instance.Caster, instance.Owner, instance.Source, request.Definition.Duration, request.Definition.Duration, request.Signal.Tags | request.Definition.Tags, request.Signal.AbilityId, instance.InstanceId, request.Signal.Position);
                EnqueueSignal(refreshedSignal);
                EmitTrace($"Refreshed effect {request.Definition.EffectId} duration to {request.Definition.Duration} second(s).");
            }
        }

        /// <summary>
        /// 推进一个实例，按需执行周期操作，并在计时结束后移除实例。
        /// </summary>
        private void TickInstance(EffectInstance instance, float deltaTime)
        {
            if (instance == null || !instance.IsActive) return;
            int tickCount = instance.Advance(deltaTime, MaxTicksPerUpdate, out bool expired);
            for (int i = 0; i < tickCount && instance.IsActive; i++)
            {
                DamageAttribute inheritedDamageAttribute = instance.LastSignal == null ? DamageAttribute.Physical : instance.LastSignal.DamageAttribute;
                EffectSignal tickSignal = new EffectSignal(EffectSignalType.PeriodicTick, instance.Caster, instance.Owner, instance.Source, instance.Stacks, instance.Stacks, instance.Definition.Tags | EffectTag.Periodic, originEffectInstanceId: instance.InstanceId, damageAttribute: inheritedDamageAttribute, damageActionType: DamageActionType.Periodic);
                RunTransaction(() =>
                {
                    EnqueueSignal(tickSignal);
                    ExecuteOperations(instance.Definition.OnTickOperations, instance.Definition, instance, tickSignal, instance.Caster, instance.Owner, instance.Source);
                });
            }
            if (!expired || !instance.IsActive) return;
            EffectSignal expirationSignal = new EffectSignal(EffectSignalType.Manual, instance.Caster, instance.Owner, instance.Source, originEffectInstanceId: instance.InstanceId);
            RunTransaction(() => RemoveInstance(instance, EffectRemovalReason.Expired, expirationSignal));
        }

        /// <summary>
        /// 从容器移除实例，执行 OnRemove，释放资源，并发布 EffectRemoved 信号。
        /// </summary>
        private void RemoveInstance(EffectInstance instance, EffectRemovalReason reason, EffectSignal signal)
        {
            if (instance == null || !instance.IsActive) return;
            if (containers.TryGetValue(instance.Owner, out EffectContainer container)) container.Remove(instance);
            ExecuteOperations(instance.Definition.OnRemoveOperations, instance.Definition, instance, signal, instance.Caster, instance.Owner, instance.Source);
            instance.Deactivate();
            EffectSignal removedSignal = signal.CreateChild(EffectSignalType.EffectRemoved, instance.Caster, instance.Owner, instance.Source, (float)reason, (float)reason, instance.Definition.Tags, signal.AbilityId, instance.InstanceId, signal.Position);
            EnqueueSignal(removedSignal);
            if (container != null && container.Instances.Count == 0) containers.Remove(instance.Owner);
            EmitTrace($"Removed effect {instance.Definition.EffectId} instance {instance.InstanceId} because {reason}.");
        }

        /// <summary>
        /// 依次执行定义中的操作，操作产生的新信号和请求只会追加到当前事务队列。
        /// </summary>
        private void ExecuteOperations(IReadOnlyList<EffectOperation> operations, EffectDefinition definition, EffectInstance instance, EffectSignal signal, Entity caster, Entity target, Entity source)
        {
            EffectOperationContext context = new EffectOperationContext(this, definition, instance, signal, caster, target, source);
            for (int i = 0; i < operations.Count; i++) operations[i]?.Execute(context);
        }

        /// <summary>
        /// 获取或创建实体对应的持续效果容器。
        /// </summary>
        private EffectContainer GetOrCreateContainer(Entity owner)
        {
            if (containers.TryGetValue(owner, out EffectContainer container)) return container;
            container = new EffectContainer(owner);
            containers.Add(owner, container);
            return container;
        }

        /// <summary>
        /// 创建全部活动实例快照，避免 Tick 中添加或移除实例时修改枚举集合。
        /// </summary>
        private List<EffectInstance> GetAllActiveInstances()
        {
            List<EffectInstance> result = new List<EffectInstance>();
            foreach (EffectContainer container in containers.Values) result.AddRange(container.Instances);
            return result;
        }

        /// <summary>
        /// 按阶段、优先级和稳定序号比较效果请求。
        /// </summary>
        private static int CompareRequests(EffectRequest left, EffectRequest right)
        {
            int phaseCompare = left.Phase.CompareTo(right.Phase);
            if (phaseCompare != 0) return phaseCompare;
            int priorityCompare = left.Priority.CompareTo(right.Priority);
            if (priorityCompare != 0) return priorityCompare;
            return left.Sequence.CompareTo(right.Sequence);
        }

        /// <summary>
        /// 将诊断信息发送给可选监听器。
        /// </summary>
        private void EmitTrace(string message)
        {
            Trace?.Invoke(message);
        }

        /// <summary>
        /// 防止在运行时释放后继续注册或执行效果。
        /// </summary>
        private void ThrowIfDisposed()
        {
            if (disposed) throw new ObjectDisposedException(nameof(EffectRuntime));
        }
    }

    /// <summary>
    /// EffectContainer 保存单个实体的活动实例并提供堆叠键查询。
    /// </summary>
    internal sealed class EffectContainer
    {
        private readonly List<EffectInstance> instances = new List<EffectInstance>();

        /// <summary>获取容器拥有者。</summary>
        public Entity Owner { get; }

        /// <summary>获取当前实例列表。</summary>
        public List<EffectInstance> Instances => instances;

        /// <summary>
        /// 创建指定实体的效果容器。
        /// </summary>
        public EffectContainer(Entity owner)
        {
            Owner = owner;
        }

        /// <summary>
        /// 将新实例加入容器。
        /// </summary>
        public void Add(EffectInstance instance)
        {
            instances.Add(instance);
        }

        /// <summary>
        /// 从容器移除实例。
        /// </summary>
        public void Remove(EffectInstance instance)
        {
            instances.Remove(instance);
        }

        /// <summary>
        /// 按定义堆叠策略查找可合并实例。
        /// </summary>
        public EffectInstance FindMatching(EffectDefinition definition, Entity caster, Entity source)
        {
            return instances.Find(instance => instance.Matches(definition, caster, source));
        }

        /// <summary>
        /// 按效果编号查找第一个活动实例。
        /// </summary>
        public EffectInstance FindByEffectId(string effectId)
        {
            return instances.Find(instance => instance.IsActive && instance.Definition.EffectId == effectId);
        }
    }

    /// <summary>
    /// EffectTriggerRuntime 保存定义之外的冷却、最近信号因果链编号和启用状态。
    /// </summary>
    internal sealed class EffectTriggerRuntime
    {
        private float cooldownRemaining;
        private long lastTriggeredSignalChainId = -1L;

        /// <summary>获取规则拥有者。</summary>
        public Entity Owner { get; }

        /// <summary>获取只读触发定义。</summary>
        public EffectTriggerDefinition Definition { get; }

        /// <summary>获取注册是否仍然有效。</summary>
        public bool IsEnabled { get; private set; } = true;

        /// <summary>
        /// 创建触发运行时。
        /// </summary>
        public EffectTriggerRuntime(Entity owner, EffectTriggerDefinition definition)
        {
            Owner = owner;
            Definition = definition;
        }

        /// <summary>
        /// 检查类型、拥有者范围、冷却、SignalChainId 去重和全部配置条件。
        /// </summary>
        public bool CanTrigger(EffectSignal signal)
        {
            if (!IsEnabled || signal.Type != Definition.SignalType || cooldownRemaining > 0f) return false;
            if (Definition.OncePerSignalChain && lastTriggeredSignalChainId == signal.SignalChainId) return false;
            if (!MatchesScope(signal)) return false;
            foreach (EffectConditionDefinition condition in Definition.Conditions) if (condition != null && !condition.Evaluate(signal)) return false;
            return true;
        }

        /// <summary>
        /// 记录成功触发并开始冷却。
        /// </summary>
        public void MarkTriggered(long signalChainId)
        {
            lastTriggeredSignalChainId = signalChainId;
            cooldownRemaining = Definition.Cooldown;
        }

        /// <summary>
        /// 推进冷却计时。
        /// </summary>
        public void Tick(float deltaTime)
        {
            cooldownRemaining = Mathf.Max(0f, cooldownRemaining - Mathf.Max(0f, deltaTime));
        }

        /// <summary>
        /// 关闭注册，使现有信号快照也不会继续触发该规则。
        /// </summary>
        public void Disable()
        {
            IsEnabled = false;
        }

        /// <summary>
        /// 判断规则拥有者是否在信号中处于配置的角色。
        /// </summary>
        private bool MatchesScope(EffectSignal signal)
        {
            switch (Definition.ListenScope)
            {
                case EffectListenScope.Caster: return ReferenceEquals(Owner, signal.Caster);
                case EffectListenScope.Target: return ReferenceEquals(Owner, signal.Target);
                case EffectListenScope.Source: return ReferenceEquals(Owner, signal.Source);
                case EffectListenScope.Any: return true;
                default: return false;
            }
        }
    }

    /// <summary>
    /// EffectRegistration 用幂等 Dispose 注销一条触发规则。
    /// </summary>
    internal sealed class EffectRegistration : IDisposable
    {
        private Action disposeAction;

        /// <summary>
        /// 创建注册句柄。
        /// </summary>
        public EffectRegistration(Action action)
        {
            disposeAction = action;
        }

        /// <summary>
        /// 首次调用时执行注销动作。
        /// </summary>
        public void Dispose()
        {
            Action action = disposeAction;
            disposeAction = null;
            action?.Invoke();
        }
    }

    /// <summary>
    /// CompositeEffectRegistration 将一组 Trigger 注册作为一个生命周期资源释放。
    /// </summary>
    internal sealed class CompositeEffectRegistration : IDisposable
    {
        private List<IDisposable> registrations;

        /// <summary>
        /// 创建组合注册句柄。
        /// </summary>
        public CompositeEffectRegistration(List<IDisposable> sourceRegistrations)
        {
            registrations = sourceRegistrations;
        }

        /// <summary>
        /// 按逆序释放全部注册，重复调用安全返回。
        /// </summary>
        public void Dispose()
        {
            if (registrations == null) return;
            for (int i = registrations.Count - 1; i >= 0; i--) registrations[i].Dispose();
            registrations = null;
        }
    }
}
