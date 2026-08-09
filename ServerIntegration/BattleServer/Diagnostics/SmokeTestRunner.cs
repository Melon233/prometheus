using System;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using PromeArchTrial.BattleServer.Configuration;
using PromeArchTrial.BattleServer.Networking;
using PromeArchTrial.Config.gameplay;
using PromeArchTrial.Core.Networking;
using PromeArchTrial.Game.Character;
using PromeArchTrial.Game.ConfigAdapter;
using PromeArchTrial.Game.Networking;
using PromeArchTrial.Game.World;

namespace PromeArchTrial.BattleServer.Diagnostics
{
    /// <summary>
    /// 提供不依赖外部测试框架的端到端验收，覆盖 v5 Protobuf、Luban ref、角色动作、预测恢复重放、双实体命中和真实 30 Hz TCP 宿主。
    /// </summary>
    public static class SmokeTestRunner
    {
        /// <summary>依次执行全部同步领域探针与本地 TCP 联调，任一不变量失败时返回非零进程退出码。</summary>
        public static async Task<int> RunAsync(BattleServerConfiguration configuration)
        {
            if (configuration == null) throw new ArgumentNullException(nameof(configuration));
            Console.WriteLine("SmokeTest: validating Luban server tables and resolved refs.");
            VerifyLubanReferences(configuration);
            Console.WriteLine("SmokeTest: validating Protocol v5 round trips and full rollback state.");
            VerifyProtocolV5RoundTrips(configuration.CharacterConfig, configuration.CharacterId);
            Console.WriteLine("SmokeTest: validating reliable event accumulation across replaceable state snapshots.");
            VerifyReliableSnapshotOutbox(configuration.CharacterConfig);
            Console.WriteLine("SmokeTest: validating prediction restore and unacknowledged-command replay.");
            VerifyPredictionReplay(configuration.CharacterConfig);
            Console.WriteLine("SmokeTest: validating walk, run, sprint, jump, dodge, four attacks, heavy attack, skill, and ultimate.");
            VerifyConfiguredCharacterActions(configuration.CharacterConfig);
            Console.WriteLine("SmokeTest: validating the shared authoritative-world behavior probe.");
            AuthoritativeBattleWorldBehaviorProbe.RunAll();
            Console.WriteLine("SmokeTest: validating idempotent late and duplicate command classification.");
            VerifyCommandSubmissionClassification(configuration.CharacterConfig);
            Console.WriteLine("SmokeTest: validating a two-entity hit with the Luban-compiled character config.");
            VerifyTwoEntityHit(configuration.CharacterConfig);
            Console.WriteLine("SmokeTest: validating real localhost handshake, immediate Pong, and 30 Hz snapshot cadence.");
            await VerifyNetworkHostAsync(configuration.CharacterId, configuration.CharacterConfig).ConfigureAwait(false);
            Console.WriteLine("SMOKE TEST PASS: Protocol v5, Luban refs, prediction replay, gameplay actions, world combat, Ping/Pong, and 30 Hz host are valid.");
            return 0;
        }

