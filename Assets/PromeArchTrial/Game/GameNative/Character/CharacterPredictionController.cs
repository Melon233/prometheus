using System;
using System.Collections.Generic;

namespace PromeArchTrial.Game.Character
{
    /// <summary>
    /// 保存一个尚未被服务器确认的本地角色命令、预测后完整状态和本次预测事件。
    /// </summary>
    public sealed class CharacterPredictionFrame
    {
        /// <summary>创建一个不可变本地预测历史帧。</summary>
        public CharacterPredictionFrame(CharacterCommand command, CharacterTickResult result)
        {
            Command = command;
            if (result == null) throw new ArgumentNullException(nameof(result));
            StateAfterTick = result.State;
            Events = result.Events;
        }

        /// <summary>获取该历史帧执行的完整玩家命令。</summary>
        public CharacterCommand Command { get; }

        /// <summary>获取该命令预测完成后的完整角色状态。</summary>
        public CharacterState StateAfterTick { get; }

        /// <summary>获取该次本地预测产生的只读领域事件。</summary>
        public IReadOnlyList<CharacterEvent> Events { get; }
    }

    /// <summary>
    /// 描述一次完整状态预测对账、权威恢复和未确认命令重放的结果。
    /// </summary>
    public readonly struct CharacterReconciliationResult
    {
        /// <summary>创建一次角色预测对账结果。</summary>
        public CharacterReconciliationResult(bool accepted, bool compared, bool corrected, bool positionThresholdExceeded, double positionErrorUnits, int acknowledgedTick, int replayedCommandCount, ulong predictedStateHash, ulong authoritativeStateHash, ulong finalStateHash)
        {
            Accepted = accepted;
            Compared = compared;
            Corrected = corrected;
            PositionThresholdExceeded = positionThresholdExceeded;
            PositionErrorUnits = positionErrorUnits;
            AcknowledgedTick = acknowledgedTick;
            ReplayedCommandCount = replayedCommandCount;
            PredictedStateHash = predictedStateHash;
            AuthoritativeStateHash = authoritativeStateHash;
            FinalStateHash = finalStateHash;
        }

        /// <summary>获取该权威状态是否未过期并已被预测器接受。</summary>
        public bool Accepted { get; }

        /// <summary>获取历史中是否存在相同 Tick 的预测完整状态可供比较。</summary>
        public bool Compared { get; }

        /// <summary>获取相同 Tick 的预测完整状态是否与权威完整状态存在任一差异。</summary>
        public bool Corrected { get; }

        /// <summary>获取位置误差是否严格超过配置的预测修正阈值。</summary>
        public bool PositionThresholdExceeded { get; }

        /// <summary>获取相同 Tick 预测位置与权威位置之间仅供诊断展示的世界单位距离。</summary>
        public double PositionErrorUnits { get; }

        /// <summary>获取本次权威状态确认的客户端命令 Tick。</summary>
        public int AcknowledgedTick { get; }

        /// <summary>获取恢复权威状态后实际重放的未确认命令数量。</summary>
        public int ReplayedCommandCount { get; }

        /// <summary>获取相同 Tick 历史预测状态的稳定哈希，无法比较时为零。</summary>
        public ulong PredictedStateHash { get; }

        /// <summary>获取服务器权威完整状态的稳定哈希。</summary>
        public ulong AuthoritativeStateHash { get; }

        /// <summary>获取权威恢复并重放全部未确认命令后的本地最终状态哈希。</summary>
        public ulong FinalStateHash { get; }
    }

    /// <summary>
    /// 在本地立即预测完整角色逻辑，并在收到权威完整状态后恢复该状态及重放确认 Tick 之后的全部命令。
    /// </summary>
    public sealed class CharacterPredictionController
    {
        private readonly CharacterRuntimeConfig config;
        private readonly SortedDictionary<int, CharacterPredictionFrame> history = new SortedDictionary<int, CharacterPredictionFrame>();
        private readonly List<CharacterCommand> commandBuffer = new List<CharacterCommand>();
        private CharacterState currentState;
        private int lastPredictedTick;
        private int lastAcknowledgedTick;

        /// <summary>使用配置创建一个位于世界原点的角色预测器。</summary>
        public CharacterPredictionController(CharacterRuntimeConfig config) : this(config, CharacterState.CreateInitial(config, FixedVector3.Zero))
        {
        }

        /// <summary>使用指定初始或权威完整状态创建角色预测器。</summary>
        public CharacterPredictionController(CharacterRuntimeConfig config, CharacterState initialState)
        {
            this.config = config ?? throw new ArgumentNullException(nameof(config));
            if (initialState.Hp > config.Stats.MaxHp) throw new ArgumentOutOfRangeException(nameof(initialState), "Initial HP exceeds the configured maximum.");
            if (initialState.CoreEnergy > config.Stats.MaxCoreEnergy || initialState.UltimateEnergy > config.Stats.MaxUltimateEnergy) throw new ArgumentOutOfRangeException(nameof(initialState), "Initial energy exceeds the configured maximum.");
            currentState = initialState;
            lastPredictedTick = initialState.Tick;
            lastAcknowledgedTick = initialState.Tick;
        }

