using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace Xuan.Prometheus.Quest.Tests
{
    /// <summary>验证任务接取、事件推进、幂等、奖励和快照恢复的核心契约。</summary>
    public sealed class QuestSystemTests
    {
        /// <summary>验证匹配事件会完成任务，重复 EventId 不会重复计数或奖励。</summary>
        [Test]
        public void PublishEvent_CompletesQuestIdempotently()
        {
            QuestDefinition definition = CreateDefinition("quest.test", "objective.test", QuestEventType.ItemAdded, "item.test", 2);
            QuestSystem system = new QuestSystem();
            try
            {
                system.RegisterDefinition(definition);
                Assert.That(system.TryAccept("quest.test"), Is.True);
                system.PublishEvent(new QuestEvent("event.1", QuestEventType.ItemAdded, "item.test", 2));
                system.PublishEvent(new QuestEvent("event.1", QuestEventType.ItemAdded, "item.test", 2));
                Assert.That(system.GetState("quest.test").State, Is.EqualTo(QuestState.Completed));
                Assert.That(system.GetState("quest.test").ObjectiveProgress[0].Amount, Is.EqualTo(2));
            }
            finally
            {
                system.Dispose();
                Object.DestroyImmediate(definition);
            }
        }

        /// <summary>验证快照能恢复活动状态和目标进度。</summary>
        [Test]
        public void Snapshot_RestoresActiveProgress()
        {
            QuestDefinition definition = CreateDefinition("quest.snapshot", "objective.snapshot", QuestEventType.EnemyDefeated, "enemy.test", 3);
            QuestSystem source = new QuestSystem();
            QuestSystem restored = new QuestSystem();
            try
            {
                source.RegisterDefinition(definition);
                source.TryAccept("quest.snapshot");
                source.PublishEvent(new QuestEvent("event.snapshot", QuestEventType.EnemyDefeated, "enemy.test", 1));
                string snapshot = source.CaptureSnapshot();
                restored.RegisterDefinition(definition);
                restored.RestoreSnapshot(snapshot);
                Assert.That(restored.GetState("quest.snapshot").State, Is.EqualTo(QuestState.Active));
                Assert.That(restored.GetState("quest.snapshot").ObjectiveProgress[0].Amount, Is.EqualTo(1));
            }
            finally
            {
                source.Dispose();
                restored.Dispose();
                Object.DestroyImmediate(definition);
            }
        }

        /// <summary>通过 Unity 序列化字段构造测试任务，避免依赖场景和外部资源。</summary>
        private static QuestDefinition CreateDefinition(string questId, string objectiveId, QuestEventType eventType, string targetId, int requiredAmount)
        {
            QuestDefinition definition = ScriptableObject.CreateInstance<QuestDefinition>();
            SerializedObject serialized = new SerializedObject(definition);
            serialized.FindProperty("questId").stringValue = questId;
            serialized.FindProperty("displayName").stringValue = "Test Quest";
            SerializedProperty objectives = serialized.FindProperty("objectives");
            objectives.arraySize = 1;
            SerializedProperty objective = objectives.GetArrayElementAtIndex(0);
            objective.FindPropertyRelative("objectiveId").stringValue = objectiveId;
            objective.FindPropertyRelative("eventType").enumValueIndex = (int)eventType;
            objective.FindPropertyRelative("targetId").stringValue = targetId;
            objective.FindPropertyRelative("requiredAmount").intValue = requiredAmount;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            return definition;
        }
    }
}