        /// <summary>确认服务端 Character 的全部一级 ref、动作集合 ref 和适配器哈希在重复编译后保持一致。</summary>
        private static void VerifyLubanReferences(BattleServerConfiguration configuration)
        {
            Character character = configuration.Tables.TbCharacter.GetOrDefault(configuration.CharacterId);
            Assert(character != null, $"Character[{configuration.CharacterId}] must exist in the server Luban projection.");
            Assert(character.BattleRuleId_Ref != null && character.BattleRuleId_Ref.Id == character.BattleRuleId, "Character.battle_rule_id must resolve to the authored row.");
            Assert(character.PropertyId_Ref != null && character.PropertyId_Ref.Id == character.PropertyId, "Character.property_id must resolve to the authored row.");
            Assert(character.LocomotionId_Ref != null && character.LocomotionId_Ref.Id == character.LocomotionId, "Character.locomotion_id must resolve to the authored row.");
            Assert(character.DodgeId_Ref != null && character.DodgeId_Ref.Id == character.DodgeId, "Character.dodge_id must resolve to the authored row.");
            Assert(character.ActionSetId_Ref != null && character.ActionSetId_Ref.Id == character.ActionSetId, "Character.action_set_id must resolve to the authored row.");
            ActionSet actionSet = character.ActionSetId_Ref;
            Assert(actionSet.NormalAttackIds_Ref != null && actionSet.NormalAttackIds_Ref.Count == 4, "ActionSet.normal_attack_ids must resolve exactly four ordered rows.");
            Assert(actionSet.MovingAttackIds_Ref != null && actionSet.MovingAttackIds_Ref.Count == 4, "ActionSet.moving_attack_ids must resolve exactly four ordered rows.");
            for (int index = 0; index < 4; index++)
            {
                Assert(actionSet.NormalAttackIds_Ref[index] != null && actionSet.NormalAttackIds_Ref[index].Id == actionSet.NormalAttackIds[index], $"Normal attack ref {index} must preserve its authored ID.");
                Assert(actionSet.MovingAttackIds_Ref[index] != null && actionSet.MovingAttackIds_Ref[index].Id == actionSet.MovingAttackIds[index], $"Moving attack ref {index} must preserve its authored ID.");
            }
            Assert(actionSet.SkillActionId_Ref != null && actionSet.SkillActionId_Ref.Id == actionSet.SkillActionId, "ActionSet.skill_action_id must resolve.");
            Assert(actionSet.SpecialActionId_Ref != null && actionSet.SpecialActionId_Ref.Id == actionSet.SpecialActionId, "ActionSet.special_action_id must resolve.");
            Assert(actionSet.UltimateActionId_Ref != null && actionSet.UltimateActionId_Ref.Id == actionSet.UltimateActionId, "ActionSet.ultimate_action_id must resolve.");
            CharacterRuntimeConfig secondCompilation = CharacterLubanConfigAdapter.Compile(configuration.Tables, configuration.CharacterId);
            Assert(secondCompilation.ContentHash == configuration.CharacterConfig.ContentHash, "Compiling the same resolved Luban Character twice must produce the same runtime content hash.");
        }

        /// <summary>逐类往返编码 v5 消息，并确认 Welcome 初态、输入预测态和快照事件不会丢失。</summary>
        private static void VerifyProtocolV5RoundTrips(CharacterRuntimeConfig config, int characterId)
        {
            Assert(BattleProtocol.Version == 5, "Battle protocol must be version five.");
            ClientHelloMessage decodedHello = BattleProtocolCodec.DecodeClientHello(BattleProtocolCodec.Encode(new ClientHelloMessage(BattleProtocol.Version, config.ContentHash)));
            Assert(decodedHello.ProtocolVersion == BattleProtocol.Version && decodedHello.ConfigHash == config.ContentHash, "ClientHello v5 round trip must preserve protocol and config hash.");
            CharacterState initialState = CharacterState.CreateInitial(config, FixedVector3.Zero);
            ServerWelcomeMessage decodedWelcome = BattleProtocolCodec.DecodeServerWelcome(BattleProtocolCodec.Encode(new ServerWelcomeMessage(11, 21, characterId, initialState.Tick, config.TickRate, config.ContentHash, CharacterNetworkMapper.ToNetworkState(initialState))));
            Assert(decodedWelcome.PlayerId == 11 && decodedWelcome.EntityId == 21 && decodedWelcome.CharacterId == characterId, "ServerWelcome must preserve player, entity, and Character identities.");
            Assert(decodedWelcome.ServerTick == initialState.Tick && decodedWelcome.TickRate == 30 && decodedWelcome.ConfigHash == config.ContentHash, "ServerWelcome must preserve tick and deterministic config metadata.");
            Assert(CharacterNetworkMapper.ToCharacterState(decodedWelcome.InitialState) == initialState, "ServerWelcome must preserve the complete initial rollback state.");
            CharacterCommand command = new CharacterCommand(0, 1, 1, CharacterMoveMode.Sprint, true, true, false, true, true, false, true, true);
            CharacterState predictedState = CharacterSimulation.Step(initialState, command, config).State;
            ClientInputMessage decodedInput = BattleProtocolCodec.DecodeClientInput(BattleProtocolCodec.Encode(CharacterNetworkMapper.ToClientInputMessage(command, predictedState)));
            Assert(CharacterNetworkMapper.ToCharacterCommand(decodedInput).Equals(command), "ClientInput must preserve every movement and action input field.");
            Assert(CharacterNetworkMapper.ToCharacterState(decodedInput.PredictedState) == predictedState, "ClientInput must preserve the complete predicted rollback state.");
            BattleEventMessage battleEvent = new BattleEventMessage(BattleEventKind.HitResolved, 21, 22, 0, 0, 0, (int)CharacterActionKind.Attack1, config.GetAction(CharacterActionKind.Attack1).Id, 7, true);
            ServerSnapshotMessage decodedSnapshot = BattleProtocolCodec.DecodeServerSnapshot(BattleProtocolCodec.Encode(new ServerSnapshotMessage(0, 0, CharacterNetworkMapper.ToNetworkState(predictedState), new[] { battleEvent })));
            Assert(decodedSnapshot.ServerTick == 0 && decodedSnapshot.AcknowledgedClientTick == 0, "ServerSnapshot must preserve world and reconciliation ticks.");
            Assert(CharacterNetworkMapper.ToCharacterState(decodedSnapshot.State) == predictedState, "ServerSnapshot must preserve the complete authoritative rollback state.");
            Assert(decodedSnapshot.Events.Count == 1 && decodedSnapshot.Events[0].TargetEntityId == 22 && decodedSnapshot.Events[0].IsCritical, "ServerSnapshot must preserve the complete tick event payload.");
            Assert(BattleProtocolCodec.DecodeClientPing(BattleProtocolCodec.Encode(new ClientPingMessage(123))).Sequence == 123, "ClientPing round trip must preserve sequence.");
            Assert(BattleProtocolCodec.DecodeServerPong(BattleProtocolCodec.Encode(new ServerPongMessage(123))).Sequence == 123, "ServerPong round trip must preserve sequence.");
        }

