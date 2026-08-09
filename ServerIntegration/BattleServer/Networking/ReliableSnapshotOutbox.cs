using System;
using System.Collections.Generic;
using PromeArchTrial.Core.Networking;

namespace PromeArchTrial.BattleServer.Networking
{
    /// <summary>
    /// 为单个会话保存可覆盖的最新权威状态与不可静默丢弃的服务器事件，事件只在对应 TCP 帧成功写入后才会被确认移除。
    /// </summary>
    internal sealed class ReliableSnapshotOutbox
    {
        private const int DefaultMaximumPendingEventCount = 512;
        private const int DefaultMaximumEventsPerPayload = 32;
        private readonly object syncRoot = new object();
        private readonly SortedDictionary<BattleEventKey, BattleEventMessage> pendingEvents = new SortedDictionary<BattleEventKey, BattleEventMessage>();
        private readonly int maximumPendingEventCount;
        private readonly int maximumEventsPerPayload;
        private PendingSnapshot latestSnapshot;
        private long nextSnapshotVersion;

        /// <summary>使用与协议帧上限相容的默认事件容量和单帧批量创建会话发件箱。</summary>
        public ReliableSnapshotOutbox() : this(DefaultMaximumPendingEventCount, DefaultMaximumEventsPerPayload)
        {
        }

        /// <summary>使用显式有界容量创建会话发件箱，仅供自动验收和容量策略测试使用。</summary>
        internal ReliableSnapshotOutbox(int maximumPendingEventCount, int maximumEventsPerPayload)
        {
            if (maximumPendingEventCount <= 0) throw new ArgumentOutOfRangeException(nameof(maximumPendingEventCount), "Maximum pending battle-event count must be positive.");
            if (maximumEventsPerPayload <= 0 || maximumEventsPerPayload > maximumPendingEventCount) throw new ArgumentOutOfRangeException(nameof(maximumEventsPerPayload), "Maximum events per snapshot payload must be positive and cannot exceed the pending-event capacity.");
            this.maximumPendingEventCount = maximumPendingEventCount;
            this.maximumEventsPerPayload = maximumEventsPerPayload;
        }

        /// <summary>获取当前仍未成功写入 TCP 的去重事件数量，仅用于诊断和自动验收。</summary>
        public int PendingEventCount
        {
            get
            {
                lock (syncRoot) return pendingEvents.Count;
            }
        }

        /// <summary>获取发件箱是否存在待发最新状态或仍未确认的事件。</summary>
        public bool HasPending
        {
            get
            {
                lock (syncRoot) return latestSnapshot != null;
            }
        }

        /// <summary>用更新的完整状态覆盖旧状态，同时按 WorldTick 与 Ordinal 幂等合并本 Tick 的不可丢失事件。</summary>
        public void Publish(ServerSnapshotMessage snapshot)
        {
            if (snapshot.State.Tick != snapshot.AcknowledgedClientTick) throw new ArgumentException("Snapshot state tick must equal its reconciliation tick.", nameof(snapshot));
            Dictionary<BattleEventKey, BattleEventMessage> additions = new Dictionary<BattleEventKey, BattleEventMessage>();
            lock (syncRoot)
            {
                for (int index = 0; index < snapshot.Events.Count; index++)
                {
                    BattleEventMessage battleEvent = snapshot.Events[index];
                    if (battleEvent.WorldTick != snapshot.ServerTick) throw new ArgumentException($"Battle event tick {battleEvent.WorldTick} must equal enclosing snapshot tick {snapshot.ServerTick}.", nameof(snapshot));
                    BattleEventKey key = new BattleEventKey(battleEvent.WorldTick, battleEvent.Ordinal);
                    if (pendingEvents.TryGetValue(key, out BattleEventMessage pendingEvent))
                    {
                        RequireSameEvent(pendingEvent, battleEvent, key);
                        continue;
                    }
                    if (additions.TryGetValue(key, out BattleEventMessage addedEvent))
                    {
                        RequireSameEvent(addedEvent, battleEvent, key);
                        continue;
                    }
                    additions.Add(key, battleEvent);
                }
                if ((long)pendingEvents.Count + additions.Count > maximumPendingEventCount) throw new InvalidOperationException($"Reliable snapshot outbox exceeded its bounded capacity of {maximumPendingEventCount} unsent battle events; the slow session must be closed instead of silently dropping gameplay feedback.");
                foreach (KeyValuePair<BattleEventKey, BattleEventMessage> addition in additions) pendingEvents.Add(addition.Key, addition.Value);
                latestSnapshot = new PendingSnapshot(checked(++nextSnapshotVersion), snapshot.ServerTick, snapshot.AcknowledgedClientTick, snapshot.State);
            }
        }

