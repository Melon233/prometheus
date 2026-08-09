using System;
using System.Collections.Generic;
using PromeArchTrial.Game.Character;

namespace PromeArchTrial.Game.World
{
    /// <summary>
    /// 提供客户端、服务器和持续集成均可直接调用的无测试框架行为探针，覆盖输入缺包、身份安全、稳定快照、同 Tick 命中、防御、暴击和事件字段。
    /// </summary>
    public static class AuthoritativeBattleWorldBehaviorProbe
    {
        /// <summary>运行权威世界全部核心行为验证，任一不变量失败时抛出 InvalidOperationException。</summary>
        public static void RunAll()
        {
            VerifySafeEntityLifecycleAndStableSnapshotOrder();
            VerifyInputTimeoutPreservesOnlyContinuousValues();
            VerifySameTickCombatAndDeterministicEvents();
        }

        /// <summary>验证重复实体、重复玩家和重复移除不会破坏身份索引，并确认快照使用 EntityId 升序。</summary>
        private static void VerifySafeEntityLifecycleAndStableSnapshotOrder()
        {
            CharacterRuntimeConfig config = LegacyYefaConfigFactory.Create();
            AuthoritativeBattleWorld world = new AuthoritativeBattleWorld(config);
            Assert(world.TryAddEntity(20, 200, FixedVector3.Zero), "The first valid entity must be accepted.");
            Assert(world.TryAddEntity(10, 100, new FixedVector3(CharacterFixedPoint.FromUnits(2m), 0L, 0L)), "A second valid entity must be accepted.");
            Assert(!world.TryAddEntity(20, 300, FixedVector3.Zero), "A duplicate entity id must be rejected without mutation.");
            Assert(!world.TryAddEntity(30, 100, FixedVector3.Zero), "A duplicate player id must be rejected without mutation.");
            AuthoritativeWorldSnapshot snapshot = world.CaptureSnapshot();
            Assert(snapshot.Entities.Count == 2 && snapshot.Entities[0].EntityId == 10 && snapshot.Entities[1].EntityId == 20, "Snapshot entities must be sorted by entity id regardless of insertion order.");
            Assert(world.TryGetEntityIdByPlayerId(100, out int entityId) && entityId == 10, "Player-to-entity lookup must preserve the unique owner mapping.");
            Assert(world.RemoveEntity(20), "Removing an existing entity must succeed exactly once.");
            Assert(!world.RemoveEntity(20), "Removing an absent entity must be an idempotent false result.");
            Assert(!world.TryGetEntityIdByPlayerId(200, out _), "Removing an entity must also remove its player ownership index.");
            world.Tick();
            Assert(world.TryAddEntity(30, 300, FixedVector3.Zero), "A newly connected player must be addable while the global world is already running.");
            Assert(world.GetState(30).Tick == world.WorldTick && world.GetLastProcessedCommandTick(30) == world.WorldTick, "A dynamic spawn state and its reconciliation acknowledgement must start at the current completed world tick.");
        }