        /// <summary>在旧状态快照尚未确认时发布更新状态，验证事件去重、排序、写入前保留和写入后精确移除。</summary>
        private static void VerifyReliableSnapshotOutbox(CharacterRuntimeConfig config)
        {
            CharacterState initialState = CharacterState.CreateInitial(config, FixedVector3.Zero);
            CharacterState tickZeroState = CharacterSimulation.Step(initialState, CharacterCommand.Empty(0), config).State;
            CharacterState tickOneState = CharacterSimulation.Step(tickZeroState, CharacterCommand.Empty(1), config).State;
            CharacterActionRuntimeConfig attack = config.GetAction(CharacterActionKind.Attack1);
            BattleEventMessage actionEvent = new BattleEventMessage(BattleEventKind.Character, 1, 0, 0, 0, (int)CharacterEventType.ActionStarted, (int)CharacterActionKind.Attack1, attack.Id, 0, false);
            BattleEventMessage hitEvent = new BattleEventMessage(BattleEventKind.HitResolved, 1, 2, 0, 1, 0, (int)CharacterActionKind.Attack1, attack.Id, 7, true);
            BattleEventMessage laterEvent = new BattleEventMessage(BattleEventKind.Character, 1, 0, 1, 0, (int)CharacterEventType.ActionEnded, (int)CharacterActionKind.Attack1, attack.Id, 0, false);
            ReliableSnapshotOutbox outbox = new ReliableSnapshotOutbox();
            ServerSnapshotMessage tickZeroSnapshot = new ServerSnapshotMessage(0, 0, CharacterNetworkMapper.ToNetworkState(tickZeroState), new[] { actionEvent, hitEvent });
            outbox.Publish(tickZeroSnapshot);
            outbox.Publish(tickZeroSnapshot);
            Assert(outbox.PendingEventCount == 2, "Publishing the same world Tick twice must deduplicate events by WorldTick and Ordinal.");
            Assert(outbox.TryReserve(out ReliableSnapshotOutbox.SnapshotOutboxReservation firstReservation), "The reliable outbox must reserve its pending snapshot.");
            ServerSnapshotMessage firstPayload = BattleProtocolCodec.DecodeServerSnapshot(firstReservation.Payload);
            Assert(firstPayload.Events.Count == 2 && firstPayload.Events[0].Ordinal == 0 && firstPayload.Events[1].Ordinal == 1, "Reserved events must preserve deterministic Tick and Ordinal order.");
            Assert(outbox.PendingEventCount == 2, "Reserving a payload must not remove events before the TCP write succeeds.");
            outbox.Publish(new ServerSnapshotMessage(1, 1, CharacterNetworkMapper.ToNetworkState(tickOneState), new[] { laterEvent }));
            outbox.Commit(firstReservation);
            Assert(outbox.PendingEventCount == 1 && outbox.HasPending, "Committing an older payload must remove only its events and retain a concurrently published newer state and event.");
            Assert(outbox.TryReserve(out ReliableSnapshotOutbox.SnapshotOutboxReservation secondReservation), "The newer merged snapshot must remain reservable after the older payload commits.");
            ServerSnapshotMessage secondPayload = BattleProtocolCodec.DecodeServerSnapshot(secondReservation.Payload);
            Assert(secondPayload.ServerTick == 1 && secondPayload.Events.Count == 1 && secondPayload.Events[0].WorldTick == 1, "The next payload must use the latest state while reliably carrying the unsent newer event.");
            outbox.Commit(secondReservation);
            Assert(outbox.PendingEventCount == 0 && !outbox.HasPending, "A successful final commit must empty both the event set and replaceable state snapshot.");
        }

