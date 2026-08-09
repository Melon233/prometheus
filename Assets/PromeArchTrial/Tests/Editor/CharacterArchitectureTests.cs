using System.IO;
using NUnit.Framework;
using PromeArchTrial.Core.Networking;
using PromeArchTrial.Game.Character;
using PromeArchTrial.Game.Networking;
using PromeArchTrial.Game.World;

namespace PromeArchTrial.Tests.Editor
{
    /// <summary>
    /// 覆盖角色核心、权威世界、预测重放和 Protobuf v5 边界的快速编辑器回归测试。
    /// </summary>
    public sealed class CharacterArchitectureTests
    {
        /// <summary>执行共享权威世界的完整行为探针，包括动态接入、缺包、命中、暴击和重复哈希。</summary>
        [Test]
        public void AuthoritativeWorldBehaviorProbePasses()
        {
            AuthoritativeBattleWorldBehaviorProbe.RunAll();
        }

        /// <summary>验证客户端收到权威完整状态后会恢复该状态并严格重放确认 Tick 之后的全部命令。</summary>
        [Test]
        public void PredictionRestoresAuthorityAndReplaysPendingCommands()
        {
            CharacterRuntimeConfig config = LegacyYefaConfigFactory.Create();
            CharacterPredictionController prediction = new CharacterPredictionController(config);
            CharacterPredictionFrame tickZero = prediction.Predict(MoveCommand(0));
            CharacterPredictionFrame tickOne = prediction.Predict(MoveCommand(1));
            prediction.Predict(MoveCommand(2));
            CharacterState authoritativeTickZero = tickZero.StateAfterTick;
            CharacterReconciliationResult result = prediction.Reconcile(0, authoritativeTickZero);
            Assert.That(result.Accepted, Is.True);
            Assert.That(result.ReplayedCommandCount, Is.EqualTo(2));
            CharacterState independentlyReplayed = CharacterSimulation.Step(CharacterSimulation.Step(authoritativeTickZero, MoveCommand(1), config).State, MoveCommand(2), config).State;
            Assert.That(prediction.CurrentState, Is.EqualTo(independentlyReplayed));
            Assert.That(tickOne.StateAfterTick.Tick, Is.EqualTo(1));
        }

        /// <summary>验证同一固定 Tick 内同时出现按下和释放边沿时会立即触发第一段轻击，而不会被蓄力分支吞掉。</summary>
        [Test]
        public void SameTickAttackTapStartsFirstLightAttack()
        {
            CharacterRuntimeConfig config = LegacyYefaConfigFactory.Create();
            CharacterTickResult result = CharacterSimulation.Step(CharacterState.CreateInitial(config, FixedVector3.Zero), TapAttackCommand(0), config);
            Assert.That(result.State.ActionKind, Is.EqualTo(CharacterActionKind.Attack1));
            Assert.That(result.State.AttackChargeTicks, Is.Zero);
            Assert.That(result.State.LightAttackBufferRemainingTicks, Is.Zero);
            Assert.That(result.State.NextAttackComboIndex, Is.EqualTo(1));
        }

        /// <summary>验证旧动作结束 Tick 收到的轻击释放会保留到下一 Tick，并严格接续第二段而不是丢失输入。</summary>
        [Test]
        public void LightAttackReleasedOnActionEndTickIsBufferedForNextCombo()
        {
            CharacterRuntimeConfig config = LegacyYefaConfigFactory.Create();
            CharacterState state = CharacterSimulation.Step(CharacterState.CreateInitial(config, FixedVector3.Zero), TapAttackCommand(0), config).State;
            state = AdvanceWithEmptyCommands(state, config, 13);
            CharacterTickResult bufferedResult = CharacterSimulation.Step(state, TapAttackCommand(14), config);
            Assert.That(bufferedResult.State.ActionKind, Is.EqualTo(CharacterActionKind.None));
            Assert.That(bufferedResult.State.LightAttackBufferRemainingTicks, Is.EqualTo(config.Combat.AttackBufferTicks));
            CharacterState consumedState = CharacterSimulation.Step(bufferedResult.State, EmptyCommand(15), config).State;
            Assert.That(consumedState.ActionKind, Is.EqualTo(CharacterActionKind.Attack2));
            Assert.That(consumedState.LightAttackBufferRemainingTicks, Is.Zero);
        }

