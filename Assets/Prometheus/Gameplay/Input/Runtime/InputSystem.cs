using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;

namespace Xuan.Prometheus.Input
{
    /// <summary>在单个 GameplayKit 内集中采样输入、按动作仲裁控制权并向 Entity 或普通对象分发输入。</summary>
    public sealed class InputSystem : XSystem
    {
        private static readonly InputActionMask[] AtomicActions = { InputActionMask.Move, InputActionMask.Attack, InputActionMask.Skill, InputActionMask.Ultimate, InputActionMask.Dodge, InputActionMask.Jump, InputActionMask.SpecialAttack, InputActionMask.ToggleSprint, InputActionMask.ToggleWalk, InputActionMask.Navigate, InputActionMask.Submit, InputActionMask.Cancel, InputActionMask.SelectTeamMember1, InputActionMask.SelectTeamMember2, InputActionMask.SelectTeamMember3 };
        private readonly Dictionary<string, IInputSource> sources = new Dictionary<string, IInputSource>(StringComparer.Ordinal);
        private readonly List<IInputSource> sourceOrder = new List<IInputSource>();
        private readonly List<InputBinding> bindings = new List<InputBinding>();
        private readonly HashSet<IInputReceiver> pendingResetReceivers = new HashSet<IInputReceiver>(InputReceiverReferenceComparer.Instance);
        private readonly HashSet<IInputReceiver> frameReceivers = new HashSet<IInputReceiver>(InputReceiverReferenceComparer.Instance);
        private readonly Dictionary<IInputReceiver, InputActionMask> deliveries = new Dictionary<IInputReceiver, InputActionMask>(InputReceiverReferenceComparer.Instance);
        private readonly List<InputBinding> routingBindings = new List<InputBinding>();
        private readonly List<IInputSource> routingSources = new List<IInputSource>();
        private IGameplayKit gameplayKit;
        private int nextBindingId = 1;
        private long nextRegistrationOrder;
        private long currentFrameId;
        private bool isDisposed;

        /// <summary>创建输入系统并注册必须存在的默认输入源。</summary>
        public InputSystem(IInputSource defaultSource)
        {
            if (defaultSource == null) throw new ArgumentNullException(nameof(defaultSource));
            DefaultSourceId = ValidateSourceId(defaultSource.SourceId, nameof(defaultSource));
            RegisterSource(defaultSource);
        }

        /// <summary>获取默认输入源标识。</summary>
        public string DefaultSourceId { get; }

        /// <summary>获取已经完成分发的最新系统帧编号。</summary>
        public long CurrentFrameId => currentFrameId;

        /// <summary>获取当前仍持有控制权的绑定数量。</summary>
        public int BindingCount => bindings.Count;

        /// <inheritdoc />
        public override void AfterNew(IGameplayKit gameplayKit)
        {
            ThrowIfDisposed();
            if (gameplayKit == null) throw new ArgumentNullException(nameof(gameplayKit));
            if (this.gameplayKit != null && !ReferenceEquals(this.gameplayKit, gameplayKit)) throw new InvalidOperationException("InputSystem cannot move between GameplayKit instances.");
            this.gameplayKit = gameplayKit;
        }

        /// <summary>注册一个由当前 InputSystem 托管生命周期的额外输入源。</summary>
        public void RegisterSource(IInputSource source)
        {
            ThrowIfDisposed();
            if (source == null) throw new ArgumentNullException(nameof(source));
            string sourceId = ValidateSourceId(source.SourceId, nameof(source));
            if (sources.ContainsKey(sourceId)) throw new InvalidOperationException($"InputSystem already contains an input source named '{sourceId}'.");
            sources.Add(sourceId, source);
            sourceOrder.Add(source);
        }