        /// <summary>制造 Tick 0 预测偏差，恢复权威状态，并确认 Tick 1、2 命令按原顺序完整重放。</summary>
        private static void VerifyPredictionReplay(CharacterRuntimeConfig config)
        {
            CharacterState initialState = CharacterState.CreateInitial(config, FixedVector3.Zero);
            CharacterPredictionController prediction = new CharacterPredictionController(config, initialState);
            CharacterCommand predictedTickZero = new CharacterCommand(0, 1, 0, CharacterMoveMode.Run, false, false, false, false, false, false, false, false);
            CharacterCommand tickOne = new CharacterCommand(1, 0, 1, CharacterMoveMode.Sprint, true, false, false, false, false, false, false, false);
            CharacterCommand tickTwo = CharacterCommand.Empty(2);
            prediction.Predict(predictedTickZero);
            prediction.Predict(tickOne);
            prediction.Predict(tickTwo);
            CharacterState authoritativeTickZero = CharacterSimulation.Step(initialState, CharacterCommand.Empty(0), config).State;
            CharacterState expectedAfterReplay = CharacterSimulation.Step(CharacterSimulation.Step(authoritativeTickZero, tickOne, config).State, tickTwo, config).State;
            CharacterReconciliationResult reconciliation = prediction.Reconcile(0, authoritativeTickZero);
            Assert(reconciliation.Accepted && reconciliation.Compared && reconciliation.Corrected, "A divergent authoritative Tick 0 must be compared, accepted, and corrected.");
            Assert(reconciliation.ReplayedCommandCount == 2 && prediction.PendingCommandCount == 2, "Prediction reconciliation must replay and retain exactly the two unacknowledged commands.");
            Assert(prediction.CurrentState == expectedAfterReplay && reconciliation.FinalStateHash == expectedAfterReplay.StableHash, "Prediction replay must finish at the same complete state as direct deterministic simulation.");
        }

        /// <summary>用真实固定 Tick 模拟分别验证所有移动档位、空中状态、闪避方向、四段连击、重击、技能和终结技。</summary>
        private static void VerifyConfiguredCharacterActions(CharacterRuntimeConfig config)
        {
            VerifyMoveMode(config, CharacterMoveMode.Walk, CharacterLocomotionState.Walk);
            VerifyMoveMode(config, CharacterMoveMode.Run, CharacterLocomotionState.Run);
            VerifyMoveMode(config, CharacterMoveMode.Sprint, CharacterLocomotionState.Sprint);
            CharacterState jumpState = CharacterSimulation.Step(CharacterState.CreateInitial(config, FixedVector3.Zero), new CharacterCommand(0, 0, 1, CharacterMoveMode.Run, true, false, false, false, false, false, false, false), config).State;
            Assert(jumpState.LocomotionState == CharacterLocomotionState.Jump && !jumpState.IsGrounded && jumpState.VerticalVelocityRaw > 0L, "Jump input must immediately enter the deterministic airborne pipeline.");
            VerifySingleAction(config, new CharacterCommand(0, 0, 1, CharacterMoveMode.Run, false, true, false, false, false, false, false, false), CharacterActionKind.DodgeForward, "forward dodge");
            VerifySingleAction(config, new CharacterCommand(0, 0, 1, CharacterMoveMode.Run, false, true, true, false, false, false, false, false), CharacterActionKind.DodgeBackward, "backward dodge");
            VerifyAttackCombo(config);
            VerifyHeavyAttack(config);
            VerifySingleAction(config, new CharacterCommand(0, 0, 0, CharacterMoveMode.Run, false, false, false, false, false, false, true, false), CharacterActionKind.Skill, "skill");
            VerifySingleAction(config, new CharacterCommand(0, 0, 0, CharacterMoveMode.Run, false, false, false, false, false, false, false, true), CharacterActionKind.Ultimate, "ultimate");
        }