        /// <summary>为唯一发送循环保留一帧已编码负载；此操作不会移除任何事件，写入失败后仍可保留完整待发集合。</summary>
        public bool TryReserve(out SnapshotOutboxReservation reservation)
        {
            lock (syncRoot)
            {
                if (latestSnapshot == null)
                {
                    reservation = null;
                    return false;
                }
                int eventCount = Math.Min(maximumEventsPerPayload, pendingEvents.Count);
                BattleEventKey[] eventKeys = new BattleEventKey[eventCount];
                BattleEventMessage[] events = new BattleEventMessage[eventCount];
                int eventIndex = 0;
                foreach (KeyValuePair<BattleEventKey, BattleEventMessage> pendingEvent in pendingEvents)
                {
                    if (eventIndex >= eventCount) break;
                    eventKeys[eventIndex] = pendingEvent.Key;
                    events[eventIndex] = pendingEvent.Value;
                    eventIndex++;
                }
                ServerSnapshotMessage message = new ServerSnapshotMessage(latestSnapshot.ServerTick, latestSnapshot.AcknowledgedClientTick, latestSnapshot.State, events);
                byte[] payload = BattleProtocolCodec.Encode(message);
                reservation = new SnapshotOutboxReservation(latestSnapshot.Version, eventKeys, payload);
                return true;
            }
        }

        /// <summary>在对应负载已成功写入网络后确认保留，仅移除该帧实际携带的事件并保留并发到达的更新状态。</summary>
        public void Commit(SnapshotOutboxReservation reservation)
        {
            if (reservation == null) throw new ArgumentNullException(nameof(reservation));
            lock (syncRoot)
            {
                for (int index = 0; index < reservation.EventKeys.Length; index++) pendingEvents.Remove(reservation.EventKeys[index]);
                if (latestSnapshot != null && latestSnapshot.Version == reservation.SnapshotVersion && pendingEvents.Count == 0) latestSnapshot = null;
            }
        }

        /// <summary>确保重复唯一键不会掩盖不同的事件载荷，否则立即中断会话以暴露服务器确定性错误。</summary>
        private static void RequireSameEvent(BattleEventMessage expected, BattleEventMessage actual, BattleEventKey key)
        {
            bool same = expected.Kind == actual.Kind && expected.SourceEntityId == actual.SourceEntityId && expected.TargetEntityId == actual.TargetEntityId && expected.WorldTick == actual.WorldTick && expected.Ordinal == actual.Ordinal && expected.CharacterEventType == actual.CharacterEventType && expected.ActionKind == actual.ActionKind && expected.ActionId == actual.ActionId && expected.Value == actual.Value && expected.IsCritical == actual.IsCritical;
            if (!same) throw new InvalidOperationException($"Battle event key ({key.WorldTick}, {key.Ordinal}) was published with conflicting payloads.");
        }

        /// <summary>保存一份可被新版本覆盖的完整权威状态，事件在独立有界字典中持久化。</summary>
        private sealed class PendingSnapshot
        {
            /// <summary>创建可用于并发确认比对的状态版本。</summary>
            public PendingSnapshot(long version, int serverTick, int acknowledgedClientTick, CharacterNetworkState state)
            {
                Version = version;
                ServerTick = serverTick;
                AcknowledgedClientTick = acknowledgedClientTick;
                State = state;
            }

            public long Version { get; }
            public int ServerTick { get; }
            public int AcknowledgedClientTick { get; }
            public CharacterNetworkState State { get; }
        }

        /// <summary>用 WorldTick 和 Tick 内稳定序号组成跨快照的事件唯一键与发送顺序。</summary>
        internal readonly struct BattleEventKey : IComparable<BattleEventKey>
        {
            /// <summary>创建一个可排序的权威事件唯一键。</summary>
            public BattleEventKey(int worldTick, int ordinal)
            {
                WorldTick = worldTick;
                Ordinal = ordinal;
            }

            public int WorldTick { get; }
            public int Ordinal { get; }

            /// <summary>首先按世界 Tick、然后按 Tick 内 Ordinal 升序排列待发事件。</summary>
            public int CompareTo(BattleEventKey other)
            {
                int tickComparison = WorldTick.CompareTo(other.WorldTick);
                return tickComparison != 0 ? tickComparison : Ordinal.CompareTo(other.Ordinal);
            }
        }

        /// <summary>
        /// 保存一帧已编码负载与它实际携带的事件键，发送循环只能在写入成功后将其交回发件箱确认。
        /// </summary>
        internal sealed class SnapshotOutboxReservation
        {
            private readonly BattleEventKey[] eventKeys;

            /// <summary>创建一份不会立即改变发件箱状态的发送保留。</summary>
            internal SnapshotOutboxReservation(long snapshotVersion, BattleEventKey[] eventKeys, byte[] payload)
            {
                SnapshotVersion = snapshotVersion;
                this.eventKeys = eventKeys ?? throw new ArgumentNullException(nameof(eventKeys));
                Payload = payload ?? throw new ArgumentNullException(nameof(payload));
            }

            /// <summary>获取保留时的状态版本，用于避免确认时清除并发到达的新快照。</summary>
            public long SnapshotVersion { get; }

            /// <summary>获取已经通过协议尺寸校验的 Protobuf Envelope 负载。</summary>
            public byte[] Payload { get; }

            /// <summary>获取本帧携带的事件键，仅允许外部发件箱在成功确认时读取。</summary>
            internal BattleEventKey[] EventKeys => eventKeys;
        }
    }
}
