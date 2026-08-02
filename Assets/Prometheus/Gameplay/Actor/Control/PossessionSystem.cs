using System;
using System.Collections.Generic;
using System.Threading;
using UnityEngine;

namespace Xuan.Prometheus.Actor
{
    /// <summary>描述一个控制器针对一个 Pawn 申请的独占控制领域和仲裁优先级。</summary>
    public readonly struct ControlLeaseRequest
    {
        /// <summary>创建一个控制租约申请。</summary>
        /// <param name="controllerId">已经注册到 PossessionSystem 的控制器编号。</param>
        /// <param name="pawnId">已经注册到 PossessionSystem 的 Pawn 编号。</param>
        /// <param name="scopes">需要申请的一个或多个控制领域。</param>
        /// <param name="priority">仲裁优先级；数值更大的租约优先，相同优先级由更早申请者获胜。</param>
        public ControlLeaseRequest(int controllerId, int pawnId, ControlScope scopes, int priority)
        {
            ControllerId = controllerId;
            PawnId = pawnId;
            Scopes = scopes;
            Priority = priority;
        }

        /// <summary>获取申请控制权的控制器编号。</summary>
        public int ControllerId { get; }

        /// <summary>获取被控制的 Pawn 编号。</summary>
        public int PawnId { get; }

        /// <summary>获取该租约申请的控制领域。</summary>
        public ControlScope Scopes { get; }

        /// <summary>获取该租约的仲裁优先级。</summary>
        public int Priority { get; }
    }

    /// <summary>标识 PossessionSystem 创建的一份控制租约；句柄只对创建它的系统实例有效。</summary>
    public readonly struct ControlLeaseHandle : IEquatable<ControlLeaseHandle>
    {
        /// <summary>创建一个控制租约句柄；仅允许 PossessionSystem 调用。</summary>
        /// <param name="systemToken">创建租约的系统实例标识。</param>
        /// <param name="leaseId">系统实例内唯一的租约编号。</param>
        internal ControlLeaseHandle(long systemToken, long leaseId)
        {
            SystemToken = systemToken;
            LeaseId = leaseId;
        }

        /// <summary>获取该句柄是否包含一个有效租约编号。</summary>
        public bool IsValid => SystemToken > 0 && LeaseId > 0;

        /// <summary>获取创建该租约的系统实例标识。</summary>
        internal long SystemToken { get; }

        /// <summary>获取系统实例内唯一的租约编号。</summary>
        internal long LeaseId { get; }

        /// <inheritdoc />
        public bool Equals(ControlLeaseHandle other)
        {
            return SystemToken == other.SystemToken && LeaseId == other.LeaseId;
        }

        /// <inheritdoc />
        public override bool Equals(object obj)
        {
            return obj is ControlLeaseHandle other && Equals(other);
        }

        /// <inheritdoc />
        public override int GetHashCode()
        {
            unchecked
            {
                return (SystemToken.GetHashCode() * 397) ^ LeaseId.GetHashCode();
            }
        }

        /// <summary>比较两个句柄是否标识同一份控制租约。</summary>
        /// <param name="left">左侧控制租约句柄。</param>
        /// <param name="right">右侧控制租约句柄。</param>
        /// <returns>两个句柄来自同一系统实例并指向同一租约时返回 true。</returns>
        public static bool operator ==(ControlLeaseHandle left, ControlLeaseHandle right)
        {
            return left.Equals(right);
        }

        /// <summary>比较两个句柄是否标识不同控制租约。</summary>
        /// <param name="left">左侧控制租约句柄。</param>
        /// <param name="right">右侧控制租约句柄。</param>
        /// <returns>两个句柄不指向同一份控制租约时返回 true。</returns>
        public static bool operator !=(ControlLeaseHandle left, ControlLeaseHandle right)
        {
            return !left.Equals(right);
        }
    }

    /// <summary>管理单局内 Controller、Pawn、分领域独占租约和经过稳定仲裁的 Pawn 控制帧。</summary>
    public sealed class PossessionSystem : XSystem
    {
        /// <summary>为每个 PossessionSystem 实例分配跨实例唯一标识。</summary>
        private static long nextSystemToken;

        /// <summary>保存已经注册的全部运行时控制器。</summary>
        private readonly Dictionary<int, IControllerRuntime> controllers = new Dictionary<int, IControllerRuntime>();

