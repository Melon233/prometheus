using System;
using System.Collections.Generic;
using Xuan.Prometheus;

namespace Xuan.Prometheus.Actor
{
    /// <summary>定义由统一固定 Tick 系统驱动的角色模拟参与者。</summary>
    public interface IActorSimulationParticipant
    {
        /// <summary>获取当前单局内稳定且唯一的正模拟编号；注册成功后该值不得改变。</summary>
        long SimulationId { get; }

        /// <summary>获取当前参与者是否应接收模拟 Tick 和表现更新。</summary>
        bool IsSimulationActive { get; }

        /// <summary>执行一次确定性的固定 Tick 模拟。</summary>
        /// <param name="tick">从一开始严格递增的全局模拟 Tick 编号。</param>
        /// <param name="fixedDeltaTime">由系统 Tick 频率计算出的固定模拟步长，单位为秒。</param>
        void SimulateTick(long tick, float fixedDeltaTime);

        /// <summary>使用最近完成的模拟状态执行一次帧表现更新。</summary>
        /// <param name="frameDeltaTime">当前表现帧的增量时间，单位为秒。</param>
        /// <param name="interpolationAlpha">基于剩余累积时间计算并限制在零到一之间的插值系数。</param>
        void Present(float frameDeltaTime, float interpolationAlpha);
    }

    /// <summary>把一个权威 Tick 拆成全局意图、运动和结算阶段，保证所有对象完成同一阶段后才进入下一阶段。</summary>
    public interface IActorPhasedSimulationParticipant : IActorSimulationParticipant
    {
        /// <summary>采样控制、推进行为并生成本 Tick 的运动与命中意图，但不得立即移动对象或结算命中。</summary>
        void PrepareSimulationTick(long tick, float fixedDeltaTime);

        /// <summary>在全部对象完成意图阶段后应用运动，使后续空间查询读取同一 Tick 的最终物理位置。</summary>
        void ApplySimulationMotion(long tick, float fixedDeltaTime);

        /// <summary>在全部对象完成运动后解析命中与其他依赖空间状态的结算。</summary>
        void ResolveSimulationTick(long tick, float fixedDeltaTime);

        /// <summary>在全部对象完成命中查询后提交已经收集的战斗信号，使同 Tick 互击结果不受 EntityId 回调先后影响。</summary>
        void CommitSimulationTick(long tick, float fixedDeltaTime);
    }

    /// <summary>由需要在固定 Tick 前缓存逐帧控制边沿的参与者选择性实现，避免高帧率下丢失 Pressed 输入。</summary>
    public interface IActorFrameCaptureParticipant
    {
        /// <summary>在当前帧任何固定 Tick 执行前缓存控制快照和瞬时事件。</summary>
        /// <param name="frameDeltaTime">当前表现帧的有限非负增量时间。</param>
        void CaptureFrame(float frameDeltaTime);
    }

    /// <summary>以稳定 SimulationId 顺序统一推进角色固定 Tick 模拟，并在普通更新阶段派发表现插值。</summary>
    public sealed class ActorSimulationSystem : XSystem
    {
        /// <summary>固定 Tick 边界判断使用的最小时间误差，避免浮点分块造成少推进一个 Tick。</summary>
        private const double MinimumAccumulatorEpsilonSeconds = 0.000000001d;

        /// <summary>按照 SimulationId 升序保存当前注册参与者。</summary>
        private readonly SortedDictionary<long, IActorSimulationParticipant> participants = new SortedDictionary<long, IActorSimulationParticipant>();

        /// <summary>保存一次分发开始时的稳定参与者快照，允许参与者在回调中安全注销对象。</summary>
        private readonly List<ParticipantEntry> iterationBuffer = new List<ParticipantEntry>();

        /// <summary>记录当前 Tick 已成功完成意图阶段的多阶段参与者，后续阶段只访问该稳定集合。</summary>
        private readonly HashSet<long> preparedPhasedParticipantIds = new HashSet<long>();

