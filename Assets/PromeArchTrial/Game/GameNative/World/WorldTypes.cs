using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using PromeArchTrial.Game.Character;

namespace PromeArchTrial.Game.World
{
    /// <summary>
    /// 区分一条精确 Tick 命令是被权威队列接受、可幂等忽略，还是违反了允许的未来时间窗口。
    /// </summary>
    public enum AuthoritativeCommandSubmissionResult
    {
        /// <summary>命令已按其精确 Tick 加入待处理队列。</summary>
        Accepted = 0,

        /// <summary>目标实体已经不在权威世界中，通常表示会话正在关闭。</summary>
        EntityNotFound = 1,

        /// <summary>命令 Tick 已经由权威世界完成模拟，应幂等忽略而不应中断会话。</summary>
        Late = 2,

        /// <summary>同一实体的同一未来 Tick 已在队列中，重传应幂等忽略。</summary>
        Duplicate = 3,

        /// <summary>命令超出 PredictionHistoryTicks 定义的可接受未来窗口，属于协议级错误。</summary>
        TooFarInFuture = 4
    }

    /// <summary>
    /// 标识权威世界输出事件的载荷种类，角色事件保留模拟核心原始语义，命中结算事件补充攻击者、目标、伤害和暴击信息。
    /// </summary>
    public enum WorldEventKind
    {
        /// <summary>事件载荷来自单个角色的确定性模拟结果。</summary>
        Character = 0,

        /// <summary>事件载荷描述权威世界完成的一次空间命中与伤害计算。</summary>
        HitResolved = 1
    }

    /// <summary>
    /// 表示权威世界单个 Tick 内可稳定排序和去重的不可变事件，EntityId、WorldTick 与 Sequence 三元组在一次世界运行中唯一。
    /// </summary>
    public readonly struct WorldEvent : IEquatable<WorldEvent>
    {
        private WorldEvent(int entityId, int worldTick, int sequence, WorldEventKind kind, CharacterEvent characterEvent, int targetEntityId, CharacterActionKind actionKind, int actionId, int value, bool isCritical)
        {
            if (entityId <= 0) throw new ArgumentOutOfRangeException(nameof(entityId), "World event entity id must be positive.");
            if (worldTick < 0) throw new ArgumentOutOfRangeException(nameof(worldTick), "World event tick cannot be negative.");
            if (sequence < 0) throw new ArgumentOutOfRangeException(nameof(sequence), "World event sequence cannot be negative.");
            if (kind == WorldEventKind.HitResolved && targetEntityId <= 0) throw new ArgumentOutOfRangeException(nameof(targetEntityId), "A resolved hit requires a positive target entity id.");
            EntityId = entityId;
            WorldTick = worldTick;
            Sequence = sequence;
            Kind = kind;
            CharacterEvent = characterEvent;
            TargetEntityId = targetEntityId;
            ActionKind = actionKind;
            ActionId = actionId;
            Value = value;
            IsCritical = isCritical;
        }

        /// <summary>获取事件所属角色编号；命中结算事件使用攻击者编号。</summary>
        public int EntityId { get; }

        /// <summary>获取事件源角色编号；命中结算事件为攻击者，角色模拟事件为产生该事件的角色。</summary>
        public int SourceEntityId => EntityId;

        /// <summary>获取事件产生的权威世界 Tick。</summary>
        public int WorldTick { get; }

        /// <summary>获取事件在当前世界 Tick 内按确定性顺序分配的零起始序号。</summary>
        public int Sequence { get; }

        /// <summary>获取事件在当前世界 Tick 内的零起始确定性序号，作为 Sequence 的协议友好别名。</summary>
        public int Ordinal => Sequence;

        /// <summary>获取事件载荷种类。</summary>
        public WorldEventKind Kind { get; }

        /// <summary>获取角色模拟原始事件；仅当 Kind 为 Character 时该字段具有业务语义。</summary>
        public CharacterEvent CharacterEvent { get; }

        /// <summary>获取命中目标编号；角色模拟事件固定为零。</summary>
        public int TargetEntityId { get; }

        /// <summary>获取事件关联的角色动作种类。</summary>
        public CharacterActionKind ActionKind { get; }

        /// <summary>获取事件关联的 Luban 动作行编号，无关联动作时为零。</summary>
        public int ActionId { get; }

        /// <summary>获取角色事件的原始整数值或命中结算事件经过防御计算后的尝试伤害。</summary>
        public int Value { get; }

        /// <summary>获取命中结算的尝试伤害；角色 DamageTaken 或 DamageIgnored 事件返回其原始伤害值，其他事件返回零。</summary>
        public int Damage => Kind == WorldEventKind.HitResolved || CharacterEvent.Type == CharacterEventType.DamageTaken || CharacterEvent.Type == CharacterEventType.DamageIgnored ? Value : 0;

        /// <summary>获取命中结算是否通过确定性随机判定触发暴击；角色模拟事件固定为假。</summary>
        public bool IsCritical { get; }

        /// <summary>创建一个保留原始角色事件载荷的世界事件。</summary>
        internal static WorldEvent FromCharacter(int entityId, int worldTick, int sequence, CharacterEvent characterEvent)
        {
            return new WorldEvent(entityId, worldTick, sequence, WorldEventKind.Character, characterEvent, 0, characterEvent.ActionKind, characterEvent.ActionId, characterEvent.Value, false);
        }

        /// <summary>创建一个描述权威空间查询和伤害公式结果的世界命中事件。</summary>
        internal static WorldEvent FromResolvedHit(int sourceEntityId, int targetEntityId, int worldTick, int sequence, CharacterActionKind actionKind, int actionId, int attemptedDamage, bool isCritical)
        {
            if (attemptedDamage < 0) throw new ArgumentOutOfRangeException(nameof(attemptedDamage), "Attempted hit damage cannot be negative.");
            return new WorldEvent(sourceEntityId, worldTick, sequence, WorldEventKind.HitResolved, default, targetEntityId, actionKind, actionId, attemptedDamage, isCritical);
        }

        /// <summary>判断两个世界事件的全部确定性字段是否一致。</summary>
        public bool Equals(WorldEvent other)
        {
            return EntityId == other.EntityId && WorldTick == other.WorldTick && Sequence == other.Sequence && Kind == other.Kind && CharacterEvent.Tick == other.CharacterEvent.Tick && CharacterEvent.Type == other.CharacterEvent.Type && CharacterEvent.ActionKind == other.CharacterEvent.ActionKind && CharacterEvent.ActionId == other.CharacterEvent.ActionId && CharacterEvent.Value == other.CharacterEvent.Value && TargetEntityId == other.TargetEntityId && ActionKind == other.ActionKind && ActionId == other.ActionId && Value == other.Value && IsCritical == other.IsCritical;
        }

        /// <summary>判断指定对象是否为字段完全一致的世界事件。</summary>
        public override bool Equals(object obj)
        {
            return obj is WorldEvent other && Equals(other);
        }

        /// <summary>获取覆盖事件唯一键和载荷字段的哈希码。</summary>
        public override int GetHashCode()
        {
            unchecked
            {
                int hash = EntityId;
                hash = hash * 397 ^ WorldTick;
                hash = hash * 397 ^ Sequence;
                hash = hash * 397 ^ (int)Kind;
                hash = hash * 397 ^ CharacterEvent.Tick;
                hash = hash * 397 ^ (int)CharacterEvent.Type;
                hash = hash * 397 ^ (int)CharacterEvent.ActionKind;
                hash = hash * 397 ^ CharacterEvent.ActionId;
                hash = hash * 397 ^ CharacterEvent.Value;
                hash = hash * 397 ^ TargetEntityId;
                hash = hash * 397 ^ (int)ActionKind;
                hash = hash * 397 ^ ActionId;
                hash = hash * 397 ^ Value;
                hash = hash * 397 ^ (IsCritical ? 1 : 0);
                return hash;
            }
        }
    }