        /// <summary>保存当前系统已注册的 Pawn 及其控制拓扑代数。</summary>
        private readonly Dictionary<int, uint> pawnGenerations = new Dictionary<int, uint>();

        /// <summary>保存仍然有效的全部控制租约。</summary>
        private readonly Dictionary<long, ControlLeaseEntry> leases = new Dictionary<long, ControlLeaseEntry>();

        /// <summary>缓存每个控制器在最近准备帧产生的唯一一次采样结果。</summary>
        private readonly Dictionary<int, ControlFrame> controllerSamples = new Dictionary<int, ControlFrame>();

        /// <summary>缓存最近准备帧中每个 Pawn 经过分领域仲裁后的最终控制帧。</summary>
        private readonly Dictionary<int, ControlFrame> pawnFrames = new Dictionary<int, ControlFrame>();

        /// <summary>为批量移除租约复用受影响 Pawn 集合，避免每次释放产生临时集合。</summary>
        private readonly HashSet<int> affectedPawns = new HashSet<int>();

        /// <summary>为批量移除租约复用租约编号缓冲，避免枚举期间修改字典。</summary>
        private readonly List<long> leaseRemovalBuffer = new List<long>();

        /// <summary>当前系统实例的跨实例唯一标识。</summary>
        private readonly long systemToken = Interlocked.Increment(ref nextSystemToken);

        /// <summary>下一个系统内租约编号。</summary>
        private long nextLeaseId = 1;

        /// <summary>下一个稳定申请序号；相同优先级时更小序号获胜。</summary>
        private long nextAcquisitionSequence = 1;

        /// <summary>记录是否已经成功准备过至少一个控制帧。</summary>
        private bool hasPreparedFrame;

        /// <summary>记录最近一次成功准备的控制帧编号。</summary>
        private ulong lastPreparedFrameId;

        /// <summary>记录系统是否已经释放。</summary>
        private bool disposed;

        /// <summary>获取最近一次成功准备的控制帧编号；尚未准备时返回零。</summary>
        public ulong LastPreparedFrameId => hasPreparedFrame ? lastPreparedFrameId : 0;

        /// <summary>注册一个运行时控制器；同一编号重复注册会立即失败。</summary>
        /// <param name="controller">需要由本系统采样和释放的运行时控制器。</param>
        public void RegisterController(IControllerRuntime controller)
        {
            ThrowIfDisposed();
            if (controller == null) throw new ArgumentNullException(nameof(controller));
            if (controller.ControllerId <= 0) throw new ArgumentOutOfRangeException(nameof(controller), controller.ControllerId, "Controller ID must be positive.");
            if (controllers.ContainsKey(controller.ControllerId)) throw new InvalidOperationException($"Controller '{controller.ControllerId}' is already registered.");
            controllers.Add(controller.ControllerId, controller);
        }

        /// <summary>注销指定控制器、释放其全部租约，并按请求决定是否释放控制器对象。</summary>
        /// <param name="controllerId">需要注销的控制器编号。</param>
        /// <param name="disposeController">是否调用控制器的 Dispose。</param>
        /// <returns>找到并注销控制器时返回 true；控制器不存在时返回 false。</returns>
        public bool UnregisterController(int controllerId, bool disposeController = true)
        {
            ThrowIfDisposed();
            if (!controllers.TryGetValue(controllerId, out IControllerRuntime controller)) return false;
            ReleaseOwnedBy(controllerId);
            controllers.Remove(controllerId);
            controllerSamples.Remove(controllerId);
            if (disposeController) controller.Dispose();
            return true;
        }

        /// <summary>注册一个可接收控制租约的 Pawn；PawnId 应直接复用当前单局 EntityId。</summary>
        /// <param name="pawnId">当前单局内唯一的正 Pawn 编号。</param>
        public void RegisterPawn(int pawnId)
        {
            ThrowIfDisposed();
            if (pawnId <= 0) throw new ArgumentOutOfRangeException(nameof(pawnId), pawnId, "Pawn ID must be positive.");
            if (pawnGenerations.ContainsKey(pawnId)) throw new InvalidOperationException($"Pawn '{pawnId}' is already registered.");
            pawnGenerations.Add(pawnId, 1);
            pawnFrames.Remove(pawnId);
        }

