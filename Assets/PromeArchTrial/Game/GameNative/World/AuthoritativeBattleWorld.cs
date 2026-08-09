using System;
using System.Collections.Generic;
using System.Numerics;
using PromeArchTrial.Game.Character;

namespace PromeArchTrial.Game.World
{
    /// <summary>
    /// 以单一写入口管理多个角色的服务器权威世界，按照 EntityId 稳定顺序执行输入选择、角色预演、空间命中、同 Tick 伤害提交和事件编号。
    /// </summary>
    public sealed class AuthoritativeBattleWorld
    {
        private readonly object syncRoot = new object();
        private readonly SortedDictionary<int, EntityRecord> entities = new SortedDictionary<int, EntityRecord>();
        private readonly Dictionary<int, int> entityIdByPlayerId = new Dictionary<int, int>();
        private readonly CharacterRuntimeConfig defaultConfig;
        private readonly int tickRate;
        private int worldTick = -1;
        private WorldTickResult lastTickResult;

        /// <summary>使用共享默认角色配置创建尚未开始模拟的权威世界。</summary>
        public AuthoritativeBattleWorld(CharacterRuntimeConfig defaultConfig)
        {
            this.defaultConfig = defaultConfig ?? throw new ArgumentNullException(nameof(defaultConfig));
            tickRate = defaultConfig.TickRate;
            lastTickResult = new WorldTickResult(CreateSnapshotUnsafe(null), Array.Empty<WorldEvent>());
        }

        /// <summary>使用固定 Tick 频率创建允许每个实体显式提供角色配置的权威世界。</summary>
        public AuthoritativeBattleWorld(int tickRate)
        {
            if (tickRate != 30) throw new ArgumentOutOfRangeException(nameof(tickRate), "Authoritative character world requires an exact 30 Hz tick rate.");
            this.tickRate = tickRate;
            lastTickResult = new WorldTickResult(CreateSnapshotUnsafe(null), Array.Empty<WorldEvent>());
        }

        /// <summary>获取客户端与服务器共同使用的固定世界模拟频率。</summary>
        public int TickRate => tickRate;

        /// <summary>获取最近完成的权威世界 Tick，尚未开始模拟时为负一。</summary>
        public int WorldTick
        {
            get
            {
                lock (syncRoot) return worldTick;
            }
        }

        /// <summary>获取当前注册到权威世界的角色实体数量。</summary>
        public int EntityCount
        {
            get
            {
                lock (syncRoot) return entities.Count;
            }
        }

        /// <summary>获取最近一次 Tick 的不可变结果，世界尚未运行时返回 Tick 为负一的空事件结果。</summary>
        public WorldTickResult LastTickResult
        {
            get
            {
                lock (syncRoot) return lastTickResult;
            }
        }

        /// <summary>使用构造世界时提供的默认配置，安全添加一个状态 Tick 与当前世界对齐的全新玩家角色。</summary>
        public bool TryAddEntity(int entityId, int playerId, FixedVector3 initialPosition)
        {
            lock (syncRoot)
            {
                if (defaultConfig == null) return false;
                CharacterState initialState = CreateInitialStateAtTick(defaultConfig, initialPosition, worldTick);
                return TryAddEntityUnsafe(entityId, playerId, defaultConfig, initialState);
            }
        }

        /// <summary>使用显式配置，安全添加一个状态 Tick 与当前世界对齐的全新玩家角色。</summary>
        public bool TryAddEntity(int entityId, int playerId, CharacterRuntimeConfig config, FixedVector3 initialPosition)
        {
            if (config == null) return false;
            lock (syncRoot)
            {
                CharacterState initialState;
                try
                {
                    initialState = CreateInitialStateAtTick(config, initialPosition, worldTick);
                }
                catch (ArgumentOutOfRangeException)
                {
                    return false;
                }
                return TryAddEntityUnsafe(entityId, playerId, config, initialState);
            }
        }

        /// <summary>使用显式配置和完整状态安全添加一个玩家角色，状态 Tick 必须与当前世界 Tick 完全一致。</summary>
        public bool TryAddEntity(int entityId, int playerId, CharacterRuntimeConfig config, CharacterState initialState)
        {
            lock (syncRoot) return TryAddEntityUnsafe(entityId, playerId, config, initialState);
        }

        /// <summary>使用构造世界时提供的默认配置添加一个状态 Tick 与当前世界对齐的全新玩家角色。</summary>
        public void AddEntity(int entityId, int playerId, FixedVector3 initialPosition)
        {
            lock (syncRoot)
            {
                if (defaultConfig == null) throw new InvalidOperationException("This world was created without a default character config.");
                CharacterState initialState = CreateInitialStateAtTick(defaultConfig, initialPosition, worldTick);
                AddEntityUnsafe(entityId, playerId, defaultConfig, initialState);
            }
        }