        /// <summary>固定 Tick 的双精度秒数，用于减少长时间累积误差。</summary>
        private readonly double fixedDeltaTimeSeconds;

        /// <summary>当前 Tick 频率对应的边界误差。</summary>
        private readonly double accumulatorEpsilonSeconds;

        /// <summary>尚未被固定 Tick 消费的累计真实时间，卡帧限步时不会被丢弃。</summary>
        private double accumulatedTimeSeconds;

        /// <summary>最近已经提交完成的全局模拟 Tick 编号。</summary>
        private long currentTick;

        /// <summary>最近一次 OnBeforeEntityUpdate 实际推进的 Tick 数量。</summary>
        private int lastFrameStepCount;

        /// <summary>记录系统当前是否处于参与者回调分发阶段。</summary>
        private bool isDispatching;

        /// <summary>记录系统是否已经释放。</summary>
        private bool disposed;

        /// <summary>在一个全局 Tick 的全部 Actor 运动与命中结算完成后触发；EffectRuntime、回放记录器和客户端预测校验可共享这一权威模拟时钟。</summary>
        public event Action<long, float> SimulationTickCompleted;

        /// <summary>创建统一固定 Tick 系统。</summary>
        /// <param name="tickRate">每秒执行的正整数模拟 Tick 数量。</param>
        /// <param name="maxStepsPerFrame">每次 OnBeforeEntityUpdate 最多执行的正整数 Tick 数量；超过部分保留在积压中。</param>
        /// <exception cref="ArgumentOutOfRangeException">tickRate 或 maxStepsPerFrame 不是正数时抛出。</exception>
        public ActorSimulationSystem(int tickRate = 60, int maxStepsPerFrame = 4)
        {
            if (tickRate <= 0) throw new ArgumentOutOfRangeException(nameof(tickRate), tickRate, "Tick 频率必须为正数。");
            if (maxStepsPerFrame <= 0) throw new ArgumentOutOfRangeException(nameof(maxStepsPerFrame), maxStepsPerFrame, "每帧最大 Tick 数量必须为正数。");
            TickRate = tickRate;
            MaxStepsPerFrame = maxStepsPerFrame;
            fixedDeltaTimeSeconds = 1d / tickRate;
            accumulatorEpsilonSeconds = Math.Max(MinimumAccumulatorEpsilonSeconds, fixedDeltaTimeSeconds * 0.000001d);
        }

        /// <summary>获取每秒执行的固定模拟 Tick 数量。</summary>
        public int TickRate { get; }

        /// <summary>获取每次 OnBeforeEntityUpdate 允许执行的最大 Tick 数量。</summary>
        public int MaxStepsPerFrame { get; }

        /// <summary>获取固定模拟步长，单位为秒。</summary>
        public float FixedDeltaTime => (float)fixedDeltaTimeSeconds;

        /// <summary>获取最近已经提交完成的全局模拟 Tick 编号；尚未推进时为零。</summary>
        public long CurrentTick => currentTick;

        /// <summary>获取当前已经注册的参与者数量。</summary>
        public int ParticipantCount => participants.Count;

        /// <summary>获取尚未被固定 Tick 消费的累计真实时间，单位为秒。</summary>
        public double AccumulatedTimeSeconds => accumulatedTimeSeconds;

        /// <summary>获取根据当前积压计算的待执行完整 Tick 数量。</summary>
        public long PendingTickCount
        {
            get
            {
                double pendingTicks = Math.Floor((accumulatedTimeSeconds + accumulatorEpsilonSeconds) / fixedDeltaTimeSeconds);
                if (pendingTicks <= 0d) return 0;
                return pendingTicks >= long.MaxValue ? long.MaxValue : (long)pendingTicks;
            }
        }

        /// <summary>获取最近一次 OnBeforeEntityUpdate 实际执行的 Tick 数量。</summary>
        public int LastFrameStepCount => lastFrameStepCount;