        /// <summary>验证一个地面方向输入使用指定档位，并产生与档位一致的移动状态和正向位移。</summary>
        private static void VerifyMoveMode(CharacterRuntimeConfig config, CharacterMoveMode moveMode, CharacterLocomotionState expectedLocomotion)
        {
            CharacterCommand command = new CharacterCommand(0, 0, 1, moveMode, false, false, false, false, false, false, false, false);
            CharacterState result = CharacterSimulation.Step(CharacterState.CreateInitial(config, FixedVector3.Zero), command, config).State;
            Assert(result.LocomotionState == expectedLocomotion && result.Position.Z > 0L, $"{moveMode} input must produce {expectedLocomotion} and positive movement.");
        }

        /// <summary>从满资源需求为零的初态执行一个离散动作，并确认动作仲裁选择预期种类。</summary>
        private static void VerifySingleAction(CharacterRuntimeConfig config, CharacterCommand command, CharacterActionKind expectedAction, string label)
        {
            CharacterState result = CharacterSimulation.Step(CharacterState.CreateInitial(config, FixedVector3.Zero), command, config).State;
            Assert(result.ActionKind == expectedAction, $"Configured {label} input must start {expectedAction}, but started {result.ActionKind}.");
        }

        /// <summary>按真实按下和释放边沿推进四段普通攻击，并在每段结束后等待公共攻击冷却归零。</summary>
        private static void VerifyAttackCombo(CharacterRuntimeConfig config)
        {
            CharacterActionKind[] expectedCombo = { CharacterActionKind.Attack1, CharacterActionKind.Attack2, CharacterActionKind.Attack3, CharacterActionKind.Attack4 };
            CharacterState state = CharacterState.CreateInitial(config, FixedVector3.Zero);
            int nextTick = 0;
            for (int segmentIndex = 0; segmentIndex < expectedCombo.Length; segmentIndex++)
            {
                state = CharacterSimulation.Step(state, new CharacterCommand(nextTick++, 0, 0, CharacterMoveMode.Run, false, false, false, true, true, false, false, false), config).State;
                state = CharacterSimulation.Step(state, new CharacterCommand(nextTick++, 0, 0, CharacterMoveMode.Run, false, false, false, false, false, true, false, false), config).State;
                Assert(state.ActionKind == expectedCombo[segmentIndex], $"Combo segment {segmentIndex + 1} must start {expectedCombo[segmentIndex]}, but started {state.ActionKind}.");
                int safetyTicks = 0;
                while (state.ActionKind != CharacterActionKind.None || state.AttackCooldownRemainingTicks > 0)
                {
                    state = CharacterSimulation.Step(state, CharacterCommand.Empty(nextTick++), config).State;
                    if (++safetyTicks > 1024) throw new InvalidOperationException($"Combo segment {segmentIndex + 1} did not complete inside 1024 ticks.");
                }
            }
        }

        /// <summary>持续按住攻击键直到配置的蓄力阈值，并确认只由模拟核心触发重击。</summary>
        private static void VerifyHeavyAttack(CharacterRuntimeConfig config)
        {
            CharacterState state = CharacterState.CreateInitial(config, FixedVector3.Zero);
            int maximumTicks = config.Combat.HeavyAttackChargeTicks + 2;
            for (int tick = 0; tick < maximumTicks && state.ActionKind != CharacterActionKind.HeavyAttack; tick++) state = CharacterSimulation.Step(state, new CharacterCommand(tick, 0, 0, CharacterMoveMode.Run, false, false, false, tick == 0, true, false, false, false), config).State;
            Assert(state.ActionKind == CharacterActionKind.HeavyAttack, "Holding attack through special_hold_ticks must start HeavyAttack.");
        }