        /// <summary>使用显式配置添加一个状态 Tick 与当前世界对齐的全新玩家角色。</summary>
        public void AddEntity(int entityId, int playerId, CharacterRuntimeConfig config, FixedVector3 initialPosition)
        {
            if (config == null) throw new ArgumentNullException(nameof(config));
            lock (syncRoot)
            {
                CharacterState initialState = CreateInitialStateAtTick(config, initialPosition, worldTick);
                AddEntityUnsafe(entityId, playerId, config, initialState);
            }
        }

        /// <summary>使用显式配置和完整状态添加一个玩家角色，验证失败时抛出明确异常。</summary>
        public void AddEntity(int entityId, int playerId, CharacterRuntimeConfig config, CharacterState initialState)
        {
            lock (syncRoot) AddEntityUnsafe(entityId, playerId, config, initialState);
        }

        /// <summary>安全移除指定角色及其全部尚未消费命令，不存在的实体返回假且不改变世界。</summary>
        public bool RemoveEntity(int entityId)
        {
            lock (syncRoot)
            {
                if (!entities.TryGetValue(entityId, out EntityRecord entity)) return false;
                entities.Remove(entityId);
                entityIdByPlayerId.Remove(entity.PlayerId);
                return true;
            }
        }

        /// <summary>判断指定角色实体当前是否存在于权威世界。</summary>
        public bool ContainsEntity(int entityId)
        {
            lock (syncRoot) return entities.ContainsKey(entityId);
        }

        /// <summary>尝试通过玩家编号读取其唯一角色实体编号。</summary>
        public bool TryGetEntityIdByPlayerId(int playerId, out int entityId)
        {
            lock (syncRoot) return entityIdByPlayerId.TryGetValue(playerId, out entityId);
        }

        /// <summary>尝试提交一条带精确模拟 Tick 的客户端命令，仅在命令进入队列时返回真。</summary>
        public bool TrySubmitCommand(int entityId, CharacterCommand command)
        {
            return SubmitCommand(entityId, command) == AuthoritativeCommandSubmissionResult.Accepted;
        }

        /// <summary>提交一条带精确模拟 Tick 的客户端命令，显式区分幂等迟到、幂等重传和不可接受的过远未来命令。</summary>
        public AuthoritativeCommandSubmissionResult SubmitCommand(int entityId, CharacterCommand command)
        {
            lock (syncRoot)
            {
                if (!entities.TryGetValue(entityId, out EntityRecord entity)) return AuthoritativeCommandSubmissionResult.EntityNotFound;
                if (command.Tick <= worldTick) return AuthoritativeCommandSubmissionResult.Late;
                long maximumAcceptedTick = (long)worldTick + entity.Config.PredictionHistoryTicks;
                if (command.Tick > maximumAcceptedTick) return AuthoritativeCommandSubmissionResult.TooFarInFuture;
                if (entity.PendingCommands.ContainsKey(command.Tick)) return AuthoritativeCommandSubmissionResult.Duplicate;
                entity.PendingCommands.Add(command.Tick, command);
                return AuthoritativeCommandSubmissionResult.Accepted;
            }
        }

        /// <summary>读取指定实体当前已提交的完整权威角色状态，不存在时抛出键异常。</summary>
        public CharacterState GetState(int entityId)
        {
            lock (syncRoot)
            {
                if (!entities.TryGetValue(entityId, out EntityRecord entity)) throw new KeyNotFoundException($"Authoritative entity {entityId} does not exist.");
                return entity.State;
            }
        }

        /// <summary>尝试读取指定实体当前已提交的完整权威角色状态。</summary>
        public bool TryGetState(int entityId, out CharacterState state)
        {
            lock (syncRoot)
            {
                if (entities.TryGetValue(entityId, out EntityRecord entity))
                {
                    state = entity.State;
                    return true;
                }
                state = default;
                return false;
            }
        }

        /// <summary>读取指定实体最近模拟的命令 Tick，包括精确、连续量合成和 neutral 命令，并始终等于完整状态 Tick。</summary>
        public int GetLastProcessedCommandTick(int entityId)
        {
            lock (syncRoot)
            {
                if (!entities.TryGetValue(entityId, out EntityRecord entity)) throw new KeyNotFoundException($"Authoritative entity {entityId} does not exist.");
                return entity.LastProcessedCommandTick;
            }
        }