        /// <summary>获取当前表现插值系数；存在一个以上完整 Tick 积压时固定返回一。</summary>
        public float InterpolationAlpha
        {
            get
            {
                if (accumulatedTimeSeconds <= 0d) return 0f;
                double alpha = accumulatedTimeSeconds / fixedDeltaTimeSeconds;
                return alpha >= 1d ? 1f : (float)alpha;
            }
        }

        /// <summary>获取系统是否已经释放。</summary>
        public bool IsDisposed => disposed;

        /// <summary>按照 SimulationId 注册参与者；系统只支持 Unity 主线程串行调用。</summary>
        /// <param name="participant">需要注册的模拟参与者。</param>
        /// <returns>首次注册成功时返回 true；同一对象以同一编号重复注册时返回 false。</returns>
        /// <exception cref="ArgumentNullException">participant 为空时抛出。</exception>
        /// <exception cref="ArgumentOutOfRangeException">SimulationId 不是正数时抛出。</exception>
        /// <exception cref="InvalidOperationException">不同对象占用相同编号、同一对象更换编号重复注册或正在分发参与者回调时抛出。</exception>
        /// <exception cref="ObjectDisposedException">系统已经释放时抛出。</exception>
        public bool RegisterParticipant(IActorSimulationParticipant participant)
        {
            ThrowIfDisposed();
            ThrowIfDispatching("参与者回调分发期间不能注册新的模拟参与者。");
            if (participant == null) throw new ArgumentNullException(nameof(participant));
            long simulationId = participant.SimulationId;
            if (simulationId <= 0) throw new ArgumentOutOfRangeException(nameof(participant), simulationId, "SimulationId 必须为正数。");
            if (participants.TryGetValue(simulationId, out IActorSimulationParticipant existingParticipant))
            {
                if (ReferenceEquals(existingParticipant, participant)) return false;
                throw new InvalidOperationException($"SimulationId '{simulationId}' 已被另一个参与者占用。");
            }
            foreach (KeyValuePair<long, IActorSimulationParticipant> pair in participants)
            {
                if (ReferenceEquals(pair.Value, participant)) throw new InvalidOperationException($"同一参与者已使用 SimulationId '{pair.Key}' 注册，注册期间不允许改变编号。");
            }
            participants.Add(simulationId, participant);
            return true;
        }

        /// <summary>按注册时的 SimulationId 注销参与者；该操作允许在 SimulateTick、IsSimulationActive 或 Present 回调期间调用。</summary>
        /// <param name="simulationId">参与者注册时使用的正模拟编号。</param>
        /// <returns>找到并注销参与者时返回 true；编号不存在或已经注销时返回 false。</returns>
        /// <exception cref="ArgumentOutOfRangeException">simulationId 不是正数时抛出。</exception>
        /// <exception cref="ObjectDisposedException">系统已经释放时抛出。</exception>
        public bool UnregisterParticipant(long simulationId)
        {
            ThrowIfDisposed();
            if (simulationId <= 0) throw new ArgumentOutOfRangeException(nameof(simulationId), simulationId, "SimulationId 必须为正数。");
            return participants.Remove(simulationId);
        }

        /// <summary>正式推进一个固定模拟 Tick；该方法不消费 accumulator，适用于确定性单步、回放和工具驱动。</summary>
        /// <remarks>同一 Tick 内某个参与者抛出异常时，系统仍会继续调用其后的参与者并提交 Tick，最后抛出 AggregateException，避免已经成功执行的参与者在重试时被重复驱动。</remarks>
        /// <exception cref="AggregateException">一个或多个参与者读取激活状态或执行模拟时失败后抛出，内部异常包含参与者编号与阶段。</exception>
        /// <exception cref="InvalidOperationException">参与者回调分发期间嵌套推进或 Tick 编号耗尽时抛出。</exception>
        /// <exception cref="ObjectDisposedException">系统已经释放时抛出。</exception>
        public void StepOneTick()
        {
            ThrowIfDisposed();
            ThrowIfDispatching("参与者回调分发期间不能嵌套推进模拟 Tick。");
            StepOneTickCore();
        }