        /// <summary>验证缺包期间限时沿用移动档位和 AttackHeld、清除所有边沿、超时归零并且不会伪造真实命令确认。</summary>
        private static void VerifyInputTimeoutPreservesOnlyContinuousValues()
        {
            CharacterRuntimeConfig baseConfig = LegacyYefaConfigFactory.Create();
            CharacterRuntimeConfig movementConfig = CopyConfigWithNetworkWindows(baseConfig, baseConfig.InputTimeoutTicks, baseConfig.PredictionHistoryTicks);
            AuthoritativeBattleWorld movementWorld = new AuthoritativeBattleWorld(movementConfig);
            movementWorld.AddEntity(1, 1, FixedVector3.Zero);
            CharacterCommand movementCommand = new CharacterCommand(0, 1, 0, CharacterMoveMode.Run, false, false, false, false, false, false, false, false);
            Assert(movementWorld.TrySubmitCommand(1, movementCommand), "An exact next-tick movement command must be accepted.");
            movementWorld.Tick();
            long previousX = movementWorld.GetState(1).Position.X;
            for (int tick = 1; tick <= movementConfig.InputTimeoutTicks; tick++)
            {
                movementWorld.Tick();
                long currentX = movementWorld.GetState(1).Position.X;
                Assert(currentX > previousX, "Movement must remain continuous inside InputTimeoutTicks when an exact command is missing.");
                previousX = currentX;
            }
            movementWorld.Tick();
            Assert(movementWorld.GetState(1).Position.X == previousX, "Movement must become fully neutral immediately after InputTimeoutTicks expires.");
            Assert(movementWorld.GetLastProcessedCommandTick(1) == movementWorld.WorldTick && movementWorld.GetState(1).Tick == movementWorld.WorldTick, "Every exact, synthesized, or neutral command must advance state and reconciliation acknowledgement to the same global world tick.");
            Assert(movementWorld.GetLastReceivedCommandTick(1) == 0, "Synthesized missing commands must not advance the latest real network command diagnostic tick.");
            Assert(!movementWorld.TrySubmitCommand(1, CharacterCommand.Empty(0)), "A command for an already simulated tick must be rejected.");
            int tooEarlyTick = checked(movementWorld.WorldTick + movementConfig.PredictionHistoryTicks + 1);
            Assert(!movementWorld.TrySubmitCommand(1, CharacterCommand.Empty(tooEarlyTick)), "A future command beyond PredictionHistoryTicks must be rejected to keep the queue bounded.");

            CharacterRuntimeConfig heldConfig = CopyConfigWithNetworkWindows(baseConfig, 20, baseConfig.PredictionHistoryTicks);
            AuthoritativeBattleWorld heldWorld = new AuthoritativeBattleWorld(heldConfig);
            heldWorld.AddEntity(1, 1, FixedVector3.Zero);
            CharacterCommand heldAttack = new CharacterCommand(0, 0, 0, CharacterMoveMode.Run, false, false, false, true, true, false, false, false);
            Assert(heldWorld.TrySubmitCommand(1, heldAttack), "The initial held attack command must be accepted.");
            heldWorld.Tick();
            for (int tick = 1; tick <= heldConfig.Combat.HeavyAttackChargeTicks - 1; tick++) heldWorld.Tick();
            Assert(heldWorld.GetState(1).ActionKind == CharacterActionKind.HeavyAttack, "Missing commands must keep AttackHeld while clearing AttackPressed so charge time advances to HeavyAttack exactly once.");
            Assert(heldWorld.GetLastProcessedCommandTick(1) == heldWorld.WorldTick, "Held input synthesis must still advance the prediction reconciliation acknowledgement.");
            Assert(heldWorld.GetLastReceivedCommandTick(1) == 0, "Held input synthesis must not be reported as an additional received network command.");
        }

        /// <summary>验证前半平面命中在打开窗口的同一 Tick 扣血，并确认暴击结果、事件顺序和世界哈希可重复。</summary>
        private static void VerifySameTickCombatAndDeterministicEvents()
        {
            CombatOutcome first = RunDeterministicCombatScenario();
            CombatOutcome second = RunDeterministicCombatScenario();
            Assert(first.SnapshotHash == second.SnapshotHash, "Replaying identical commands must produce the same authoritative world snapshot hash.");
            Assert(first.ResolvedHit.Equals(second.ResolvedHit), "Replaying identical commands must produce exactly the same deterministic critical hit event.");
            Assert(first.ResolvedHit.SourceEntityId == 1 && first.ResolvedHit.TargetEntityId == 2, "Resolved hit events must expose explicit source and target entity ids.");
            Assert(first.ResolvedHit.ActionKind == CharacterActionKind.Attack1 && first.ResolvedHit.ActionId == 1101, "Resolved hit events must expose both action kind and Luban action row id.");
            int expectedDamage = first.ResolvedHit.IsCritical ? 190 : 90;
            Assert(first.ResolvedHit.Damage == expectedDamage, "Damage must use base action damage, old bonus critical semantics, and temporary linear target defense reduction.");
            Assert(first.FrontTargetHp == 3000 - expectedDamage, "The front target must lose resolved damage in the exact HitWindowOpened world tick.");
            Assert(first.BehindTargetHp == 3000, "A target inside range but behind the attack direction must fail the front-half-plane query.");
            Assert(first.PlayerEventCount >= 2, "The damaged player's latest event query must include both its character damage event and the incoming resolved hit event.");
        }