        /// <summary>尝试读取指定实体最近模拟的命令 Tick。</summary>
        public bool TryGetLastProcessedCommandTick(int entityId, out int lastProcessedCommandTick)
        {
            lock (syncRoot)
            {
                if (entities.TryGetValue(entityId, out EntityRecord entity))
                {
                    lastProcessedCommandTick = entity.LastProcessedCommandTick;
                    return true;
                }
                lastProcessedCommandTick = -1;
                return false;
            }
        }

        /// <summary>读取指定实体最后实际收到并消费的客户端命令 Tick，仅用于 InputTimeoutTicks 计算和网络诊断，不得作为预测状态确认 Tick。</summary>
        public int GetLastReceivedCommandTick(int entityId)
        {
            lock (syncRoot)
            {
                if (!entities.TryGetValue(entityId, out EntityRecord entity)) throw new KeyNotFoundException($"Authoritative entity {entityId} does not exist.");
                return entity.LastReceivedCommandTick;
            }
        }

        /// <summary>复制最近一次 Tick 中与指定玩家有关的全部事件，包括该角色产生的事件以及其他角色命中该角色的结算事件。</summary>
        public IReadOnlyList<WorldEvent> GetLatestEventsForPlayer(int playerId)
        {
            lock (syncRoot)
            {
                if (!entityIdByPlayerId.TryGetValue(playerId, out int entityId)) throw new KeyNotFoundException($"Authoritative player {playerId} does not own an entity.");
                List<WorldEvent> playerEvents = new List<WorldEvent>();
                IReadOnlyList<WorldEvent> latestEvents = lastTickResult.Events;
                for (int index = 0; index < latestEvents.Count; index++)
                {
                    WorldEvent worldEvent = latestEvents[index];
                    if (worldEvent.SourceEntityId == entityId || worldEvent.TargetEntityId == entityId) playerEvents.Add(worldEvent);
                }
                return Array.AsReadOnly(playerEvents.ToArray());
            }
        }

        /// <summary>复制当前世界全部实体的完整状态并返回按照 EntityId 稳定排序的不可变快照。</summary>
        public AuthoritativeWorldSnapshot CaptureSnapshot()
        {
            lock (syncRoot) return CreateSnapshotUnsafe(null);
        }

        /// <summary>执行并原子提交下一个权威 Tick；任意预演或结算异常都会保持世界 Tick、状态、命令确认和队列不变。</summary>
        public WorldTickResult Tick()
        {
            lock (syncRoot)
            {
                int nextWorldTick = checked(worldTick + 1);
                List<TickSelection> selections = SelectCommandsUnsafe(nextWorldTick);
                Dictionary<int, CharacterTickResult> provisionalResults = SimulateUnsafe(selections, null, null);
                HitResolutionBatch hitBatch = ResolveHitsUnsafe(nextWorldTick, provisionalResults);
                Dictionary<int, CharacterTickResult> finalResults = SimulateUnsafe(selections, hitBatch.IncomingDamageByEntityId, hitBatch.ConfirmedHitCountByEntityId);
                List<WorldEvent> worldEvents = CreateWorldEventsUnsafe(nextWorldTick, finalResults, hitBatch.Hits);
                AuthoritativeWorldSnapshot snapshot = CreateSnapshotUnsafe(new ProspectiveCommit(nextWorldTick, finalResults));
                WorldTickResult tickResult = new WorldTickResult(snapshot, worldEvents);
                CommitUnsafe(nextWorldTick, selections, finalResults);
                lastTickResult = tickResult;
                return tickResult;
            }
        }

        /// <summary>在锁内校验并添加实体，失败时返回假且不改变身份索引。</summary>
        private bool TryAddEntityUnsafe(int entityId, int playerId, CharacterRuntimeConfig config, CharacterState initialState)
        {
            if (!IsValidEntityForAdd(entityId, playerId, config, initialState)) return false;
            EntityRecord entity = new EntityRecord(entityId, playerId, config, initialState);
            entities.Add(entityId, entity);
            entityIdByPlayerId.Add(playerId, entityId);
            return true;
        }

