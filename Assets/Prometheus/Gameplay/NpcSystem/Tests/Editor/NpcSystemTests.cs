using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using Xuan.Prometheus.World;

namespace Xuan.Prometheus.Npc.Tests
{
    /// <summary>验证 NPC 定义校验与交互会话的基础契约；POI 不使用 ELC，会话以稳定 POI Id 为键。</summary>
    public sealed class NpcSystemTests
    {
        /// <summary>验证有效 NPC 定义可以通过校验。</summary>
        [Test]
        public void NpcDefinition_ValidatesRequiredIdentity()
        {
            NpcDefinition definition = CreateDefinition("npc.test", "manual_dialogue");
            try
            {
                Assert.DoesNotThrow(definition.Validate);
            }
            finally
            {
                Object.DestroyImmediate(definition);
            }
        }

        /// <summary>验证 NPC 交互会话以稳定 POI Id 为键创建、发布并完成，且同一时刻只允许一个会话。</summary>
        [Test]
        public void NpcInteraction_UsesStablePoiIdAsSessionKey()
        {
            GameObject gameObject = new GameObject("NpcSystemTests.Npc");
            NpcDefinition definition = CreateDefinition("npc.test", "manual_dialogue");
            try
            {
                PoiMono npc = gameObject.AddComponent<PoiMono>();
                npc.Config = new PoiConfig { Id = "poi.npc.test", PoiType = PoiType.Npc, Npc = definition };
                NpcSystem npcSystem = new NpcSystem();
                NpcInteractionContext? observed = null;
                npcSystem.InteractionRequested += context => observed = context;

                Assert.That(npcSystem.TryBeginInteraction(npc), Is.True);
                Assert.That(observed.HasValue, Is.True);
                Assert.That(observed.Value.PoiId, Is.EqualTo("poi.npc.test"));
                Assert.That(observed.Value.NpcId, Is.EqualTo("npc.test"));
                Assert.That(npcSystem.ActiveInteraction.HasValue, Is.True);

                Assert.That(npcSystem.TryBeginInteraction(npc), Is.False, "同一时刻只允许一个活动交互会话。");
                Assert.That(npcSystem.CompleteInteraction("poi.other"), Is.False, "Id 不匹配时应保持当前会话。");
                Assert.That(npcSystem.CompleteInteraction("poi.npc.test"), Is.True);
                Assert.That(npcSystem.ActiveInteraction.HasValue, Is.False);
                npcSystem.Dispose();
            }
            finally
            {
                Object.DestroyImmediate(definition);
                if (gameObject != null) Object.DestroyImmediate(gameObject);
            }
        }

        /// <summary>通过序列化字段创建测试定义，避免依赖运行时资源或场景资产。</summary>
        private static NpcDefinition CreateDefinition(string npcId, string interactionId)
        {
            NpcDefinition definition = ScriptableObject.CreateInstance<NpcDefinition>();
            SerializedObject serialized = new SerializedObject(definition);
            serialized.FindProperty("npcId").stringValue = npcId;
            serialized.FindProperty("displayName").stringValue = "Test NPC";
            serialized.FindProperty("defaultInteractionId").stringValue = interactionId;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            return definition;
        }
    }
}
