using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using Xuan.Prometheus.Narrative;
using Xuan.Prometheus.Quest;
using Xuan.Prometheus.World;
using Xuan.Prometheus.Expression;

namespace Xuan.Prometheus.Npc.Tests
{
    /// <summary>
    /// NPC 交互编排的 EditMode 覆盖：对话绑定查询、兜底回落、头顶标记与失效通知。
    ///
    /// 演出本身（<c>NarrativePlayback</c>）需要完整的资源与界面管线，不在 EditMode 内驱动；
    /// 本组用例只覆盖**编排决策**——也就是真正属于 NpcSystem 的那部分逻辑。
    /// </summary>
    public sealed class NpcSystemTests
    {
        private GameplayKit gameplayKit;
        private QuestSystem questSystem;
        private StoryVariables storyVariables;
        private NarrativeSystem narrativeSystem;
        private NpcSystem npcSystem;
        private readonly List<Object> cleanup = new List<Object>();

        /// <summary>装配一个只含任务与 NPC 两个系统的最小玩法容器。</summary>
        [SetUp]
        public void SetUp()
        {
            gameplayKit = new GameplayKit();
            Core.Gameplay = gameplayKit;
            // 与组合根一致：一份共享存储，两个命名空间各管各的根段。
            // 任务条件因此能读 flag.*，否则条件表达式在注册期就会被校验器拒绝。
            VariableStore variables = new VariableStore();
            questSystem = new QuestSystem(variables);
            narrativeSystem = new NarrativeSystem(variables);
            storyVariables = narrativeSystem.Variables;
            gameplayKit.AddSystem<IQuestSystem>(questSystem);
            gameplayKit.AddSystem<INarrativeSystem>(narrativeSystem);
            npcSystem = new NpcSystem(questSystem, narrativeSystem, new FakeDialogueHost());
            gameplayKit.AddSystem<INpcSystem>(npcSystem);
            npcSystem.AfterNew();
        }

        /// <summary>释放容器与本用例创建的全部对象。</summary>
        [TearDown]
        public void TearDown()
        {
            gameplayKit?.Dispose();
            gameplayKit = null;
            Core.Gameplay = null;
            narrativeSystem = null;
            storyVariables = null;
            questSystem = null;
            npcSystem = null;
            for (int index = cleanup.Count - 1; index >= 0; index--)
            {
                if (cleanup[index] != null) Object.DestroyImmediate(cleanup[index]);
            }
            cleanup.Clear();
        }

        /// <summary>缺少稳定标识的 NPC 定义无法参与任何任务绑定，必须在校验期暴露。</summary>
        [Test]
        public void NpcDefinition_RequiresStableId()
        {
            NpcDefinition valid = NewNpc("elder");
            NpcDefinition invalid = NewNpc(null);

            Assert.DoesNotThrow(valid.Validate);
            Assert.That(invalid.Validate, Throws.InvalidOperationException);
        }

        /// <summary>头顶标记直接来自任务系统的对话解析，NpcSystem 不自己计算。</summary>
        [Test]
        public void Marker_ComesFromQuestDialogueResolution()
        {
            Assert.That(npcSystem.GetMarker("elder"), Is.EqualTo(QuestMarker.None), "没有任何绑定时不应有标记。");

            RegisterQuest("q_offer", new DialogueBinding("b_offer", "elder", "Story_Offer", QuestMarker.QuestAvailable));

            Assert.That(npcSystem.GetMarker("elder"), Is.EqualTo(QuestMarker.QuestAvailable));
            Assert.That(npcSystem.GetMarker("someone_else"), Is.EqualTo(QuestMarker.None));
        }

        /// <summary>任务状态变化后标记缓存失效并转发通知，整个链路由通知驱动而不是轮询。</summary>
        [Test]
        public void MarkerChanged_IsForwardedWhenQuestInvalidatesBindings()
        {
            RegisterQuest("q_track", new DialogueBinding("b_a", "elder", "Story_A", QuestMarker.QuestAvailable, condition: "!flag.talked"),
                new DialogueBinding("b_b", "elder", "Story_B", QuestMarker.QuestTurnIn, condition: "flag.talked"));
            List<string> notified = new List<string>();
            npcSystem.MarkerChanged += npcId => notified.Add(npcId);

            Assert.That(npcSystem.GetMarker("elder"), Is.EqualTo(QuestMarker.QuestAvailable));

            storyVariables.SetFlag("talked", true);
            questSystem.FlushNow();

            Assert.That(notified, Does.Contain("elder"), "绑定条件变化必须转发给表现层。");
            Assert.That(npcSystem.GetMarker("elder"), Is.EqualTo(QuestMarker.QuestTurnIn), "条件翻转后应当解析到另一条绑定，缓存必须已经失效。");
        }