        /// <summary>在锁内添加实体并把所有校验失败转化为带上下文的异常。</summary>
        private void AddEntityUnsafe(int entityId, int playerId, CharacterRuntimeConfig config, CharacterState initialState)
        {
            if (entityId <= 0) throw new ArgumentOutOfRangeException(nameof(entityId), "Authoritative entity id must be positive.");
            if (playerId <= 0) throw new ArgumentOutOfRangeException(nameof(playerId), "Authoritative player id must be positive.");
            if (config == null) throw new ArgumentNullException(nameof(config));
            if (config.TickRate != tickRate) throw new ArgumentException("Entity character config tick rate must match the authoritative world tick rate.", nameof(config));
            if (initialState.Tick != worldTick) throw new ArgumentException($"Entity initial state tick {initialState.Tick} must equal current world tick {worldTick}.", nameof(initialState));
            ValidateInitialState(config, initialState);
            if (entities.ContainsKey(entityId)) throw new InvalidOperationException($"Authoritative entity id {entityId} is already registered.");
            if (entityIdByPlayerId.ContainsKey(playerId)) throw new InvalidOperationException($"Authoritative player id {playerId} already owns an entity.");
            EntityRecord entity = new EntityRecord(entityId, playerId, config, initialState);
            entities.Add(entityId, entity);
            entityIdByPlayerId.Add(playerId, entityId);
        }

        /// <summary>判断实体身份、配置、世界 Tick 和完整角色状态是否满足安全添加约束。</summary>
        private bool IsValidEntityForAdd(int entityId, int playerId, CharacterRuntimeConfig config, CharacterState initialState)
        {
            if (entityId <= 0 || playerId <= 0 || config == null || config.TickRate != tickRate || initialState.Tick != worldTick) return false;
            if (entities.ContainsKey(entityId) || entityIdByPlayerId.ContainsKey(playerId)) return false;
            try
            {
                ValidateInitialState(config, initialState);
                return true;
            }
            catch (ArgumentException)
            {
                return false;
            }
            catch (InvalidOperationException)
            {
                return false;
            }
        }

        /// <summary>校验外部恢复状态不会突破配置上限、动作区间或固定地面约束。</summary>
        private static void ValidateInitialState(CharacterRuntimeConfig config, CharacterState state)
        {
            if (state.Hp > config.Stats.MaxHp) throw new ArgumentException("Initial state HP exceeds its character config maximum.", nameof(state));
            if (state.CoreEnergy > config.Stats.MaxCoreEnergy) throw new ArgumentException("Initial state core energy exceeds its character config maximum.", nameof(state));
            if (state.UltimateEnergy > config.Stats.MaxUltimateEnergy) throw new ArgumentException("Initial state ultimate energy exceeds its character config maximum.", nameof(state));
            if (state.IsGrounded && state.Position.Y != 0L) throw new ArgumentException("A grounded initial state must be on the simulation ground plane.", nameof(state));
            if (state.ActionKind == CharacterActionKind.None && state.ActionElapsedTicks != 0) throw new ArgumentException("An initial state without an action cannot have elapsed action ticks.", nameof(state));
            if (state.ActionKind != CharacterActionKind.None && (!config.TryGetAction(state.ActionKind, out CharacterActionRuntimeConfig action) || state.ActionElapsedTicks >= action.TotalTicks)) throw new ArgumentException("Initial state action is missing from config or exceeds its configured duration.", nameof(state));
        }

        /// <summary>创建位于指定位置且 Tick 与当前权威世界最近完成 Tick 对齐的全新角色状态，供运行中动态玩家接入使用。</summary>
        private static CharacterState CreateInitialStateAtTick(CharacterRuntimeConfig config, FixedVector3 position, int stateTick)
        {
            if (config == null) throw new ArgumentNullException(nameof(config));
            if (stateTick < -1) throw new ArgumentOutOfRangeException(nameof(stateTick), "Spawn state tick cannot be lower than -1.");
            if (position.Y < 0L) throw new ArgumentOutOfRangeException(nameof(position), "Initial character position cannot be below the simulation ground.");
            bool grounded = position.Y == 0L;
            CharacterLocomotionState locomotion = grounded ? CharacterLocomotionState.Idle : CharacterLocomotionState.Fall;
            return new CharacterState(stateTick, position, 0L, CharacterFixedPoint.DirectionScale, locomotion, CharacterActionKind.None, 0, 0L, CharacterFixedPoint.DirectionScale, 0L, 0L, 0L, 0L, 0L, grounded, false, config.Stats.MaxHp, 0, 0, 0, false, 0, false, 0, 0, 0, 0, 0, 0, 0);
        }

        /// <summary>为每个实体选择精确 Tick 命令或按 InputTimeoutTicks 合成只保留连续量的命令。</summary>
        private List<TickSelection> SelectCommandsUnsafe(int nextWorldTick)
        {
            List<TickSelection> selections = new List<TickSelection>(entities.Count);
            foreach (KeyValuePair<int, EntityRecord> pair in entities)
            {
                EntityRecord entity = pair.Value;
                if (entity.PendingCommands.TryGetValue(nextWorldTick, out CharacterCommand exactCommand))
                {
                    selections.Add(new TickSelection(entity, exactCommand, true));
                    continue;
                }
                CharacterCommand synthesizedCommand = CreateMissingCommand(entity, nextWorldTick);
                selections.Add(new TickSelection(entity, synthesizedCommand, false));
            }
            return selections;
        }