        /// <summary>获取权威恢复和命令重放之后的当前本地完整状态。</summary>
        public CharacterState CurrentState => currentState;

        /// <summary>获取尚未被服务器确认且可用于重放的命令数量。</summary>
        public int PendingCommandCount => history.Count;

        /// <summary>获取最近一次已经接受的服务器确认 Tick。</summary>
        public int LastAcknowledgedTick => lastAcknowledgedTick;

        /// <summary>立即预测一个严格连续的本地命令并保存完整历史帧。</summary>
        public CharacterPredictionFrame Predict(CharacterCommand command)
        {
            if (command.Tick != lastPredictedTick + 1) throw new InvalidOperationException($"Prediction command tick {command.Tick} must immediately follow tick {lastPredictedTick}.");
            if (history.Count >= config.PredictionHistoryTicks) throw new InvalidOperationException("Pending prediction history is full; pause local simulation until an authoritative snapshot is received instead of dropping commands required for correct replay.");
            CharacterTickResult result = CharacterSimulation.Step(currentState, command, config);
            CharacterPredictionFrame frame = new CharacterPredictionFrame(command, result);
            history.Add(command.Tick, frame);
            currentState = frame.StateAfterTick;
            lastPredictedTick = command.Tick;
            return frame;
        }

        /// <summary>恢复服务器权威完整状态，丢弃已确认命令，并从该状态严格重放确认 Tick 之后的全部本地命令。</summary>
        public CharacterReconciliationResult Reconcile(int acknowledgedTick, CharacterState authoritativeState)
        {
            if (authoritativeState.Tick != acknowledgedTick) throw new ArgumentException("Authoritative state tick must equal the acknowledged command tick.", nameof(authoritativeState));
            if (acknowledgedTick < lastAcknowledgedTick) return new CharacterReconciliationResult(false, false, false, false, 0.0d, acknowledgedTick, 0, 0UL, authoritativeState.StableHash, currentState.StableHash);
            bool compared = history.TryGetValue(acknowledgedTick, out CharacterPredictionFrame predictedFrame);
            CharacterState predictedState = compared ? predictedFrame.StateAfterTick : default;
            if (!compared && currentState.Tick == acknowledgedTick)
            {
                predictedState = currentState;
                compared = true;
            }
            ulong predictedHash = compared ? predictedState.StableHash : 0UL;
            double positionError = compared ? predictedState.Position.DistanceUnitsTo(authoritativeState.Position) : 0.0d;
            bool thresholdExceeded = compared && predictedState.Position.IsDistanceGreaterThan(authoritativeState.Position, config.Locomotion.ReconciliationDistanceRaw);
            bool corrected = compared && predictedState != authoritativeState;
            CollectUnacknowledgedCommands(acknowledgedTick);
            currentState = authoritativeState;
            history.Clear();
            int replayedCount = ReplayBufferedCommands();
            lastAcknowledgedTick = acknowledgedTick;
            lastPredictedTick = currentState.Tick;
            return new CharacterReconciliationResult(true, compared, corrected, thresholdExceeded, positionError, acknowledgedTick, replayedCount, predictedHash, authoritativeState.StableHash, currentState.StableHash);
        }

        /// <summary>以一个新的完整权威状态重置预测器并清空全部未确认命令，适用于重连或切换角色。</summary>
        public void Reset(CharacterState authoritativeState)
        {
            if (authoritativeState.Hp > config.Stats.MaxHp) throw new ArgumentOutOfRangeException(nameof(authoritativeState), "Authoritative HP exceeds the configured maximum.");
            history.Clear();
            commandBuffer.Clear();
            currentState = authoritativeState;
            lastPredictedTick = authoritativeState.Tick;
            lastAcknowledgedTick = authoritativeState.Tick;
        }

        /// <summary>按 Tick 顺序复制确认点之后的全部命令并校验它们可以从权威状态连续重放。</summary>
        private void CollectUnacknowledgedCommands(int acknowledgedTick)
        {
            commandBuffer.Clear();
            foreach (KeyValuePair<int, CharacterPredictionFrame> pair in history)
            {
                if (pair.Key > acknowledgedTick) commandBuffer.Add(pair.Value.Command);
            }
            int expectedTick = acknowledgedTick + 1;
            for (int index = 0; index < commandBuffer.Count; index++)
            {
                if (commandBuffer[index].Tick != expectedTick) throw new InvalidOperationException($"Prediction history cannot replay tick {commandBuffer[index].Tick} after authoritative tick {acknowledgedTick}; expected tick {expectedTick}.");
                expectedTick++;
            }
        }

        /// <summary>从已经恢复的权威状态重放缓存命令并重建每个 Tick 的完整预测历史。</summary>
        private int ReplayBufferedCommands()
        {
            int replayedCount = 0;
            for (int index = 0; index < commandBuffer.Count; index++)
            {
                CharacterCommand command = commandBuffer[index];
                CharacterTickResult result = CharacterSimulation.Step(currentState, command, config);
                CharacterPredictionFrame frame = new CharacterPredictionFrame(command, result);
                history.Add(command.Tick, frame);
                currentState = frame.StateAfterTick;
                replayedCount++;
            }
            commandBuffer.Clear();
            return replayedCount;
        }
    }
}