        /// <summary>验证过早写入且在动作结束前耗尽的轻击缓冲不会延迟触发幽灵攻击。</summary>
        [Test]
        public void ExpiredLightAttackBufferDoesNotTriggerAfterActionEnds()
        {
            CharacterRuntimeConfig config = LegacyYefaConfigFactory.Create();
            CharacterState state = CharacterSimulation.Step(CharacterState.CreateInitial(config, FixedVector3.Zero), TapAttackCommand(0), config).State;
            state = CharacterSimulation.Step(state, TapAttackCommand(1), config).State;
            state = AdvanceWithEmptyCommands(state, config, 14);
            Assert.That(state.ActionKind, Is.EqualTo(CharacterActionKind.None));
            Assert.That(state.LightAttackBufferRemainingTicks, Is.Zero);
            state = CharacterSimulation.Step(state, EmptyCommand(15), config).State;
            Assert.That(state.ActionKind, Is.EqualTo(CharacterActionKind.None));
        }

        /// <summary>验证在六十 Tick 接续窗口内依次点按会稳定产生一到四段普攻并在第四段后回到零索引。</summary>
        [Test]
        public void FourTimedAttackTapsAdvanceThroughAllComboStages()
        {
            CharacterRuntimeConfig config = LegacyYefaConfigFactory.Create();
            CharacterState state = CharacterSimulation.Step(CharacterState.CreateInitial(config, FixedVector3.Zero), TapAttackCommand(0), config).State;
            Assert.That(state.ActionKind, Is.EqualTo(CharacterActionKind.Attack1));
            state = AdvanceWithEmptyCommands(state, config, 14);
            state = CharacterSimulation.Step(state, TapAttackCommand(15), config).State;
            Assert.That(state.ActionKind, Is.EqualTo(CharacterActionKind.Attack2));
            state = AdvanceWithEmptyCommands(state, config, 29);
            state = CharacterSimulation.Step(state, TapAttackCommand(30), config).State;
            Assert.That(state.ActionKind, Is.EqualTo(CharacterActionKind.Attack3));
            state = AdvanceWithEmptyCommands(state, config, 47);
            state = CharacterSimulation.Step(state, TapAttackCommand(48), config).State;
            Assert.That(state.ActionKind, Is.EqualTo(CharacterActionKind.Attack4));
            Assert.That(state.NextAttackComboIndex, Is.Zero);
        }

        /// <summary>验证在蓄力阈值前一 Tick 释放会选择轻击，释放 Tick 本身不会被错误计入重击阈值。</summary>
        [Test]
        public void ReleaseBeforeHeavyThresholdStartsLightAttack()
        {
            CharacterRuntimeConfig config = LegacyYefaConfigFactory.Create();
            CharacterState state = CharacterState.CreateInitial(config, FixedVector3.Zero);
            for (int tick = 0; tick < config.Combat.HeavyAttackChargeTicks - 1; tick++) state = CharacterSimulation.Step(state, HoldAttackCommand(tick, tick == 0), config).State;
            Assert.That(state.AttackChargeTicks, Is.EqualTo(config.Combat.HeavyAttackChargeTicks - 1));
            state = CharacterSimulation.Step(state, ReleaseAttackCommand(config.Combat.HeavyAttackChargeTicks - 1), config).State;
            Assert.That(state.ActionKind, Is.EqualTo(CharacterActionKind.Attack1));
            Assert.That(state.AttackHoldConsumed, Is.False);
        }

        /// <summary>验证持续按住达到配置阈值时会恰好启动一次重击并把本次物理按住标记为已消费。</summary>
        [Test]
        public void HoldThroughHeavyThresholdStartsConsumedHeavyAttack()
        {
            CharacterRuntimeConfig config = LegacyYefaConfigFactory.Create();
            CharacterState state = CharacterState.CreateInitial(config, FixedVector3.Zero);
            for (int tick = 0; tick < config.Combat.HeavyAttackChargeTicks; tick++) state = CharacterSimulation.Step(state, HoldAttackCommand(tick, tick == 0), config).State;
            Assert.That(state.ActionKind, Is.EqualTo(CharacterActionKind.HeavyAttack));
            Assert.That(state.AttackHoldConsumed, Is.True);
            Assert.That(state.AttackChargeTicks, Is.Zero);
        }

        /// <summary>验证一次物理按住在重击结束后不会自动重复，且只有释放再按下才能重新触发下一次重击。</summary>
        [Test]
        public void ContinuousHoldCannotRepeatHeavyAttackUntilReleasedAndPressedAgain()
        {
            CharacterRuntimeConfig config = LegacyYefaConfigFactory.Create();
            CharacterState state = CharacterState.CreateInitial(config, FixedVector3.Zero);
            int heavyStartCount = 0;
            for (int tick = 0; tick <= 90; tick++)
            {
                CharacterTickResult result = CharacterSimulation.Step(state, HoldAttackCommand(tick, tick == 0), config);
                heavyStartCount += CountStartedActions(result, CharacterActionKind.HeavyAttack);
                state = result.State;
            }
            Assert.That(heavyStartCount, Is.EqualTo(1));
            Assert.That(state.AttackHoldConsumed, Is.True);
            state = CharacterSimulation.Step(state, ReleaseAttackCommand(91), config).State;
            Assert.That(state.AttackHoldConsumed, Is.False);
            for (int tick = 92; tick < 92 + config.Combat.HeavyAttackChargeTicks; tick++)
            {
                CharacterTickResult result = CharacterSimulation.Step(state, HoldAttackCommand(tick, tick == 92), config);
                heavyStartCount += CountStartedActions(result, CharacterActionKind.HeavyAttack);
                state = result.State;
            }
            Assert.That(heavyStartCount, Is.EqualTo(2));
            Assert.That(state.AttackHoldConsumed, Is.True);
        }