        /// <summary>在缺少精确 Tick 命令时限时沿用移动、移动档位和攻击按住状态，并始终清除全部边沿输入。</summary>
        private static CharacterCommand CreateMissingCommand(EntityRecord entity, int nextWorldTick)
        {
            if (!entity.HasReceivedCommand) return CharacterCommand.Empty(nextWorldTick);
            int missingTickCount = nextWorldTick - entity.LastReceivedCommandTick;
            if (missingTickCount > entity.Config.InputTimeoutTicks) return CharacterCommand.Empty(nextWorldTick);
            CharacterCommand previous = entity.LastReceivedCommand;
            return new CharacterCommand(nextWorldTick, previous.MoveX, previous.MoveZ, previous.RequestedMoveMode, false, false, false, false, previous.AttackHeld, false, false, false);
        }

        /// <summary>按照 EntityId 升序执行一次无副作用角色模拟，可选地注入本 Tick 聚合伤害和确认命中数。</summary>
        private static Dictionary<int, CharacterTickResult> SimulateUnsafe(IList<TickSelection> selections, IReadOnlyDictionary<int, int> incomingDamageByEntityId, IReadOnlyDictionary<int, int> confirmedHitCountByEntityId)
        {
            Dictionary<int, CharacterTickResult> results = new Dictionary<int, CharacterTickResult>(selections.Count);
            for (int index = 0; index < selections.Count; index++)
            {
                TickSelection selection = selections[index];
                int incomingDamage = GetValueOrZero(incomingDamageByEntityId, selection.Entity.EntityId);
                int confirmedHitCount = GetValueOrZero(confirmedHitCountByEntityId, selection.Entity.EntityId);
                CharacterTickContext context = new CharacterTickContext(incomingDamage, confirmedHitCount);
                CharacterTickResult result = CharacterSimulation.Step(selection.Entity.State, selection.Command, selection.Entity.Config, context);
                results.Add(selection.Entity.EntityId, result);
            }
            return results;
        }

        /// <summary>消费角色预演的 HitWindowOpened 事件并以稳定攻击者和目标顺序完成空间查询、暴击和防御计算。</summary>
        private HitResolutionBatch ResolveHitsUnsafe(int nextWorldTick, IReadOnlyDictionary<int, CharacterTickResult> provisionalResults)
        {
            Dictionary<int, int> incomingDamageByEntityId = new Dictionary<int, int>(entities.Count);
            Dictionary<int, int> confirmedHitCountByEntityId = new Dictionary<int, int>(entities.Count);
            List<PendingHit> hits = new List<PendingHit>();
            foreach (KeyValuePair<int, EntityRecord> attackerPair in entities)
            {
                int attackerEntityId = attackerPair.Key;
                EntityRecord attacker = attackerPair.Value;
                CharacterTickResult attackerResult = provisionalResults[attackerEntityId];
                if (attackerResult.State.IsDead) continue;
                for (int eventIndex = 0; eventIndex < attackerResult.Events.Count; eventIndex++)
                {
                    CharacterEvent characterEvent = attackerResult.Events[eventIndex];
                    if (characterEvent.Type != CharacterEventType.HitWindowOpened) continue;
                    CharacterActionRuntimeConfig action = attacker.Config.GetAction(characterEvent.ActionKind);
                    DeterministicRandom random = new DeterministicRandom(CreateCriticalSeed(attackerEntityId, nextWorldTick, action.Id));
                    foreach (KeyValuePair<int, EntityRecord> targetPair in entities)
                    {
                        int targetEntityId = targetPair.Key;
                        if (targetEntityId == attackerEntityId) continue;
                        CharacterTickResult targetResult = provisionalResults[targetEntityId];
                        if (targetResult.State.IsDead || !IsInsideHitArea(attackerResult.State, targetResult.State, action.HitRangeRaw)) continue;
                        bool isCritical = random.NextPermille() < attacker.Config.Stats.CriticalRatePermille;
                        int attemptedDamage = CalculateDamage(attacker.Config, action, targetPair.Value.Config, isCritical);
                        hits.Add(new PendingHit(attackerEntityId, targetEntityId, action.Kind, action.Id, attemptedDamage, isCritical));
                        confirmedHitCountByEntityId[attackerEntityId] = SaturatingAdd(GetValueOrZero(confirmedHitCountByEntityId, attackerEntityId), 1);
                        incomingDamageByEntityId[targetEntityId] = SaturatingAdd(GetValueOrZero(incomingDamageByEntityId, targetEntityId), attemptedDamage);
                    }
                }
            }
            return new HitResolutionBatch(incomingDamageByEntityId, confirmedHitCountByEntityId, hits);
        }