        /// <summary>用 Luban 编译配置让两个实体在前半平面内完成同 Tick 命中，并确认目标扣血和事件身份。</summary>
        private static void VerifyTwoEntityHit(CharacterRuntimeConfig config)
        {
            CharacterActionRuntimeConfig attack = config.GetAction(CharacterActionKind.Attack1);
            Assert(attack.HitRangeRaw > 0L && attack.DamagePermille > 0, "Attack1 requires positive hit range and damage for the server-authoritative acceptance scene.");
            long targetDistance = Math.Max(1L, attack.HitRangeRaw / 2L);
            AuthoritativeBattleWorld world = new AuthoritativeBattleWorld(config);
            world.AddEntity(1, 1, FixedVector3.Zero);
            world.AddEntity(2, 2, new FixedVector3(0L, 0L, targetDistance));
            Assert(world.TrySubmitCommand(1, new CharacterCommand(0, 0, 0, CharacterMoveMode.Run, false, false, false, true, true, false, false, false)), "The world must accept the attack press command.");
            Assert(world.TrySubmitCommand(1, new CharacterCommand(1, 0, 0, CharacterMoveMode.Run, false, false, false, false, false, true, false, false)), "The world must accept the attack release command.");
            WorldEvent resolvedHit = default;
            bool foundHit = false;
            int maximumTicks = attack.TotalTicks + 8;
            for (int tick = 0; tick < maximumTicks && !foundHit; tick++)
            {
                WorldTickResult result = world.Tick();
                for (int eventIndex = 0; eventIndex < result.Events.Count; eventIndex++)
                {
                    WorldEvent worldEvent = result.Events[eventIndex];
                    if (worldEvent.Kind == WorldEventKind.HitResolved && worldEvent.SourceEntityId == 1 && worldEvent.TargetEntityId == 2)
                    {
                        resolvedHit = worldEvent;
                        foundHit = true;
                        break;
                    }
                }
            }
            Assert(foundHit, "Attack1 must resolve a hit against the second entity inside its authored range.");
            Assert(resolvedHit.ActionKind == CharacterActionKind.Attack1 && resolvedHit.ActionId == attack.Id && resolvedHit.Damage > 0, "Resolved hit must preserve Attack1 ID and positive authoritative damage.");
            Assert(world.GetState(2).Hp < config.Stats.MaxHp, "The target HP must decrease in the exact resolved-hit world tick.");
            Assert(world.GetLatestEventsForPlayer(2).Count > 0, "The target player event view must include the incoming hit and damage feedback.");
        }

        /// <summary>确认权威世界能区分可幂等忽略的重传与迟到命令，以及必须按协议错误处理的过远未来命令。</summary>
        private static void VerifyCommandSubmissionClassification(CharacterRuntimeConfig config)
        {
            AuthoritativeBattleWorld world = new AuthoritativeBattleWorld(config);
            world.AddEntity(1, 1, FixedVector3.Zero);
            CharacterCommand tickZero = CharacterCommand.Empty(0);
            Assert(world.SubmitCommand(1, tickZero) == AuthoritativeCommandSubmissionResult.Accepted, "The exact next-tick command must be accepted.");
            Assert(world.SubmitCommand(1, tickZero) == AuthoritativeCommandSubmissionResult.Duplicate, "A queued retransmission for the same future tick must be classified as an idempotent duplicate.");
            world.Tick();
            Assert(world.SubmitCommand(1, tickZero) == AuthoritativeCommandSubmissionResult.Late, "A command for an already simulated tick must be classified as idempotently late.");
            CharacterCommand tooFarFuture = CharacterCommand.Empty(checked(world.WorldTick + config.PredictionHistoryTicks + 1));
            Assert(world.SubmitCommand(1, tooFarFuture) == AuthoritativeCommandSubmissionResult.TooFarInFuture, "A command beyond PredictionHistoryTicks must be classified as a protocol-level future-window violation.");
            Assert(world.SubmitCommand(999, CharacterCommand.Empty(world.WorldTick + 1)) == AuthoritativeCommandSubmissionResult.EntityNotFound, "A command for a removed or unknown entity must be distinguished from malformed client input.");
        }