        /// <summary>累计当前帧时间并在上限内推进固定 Tick；超过上限的积压完整保留，传入零时间也会继续消费已有积压。</summary>
        /// <param name="dt">当前帧有限且非负的增量时间，单位为秒。</param>
        /// <exception cref="AggregateException">某个已消费 Tick 中存在参与者异常时抛出；该 Tick 已提交，剩余积压继续保留到后续调用。</exception>
        /// <exception cref="ArgumentOutOfRangeException">dt 为负数、NaN 或无穷大时抛出。</exception>
        /// <exception cref="InvalidOperationException">参与者回调中嵌套调用时抛出。</exception>
        public override void OnBeforeEntityUpdate(float dt)
        {
            if (disposed) return;
            ThrowIfDispatching("参与者回调分发期间不能嵌套推进帧模拟。");
            ValidateDeltaTime(dt, nameof(dt));
            DispatchFrameCapture(dt);
            accumulatedTimeSeconds += dt;
            lastFrameStepCount = 0;
            while (lastFrameStepCount < MaxStepsPerFrame && HasPendingTick())
            {
                ConsumeOneTickDuration();
                lastFrameStepCount++;
                StepOneTickCore();
            }
        }

        /// <summary>按照稳定 SimulationId 顺序表现所有活跃参与者，并传入当前帧时间与插值系数。</summary>
        /// <param name="dt">当前表现帧有限且非负的增量时间，单位为秒。</param>
        /// <exception cref="AggregateException">一个或多个参与者读取激活状态或执行表现时失败后抛出；同一帧中的其他参与者仍会继续执行。</exception>
        /// <exception cref="ArgumentOutOfRangeException">dt 为负数、NaN 或无穷大时抛出。</exception>
        /// <exception cref="InvalidOperationException">参与者回调中嵌套调用时抛出。</exception>
        public override void OnUpdate(float dt)
        {
            if (disposed) return;
            ThrowIfDispatching("参与者回调分发期间不能嵌套执行帧表现。");
            ValidateDeltaTime(dt, nameof(dt));
            DispatchPresentation(dt, InterpolationAlpha);
        }

        /// <summary>幂等释放系统，清空全部参与者与时间积压；接口不拥有参与者生命周期，因此不会调用参与者 Dispose。</summary>
        /// <exception cref="InvalidOperationException">参与者回调分发期间调用时抛出。</exception>
        public override void Dispose()
        {
            if (disposed) return;
            ThrowIfDispatching("参与者回调分发期间不能释放模拟系统。");
            disposed = true;
            participants.Clear();
            iterationBuffer.Clear();
            preparedPhasedParticipantIds.Clear();
            SimulationTickCompleted = null;
            accumulatedTimeSeconds = 0d;
            lastFrameStepCount = 0;
        }

        /// <summary>推进全局 Tick 并向全部仍然注册且活跃的参与者派发固定模拟。</summary>
        private void StepOneTickCore()
        {
            if (currentTick == long.MaxValue) throw new InvalidOperationException("全局模拟 Tick 编号已经耗尽。");
            currentTick++;
            List<Exception> exceptions = null;
            PrepareIterationBuffer();
            preparedPhasedParticipantIds.Clear();
            isDispatching = true;
            try
            {
                DispatchSimulationPreparation(ref exceptions);
                DispatchSimulationMotion(ref exceptions);
                DispatchSimulationResolution(ref exceptions);
                DispatchSimulationCommit(ref exceptions);
                DispatchSimulationTickCompleted(ref exceptions);
            }
            finally
            {
                isDispatching = false;
                iterationBuffer.Clear();
                preparedPhasedParticipantIds.Clear();
            }
            ThrowParticipantExceptions(exceptions, $"模拟 Tick '{currentTick}' 中存在参与者异常。");
        }