        /// <summary>判断目标是否位于攻击者动作锁定方向的 XZ 前半平面和配置半径内，使用 BigInteger 避免极端定点坐标乘法溢出。</summary>
        private static bool IsInsideHitArea(CharacterState attackerState, CharacterState targetState, long hitRangeRaw)
        {
            if (hitRangeRaw <= 0L) return false;
            BigInteger deltaXBig = new BigInteger(targetState.Position.X) - attackerState.Position.X;
            BigInteger deltaZBig = new BigInteger(targetState.Position.Z) - attackerState.Position.Z;
            BigInteger rangeBig = new BigInteger(hitRangeRaw);
            if (deltaXBig * deltaXBig + deltaZBig * deltaZBig > rangeBig * rangeBig) return false;
            long directionXRaw = attackerState.ActionDirectionXRaw;
            long directionZRaw = attackerState.ActionDirectionZRaw;
            if (directionXRaw == 0L && directionZRaw == 0L)
            {
                directionXRaw = attackerState.FacingXRaw;
                directionZRaw = attackerState.FacingZRaw;
            }
            BigInteger forwardDot = deltaXBig * directionXRaw + deltaZBig * directionZRaw;
            return forwardDot >= BigInteger.Zero;
        }

        /// <summary>复刻 Assets/Prometheus/Gameplay/Component/PropertyComponent.cs 中 GetCalculatedDamage 的“基础攻击乘一加额外暴伤”语义，并在旧版未统一消费的防御字段上采用演示期线性扣减且保底一点。</summary>
        private static int CalculateDamage(CharacterRuntimeConfig attackerConfig, CharacterActionRuntimeConfig action, CharacterRuntimeConfig targetConfig, bool isCritical)
        {
            int baseDamage = Math.Max(1, CharacterSimulation.CalculateBaseDamage(attackerConfig, action.Kind));
            long preDefenseDamage = baseDamage;
            if (isCritical) preDefenseDamage = checked(preDefenseDamage * checked(1000L + attackerConfig.Stats.CriticalDamagePermille) / 1000L);
            long mitigatedDamage = preDefenseDamage - targetConfig.Stats.Defense;
            if (mitigatedDamage <= 0L) return 1;
            return mitigatedDamage >= int.MaxValue ? int.MaxValue : (int)mitigatedDamage;
        }

        /// <summary>将最终角色事件和命中结算事件分配为全 Tick 唯一且稳定递增的 Sequence。</summary>
        private List<WorldEvent> CreateWorldEventsUnsafe(int nextWorldTick, IReadOnlyDictionary<int, CharacterTickResult> finalResults, IList<PendingHit> hits)
        {
            List<WorldEvent> worldEvents = new List<WorldEvent>();
            int sequence = 0;
            foreach (KeyValuePair<int, EntityRecord> pair in entities)
            {
                CharacterTickResult result = finalResults[pair.Key];
                for (int eventIndex = 0; eventIndex < result.Events.Count; eventIndex++) worldEvents.Add(WorldEvent.FromCharacter(pair.Key, nextWorldTick, sequence++, result.Events[eventIndex]));
            }
            for (int hitIndex = 0; hitIndex < hits.Count; hitIndex++)
            {
                PendingHit hit = hits[hitIndex];
                worldEvents.Add(WorldEvent.FromResolvedHit(hit.SourceEntityId, hit.TargetEntityId, nextWorldTick, sequence++, hit.ActionKind, hit.ActionId, hit.AttemptedDamage, hit.IsCritical));
            }
            return worldEvents;
        }

        /// <summary>在全部预演和结果构建成功后一次性提交状态、真实命令确认、队列消费和世界 Tick。</summary>
        private void CommitUnsafe(int nextWorldTick, IList<TickSelection> selections, IReadOnlyDictionary<int, CharacterTickResult> finalResults)
        {
            for (int index = 0; index < selections.Count; index++)
            {
                TickSelection selection = selections[index];
                EntityRecord entity = selection.Entity;
                entity.State = finalResults[entity.EntityId].State;
                entity.LastProcessedCommandTick = nextWorldTick;
                if (!selection.ConsumedExactCommand) continue;
                entity.PendingCommands.Remove(nextWorldTick);
                entity.HasReceivedCommand = true;
                entity.LastReceivedCommand = selection.Command;
                entity.LastReceivedCommandTick = nextWorldTick;
            }
            worldTick = nextWorldTick;
        }