        /// <summary>运行一次包含前方和后方目标的确定性普攻场景并返回可跨运行比较的结果。</summary>
        private static CombatOutcome RunDeterministicCombatScenario()
        {
            CharacterRuntimeConfig baseConfig = LegacyYefaConfigFactory.Create();
            CharacterRuntimeConfig attackerConfig = CopyConfigWithStats(baseConfig, new CharacterStatsRuntimeConfig(3000, 100, 0, 1000, 1000, 500, 100, 100));
            CharacterRuntimeConfig targetConfig = CopyConfigWithStats(baseConfig, new CharacterStatsRuntimeConfig(3000, 10, 10, 1000, 1000, 0, 100, 100));
            AuthoritativeBattleWorld world = new AuthoritativeBattleWorld(baseConfig.TickRate);
            world.AddEntity(1, 1, attackerConfig, CharacterState.CreateInitial(attackerConfig, FixedVector3.Zero));
            world.AddEntity(2, 2, targetConfig, CharacterState.CreateInitial(targetConfig, new FixedVector3(0L, 0L, CharacterFixedPoint.FromUnits(1m))));
            world.AddEntity(3, 3, targetConfig, CharacterState.CreateInitial(targetConfig, new FixedVector3(0L, 0L, CharacterFixedPoint.FromUnits(-0.5m))));
            CharacterCommand press = new CharacterCommand(0, 0, 0, CharacterMoveMode.Run, false, false, false, true, true, false, false, false);
            CharacterCommand release = new CharacterCommand(1, 0, 0, CharacterMoveMode.Run, false, false, false, false, false, true, false, false);
            Assert(world.TrySubmitCommand(1, press) && world.TrySubmitCommand(1, release), "The deterministic combat scenario must accept its ordered attack commands.");
            WorldTickResult result = null;
            for (int tick = 0; tick <= 7; tick++) result = world.Tick();
            Assert(result != null && result.Snapshot.WorldTick == 7, "Attack1 must open its configured hit window at authoritative world tick seven in this command sequence.");
            WorldEvent resolvedHit = default;
            bool foundResolvedHit = false;
            for (int index = 0; index < result.Events.Count; index++)
            {
                WorldEvent worldEvent = result.Events[index];
                Assert(worldEvent.Ordinal == index && worldEvent.Sequence == index, "World event ordinal and sequence must be contiguous and identical inside a tick.");
                if (worldEvent.Kind != WorldEventKind.HitResolved) continue;
                Assert(!foundResolvedHit, "Only the single front target may resolve a hit in this scenario.");
                resolvedHit = worldEvent;
                foundResolvedHit = true;
            }
            Assert(foundResolvedHit, "HitWindowOpened must be consumed into a world-level resolved hit event.");
            IReadOnlyList<WorldEvent> targetEvents = world.GetLatestEventsForPlayer(2);
            return new CombatOutcome(result.Snapshot.StableHash, resolvedHit, world.GetState(2).Hp, world.GetState(3).Hp, targetEvents.Count);
        }

        /// <summary>复制角色配置并只替换网络缺包与预测历史窗口。</summary>
        private static CharacterRuntimeConfig CopyConfigWithNetworkWindows(CharacterRuntimeConfig source, int inputTimeoutTicks, int predictionHistoryTicks)
        {
            return new CharacterRuntimeConfig(source.TickRate, inputTimeoutTicks, predictionHistoryTicks, source.Stats, source.Locomotion, source.Combat, source.Actions.Values);
        }

        /// <summary>复制角色配置并只替换角色战斗属性。</summary>
        private static CharacterRuntimeConfig CopyConfigWithStats(CharacterRuntimeConfig source, CharacterStatsRuntimeConfig stats)
        {
            return new CharacterRuntimeConfig(source.TickRate, source.InputTimeoutTicks, source.PredictionHistoryTicks, stats, source.Locomotion, source.Combat, source.Actions.Values);
        }

        /// <summary>在行为不变量失败时抛出包含明确原因的异常。</summary>
        private static void Assert(bool condition, string message)
        {
            if (!condition) throw new InvalidOperationException(message);
        }

        /// <summary>保存一次确定性战斗场景用于跨运行比较的最小验收数据。</summary>
        private readonly struct CombatOutcome
        {
            public CombatOutcome(ulong snapshotHash, WorldEvent resolvedHit, int frontTargetHp, int behindTargetHp, int playerEventCount)
            {
                SnapshotHash = snapshotHash;
                ResolvedHit = resolvedHit;
                FrontTargetHp = frontTargetHp;
                BehindTargetHp = behindTargetHp;
                PlayerEventCount = playerEventCount;
            }

            /// <summary>获取场景最终权威世界快照哈希。</summary>
            public ulong SnapshotHash { get; }
            /// <summary>获取前方目标收到的世界命中事件。</summary>
            public WorldEvent ResolvedHit { get; }
            /// <summary>获取前方目标在同 Tick 结算后的生命值。</summary>
            public int FrontTargetHp { get; }
            /// <summary>获取后方目标在前半平面过滤后的生命值。</summary>
            public int BehindTargetHp { get; }
            /// <summary>获取受击玩家通过最新事件 API 读到的事件数量。</summary>
            public int PlayerEventCount { get; }
        }
    }
}