        /// <summary>按稳定编号执行传统单阶段参与者，或执行多阶段参与者的意图准备并记录成功项。</summary>
        private void DispatchSimulationPreparation(ref List<Exception> exceptions)
        {
            for (int index = 0; index < iterationBuffer.Count; index++)
            {
                ParticipantEntry entry = iterationBuffer[index];
                if (!TryValidateActiveParticipant(entry, ref exceptions)) continue;
                try
                {
                    if (entry.Participant is IActorPhasedSimulationParticipant phasedParticipant)
                    {
                        phasedParticipant.PrepareSimulationTick(currentTick, FixedDeltaTime);
                        preparedPhasedParticipantIds.Add(entry.SimulationId);
                    }
                    else entry.Participant.SimulateTick(currentTick, FixedDeltaTime);
                }
                catch (Exception exception)
                {
                    AddParticipantException(ref exceptions, entry.SimulationId, entry.Participant is IActorPhasedSimulationParticipant ? "执行意图准备阶段" : "执行 SimulateTick", exception);
                }
            }
        }

        /// <summary>让全部已成功准备的多阶段参与者按稳定编号应用运动；失败项不会进入结算阶段。</summary>
        private void DispatchSimulationMotion(ref List<Exception> exceptions)
        {
            for (int index = 0; index < iterationBuffer.Count; index++)
            {
                ParticipantEntry entry = iterationBuffer[index];
                if (!preparedPhasedParticipantIds.Contains(entry.SimulationId) || !(entry.Participant is IActorPhasedSimulationParticipant phasedParticipant) || !ValidateStableRegistration(entry, ref exceptions)) continue;
                try
                {
                    phasedParticipant.ApplySimulationMotion(currentTick, FixedDeltaTime);
                }
                catch (Exception exception)
                {
                    preparedPhasedParticipantIds.Remove(entry.SimulationId);
                    AddParticipantException(ref exceptions, entry.SimulationId, "执行运动阶段", exception);
                }
            }
        }

        /// <summary>在全体运动完成后按稳定编号结算空间查询和战斗结果。</summary>
        private void DispatchSimulationResolution(ref List<Exception> exceptions)
        {
            for (int index = 0; index < iterationBuffer.Count; index++)
            {
                ParticipantEntry entry = iterationBuffer[index];
                if (!preparedPhasedParticipantIds.Contains(entry.SimulationId) || !(entry.Participant is IActorPhasedSimulationParticipant phasedParticipant) || !ValidateStableRegistration(entry, ref exceptions)) continue;
                try
                {
                    phasedParticipant.ResolveSimulationTick(currentTick, FixedDeltaTime);
                }
                catch (Exception exception)
                {
                    AddParticipantException(ref exceptions, entry.SimulationId, "执行结算阶段", exception);
                }
            }
        }

        /// <summary>在全部空间查询完成后按稳定编号提交不可变战斗结果；结算阶段失败不会阻止其他已准备对象提交其成功收集的结果。</summary>
        private void DispatchSimulationCommit(ref List<Exception> exceptions)
        {
            for (int index = 0; index < iterationBuffer.Count; index++)
            {
                ParticipantEntry entry = iterationBuffer[index];
                if (!preparedPhasedParticipantIds.Contains(entry.SimulationId) || !(entry.Participant is IActorPhasedSimulationParticipant phasedParticipant) || !ValidateStableRegistration(entry, ref exceptions)) continue;
                try
                {
                    phasedParticipant.CommitSimulationTick(currentTick, FixedDeltaTime);
                }
                catch (Exception exception)
                {
                    AddParticipantException(ref exceptions, entry.SimulationId, "执行战斗提交阶段", exception);
                }
            }
        }

        /// <summary>按稳定订阅顺序通知全部 Tick 后置系统；单个订阅者失败不会阻止其他订阅者完成同一已提交 Tick。</summary>
        private void DispatchSimulationTickCompleted(ref List<Exception> exceptions)
        {
            Action<long, float> callbacks = SimulationTickCompleted;
            if (callbacks == null) return;
            Delegate[] invocationList = callbacks.GetInvocationList();
            for (int index = 0; index < invocationList.Length; index++)
            {
                try
                {
                    ((Action<long, float>)invocationList[index]).Invoke(currentTick, FixedDeltaTime);
                }
                catch (Exception exception)
                {
                    if (exceptions == null) exceptions = new List<Exception>();
                    exceptions.Add(new InvalidOperationException($"模拟 Tick '{currentTick}' 的后置订阅者 '{invocationList[index].Method.DeclaringType?.FullName}.{invocationList[index].Method.Name}' 执行失败。", exception));
                }
            }
        }