        /// <summary>启动随机回环端口，验证握手初态、非 Tick 阻塞 Pong 和连续权威快照的实测平均频率。</summary>
        private static async Task VerifyNetworkHostAsync(int characterId, CharacterRuntimeConfig characterConfig)
        {
            using (CancellationTokenSource serverCancellation = new CancellationTokenSource())
            using (BattleServerHost host = new BattleServerHost(0, characterId, characterConfig))
            {
                Task serverTask = host.RunAsync(serverCancellation.Token);
                try
                {
                    int port = await WaitForBoundPortAsync(host, serverTask).ConfigureAwait(false);
                    using (TcpClient client = new TcpClient(AddressFamily.InterNetwork))
                    using (CancellationTokenSource testTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(8)))
                    {
                        client.NoDelay = true;
                        await client.ConnectAsync(IPAddress.Loopback, port, testTimeout.Token).ConfigureAwait(false);
                        using (NetworkStream stream = client.GetStream())
                        {
                            await BattleProtocolCodec.WriteFrameAsync(stream, BattleProtocolCodec.Encode(new ClientHelloMessage(BattleProtocol.Version, characterConfig.ContentHash)), testTimeout.Token).ConfigureAwait(false);
                            DecodedBattleMessage welcomeFrame = BattleProtocolCodec.DecodeFrame(await RequireFrameAsync(stream, testTimeout.Token).ConfigureAwait(false));
                            Assert(welcomeFrame.MessageType == BattleMessageType.ServerWelcome, "A valid v5 client must receive ServerWelcome.");
                            ServerWelcomeMessage welcome = welcomeFrame.GetMessage<ServerWelcomeMessage>();
                            Assert(welcome.CharacterId == characterId && welcome.ConfigHash == characterConfig.ContentHash && welcome.TickRate == 30, "ServerWelcome must expose the loaded Character, hash, and 30 Hz tick rate.");
                            Assert(welcome.InitialState.Tick == welcome.ServerTick, "Dynamically joined Welcome state must align exactly with the current global world tick.");
                            CharacterState delayedClientState = CharacterNetworkMapper.ToCharacterState(welcome.InitialState);
                            byte[] firstLeadPayload = null;
                            byte[] lastLeadPayload = null;
                            for (int leadIndex = 0; leadIndex < BattlePredictionPolicy.ClientInputLeadTicks; leadIndex++)
                            {
                                CharacterCommand leadCommand = CharacterCommand.Empty(delayedClientState.Tick + 1);
                                delayedClientState = CharacterSimulation.Step(delayedClientState, leadCommand, characterConfig).State;
                                byte[] leadPayload = BattleProtocolCodec.Encode(CharacterNetworkMapper.ToClientInputMessage(leadCommand, delayedClientState));
                                firstLeadPayload ??= leadPayload;
                                lastLeadPayload = leadPayload;
                                await BattleProtocolCodec.WriteFrameAsync(stream, leadPayload, testTimeout.Token).ConfigureAwait(false);
                            }
                            await BattleProtocolCodec.WriteFrameAsync(stream, lastLeadPayload, testTimeout.Token).ConfigureAwait(false);
                            await WaitForServerTickAsync(host, welcome.ServerTick + 2, testTimeout.Token).ConfigureAwait(false);
                            await BattleProtocolCodec.WriteFrameAsync(stream, firstLeadPayload, testTimeout.Token).ConfigureAwait(false);
                            int delayedMovementTick = checked(host.ServerTick + BattlePredictionPolicy.ClientInputLeadTicks);
                            while (delayedClientState.Tick < delayedMovementTick)
                            {
                                int commandTick = delayedClientState.Tick + 1;
                                bool isMovementCommand = commandTick == delayedMovementTick;
                                CharacterCommand delayedCommand = isMovementCommand ? new CharacterCommand(commandTick, 0, 1, CharacterMoveMode.Run, false, false, false, false, false, false, false, false) : CharacterCommand.Empty(commandTick);
                                delayedClientState = CharacterSimulation.Step(delayedClientState, delayedCommand, characterConfig).State;
                                await BattleProtocolCodec.WriteFrameAsync(stream, BattleProtocolCodec.Encode(CharacterNetworkMapper.ToClientInputMessage(delayedCommand, delayedClientState)), testTimeout.Token).ConfigureAwait(false);
                            }
                            const int pingSequence = 9001;
                            Stopwatch pingStopwatch = Stopwatch.StartNew();
                            await BattleProtocolCodec.WriteFrameAsync(stream, BattleProtocolCodec.Encode(new ClientPingMessage(pingSequence)), testTimeout.Token).ConfigureAwait(false);
                            bool pongReceived = false;
                            int firstSnapshotTick = -1;
                            int lastSnapshotTick = -1;
                            long firstSnapshotTimestamp = 0L;
                            long lastSnapshotTimestamp = 0L;
                            bool delayedMovementObserved = false;
                            Stopwatch cadenceStopwatch = Stopwatch.StartNew();
                            while (!pongReceived || lastSnapshotTick - firstSnapshotTick < 12)
                            {
                                DecodedBattleMessage frame = BattleProtocolCodec.DecodeFrame(await RequireFrameAsync(stream, testTimeout.Token).ConfigureAwait(false));
                                if (frame.MessageType == BattleMessageType.ServerPong)
                                {
                                    ServerPongMessage pong = frame.GetMessage<ServerPongMessage>();
                                    Assert(pong.Sequence == pingSequence, "Immediate Pong must preserve the client sequence.");
                                    pongReceived = true;
                                    pingStopwatch.Stop();
                                    Assert(pingStopwatch.Elapsed < TimeSpan.FromMilliseconds(500), $"Local Pong must not wait behind simulation work; observed {pingStopwatch.Elapsed.TotalMilliseconds:F1} ms.");
                                }
                                else if (frame.MessageType == BattleMessageType.ServerSnapshot)
                                {
                                    ServerSnapshotMessage snapshot = frame.GetMessage<ServerSnapshotMessage>();
                                    Assert(snapshot.State.Tick == snapshot.ServerTick && snapshot.AcknowledgedClientTick == snapshot.ServerTick, "Every no-input snapshot must carry a full state and reconciliation tick aligned with the global world tick.");
                                    if (firstSnapshotTick < 0)
                                    {
                                        firstSnapshotTick = snapshot.ServerTick;
                                        firstSnapshotTimestamp = cadenceStopwatch.ElapsedTicks;
                                    }
                                    lastSnapshotTick = snapshot.ServerTick;
                                    lastSnapshotTimestamp = cadenceStopwatch.ElapsedTicks;
                                    if (snapshot.ServerTick >= delayedMovementTick && snapshot.State.PositionZ > welcome.InitialState.PositionZ) delayedMovementObserved = true;
                                }
                                else
                                {
                                    throw new InvalidOperationException($"Smoke client received unexpected message {frame.MessageType} after Welcome.");
                                }
                            }
                            double elapsedSeconds = (lastSnapshotTimestamp - firstSnapshotTimestamp) / (double)Stopwatch.Frequency;
                            double measuredHertz = (lastSnapshotTick - firstSnapshotTick) / elapsedSeconds;
                            Assert(measuredHertz >= 24.0d && measuredHertz <= 38.0d, $"Authoritative snapshot cadence must remain close to 30 Hz; measured {measuredHertz:F2} Hz across ticks {firstSnapshotTick}-{lastSnapshotTick}.");
                            Assert(delayedMovementObserved, $"A client that prefilled {BattlePredictionPolicy.ClientInputLeadTicks} future ticks and then delayed input by more than one server tick must remain connected and execute movement tick {delayedMovementTick}.");
                            Console.WriteLine($"SmokeTest network: local Pong={pingStopwatch.Elapsed.TotalMilliseconds:F1} ms, snapshots={measuredHertz:F2} Hz, tick={lastSnapshotTick}.");
                        }
                    }
                }
                finally
                {
                    serverCancellation.Cancel();
                    await serverTask.ConfigureAwait(false);
                }
            }
        }