        /// <summary>从当前已提交状态或尚未提交的候选结果创建稳定排序快照。</summary>
        private AuthoritativeWorldSnapshot CreateSnapshotUnsafe(ProspectiveCommit prospectiveCommit)
        {
            List<WorldEntitySnapshot> snapshotEntities = new List<WorldEntitySnapshot>(entities.Count);
            if (prospectiveCommit == null)
            {
                foreach (KeyValuePair<int, EntityRecord> pair in entities)
                {
                    EntityRecord entity = pair.Value;
                    snapshotEntities.Add(new WorldEntitySnapshot(entity.EntityId, entity.PlayerId, entity.Config.ContentHash, entity.State, entity.LastProcessedCommandTick));
                }
                return new AuthoritativeWorldSnapshot(worldTick, snapshotEntities);
            }
            foreach (KeyValuePair<int, EntityRecord> pair in entities)
            {
                EntityRecord entity = pair.Value;
                CharacterState state = prospectiveCommit.FinalResults[entity.EntityId].State;
                snapshotEntities.Add(new WorldEntitySnapshot(entity.EntityId, entity.PlayerId, entity.Config.ContentHash, state, prospectiveCommit.WorldTick));
            }
            return new AuthoritativeWorldSnapshot(prospectiveCommit.WorldTick, snapshotEntities);
        }

        /// <summary>从可空只读字典读取非负整数，字典为空或不存在键时返回零。</summary>
        private static int GetValueOrZero(IReadOnlyDictionary<int, int> values, int entityId)
        {
            return values != null && values.TryGetValue(entityId, out int value) ? value : 0;
        }

        /// <summary>执行不会溢出的非负整数饱和加法。</summary>
        private static int SaturatingAdd(int left, int right)
        {
            long sum = (long)left + right;
            return sum >= int.MaxValue ? int.MaxValue : (int)sum;
        }

        /// <summary>把攻击者、世界 Tick 和动作行编号混合为确定性暴击随机种子。</summary>
        private static ulong CreateCriticalSeed(int entityId, int tick, int actionId)
        {
            ulong seed = 14695981039346656037UL;
            MixSeed(ref seed, unchecked((uint)entityId));
            MixSeed(ref seed, unchecked((uint)tick));
            MixSeed(ref seed, unchecked((uint)actionId));
            return seed;
        }

        /// <summary>把一个三十二位字段逐字节加入稳定 FNV-1a 随机种子。</summary>
        private static void MixSeed(ref ulong seed, uint value)
        {
            for (int byteIndex = 0; byteIndex < 4; byteIndex++)
            {
                seed ^= (byte)(value >> (byteIndex * 8));
                seed *= 1099511628211UL;
            }
        }

        /// <summary>保存世界内单个玩家角色的可变权威记录，仅允许在 syncRoot 锁内访问。</summary>
        private sealed class EntityRecord
        {
            /// <summary>创建一个不含待处理命令的权威角色记录。</summary>
            public EntityRecord(int entityId, int playerId, CharacterRuntimeConfig config, CharacterState state)
            {
                EntityId = entityId;
                PlayerId = playerId;
                Config = config;
                State = state;
                LastProcessedCommandTick = state.Tick;
                LastReceivedCommandTick = -1;
            }

            /// <summary>获取世界内稳定实体编号。</summary>
            public int EntityId { get; }
            /// <summary>获取实体所属玩家编号。</summary>
            public int PlayerId { get; }
            /// <summary>获取该实体不可变角色运行时配置。</summary>
            public CharacterRuntimeConfig Config { get; }
            /// <summary>获取按命令 Tick 排序且有 PredictionHistoryTicks 上限的待消费命令表。</summary>
            public SortedDictionary<int, CharacterCommand> PendingCommands { get; } = new SortedDictionary<int, CharacterCommand>();
            /// <summary>获取或设置最近提交的完整权威角色状态。</summary>
            public CharacterState State { get; set; }
            /// <summary>获取或设置该实体是否曾实际消费过客户端命令。</summary>
            public bool HasReceivedCommand { get; set; }
            /// <summary>获取或设置最近实际消费的客户端命令，用于限时延续连续量。</summary>
            public CharacterCommand LastReceivedCommand { get; set; }
            /// <summary>获取或设置最近完成模拟的命令 Tick，并始终与 State.Tick 一致。</summary>
            public int LastProcessedCommandTick { get; set; }
            /// <summary>获取或设置最近实际消费的客户端命令 Tick。</summary>
            public int LastReceivedCommandTick { get; set; }
        }