    /// <summary>
    /// 保存一个权威角色在世界快照中的身份、配置校验值、完整模拟状态和最近模拟命令 Tick。
    /// </summary>
    public readonly struct WorldEntitySnapshot
    {
        /// <summary>创建一个不可变的权威角色快照条目。</summary>
        public WorldEntitySnapshot(int entityId, int playerId, ulong configHash, CharacterState state, int lastProcessedCommandTick)
        {
            if (entityId <= 0) throw new ArgumentOutOfRangeException(nameof(entityId), "Snapshot entity id must be positive.");
            if (playerId <= 0) throw new ArgumentOutOfRangeException(nameof(playerId), "Snapshot player id must be positive.");
            if (lastProcessedCommandTick != state.Tick) throw new ArgumentOutOfRangeException(nameof(lastProcessedCommandTick), "Last processed command tick must exactly equal the complete character state tick for prediction reconciliation.");
            EntityId = entityId;
            PlayerId = playerId;
            ConfigHash = configHash;
            State = state;
            LastProcessedCommandTick = lastProcessedCommandTick;
        }

        /// <summary>获取世界内稳定且唯一的角色实体编号。</summary>
        public int EntityId { get; }

        /// <summary>获取拥有该角色的稳定玩家编号。</summary>
        public int PlayerId { get; }

        /// <summary>获取该角色运行时配置的稳定内容哈希。</summary>
        public ulong ConfigHash { get; }

        /// <summary>获取角色在快照 Tick 提交后的完整可回滚状态。</summary>
        public CharacterState State { get; }

        /// <summary>获取服务器最近模拟的命令 Tick，包括精确客户端命令、连续量合成命令和完全 neutral 命令，并始终等于 State.Tick。</summary>
        public int LastProcessedCommandTick { get; }
    }