        /// <summary>注销一个 Pawn 并释放所有指向它的租约。</summary>
        /// <param name="pawnId">需要注销的 Pawn 编号。</param>
        /// <returns>找到并注销 Pawn 时返回 true；Pawn 不存在时返回 false。</returns>
        public bool UnregisterPawn(int pawnId)
        {
            ThrowIfDisposed();
            if (!pawnGenerations.ContainsKey(pawnId)) return false;
            RemoveLeases(entry => entry.PawnId == pawnId, false);
            pawnGenerations.Remove(pawnId);
            pawnFrames.Remove(pawnId);
            return true;
        }

        /// <summary>创建一份分领域独占控制租约；变更从下一次 PrepareFrame 开始生效，当前已发布帧在整个 Entity 阶段保持不可变。</summary>
        /// <param name="request">控制器、Pawn、控制领域和优先级组成的租约申请。</param>
        /// <returns>只对当前 PossessionSystem 有效的租约句柄。</returns>
        public ControlLeaseHandle AcquireLease(ControlLeaseRequest request)
        {
            ThrowIfDisposed();
            ValidateRequest(request);
            long leaseId = nextLeaseId++;
            long sequence = nextAcquisitionSequence++;
            leases.Add(leaseId, new ControlLeaseEntry(leaseId, request.ControllerId, request.PawnId, request.Scopes, request.Priority, sequence));
            IncrementPawnGeneration(request.PawnId);
            return new ControlLeaseHandle(systemToken, leaseId);
        }

        /// <summary>释放一份控制租约；空句柄、其他系统的句柄和重复释放都安全返回 false。</summary>
        /// <param name="handle">AcquireLease 返回的租约句柄。</param>
        /// <returns>实际释放租约时返回 true，否则返回 false。</returns>
        public bool ReleaseLease(ControlLeaseHandle handle)
        {
            ThrowIfDisposed();
            if (!handle.IsValid || handle.SystemToken != systemToken) return false;
            if (!leases.TryGetValue(handle.LeaseId, out ControlLeaseEntry entry)) return false;
            leases.Remove(handle.LeaseId);
            IncrementPawnGeneration(entry.PawnId);
            return true;
        }

        /// <summary>释放指定控制器持有的全部租约，并保证每个受影响 Pawn 只递增一次控制代数。</summary>
        /// <param name="controllerId">需要释放租约的控制器编号。</param>
        /// <returns>实际释放的租约数量。</returns>
        public int ReleaseOwnedBy(int controllerId)
        {
            ThrowIfDisposed();
            return RemoveLeases(entry => entry.ControllerId == controllerId, true);
        }

        /// <summary>在指定帧采样每个控制器一次，并按照领域、优先级和稳定申请顺序生成全部 Pawn 控制帧。</summary>
        /// <param name="frameId">必须严格大于上一次成功准备帧的编号；重复传入同一帧会直接跳过。</param>
        /// <param name="deltaTime">当前帧的非负增量时间。</param>
        /// <returns>本次实际完成采样和仲裁时返回 true；同一帧重复调用时返回 false。</returns>
        public bool PrepareFrame(ulong frameId, float deltaTime)
        {
            ThrowIfDisposed();
            if (hasPreparedFrame && frameId == lastPreparedFrameId) return false;
            if (hasPreparedFrame && frameId < lastPreparedFrameId) throw new ArgumentOutOfRangeException(nameof(frameId), frameId, "Control frame IDs must be monotonic.");
            controllerSamples.Clear();
            pawnFrames.Clear();
            ControllerSampleContext context = new ControllerSampleContext(frameId, deltaTime);
            foreach (KeyValuePair<int, IControllerRuntime> pair in controllers) controllerSamples.Add(pair.Key, pair.Value.Sample(context));
            foreach (KeyValuePair<int, uint> pair in pawnGenerations)
            {
                if (HasAnyEffectiveLease(pair.Key)) pawnFrames.Add(pair.Key, BuildPawnFrame(pair.Key, pair.Value, frameId));
            }
            lastPreparedFrameId = frameId;
            hasPreparedFrame = true;
            return true;
        }

        /// <summary>尝试读取指定 Pawn 在最近准备帧经过完整领域仲裁后的控制帧。</summary>
        /// <param name="pawnId">需要查询的 Pawn 编号。</param>
        /// <param name="frame">成功时返回最终控制帧；失败时返回默认值。</param>
        /// <returns>最近准备帧中该 Pawn 至少有一个有效控制领域租约时返回 true；仅注册但没有租约的 Pawn 返回 false。</returns>
        public bool TryGetControlFrame(int pawnId, out ControlFrame frame)
        {
            ThrowIfDisposed();
            return pawnFrames.TryGetValue(pawnId, out frame);
        }