        /// <summary>保存一个世界 Tick 为实体选中的命令以及该命令是否来自精确 Tick 队列。</summary>
        private readonly struct TickSelection
        {
            public TickSelection(EntityRecord entity, CharacterCommand command, bool consumedExactCommand)
            {
                Entity = entity;
                Command = command;
                ConsumedExactCommand = consumedExactCommand;
            }

            /// <summary>获取本 Tick 接受该命令的实体记录。</summary>
            public EntityRecord Entity { get; }
            /// <summary>获取用于本 Tick 模拟的连续角色命令。</summary>
            public CharacterCommand Command { get; }
            /// <summary>获取命令是否来自客户端精确 Tick 队列。</summary>
            public bool ConsumedExactCommand { get; }
        }

        /// <summary>保存一次几何命中经过暴击与防御计算后的稳定结算数据。</summary>
        private readonly struct PendingHit
        {
            public PendingHit(int sourceEntityId, int targetEntityId, CharacterActionKind actionKind, int actionId, int attemptedDamage, bool isCritical)
            {
                SourceEntityId = sourceEntityId;
                TargetEntityId = targetEntityId;
                ActionKind = actionKind;
                ActionId = actionId;
                AttemptedDamage = attemptedDamage;
                IsCritical = isCritical;
            }

            /// <summary>获取攻击者实体编号。</summary>
            public int SourceEntityId { get; }
            /// <summary>获取目标实体编号。</summary>
            public int TargetEntityId { get; }
            /// <summary>获取命中的角色动作种类。</summary>
            public CharacterActionKind ActionKind { get; }
            /// <summary>获取命中的 Luban 动作行编号。</summary>
            public int ActionId { get; }
            /// <summary>获取经过暴击和防御计算后的尝试伤害。</summary>
            public int AttemptedDamage { get; }
            /// <summary>获取该命中是否触发确定性暴击。</summary>
            public bool IsCritical { get; }
        }

        /// <summary>聚合本 Tick 全部命中、目标总伤害和攻击者确认命中数。</summary>
        private sealed class HitResolutionBatch
        {
            public HitResolutionBatch(IReadOnlyDictionary<int, int> incomingDamageByEntityId, IReadOnlyDictionary<int, int> confirmedHitCountByEntityId, IList<PendingHit> hits)
            {
                IncomingDamageByEntityId = incomingDamageByEntityId;
                ConfirmedHitCountByEntityId = confirmedHitCountByEntityId;
                Hits = hits;
            }

            /// <summary>获取按照目标实体聚合的本 Tick 尝试伤害。</summary>
            public IReadOnlyDictionary<int, int> IncomingDamageByEntityId { get; }
            /// <summary>获取按照攻击者实体聚合的本 Tick 确认命中数。</summary>
            public IReadOnlyDictionary<int, int> ConfirmedHitCountByEntityId { get; }
            /// <summary>获取按照攻击者和目标稳定顺序保存的逐次命中。</summary>
            public IList<PendingHit> Hits { get; }
        }

        /// <summary>描述尚未写入世界记录的候选提交，用于在原子写入前构造并验证最终快照。</summary>
        private sealed class ProspectiveCommit
        {
            public ProspectiveCommit(int worldTick, IReadOnlyDictionary<int, CharacterTickResult> finalResults)
            {
                WorldTick = worldTick;
                FinalResults = finalResults;
            }

            /// <summary>获取候选提交对应的世界 Tick。</summary>
            public int WorldTick { get; }
            /// <summary>获取本 Tick 注入命中上下文后的最终角色结果。</summary>
            public IReadOnlyDictionary<int, CharacterTickResult> FinalResults { get; }
        }

        /// <summary>提供不依赖 System.Random 实现细节的 SplitMix64 确定性随机序列。</summary>
        private struct DeterministicRandom
        {
            private ulong state;

            public DeterministicRandom(ulong seed)
            {
                state = seed;
            }

            /// <summary>返回零到九百九十九之间的确定性均匀整数。</summary>
            public int NextPermille()
            {
                state += 0x9E3779B97F4A7C15UL;
                ulong value = state;
                value = (value ^ (value >> 30)) * 0xBF58476D1CE4E5B9UL;
                value = (value ^ (value >> 27)) * 0x94D049BB133111EBUL;
                value ^= value >> 31;
                return (int)(value % 1000UL);
            }
        }
    }
}