        /// <summary>为任意接收者申请指定输入源的部分动作控制权。</summary>
        public ControlLease AcquireControl(string sourceId, IInputReceiver receiver, InputActionMask actions, InputContext context, int bindingPriority = 0, InputDeliveryMode deliveryMode = InputDeliveryMode.Exclusive)
        {
            ThrowIfDisposed();
            sourceId = ValidateSourceId(sourceId, nameof(sourceId));
            if (!sources.ContainsKey(sourceId)) throw new InvalidOperationException($"InputSystem does not contain an input source named '{sourceId}'.");
            if (receiver == null) throw new ArgumentNullException(nameof(receiver));
            ValidateActions(actions);
            if (string.IsNullOrWhiteSpace(context.Name)) throw new ArgumentException("Input context must be created with a non-empty name.", nameof(context));
            if (!Enum.IsDefined(typeof(InputDeliveryMode), deliveryMode)) throw new ArgumentOutOfRangeException(nameof(deliveryMode), deliveryMode, "Unknown input delivery mode.");
            ValidateExclusiveConflict(sourceId, actions, context.Priority, bindingPriority, deliveryMode);
            int bindingId = nextBindingId++;
            ControlLease lease = new ControlLease(this, bindingId);
            bindings.Add(new InputBinding(bindingId, nextRegistrationOrder++, sourceId, receiver, actions, context, bindingPriority, deliveryMode, lease));
            return lease;
        }

        /// <summary>为指定 Entity 申请动作控制权，并使用适配器把输入写入其 InputComponent。</summary>
        public ControlLease AcquireEntityControl(int entityId, InputActionMask actions, InputContext context, int bindingPriority = 0, InputDeliveryMode deliveryMode = InputDeliveryMode.Exclusive)
        {
            return AcquireEntityControl(DefaultSourceId, entityId, actions, context, bindingPriority, deliveryMode);
        }

        /// <summary>为指定 Entity 申请来自特定输入源的动作控制权。</summary>
        public ControlLease AcquireEntityControl(string sourceId, int entityId, InputActionMask actions, InputContext context, int bindingPriority = 0, InputDeliveryMode deliveryMode = InputDeliveryMode.Exclusive)
        {
            ThrowIfDisposed();
            if (gameplayKit == null) throw new InvalidOperationException("InputSystem must complete AfterNew before it can bind an Entity.");
            return AcquireControl(sourceId, new EntityInputReceiver(gameplayKit, entityId), actions, context, bindingPriority, deliveryMode);
        }

        /// <summary>在 Entity Logic 执行前完成一次输入采样、状态清理、动作仲裁和分发。</summary>
        public override void BeforeEntityUpdate(float dt)
        {
            if (isDisposed) return;
            currentFrameId++;
            PruneDeadBindings();
            routingBindings.Clear();
            routingBindings.AddRange(bindings);
            routingSources.Clear();
            routingSources.AddRange(sourceOrder);
            try
            {
                ResetFrameReceivers();
                for (int sourceIndex = 0; sourceIndex < routingSources.Count; sourceIndex++)
                {
                    IInputSource source = routingSources[sourceIndex];
                    InputFrame frame = source.Sample(currentFrameId);
                    RouteSourceFrame(source.SourceId, frame);
                }
            }
            finally
            {
                deliveries.Clear();
                frameReceivers.Clear();
                routingBindings.Clear();
                routingSources.Clear();
            }
        }

        /// <summary>释放全部绑定和输入源，并让仍存活的非 Entity 接收者清除残留状态。</summary>
        public override void Dispose()
        {
            if (isDisposed) return;
            for (int index = 0; index < bindings.Count; index++)
            {
                InputBinding binding = bindings[index];
                if (binding.Receiver.IsAlive) binding.Receiver.ResetInput();
                binding.Lease.Invalidate();
            }
            for (int index = sourceOrder.Count - 1; index >= 0; index--) sourceOrder[index].Dispose();
            bindings.Clear();
            pendingResetReceivers.Clear();
            frameReceivers.Clear();
            deliveries.Clear();
            routingBindings.Clear();
            routingSources.Clear();
            sourceOrder.Clear();
            sources.Clear();
            gameplayKit = null;
            isDisposed = true;
        }

        /// <summary>由 ControlLease 释放对应的内部绑定，并安排目标在下一输入帧清除旧状态。</summary>
        internal void ReleaseControl(int bindingId)
        {
            if (isDisposed) return;
            for (int index = bindings.Count - 1; index >= 0; index--)
            {
                InputBinding binding = bindings[index];
                if (binding.BindingId != bindingId) continue;
                bindings.RemoveAt(index);
                binding.Lease.Invalidate();
                pendingResetReceivers.Add(binding.Receiver);
                return;
            }
        }

