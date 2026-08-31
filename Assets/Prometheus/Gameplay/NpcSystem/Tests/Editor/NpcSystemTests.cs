using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using Xuan.Prometheus.World;

namespace Xuan.Prometheus.Npc.Tests
{
    /// <summary>验证 NPC 定义校验和 Entity 组件组合的第一阶段基础契约。</summary>
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

        /// <summary>验证 NpcEntity 会复用 POI 配置并挂接 NPC 定义与运行时状态。</summary>
        [Test]
        public void NpcEntity_ComposesPoiAndNpcComponents()
        {
            GameObject gameObject = new GameObject("NpcSystemTests.Npc");
            NpcDefinition definition = CreateDefinition("npc.test", "manual_dialogue");
            PoiConfig config = new PoiConfig { Id = "poi.npc.test", PoiType = PoiType.Npc, Npc = definition };
            try
            {
                NpcEntity entity = new NpcEntity(gameObject, config, definition);
                Assert.That(entity.Config, Is.SameAs(config));
                Assert.That(entity.Definition, Is.SameAs(definition));
                Assert.That(entity.RuntimeState, Is.Not.Null);
                Assert.That(entity.RuntimeState.IsUnlocked, Is.True);
                entity.RequestDispose(0f);
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