        /// <summary>验证移动与静止普攻在起手 Tick 冻结不同动画变体，状态映射和预测恢复后仍保持确定性选择。</summary>
        [Test]
        public void MovingAttackVariantIsFrozenAndPreservedAcrossRollbackStateMapping()
        {
            CharacterRuntimeConfig config = LegacyYefaConfigFactory.Create();
            CharacterState initialState = CharacterState.CreateInitial(config, FixedVector3.Zero);
            CharacterState staticAttack = CharacterSimulation.Step(initialState, TapAttackCommand(0), config).State;
            CharacterState movingAttack = CharacterSimulation.Step(initialState, TapAttackCommand(0, 1, 0), config).State;
            Assert.That(staticAttack.UsesMovingAttackVariant, Is.False);
            Assert.That(movingAttack.UsesMovingAttackVariant, Is.True);
            CharacterState mappedState = CharacterNetworkMapper.ToCharacterState(CharacterNetworkMapper.ToNetworkState(movingAttack));
            Assert.That(mappedState, Is.EqualTo(movingAttack));
            CharacterPredictionController prediction = new CharacterPredictionController(config);
            prediction.Predict(TapAttackCommand(0, 1, 0));
            prediction.Predict(EmptyCommand(1));
            CharacterReconciliationResult reconciliation = prediction.Reconcile(0, movingAttack);
            Assert.That(reconciliation.Accepted, Is.True);
            Assert.That(prediction.CurrentState.UsesMovingAttackVariant, Is.True);
            CharacterState endedAttack = AdvanceWithEmptyCommands(movingAttack, config, 14);
            Assert.That(endedAttack.UsesMovingAttackVariant, Is.False);
        }

        /// <summary>验证 Protobuf v5 会往返保存按住消费、轻击缓冲和移动普攻变体三个新增回滚字段。</summary>
        [Test]
        public void ProtobufV5PreservesAttackInputRollbackFields()
        {
            CharacterRuntimeConfig config = LegacyYefaConfigFactory.Create();
            CharacterState movingAttack = CharacterSimulation.Step(CharacterState.CreateInitial(config, FixedVector3.Zero), TapAttackCommand(0, 1, 0), config).State;
            CharacterNetworkState movingNetworkState = CharacterNetworkMapper.ToNetworkState(movingAttack);
            ServerSnapshotMessage movingSnapshot = BattleProtocolCodec.DecodeServerSnapshot(BattleProtocolCodec.Encode(new ServerSnapshotMessage(0, 0, movingNetworkState)));
            Assert.That(CharacterNetworkMapper.ToCharacterState(movingSnapshot.State), Is.EqualTo(movingAttack));
            CharacterState bufferedState = AdvanceWithEmptyCommands(movingAttack, config, 13);
            bufferedState = CharacterSimulation.Step(bufferedState, TapAttackCommand(14), config).State;
            ServerSnapshotMessage bufferedSnapshot = BattleProtocolCodec.DecodeServerSnapshot(BattleProtocolCodec.Encode(new ServerSnapshotMessage(14, 14, CharacterNetworkMapper.ToNetworkState(bufferedState))));
            Assert.That(CharacterNetworkMapper.ToCharacterState(bufferedSnapshot.State), Is.EqualTo(bufferedState));
            CharacterState heavyState = CharacterState.CreateInitial(config, FixedVector3.Zero);
            for (int tick = 0; tick < config.Combat.HeavyAttackChargeTicks; tick++) heavyState = CharacterSimulation.Step(heavyState, HoldAttackCommand(tick, tick == 0), config).State;
            ServerSnapshotMessage heavySnapshot = BattleProtocolCodec.DecodeServerSnapshot(BattleProtocolCodec.Encode(new ServerSnapshotMessage(heavyState.Tick, heavyState.Tick, CharacterNetworkMapper.ToNetworkState(heavyState))));
            Assert.That(CharacterNetworkMapper.ToCharacterState(heavySnapshot.State), Is.EqualTo(heavyState));
        }