        /// <summary>清除上一帧状态，并确保一个接收者即使拥有多条绑定也只清理一次。</summary>
        private void ResetFrameReceivers()
        {
            frameReceivers.Clear();
            foreach (IInputReceiver receiver in pendingResetReceivers)
            {
                if (receiver.IsAlive) frameReceivers.Add(receiver);
            }
            pendingResetReceivers.Clear();
            for (int index = 0; index < routingBindings.Count; index++)
            {
                IInputReceiver receiver = routingBindings[index].Receiver;
                if (receiver.IsAlive) frameReceivers.Add(receiver);
            }
            foreach (IInputReceiver receiver in frameReceivers) receiver.ResetInput();
        }

        /// <summary>对一个输入源逐动作执行上下文和绑定优先级仲裁，并向最终接收者分发合并后的动作掩码。</summary>
        private void RouteSourceFrame(string sourceId, InputFrame frame)
        {
            deliveries.Clear();
            for (int actionIndex = 0; actionIndex < AtomicActions.Length; actionIndex++) RouteAction(sourceId, AtomicActions[actionIndex]);
            foreach (KeyValuePair<IInputReceiver, InputActionMask> delivery in deliveries)
            {
                if (delivery.Key.IsAlive) delivery.Key.ReceiveInput(frame, delivery.Value);
            }
        }

        /// <summary>为一个原子动作找出最高仲裁层级，并同时保留所有观察者。</summary>
        private void RouteAction(string sourceId, InputActionMask action)
        {
            int bestContextPriority = int.MinValue;
            int bestBindingPriority = int.MinValue;
            for (int index = 0; index < routingBindings.Count; index++)
            {
                InputBinding binding = routingBindings[index];
                if (!binding.Receiver.IsAlive || !string.Equals(binding.SourceId, sourceId, StringComparison.Ordinal) || (binding.Actions & action) == 0) continue;
                if (binding.DeliveryMode == InputDeliveryMode.Observe)
                {
                    AddDelivery(binding.Receiver, action);
                    continue;
                }
                if (binding.Context.Priority > bestContextPriority || binding.Context.Priority == bestContextPriority && binding.BindingPriority > bestBindingPriority)
                {
                    bestContextPriority = binding.Context.Priority;
                    bestBindingPriority = binding.BindingPriority;
                }
            }
            if (bestContextPriority == int.MinValue) return;
            for (int index = 0; index < routingBindings.Count; index++)
            {
                InputBinding binding = routingBindings[index];
                if (!binding.Receiver.IsAlive || binding.DeliveryMode == InputDeliveryMode.Observe || !string.Equals(binding.SourceId, sourceId, StringComparison.Ordinal) || (binding.Actions & action) == 0) continue;
                if (binding.Context.Priority == bestContextPriority && binding.BindingPriority == bestBindingPriority) AddDelivery(binding.Receiver, action);
            }
        }

        /// <summary>把同一输入源准备交给同一接收者的多个动作合并为一次调用。</summary>
        private void AddDelivery(IInputReceiver receiver, InputActionMask action)
        {
            if (deliveries.TryGetValue(receiver, out InputActionMask existingActions)) deliveries[receiver] = existingActions | action;
            else deliveries.Add(receiver, action);
        }

        /// <summary>移除目标已经失效的绑定，并使对应租约立即进入已释放状态。</summary>
        private void PruneDeadBindings()
        {
            for (int index = bindings.Count - 1; index >= 0; index--)
            {
                InputBinding binding = bindings[index];
                if (binding.Receiver.IsAlive) continue;
                bindings.RemoveAt(index);
                binding.Lease.Invalidate();
            }
        }