        /// <summary>判断指定 Pawn 在一个或多个领域中是否至少存在一份有效租约，供运行时区分静止控制帧与完全未被接管的对象。</summary>
        /// <param name="pawnId">需要查询的 Pawn 编号。</param>
        /// <param name="scopes">需要检查的一个或多个受支持控制领域。</param>
        /// <returns>至少一个请求领域存在租约时返回 true。</returns>
        public bool HasEffectiveControl(int pawnId, ControlScope scopes)
        {
            ThrowIfDisposed();
            if (scopes == ControlScope.None || (scopes & ~ControlScope.All) != 0) throw new ArgumentOutOfRangeException(nameof(scopes), scopes, "At least one supported control scope is required.");
            foreach (ControlLeaseEntry candidate in leases.Values)
            {
                if (candidate.PawnId == pawnId && (candidate.Scopes & scopes) != 0) return true;
            }
            return false;
        }

        /// <summary>尝试查询指定 Pawn 某个单一控制领域当前实际生效的控制器。</summary>
        /// <param name="pawnId">需要查询的 Pawn 编号。</param>
        /// <param name="scope">需要查询的单一控制领域。</param>
        /// <param name="controllerId">成功时返回获胜控制器编号；失败时返回零。</param>
        /// <returns>该领域存在有效租约时返回 true。</returns>
        public bool TryGetEffectiveController(int pawnId, ControlScope scope, out int controllerId)
        {
            ThrowIfDisposed();
            ValidateSingleScope(scope);
            ControlLeaseEntry winner = FindWinningLease(pawnId, scope);
            controllerId = winner == null ? 0 : winner.ControllerId;
            return winner != null;
        }

        /// <inheritdoc />
        public override void OnBeforeEntityUpdate(float dt)
        {
            if (disposed) return;
            PrepareFrame(unchecked((ulong)Mathf.Max(0, Time.frameCount)), dt);
        }

        /// <inheritdoc />
        public override void Dispose()
        {
            if (disposed) return;
            disposed = true;
            foreach (IControllerRuntime controller in controllers.Values)
            {
                try
                {
                    controller.Dispose();
                }
                catch (Exception exception)
                {
                    Debug.LogException(exception);
                }
            }
            controllers.Clear();
            pawnGenerations.Clear();
            leases.Clear();
            controllerSamples.Clear();
            pawnFrames.Clear();
            affectedPawns.Clear();
            leaseRemovalBuffer.Clear();
        }

        /// <summary>根据四个独立领域的获胜租约构建一个 Pawn 的最终控制帧。</summary>
        private ControlFrame BuildPawnFrame(int pawnId, uint generation, ulong frameId)
        {
            ControlFrame result = ControlFrame.Empty(frameId, generation);
            result = MergeWinningScope(result, pawnId, ControlScope.Locomotion, generation);
            result = MergeWinningScope(result, pawnId, ControlScope.Facing, generation);
            result = MergeWinningScope(result, pawnId, ControlScope.Action, generation);
            result = MergeWinningScope(result, pawnId, ControlScope.Camera, generation);
            return result;
        }

        /// <summary>判断 Pawn 当前是否至少被一份仍然有效的领域租约控制，避免把无租约空帧误认为玩家或剧情接管。</summary>
        private bool HasAnyEffectiveLease(int pawnId)
        {
            return HasEffectiveControl(pawnId, ControlScope.All);
        }

        /// <summary>将指定领域获胜控制器的唯一采样结果合并到 Pawn 控制帧。</summary>
        private ControlFrame MergeWinningScope(ControlFrame destination, int pawnId, ControlScope scope, uint generation)
        {
            ControlLeaseEntry winner = FindWinningLease(pawnId, scope);
            if (winner == null || !controllerSamples.TryGetValue(winner.ControllerId, out ControlFrame sample)) return destination;
            return destination.Merge(sample, scope, generation);
        }

