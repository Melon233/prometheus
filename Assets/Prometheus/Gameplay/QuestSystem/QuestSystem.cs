using System;
using System.Collections.Generic;
using UnityEngine;
using Xuan.Prometheus.Npc;

namespace Xuan.Prometheus.Quest
{
    /// <summary>纯逻辑任务系统：管理配置、状态转换、事件幂等和目标进度。</summary>
    internal sealed class QuestSystem : XSystem, IQuestSystem
    {
        private readonly Dictionary<string, QuestDefinition> definitions = new Dictionary<string, QuestDefinition>();
        private readonly Dictionary<string, QuestRuntimeState> states = new Dictionary<string, QuestRuntimeState>();
        private QuestNpcAdapter npcAdapter;
        private INpcSystem npcSystem;

        /// <summary>任务状态变化事件，供 UI、存档和网络适配器消费。</summary>
        public event Action<QuestStateChanged> StateChanged;

        /// <summary>任务目标进度变化事件。</summary>
        public event Action<QuestObjectiveProgressChanged> ObjectiveProgressChanged;

        /// <summary>任务完成奖励通知；背包、货币或经验系统负责实际写入。</summary>
        public event Action<QuestRewardGranted> RewardGranted;

        /// <summary>初始化 NPC 领域事件适配器。</summary>
        public override void AfterNew()
        {
            if (Core.Gameplay.TryGetSystem(out npcSystem))
            {
                npcAdapter = new QuestNpcAdapter(this);
                npcSystem.InteractionRequested += npcAdapter.OnInteractionRequested;
            }
        }

        /// <summary>批量注册任务目录，适合场景或存档加载完成后的初始化阶段。</summary>
        public void RegisterCatalog(QuestCatalog catalog)
        {
            if (catalog == null) throw new ArgumentNullException(nameof(catalog));
            catalog.Validate();
            for (int index = 0; index < catalog.Definitions.Count; index++) RegisterDefinition(catalog.Definitions[index]);
        }

        /// <summary>注册一个任务配置并创建对应的运行时状态。</summary>
        public void RegisterDefinition(QuestDefinition definition)
        {
            if (definition == null) throw new ArgumentNullException(nameof(definition));
            definition.Validate();
            if (definitions.ContainsKey(definition.QuestId)) throw new InvalidOperationException($"Quest '{definition.QuestId}' is already registered.");
            definitions.Add(definition.QuestId, definition);
            QuestRuntimeState state = new QuestRuntimeState(definition.QuestId);
            state.SetState(QuestState.Available);
            states.Add(definition.QuestId, state);
        }

        /// <summary>尝试接取任务；前置任务必须完成且任务当前可接取。</summary>
        public bool TryAccept(string questId)
        {
            QuestDefinition definition = GetDefinition(questId);
            QuestRuntimeState state = states[questId];
            if (state.State != QuestState.Available || !ArePrerequisitesCompleted(definition)) return false;
            SetState(state, QuestState.Accepted);
            SetState(state, QuestState.Active);
            return true;
        }

        /// <summary>放弃一个进行中的任务，并保留其进度供后续策略读取。</summary>
        public bool Abandon(string questId)
        {
            QuestRuntimeState state = GetState(questId);
            if (state.State != QuestState.Accepted && state.State != QuestState.Active) return false;
            SetState(state, QuestState.Abandoned);
            return true;
        }

        /// <summary>将进行中的任务标记为失败。</summary>
        public bool Fail(string questId)
        {
            QuestRuntimeState state = GetState(questId);
            if (state.State != QuestState.Accepted && state.State != QuestState.Active) return false;
            SetState(state, QuestState.Failed);
            return true;
        }

        /// <summary>将进行中的任务标记为过期。</summary>
        public bool Expire(string questId)
        {
            QuestRuntimeState state = GetState(questId);
            if (state.State != QuestState.Accepted && state.State != QuestState.Active) return false;
            SetState(state, QuestState.Expired);
            return true;
        }