        /// <summary>拒绝同一仲裁层级中语义不明确的独占动作重叠。</summary>
        private void ValidateExclusiveConflict(string sourceId, InputActionMask actions, int contextPriority, int bindingPriority, InputDeliveryMode deliveryMode)
        {
            if (deliveryMode == InputDeliveryMode.Observe) return;
            for (int index = 0; index < bindings.Count; index++)
            {
                InputBinding binding = bindings[index];
                if (binding.DeliveryMode == InputDeliveryMode.Observe || !string.Equals(binding.SourceId, sourceId, StringComparison.Ordinal)) continue;
                if (binding.Context.Priority != contextPriority || binding.BindingPriority != bindingPriority || (binding.Actions & actions) == 0) continue;
                if (binding.DeliveryMode == InputDeliveryMode.Exclusive || deliveryMode == InputDeliveryMode.Exclusive) throw new InvalidOperationException($"Input actions '{binding.Actions & actions}' already have an exclusive binding at context priority {contextPriority} and binding priority {bindingPriority}.");
            }
        }

        /// <summary>校验绑定动作只包含已声明的原子动作且不能为空。</summary>
        private static void ValidateActions(InputActionMask actions)
        {
            if (actions == InputActionMask.None) throw new ArgumentOutOfRangeException(nameof(actions), actions, "Input binding actions cannot be empty.");
            if ((actions & ~InputActionMask.All) != 0) throw new ArgumentOutOfRangeException(nameof(actions), actions, "Input binding contains unknown actions.");
        }

        /// <summary>校验输入源标识并返回原值，避免静默修剪产生重复名称。</summary>
        private static string ValidateSourceId(string sourceId, string parameterName)
        {
            if (string.IsNullOrWhiteSpace(sourceId)) throw new ArgumentException("Input source ID cannot be empty.", parameterName);
            return sourceId;
        }

        /// <summary>防止已经释放的输入系统重新注册输入源或控制权。</summary>
        private void ThrowIfDisposed()
        {
            if (isDisposed) throw new ObjectDisposedException(nameof(InputSystem));
        }

        /// <summary>保存一条不可变的内部输入路由及其控制权租约。</summary>
        private sealed class InputBinding
        {
            /// <summary>创建一条具有稳定注册序号的内部输入绑定。</summary>
            public InputBinding(int bindingId, long registrationOrder, string sourceId, IInputReceiver receiver, InputActionMask actions, InputContext context, int bindingPriority, InputDeliveryMode deliveryMode, ControlLease lease)
            {
                BindingId = bindingId;
                RegistrationOrder = registrationOrder;
                SourceId = sourceId;
                Receiver = receiver;
                Actions = actions;
                Context = context;
                BindingPriority = bindingPriority;
                DeliveryMode = deliveryMode;
                Lease = lease;
            }

            /// <summary>获取内部绑定编号。</summary>
            public int BindingId { get; }

            /// <summary>获取稳定注册序号，供诊断和未来确定性扩展使用。</summary>
            public long RegistrationOrder { get; }

            /// <summary>获取输入源标识。</summary>
            public string SourceId { get; }

            /// <summary>获取输入接收者。</summary>
            public IInputReceiver Receiver { get; }

            /// <summary>获取绑定的动作集合。</summary>
            public InputActionMask Actions { get; }

            /// <summary>获取绑定所属上下文。</summary>
            public InputContext Context { get; }

            /// <summary>获取上下文内部优先级。</summary>
            public int BindingPriority { get; }

            /// <summary>获取动作分发模式。</summary>
            public InputDeliveryMode DeliveryMode { get; }

            /// <summary>获取与绑定对应的控制权租约。</summary>
            public ControlLease Lease { get; }
        }

        /// <summary>保证接收者容器按对象引用而不是业务 Equals 实现区分目标。</summary>
        private sealed class InputReceiverReferenceComparer : IEqualityComparer<IInputReceiver>
        {
            /// <summary>获取共享的引用比较器实例。</summary>
            public static readonly InputReceiverReferenceComparer Instance = new InputReceiverReferenceComparer();

            /// <inheritdoc />
            public bool Equals(IInputReceiver left, IInputReceiver right)
            {
                return ReferenceEquals(left, right);
            }

            /// <inheritdoc />
            public int GetHashCode(IInputReceiver receiver)
            {
                return RuntimeHelpers.GetHashCode(receiver);
            }
        }
    }
}