        /// <summary>按照优先级降序、申请序号升序选择指定 Pawn 和领域的稳定获胜租约。</summary>
        private ControlLeaseEntry FindWinningLease(int pawnId, ControlScope scope)
        {
            ControlLeaseEntry winner = null;
            foreach (ControlLeaseEntry candidate in leases.Values)
            {
                if (candidate.PawnId != pawnId || (candidate.Scopes & scope) == 0) continue;
                if (winner == null || candidate.Priority > winner.Priority || candidate.Priority == winner.Priority && candidate.Sequence < winner.Sequence) winner = candidate;
            }
            return winner;
        }

        /// <summary>移除符合条件的租约，并按 Pawn 汇总控制代数更新。</summary>
        private int RemoveLeases(Predicate<ControlLeaseEntry> predicate, bool incrementGenerations)
        {
            leaseRemovalBuffer.Clear();
            affectedPawns.Clear();
            foreach (KeyValuePair<long, ControlLeaseEntry> pair in leases)
            {
                if (!predicate(pair.Value)) continue;
                leaseRemovalBuffer.Add(pair.Key);
                affectedPawns.Add(pair.Value.PawnId);
            }
            for (int index = 0; index < leaseRemovalBuffer.Count; index++) leases.Remove(leaseRemovalBuffer[index]);
            if (incrementGenerations)
            {
                foreach (int pawnId in affectedPawns) IncrementPawnGeneration(pawnId);
            }
            int removedCount = leaseRemovalBuffer.Count;
            leaseRemovalBuffer.Clear();
            affectedPawns.Clear();
            return removedCount;
        }

        /// <summary>递增 Pawn 控制拓扑代数；已经发布的控制帧保留到下一次 PrepareFrame，避免结果依赖 Entity 更新顺序。</summary>
        private void IncrementPawnGeneration(int pawnId)
        {
            if (!pawnGenerations.TryGetValue(pawnId, out uint generation)) return;
            pawnGenerations[pawnId] = generation == uint.MaxValue ? 1u : generation + 1u;
        }

        /// <summary>验证租约申请引用的控制器、Pawn 和领域均有效。</summary>
        private void ValidateRequest(ControlLeaseRequest request)
        {
            if (!controllers.ContainsKey(request.ControllerId)) throw new InvalidOperationException($"Controller '{request.ControllerId}' is not registered.");
            if (!pawnGenerations.ContainsKey(request.PawnId)) throw new InvalidOperationException($"Pawn '{request.PawnId}' is not registered.");
            if (request.Scopes == ControlScope.None || (request.Scopes & ~ControlScope.All) != 0) throw new ArgumentOutOfRangeException(nameof(request), request.Scopes, "A control lease requires at least one supported scope.");
        }

        /// <summary>验证查询参数只包含一个受支持控制领域。</summary>
        private static void ValidateSingleScope(ControlScope scope)
        {
            int rawScope = (int)scope;
            if (scope == ControlScope.None || (scope & ~ControlScope.All) != 0 || (rawScope & (rawScope - 1)) != 0) throw new ArgumentOutOfRangeException(nameof(scope), scope, "Exactly one supported control scope is required.");
        }

        /// <summary>阻止已经释放的系统继续接收注册、租约或帧采样请求。</summary>
        private void ThrowIfDisposed()
        {
            if (disposed) throw new ObjectDisposedException(nameof(PossessionSystem));
        }

        /// <summary>保存一份租约的不可变仲裁数据。</summary>
        private sealed class ControlLeaseEntry
        {
            /// <summary>创建一份不可变控制租约记录。</summary>
            internal ControlLeaseEntry(long leaseId, int controllerId, int pawnId, ControlScope scopes, int priority, long sequence)
            {
                LeaseId = leaseId;
                ControllerId = controllerId;
                PawnId = pawnId;
                Scopes = scopes;
                Priority = priority;
                Sequence = sequence;
            }

            /// <summary>获取系统内唯一租约编号。</summary>
            internal long LeaseId { get; }

            /// <summary>获取持有租约的控制器编号。</summary>
            internal int ControllerId { get; }

            /// <summary>获取租约指向的 Pawn 编号。</summary>
            internal int PawnId { get; }

            /// <summary>获取租约覆盖的控制领域。</summary>
            internal ControlScope Scopes { get; }

            /// <summary>获取租约优先级。</summary>
            internal int Priority { get; }

            /// <summary>获取用于相同优先级稳定决胜的申请序号。</summary>
            internal long Sequence { get; }
        }
    }
}