        /// <summary>等待异步宿主完成端口绑定，并在宿主提前退出时立即传播其异常。</summary>
        private static async Task<int> WaitForBoundPortAsync(BattleServerHost host, Task serverTask)
        {
            using (CancellationTokenSource timeout = new CancellationTokenSource(TimeSpan.FromSeconds(3)))
            {
                while (host.BoundPort == 0)
                {
                    if (serverTask.IsCompleted) await serverTask.ConfigureAwait(false);
                    await Task.Delay(10, timeout.Token).ConfigureAwait(false);
                }
                return host.BoundPort;
            }
        }

        /// <summary>等待真实宿主至少推进到指定 Tick，用于在端到端验收中制造超过一个 Tick 的可重复输入延迟。</summary>
        private static async Task WaitForServerTickAsync(BattleServerHost host, int targetTick, CancellationToken cancellationToken)
        {
            while (host.ServerTick < targetTick) await Task.Delay(2, cancellationToken).ConfigureAwait(false);
        }

        /// <summary>读取一帧并把帧边界关闭转换为含义明确的验收失败。</summary>
        private static async Task<byte[]> RequireFrameAsync(NetworkStream stream, CancellationToken cancellationToken)
        {
            byte[] payload = await BattleProtocolCodec.ReadFrameAsync(stream, cancellationToken).ConfigureAwait(false);
            if (payload == null) throw new InvalidOperationException("Battle server closed the smoke-test connection before the expected frame arrived.");
            return payload;
        }

        /// <summary>在验收条件不成立时抛出包含具体不变量的异常。</summary>
        private static void Assert(bool condition, string message)
        {
            if (!condition) throw new InvalidOperationException(message);
        }
    }
}