        /// <summary>向所有活动任务广播事件，并按 EventId 保证重复事件不会重复计数。</summary>
        public void PublishEvent(QuestEvent questEvent)
        {
            foreach (KeyValuePair<string, QuestRuntimeState> pair in states)
            {
                QuestRuntimeState state = pair.Value;
                if (state.State != QuestState.Accepted && state.State != QuestState.Active || state.HasProcessed(questEvent.EventId)) continue;
                QuestDefinition definition = definitions[pair.Key];
                bool matched = false;
                for (int index = 0; index < definition.Objectives.Count; index++)
                {
                    QuestObjectiveDefinition objective = definition.Objectives[index];
                    if (objective.EventType != questEvent.Type || !string.Equals(objective.TargetId, questEvent.TargetId, StringComparison.Ordinal)) continue;
                    QuestObjectiveProgress progress = state.GetOrCreateProgress(objective.ObjectiveId);
                    int previous = progress.Amount;
                    progress.SetAmount(Math.Min(objective.RequiredAmount, previous + Math.Max(0, questEvent.Amount)));
                    matched |= progress.Amount != previous;
                    if (progress.Amount != previous) ObjectiveProgressChanged?.Invoke(new QuestObjectiveProgressChanged(state.QuestId, objective.ObjectiveId, previous, progress.Amount, objective.RequiredAmount));
                }
                if (!matched) continue;
                state.MarkProcessed(questEvent.EventId);
                if (AreObjectivesCompleted(definition, state)) Complete(state, definition);
            }
        }

        /// <summary>读取任务状态；未注册任务直接抛出配置错误。</summary>
        public QuestRuntimeState GetState(string questId)
        {
            if (!states.TryGetValue(questId, out QuestRuntimeState state)) throw new KeyNotFoundException($"Quest '{questId}' is not registered.");
            return state;
        }

        /// <summary>导出当前任务状态、目标进度和幂等事件 ID，供存档系统持久化。</summary>
        public string CaptureSnapshot()
        {
            return JsonUtility.ToJson(new QuestSystemSnapshot(states.Values));
        }

        /// <summary>恢复已注册任务的运行时状态；未知任务会被拒绝，避免存档污染当前配置。</summary>
        public void RestoreSnapshot(string json)
        {
            if (string.IsNullOrWhiteSpace(json)) throw new ArgumentException("Quest snapshot JSON cannot be empty.", nameof(json));
            QuestSystemSnapshot snapshot = JsonUtility.FromJson<QuestSystemSnapshot>(json);
            if (snapshot == null) throw new InvalidOperationException("Quest snapshot JSON is invalid.");
            for (int index = 0; index < snapshot.States.Count; index++) if (!definitions.ContainsKey(snapshot.States[index].QuestId)) throw new InvalidOperationException($"Quest snapshot references unknown quest '{snapshot.States[index].QuestId}'.");
            states.Clear();
            for (int index = 0; index < snapshot.States.Count; index++) states[snapshot.States[index].QuestId] = snapshot.States[index];
        }

        /// <summary>释放 NPC 事件订阅和运行时引用。</summary>
        public override void Dispose()
        {
            definitions.Clear();
            states.Clear();
            StateChanged = null;
            ObjectiveProgressChanged = null;
            RewardGranted = null;
            if (npcSystem != null && npcAdapter != null) npcSystem.InteractionRequested -= npcAdapter.OnInteractionRequested;
            npcAdapter = null;
            npcSystem = null;
        }

        private QuestDefinition GetDefinition(string questId)
        {
            if (!definitions.TryGetValue(questId, out QuestDefinition definition)) throw new KeyNotFoundException($"Quest '{questId}' is not registered.");
            return definition;
        }

        private bool ArePrerequisitesCompleted(QuestDefinition definition)
        {
            for (int index = 0; index < definition.PrerequisiteQuestIds.Count; index++) if (!states.TryGetValue(definition.PrerequisiteQuestIds[index], out QuestRuntimeState prerequisite) || prerequisite.State != QuestState.Completed) return false;
            return true;
        }

        private static bool AreObjectivesCompleted(QuestDefinition definition, QuestRuntimeState state)
        {
            for (int index = 0; index < definition.Objectives.Count; index++) if (state.GetOrCreateProgress(definition.Objectives[index].ObjectiveId).Amount < definition.Objectives[index].RequiredAmount) return false;
            return true;
        }

        private void SetState(QuestRuntimeState state, QuestState next)
        {
            QuestState previous = state.State;
            if (previous == next) return;
            state.SetState(next);
            StateChanged?.Invoke(new QuestStateChanged(state.QuestId, previous, next));
        }

        private void Complete(QuestRuntimeState state, QuestDefinition definition)
        {
            SetState(state, QuestState.Completed);
            for (int index = 0; index < definition.Rewards.Count; index++)
            {
                QuestRewardDefinition reward = definition.Rewards[index];
                RewardGranted?.Invoke(new QuestRewardGranted(state.QuestId, reward.Type, reward.RewardId, reward.Amount));
            }
        }
    }
}