        /// <summary>验证欢迎初态和权威事件在 Protobuf v5 往返后保持全部关键字段。</summary>
        [Test]
        public void ProtobufV5PreservesWelcomeBaselineAndBattleEvents()
        {
            CharacterRuntimeConfig config = LegacyYefaConfigFactory.Create();
            CharacterState state = CharacterState.CreateInitial(config, FixedVector3.Zero);
            CharacterNetworkState networkState = CharacterNetworkMapper.ToNetworkState(state);
            ServerWelcomeMessage welcome = new ServerWelcomeMessage(1, 11, 1001, state.Tick, config.TickRate, config.ContentHash, networkState);
            ServerWelcomeMessage decodedWelcome = BattleProtocolCodec.DecodeServerWelcome(BattleProtocolCodec.Encode(welcome));
            Assert.That(decodedWelcome.EntityId, Is.EqualTo(11));
            Assert.That(decodedWelcome.InitialState.Tick, Is.EqualTo(state.Tick));
            BattleEventMessage battleEvent = new BattleEventMessage(BattleEventKind.HitResolved, 11, 12, 3, 2, 0, (int)CharacterActionKind.Attack1, 2001, 17, true);
            ServerSnapshotMessage snapshot = new ServerSnapshotMessage(3, state.Tick, networkState, new[] { battleEvent });
            ServerSnapshotMessage decodedSnapshot = BattleProtocolCodec.DecodeServerSnapshot(BattleProtocolCodec.Encode(snapshot));
            Assert.That(decodedSnapshot.Events.Count, Is.EqualTo(1));
            Assert.That(decodedSnapshot.Events[0].Value, Is.EqualTo(17));
            Assert.That(decodedSnapshot.Events[0].IsCritical, Is.True);
        }

        /// <summary>验证协议边界拒绝尚未定义的输入按钮，防止双端对同一比特产生不同解释。</summary>
        [Test]
        public void ProtobufV5RejectsUnknownInputButtons()
        {
            CharacterRuntimeConfig config = LegacyYefaConfigFactory.Create();
            CharacterNetworkState state = CharacterNetworkMapper.ToNetworkState(CharacterState.CreateInitial(config, FixedVector3.Zero));
            ClientInputMessage invalidInput = new ClientInputMessage(1, 0, 0, (int)CharacterMoveMode.Run, (CharacterInputButtons)0x80000000U, state);
            Assert.Throws<InvalidDataException>(() => BattleProtocolCodec.DecodeFrame(BattleProtocolCodec.Encode(invalidInput)));
        }

        /// <summary>创建一个沿 X 轴持续跑步的确定性测试命令。</summary>
        private static CharacterCommand MoveCommand(int tick)
        {
            return new CharacterCommand(tick, 1, 0, CharacterMoveMode.Run, false, false, false, false, false, false, false, false);
        }

        /// <summary>创建在同一固定 Tick 内完成按下和释放的轻击命令，可选移动方向用于验证移动普攻变体。</summary>
        private static CharacterCommand TapAttackCommand(int tick, sbyte moveX = 0, sbyte moveZ = 0)
        {
            return new CharacterCommand(tick, moveX, moveZ, CharacterMoveMode.Run, false, false, false, true, false, true, false, false);
        }

        /// <summary>创建保持攻击键按下的蓄力命令，首个 Tick 可额外携带按下边沿。</summary>
        private static CharacterCommand HoldAttackCommand(int tick, bool pressed)
        {
            return new CharacterCommand(tick, 0, 0, CharacterMoveMode.Run, false, false, false, pressed, true, false, false, false);
        }

        /// <summary>创建只包含攻击键释放边沿的命令。</summary>
        private static CharacterCommand ReleaseAttackCommand(int tick)
        {
            return new CharacterCommand(tick, 0, 0, CharacterMoveMode.Run, false, false, false, false, false, true, false, false);
        }

        /// <summary>创建指定 Tick 的完全中性命令。</summary>
        private static CharacterCommand EmptyCommand(int tick)
        {
            return CharacterCommand.Empty(tick);
        }

        /// <summary>从当前状态的下一 Tick 起持续提交中性命令，直到包含指定结束 Tick。</summary>
        private static CharacterState AdvanceWithEmptyCommands(CharacterState state, CharacterRuntimeConfig config, int inclusiveEndTick)
        {
            for (int tick = state.Tick + 1; tick <= inclusiveEndTick; tick++) state = CharacterSimulation.Step(state, EmptyCommand(tick), config).State;
            return state;
        }

        /// <summary>统计单 Tick 结果中指定动作的开始事件数量，用于证明重击不会在一次物理按住期间重复。</summary>
        private static int CountStartedActions(CharacterTickResult result, CharacterActionKind actionKind)
        {
            int count = 0;
            for (int index = 0; index < result.Events.Count; index++)
            {
                CharacterEvent characterEvent = result.Events[index];
                if (characterEvent.Type == CharacterEventType.ActionStarted && characterEvent.ActionKind == actionKind) count++;
            }
            return count;
        }
    }
}