    /// <summary>
    /// 保存一个世界 Tick 提交后的不可变全量快照，实体条目始终按照 EntityId 升序排列。
    /// </summary>
    public sealed class AuthoritativeWorldSnapshot
    {
        private readonly ReadOnlyCollection<WorldEntitySnapshot> entities;
        private readonly ReadOnlyDictionary<int, WorldEntitySnapshot> entitiesById;

        /// <summary>复制稳定排序的实体条目并计算可用于确定性校验的世界状态哈希。</summary>
        internal AuthoritativeWorldSnapshot(int worldTick, IList<WorldEntitySnapshot> entities)
        {
            if (worldTick < -1) throw new ArgumentOutOfRangeException(nameof(worldTick), "World snapshot tick cannot be lower than -1.");
            if (entities == null) throw new ArgumentNullException(nameof(entities));
            WorldTick = worldTick;
            WorldEntitySnapshot[] entityCopy = new WorldEntitySnapshot[entities.Count];
            Dictionary<int, WorldEntitySnapshot> entityMap = new Dictionary<int, WorldEntitySnapshot>(entities.Count);
            int previousEntityId = 0;
            for (int index = 0; index < entities.Count; index++)
            {
                WorldEntitySnapshot entity = entities[index];
                if (entity.EntityId <= previousEntityId) throw new ArgumentException("World snapshot entities must be strictly sorted by entity id.", nameof(entities));
                entityCopy[index] = entity;
                entityMap.Add(entity.EntityId, entity);
                previousEntityId = entity.EntityId;
            }
            this.entities = Array.AsReadOnly(entityCopy);
            entitiesById = new ReadOnlyDictionary<int, WorldEntitySnapshot>(entityMap);
            StableHash = ComputeStableHash(worldTick, entityCopy);
        }

        /// <summary>获取快照对应的最近完成世界 Tick，尚未开始模拟时为负一。</summary>
        public int WorldTick { get; }

        /// <summary>获取按照 EntityId 升序冻结的全部权威角色条目。</summary>
        public IReadOnlyList<WorldEntitySnapshot> Entities => entities;

        /// <summary>获取覆盖 Tick、身份、配置哈希、命令确认和完整角色状态的稳定六十四位哈希。</summary>
        public ulong StableHash { get; }

        /// <summary>尝试按照实体编号读取一个不可变快照条目。</summary>
        public bool TryGetEntity(int entityId, out WorldEntitySnapshot entity)
        {
            return entitiesById.TryGetValue(entityId, out entity);
        }

        /// <summary>按照固定字段顺序计算跨客户端和服务器一致的 FNV-1a 世界哈希。</summary>
        private static ulong ComputeStableHash(int worldTick, IList<WorldEntitySnapshot> entities)
        {
            ulong hash = 14695981039346656037UL;
            AppendHash(ref hash, unchecked((ulong)(long)worldTick));
            AppendHash(ref hash, unchecked((ulong)entities.Count));
            for (int index = 0; index < entities.Count; index++)
            {
                WorldEntitySnapshot entity = entities[index];
                AppendHash(ref hash, unchecked((ulong)entity.EntityId));
                AppendHash(ref hash, unchecked((ulong)entity.PlayerId));
                AppendHash(ref hash, entity.ConfigHash);
                AppendHash(ref hash, unchecked((ulong)(long)entity.LastProcessedCommandTick));
                AppendHash(ref hash, entity.State.StableHash);
            }
            return hash;
        }

        /// <summary>把一个六十四位字段逐字节加入稳定 FNV-1a 哈希。</summary>
        private static void AppendHash(ref ulong hash, ulong value)
        {
            for (int byteIndex = 0; byteIndex < 8; byteIndex++)
            {
                hash ^= (byte)(value >> (byteIndex * 8));
                hash *= 1099511628211UL;
            }
        }
    }

    /// <summary>
    /// 聚合一次权威世界 Tick 的最终快照和按 Sequence 冻结的全部事件。
    /// </summary>
    public sealed class WorldTickResult
    {
        private readonly ReadOnlyCollection<WorldEvent> events;

        /// <summary>创建一个完成提交的世界 Tick 结果并复制事件集合。</summary>
        internal WorldTickResult(AuthoritativeWorldSnapshot snapshot, IList<WorldEvent> events)
        {
            Snapshot = snapshot ?? throw new ArgumentNullException(nameof(snapshot));
            if (events == null) throw new ArgumentNullException(nameof(events));
            WorldEvent[] eventCopy = new WorldEvent[events.Count];
            events.CopyTo(eventCopy, 0);
            this.events = Array.AsReadOnly(eventCopy);
        }

        /// <summary>获取本次 Tick 提交后的不可变权威世界快照。</summary>
        public AuthoritativeWorldSnapshot Snapshot { get; }

        /// <summary>获取本次 Tick 按确定性 Sequence 排列的只读世界事件。</summary>
        public IReadOnlyList<WorldEvent> Events => events;
    }
}