        /// <summary>在 Tick 第一个阶段统一验证注册稳定性和激活状态，后续阶段复用该决定。</summary>
        private bool TryValidateActiveParticipant(ParticipantEntry entry, ref List<Exception> exceptions)
        {
            if (!ValidateStableRegistration(entry, ref exceptions)) return false;
            bool isActive;
            try
            {
                isActive = entry.Participant.IsSimulationActive;
            }
            catch (Exception exception)
            {
                AddParticipantException(ref exceptions, entry.SimulationId, "读取 IsSimulationActive", exception);
                return false;
            }
            return isActive && ValidateStableRegistration(entry, ref exceptions);
        }

        /// <summary>向全部仍然注册且活跃的参与者派发表现更新。</summary>
        private void DispatchPresentation(float frameDeltaTime, float interpolationAlpha)
        {
            List<Exception> exceptions = null;
            PrepareIterationBuffer();
            isDispatching = true;
            try
            {
                for (int index = 0; index < iterationBuffer.Count; index++)
                {
                    ParticipantEntry entry = iterationBuffer[index];
                    if (!ValidateStableRegistration(entry, ref exceptions)) continue;
                    bool isActive;
                    try
                    {
                        isActive = entry.Participant.IsSimulationActive;
                    }
                    catch (Exception exception)
                    {
                        AddParticipantException(ref exceptions, entry.SimulationId, "读取 IsSimulationActive", exception);
                        continue;
                    }
                    if (!isActive || !ValidateStableRegistration(entry, ref exceptions)) continue;
                    try
                    {
                        entry.Participant.Present(frameDeltaTime, interpolationAlpha);
                    }
                    catch (Exception exception)
                    {
                        AddParticipantException(ref exceptions, entry.SimulationId, "执行 Present", exception);
                    }
                }
            }
            finally
            {
                isDispatching = false;
                iterationBuffer.Clear();
            }
            ThrowParticipantExceptions(exceptions, "角色表现更新中存在参与者异常。");
        }

        /// <summary>按稳定 SimulationId 顺序让可选帧采样参与者缓存控制边沿，随后本帧全部固定 Tick 读取同一缓冲。</summary>
        private void DispatchFrameCapture(float frameDeltaTime)
        {
            List<Exception> exceptions = null;
            PrepareIterationBuffer();
            isDispatching = true;
            try
            {
                for (int index = 0; index < iterationBuffer.Count; index++)
                {
                    ParticipantEntry entry = iterationBuffer[index];
                    if (!(entry.Participant is IActorFrameCaptureParticipant captureParticipant) || !ValidateStableRegistration(entry, ref exceptions)) continue;
                    bool isActive;
                    try
                    {
                        isActive = entry.Participant.IsSimulationActive;
                    }
                    catch (Exception exception)
                    {
                        AddParticipantException(ref exceptions, entry.SimulationId, "读取 IsSimulationActive", exception);
                        continue;
                    }
                    if (!isActive || !ValidateStableRegistration(entry, ref exceptions)) continue;
                    try
                    {
                        captureParticipant.CaptureFrame(frameDeltaTime);
                    }
                    catch (Exception exception)
                    {
                        AddParticipantException(ref exceptions, entry.SimulationId, "执行 CaptureFrame", exception);
                    }
                }
            }
            finally
            {
                isDispatching = false;
                iterationBuffer.Clear();
            }
            ThrowParticipantExceptions(exceptions, "角色帧控制采样中存在参与者异常。");
        }

        /// <summary>按照 SortedDictionary 的 SimulationId 升序生成本轮稳定快照。</summary>
        private void PrepareIterationBuffer()
        {
            iterationBuffer.Clear();
            foreach (KeyValuePair<long, IActorSimulationParticipant> pair in participants) iterationBuffer.Add(new ParticipantEntry(pair.Key, pair.Value));
        }