        /// <summary>没有任务绑定也没有兜底闲聊时，交互被拒绝而不是演一段空剧情。</summary>
        [Test]
        public void Interact_IsRejectedWhenNothingToSay()
        {
            GameObject host = NewGameObject("NpcSystemTests.Silent");
            PoiMono poi = host.AddComponent<PoiMono>();
            poi.Config = new PoiConfig { Id = "poi.silent", PoiType = PoiType.Npc, Npc = NewNpc("silent") };

            NpcInteractionResult result = npcSystem.InteractAsync(poi).GetAwaiter().GetResult();

            Assert.That(result, Is.EqualTo(NpcInteractionResult.Rejected));
            Assert.That(npcSystem.HasActiveInteraction, Is.False, "被拒绝的交互不应留下活动会话。");
        }

        /// <summary>不是 NPC 的 POI 交互属于调用方错误，直接抛出而不是静默返回。</summary>
        [Test]
        public void Interact_ThrowsWhenPoiIsNotAnNpc()
        {
            GameObject host = NewGameObject("NpcSystemTests.Chest");
            PoiMono poi = host.AddComponent<PoiMono>();
            poi.Config = new PoiConfig { Id = "poi.chest", PoiType = PoiType.Chest };

            Assert.That(() => npcSystem.InteractAsync(poi).GetAwaiter().GetResult(), Throws.InvalidOperationException);
        }

        /// <summary>任务绑定优先于 NPC 自己的兜底闲聊。</summary>
        [Test]
        public void DialogueResolution_PrefersQuestBindingOverIdleStory()
        {
            RegisterQuest("q_talk", new DialogueBinding("b_talk", "elder", "Story_Quest", QuestMarker.QuestTurnIn));

            Assert.That(questSystem.ResolveDialogue("elder", out QuestDialogueResolution resolution), Is.True);
            Assert.That(resolution.StoryLocation, Is.EqualTo("Story_Quest"), "有任务绑定命中时不应回落到闲聊。");
        }

        /// <summary>注册一个带对话绑定的最小任务，并推进到可解析状态。</summary>
        private void RegisterQuest(string questId, params DialogueBinding[] bindings)
        {
            QuestDefinition definition = ScriptableObject.CreateInstance<QuestDefinition>();
            definition.name = questId;
            cleanup.Add(definition);
            definition.Configure(questId, QuestCategory.World, QuestAcceptMode.Manual);
            definition.AddStep(new QuestStep("s1").WithTransition(new QuestTransition($"quest.{questId}.done >= 1", QuestTransitionKind.CompleteQuest)));
            for (int index = 0; index < bindings.Length; index++) definition.AddDialogueBinding(bindings[index]);
            questSystem.RegisterDefinition(definition);
            // 失效通知在帧末统一推送，测试里显式推一次，等价于跑过一帧。
            questSystem.FlushNow();
        }

        /// <summary>创建一个带清理登记的 NPC 定义。</summary>
        private NpcDefinition NewNpc(string npcId, string idleStory = null)
        {
            NpcDefinition definition = ScriptableObject.CreateInstance<NpcDefinition>();
            definition.name = npcId ?? "unnamed";
            cleanup.Add(definition);
            definition.Configure(npcId, npcId, idleStory);
            return definition;
        }

        /// <summary>创建一个带清理登记的场景对象。</summary>
        private GameObject NewGameObject(string name)
        {
            GameObject created = new GameObject(name);
            cleanup.Add(created);
            return created;
        }

        /// <summary>不打开任何界面的对话宿主，供不驱动演出的编排用例使用。</summary>
        private sealed class FakeDialogueHost : IDialogueHost
        {
            /// <summary>返回剧情系统自带的空视图。</summary>
            public IDialogueView Open()
            {
                return NullDialogueView.Instance;
            }

            /// <summary>无界面可关。</summary>
            public void Close()
            {
            }
        }
    }
}