        /// <summary>确认快照条目仍然以原编号和原对象注册，并把编号读取或运行时变更转换为带上下文的参与者异常。</summary>
        private bool ValidateStableRegistration(ParticipantEntry entry, ref List<Exception> exceptions)
        {
            if (!participants.TryGetValue(entry.SimulationId, out IActorSimulationParticipant participant) || !ReferenceEquals(participant, entry.Participant)) return false;
            long currentSimulationId;
            try
            {
                currentSimulationId = participant.SimulationId;
            }
            catch (Exception exception)
            {
                AddParticipantException(ref exceptions, entry.SimulationId, "读取 SimulationId", exception);
                return false;
            }
            if (!participants.TryGetValue(entry.SimulationId, out IActorSimulationParticipant currentParticipant) || !ReferenceEquals(currentParticipant, entry.Participant)) return false;
            if (currentSimulationId == entry.SimulationId) return true;
            AddParticipantException(ref exceptions, entry.SimulationId, "验证 SimulationId", new InvalidOperationException($"参与者注册后把 SimulationId 从 '{entry.SimulationId}' 改为 '{currentSimulationId}'。"));
            return false;
        }

        /// <summary>判断 accumulator 是否包含至少一个完整固定 Tick。</summary>
        private bool HasPendingTick()
        {
            return accumulatedTimeSeconds + accumulatorEpsilonSeconds >= fixedDeltaTimeSeconds;
        }

        /// <summary>从 accumulator 消费一个固定 Tick 时间并归零边界附近的微小负误差。</summary>
        private void ConsumeOneTickDuration()
        {
            accumulatedTimeSeconds -= fixedDeltaTimeSeconds;
            if (accumulatedTimeSeconds < 0d && accumulatedTimeSeconds > -accumulatorEpsilonSeconds) accumulatedTimeSeconds = 0d;
        }

        /// <summary>为参与者异常增加稳定编号和失败阶段上下文。</summary>
        private static void AddParticipantException(ref List<Exception> exceptions, long simulationId, string stage, Exception exception)
        {
            if (exceptions == null) exceptions = new List<Exception>();
            exceptions.Add(new InvalidOperationException($"模拟参与者 '{simulationId}' 在{stage}时失败。", exception));
        }

        /// <summary>存在参与者异常时在完整分发结束后统一抛出。</summary>
        private static void ThrowParticipantExceptions(List<Exception> exceptions, string message)
        {
            if (exceptions != null && exceptions.Count > 0) throw new AggregateException(message, exceptions);
        }

        /// <summary>校验帧增量时间为有限非负数。</summary>
        private static void ValidateDeltaTime(float deltaTime, string parameterName)
        {
            if (float.IsNaN(deltaTime) || float.IsInfinity(deltaTime) || deltaTime < 0f) throw new ArgumentOutOfRangeException(parameterName, deltaTime, "增量时间必须是有限非负数。");
        }

        /// <summary>系统释放后阻止继续修改或手动推进。</summary>
        private void ThrowIfDisposed()
        {
            if (disposed) throw new ObjectDisposedException(nameof(ActorSimulationSystem));
        }

        /// <summary>参与者回调期间阻止会破坏稳定快照的注册、嵌套推进和释放操作。</summary>
        private void ThrowIfDispatching(string message)
        {
            if (isDispatching) throw new InvalidOperationException(message);
        }

        /// <summary>保存稳定快照中的注册编号与对象引用。</summary>
        private readonly struct ParticipantEntry
        {
            /// <summary>创建一个参与者快照条目。</summary>
            public ParticipantEntry(long simulationId, IActorSimulationParticipant participant)
            {
                SimulationId = simulationId;
                Participant = participant;
            }

            /// <summary>获取快照生成时的注册编号。</summary>
            public long SimulationId { get; }

            /// <summary>获取快照生成时的参与者引用。</summary>
            public IActorSimulationParticipant Participant { get; }
        }
    }
}
