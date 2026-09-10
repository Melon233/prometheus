using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using Xuan.Prometheus.Expression;
using Xuan.Prometheus.Narrative;

namespace Xuan.Prometheus.Quest.Tests
{
    /// <summary>
    /// 任务内核的 EditMode 覆盖：状态推导、步骤推进、触发器计数、回滚、存档与对话解析。
    /// 全部用例零 Unity 运行时依赖，不创建任何 GameObject。
    /// </summary>
    public sealed class QuestSystemTests
    {
        private QuestSystem quest;
        private VariableStore variables;
        private StoryVariables storyVariables;
        private readonly List<ScriptableObject> created = new List<ScriptableObject>();

        /// <summary>每个用例都拿到一个全新的任务系统，并与剧情变量共享同一份存储。</summary>
        [SetUp]
        public void SetUp()
        {
            // 与组合根一致：一份共享存储，两个命名空间各管各的根段。
            // 任务条件因此读得到 flag.*，剧情条件读得到 quest.*，而两边不需要认识对方。
            variables = new VariableStore();
            quest = new QuestSystem(variables);
            storyVariables = new StoryVariables(variables);
            // 任务系统在构造时已订阅整个存储的变化，剧情旗标的改动会自动弄脏引用它的任务。
        }

        /// <summary>释放系统与本用例创建的全部配置资产。</summary>
        [TearDown]
        public void TearDown()
        {
            storyVariables.Changed -= quest.Invalidate;
            quest.Dispose();
            for (int index = 0; index < created.Count; index++)
            {
                if (created[index] != null) Object.DestroyImmediate(created[index]);
            }
            created.Clear();
        }

        /// <summary>未接取的任务不落记录，状态由解锁条件现算——这是铁律 Q5 的直接表现。</summary>
        [Test]
        public void UnregisteredProgress_DerivesStatusFromUnlockCondition()
        {
            QuestDefinition definition = NewQuest("q_wind", unlock: "flag.met_elder", mode: QuestAcceptMode.Manual);
            quest.RegisterDefinition(definition);

            Assert.That(quest.GetStatus("q_wind"), Is.EqualTo(QuestStatus.Locked));

            storyVariables.SetFlag("met_elder", true);

            Assert.That(quest.GetStatus("q_wind"), Is.EqualTo(QuestStatus.Available), "解锁条件成立后状态应立即推导为可接取，不需要任何记录。");
        }

        /// <summary>自动接取的任务在解锁条件成立后的帧末推进里自行进入进行中。</summary>
        [Test]
        public void AutoAcceptQuest_ActivatesOnFlushAfterUnlock()
        {
            quest.RegisterDefinition(NewQuest("q_auto", unlock: "flag.ready"));
            quest.FlushNow();
            Assert.That(quest.GetStatus("q_auto"), Is.EqualTo(QuestStatus.Locked));

            storyVariables.SetFlag("ready", true);
            quest.FlushNow();

            Assert.That(quest.GetStatus("q_auto"), Is.EqualTo(QuestStatus.Active));
            Assert.That(quest.GetCurrentStepId("q_auto"), Is.EqualTo("s1"));
        }

        /// <summary>手动接取的任务不会自动进入，且只有解锁后才接得上。</summary>
        [Test]
        public void ManualQuest_RequiresExplicitAcceptAndUnlock()
        {
            quest.RegisterDefinition(NewQuest("q_manual", unlock: "flag.ready", mode: QuestAcceptMode.Manual));
            quest.FlushNow();

            Assert.That(quest.Accept("q_manual"), Is.False, "未解锁时不允许接取。");
            storyVariables.SetFlag("ready", true);
            quest.FlushNow();
            Assert.That(quest.GetStatus("q_manual"), Is.EqualTo(QuestStatus.Available), "手动任务不应被帧末推进自动接取。");

            Assert.That(quest.Accept("q_manual"), Is.True);
            Assert.That(quest.GetStatus("q_manual"), Is.EqualTo(QuestStatus.Active));
        }

        /// <summary>
        /// 「击败 N 只怪」端到端：触发器累加变量、目标行读变量、转移条件比较变量。
        /// 同时锁住旧实现的缺陷——重复投递同一条事件必须继续计数，而不是被幂等表吞掉。
        /// </summary>
        [Test]
        public void RepeatedIdenticalEvents_KeepCountingAndCompleteTheQuest()
        {
            QuestDefinition definition = NewQuest("q_hunt", withDefaultStep: false);
            QuestStep step = definition.AddStep(new QuestStep("s_kill"));  // AddStep 返回同一实例，后续链式配置即可
            step.WithTrigger(new QuestTrigger(QuestEventNames.EnemyDefeated, "quest.q_hunt.kills", QuestTriggerOp.Increment, "e.id == \"hilichurl\""));
            step.WithObjective(new QuestObjective("o_kill", "quest.q_hunt.kills", "3"));
            step.WithTransition(new QuestTransition("quest.q_hunt.kills >= 3", QuestTransitionKind.CompleteQuest));
            quest.RegisterDefinition(definition);
            quest.Accept("q_hunt");

            for (int index = 0; index < 3; index++)
            {
                quest.Emit(QuestEvent.Create(QuestEventNames.EnemyDefeated, ("id", "hilichurl")));
            }

            IReadOnlyList<QuestObjectiveProgress> progressBeforeFlush = quest.GetObjectiveProgress("q_hunt");
            Assert.That(progressBeforeFlush[0].Current, Is.EqualTo(3d), "三次相同事件必须累加成 3，事件去重表在新模型里不存在。");
            Assert.That(progressBeforeFlush[0].Target, Is.EqualTo(3d));
            Assert.That(progressBeforeFlush[0].IsDone, Is.True);

            quest.FlushNow();
            Assert.That(quest.GetStatus("q_hunt"), Is.EqualTo(QuestStatus.Completed));
        }

        /// <summary>不匹配过滤表达式的事件不参与计数。</summary>
        [Test]
        public void TriggerFilter_RejectsNonMatchingPayload()
        {
            QuestDefinition definition = NewQuest("q_filter", withDefaultStep: false);
            QuestStep step = new QuestStep("s_kill");
            step.WithTrigger(new QuestTrigger(QuestEventNames.EnemyDefeated, "quest.q_filter.kills", QuestTriggerOp.Increment, "e.id == \"slime\""));
            step.WithTransition(new QuestTransition("quest.q_filter.kills >= 1", QuestTransitionKind.CompleteQuest));
            definition.AddStep(step);
            quest.RegisterDefinition(definition);
            quest.Accept("q_filter");

            quest.Emit(QuestEvent.Create(QuestEventNames.EnemyDefeated, ("id", "hilichurl")));
            quest.FlushNow();

            Assert.That(quest.GetStatus("q_filter"), Is.EqualTo(QuestStatus.Active), "载荷不匹配的事件不应推进任务。");
        }

        /// <summary>转移按声明顺序取第一条成立者，顺序即优先级。</summary>
        [Test]
        public void Transitions_TakeFirstMatchingInDeclarationOrder()
        {
            QuestDefinition definition = NewQuest("q_branch", withDefaultStep: false);
            QuestStep entry = new QuestStep("s_entry");
            entry.WithTransition(new QuestTransition("flag.take_left", QuestTransitionKind.ToStep, "s_left"));
            entry.WithTransition(new QuestTransition(null, QuestTransitionKind.ToStep, "s_right"));
            definition.AddStep(entry);
            definition.AddStep(new QuestStep("s_left").WithTransition(new QuestTransition("flag.done", QuestTransitionKind.CompleteQuest)));
            definition.AddStep(new QuestStep("s_right").WithTransition(new QuestTransition("flag.done", QuestTransitionKind.CompleteQuest)));
            quest.RegisterDefinition(definition);

            storyVariables.SetFlag("take_left", true);
            quest.Accept("q_branch");
            quest.FlushNow();

            Assert.That(quest.GetCurrentStepId("q_branch"), Is.EqualTo("s_left"), "第一条成立的转移应当胜出，兜底分支不该被取到。");
        }

        /// <summary>转移可以指向更早的步骤，这就是任务回滚；声明了可重跑的动作会被重新执行。</summary>
        [Test]
        public void RollbackTransition_RerunsActionsMarkedForRollback()
        {
            QuestDefinition definition = NewQuest("q_rollback", withDefaultStep: false);
            QuestStep first = new QuestStep("s_first");
            first.WithEnterAction((QuestAction)new SetQuestVariableAction("quest.q_rollback.entered", 1d).WithId("a_enter", rerun: true));
            first.WithTransition(new QuestTransition("flag.go_second", QuestTransitionKind.ToStep, "s_second"));
            QuestStep second = new QuestStep("s_second");
            second.WithTransition(new QuestTransition("flag.rollback", QuestTransitionKind.ToStep, "s_first"));
            second.WithTransition(new QuestTransition("flag.done", QuestTransitionKind.CompleteQuest));
            definition.AddStep(first);
            definition.AddStep(second);
            quest.RegisterDefinition(definition);
            quest.Accept("q_rollback");

            quest.Variables.Set("quest.q_rollback.entered", new StoryValue(0d));
            storyVariables.SetFlag("go_second", true);
            quest.FlushNow();
            Assert.That(quest.GetCurrentStepId("q_rollback"), Is.EqualTo("s_second"));

            quest.Variables.Set("quest.q_rollback.entered", new StoryValue(0d));
            storyVariables.SetFlag("go_second", false);
            storyVariables.SetFlag("rollback", true);
            quest.FlushNow();

            Assert.That(quest.GetCurrentStepId("q_rollback"), Is.EqualTo("s_first"), "转移应能指向更早的步骤。");
            Assert.That(quest.Variables.Get("quest.q_rollback.entered").AsNumber(), Is.EqualTo(1d), "标记了 RerunOnRollback 的进入动作应在回滚后重新执行。");
        }

        /// <summary>完成动作按「恰好一次」执行，重复推进不会重复发奖。</summary>
        [Test]
        public void CompleteActions_GrantRewardExactlyOnce()
        {
            QuestDefinition definition = NewQuest("q_reward");
            definition.AddCompleteAction((QuestAction)new GrantRewardAction(QuestRewardType.Item, "apple", 2).WithId("a_reward"));
            quest.RegisterDefinition(definition);
            List<QuestRewardGranted> rewards = new List<QuestRewardGranted>();
            quest.RewardGranted += reward => rewards.Add(reward);

            quest.Accept("q_reward");
            storyVariables.SetFlag("done", true);
            quest.FlushNow();
            quest.FlushNow();

            Assert.That(quest.GetStatus("q_reward"), Is.EqualTo(QuestStatus.Completed));
            Assert.That(rewards.Count, Is.EqualTo(1), "奖励动作必须恰好执行一次。");
            Assert.That(rewards[0].RewardId, Is.EqualTo("apple"));
            Assert.That(rewards[0].Amount, Is.EqualTo(2));
        }

        /// <summary>存档只记录偏离默认的任务；版本更新新增的任务在旧存档上自然处于正确状态。</summary>
        [Test]
        public void Snapshot_RecordsOnlyDeviationsAndNewQuestsNeedNoMigration()
        {
            quest.RegisterDefinition(NewQuest("q_old"));
            quest.RegisterDefinition(NewQuest("q_untouched", unlock: "flag.never", mode: QuestAcceptMode.Manual));
            quest.Accept("q_old");
            quest.TrackedQuestId = "q_old";
            string json = quest.CaptureSnapshot();

            Assert.That(json, Does.Contain("q_old"));
            Assert.That(json, Does.Not.Contain("q_untouched"), "从未碰过的任务不应进入存档。");

            // 模拟版本更新：读档时配置里多了一个新任务。
            QuestSystem restored = new QuestSystem(new VariableStore());
            _ = new StoryVariables(restored.Variables.Store);
            restored.RegisterDefinition(NewQuest("q_old"));
            restored.RegisterDefinition(NewQuest("q_untouched", unlock: "flag.never", mode: QuestAcceptMode.Manual));
            restored.RegisterDefinition(NewQuest("q_added_in_patch", mode: QuestAcceptMode.Manual));
            restored.RestoreSnapshot(json);

            Assert.That(restored.GetStatus("q_old"), Is.EqualTo(QuestStatus.Active));
            Assert.That(restored.GetCurrentStepId("q_old"), Is.EqualTo("s1"));
            Assert.That(restored.TrackedQuestId, Is.EqualTo("q_old"));
            Assert.That(restored.GetStatus("q_untouched"), Is.EqualTo(QuestStatus.Locked));
            Assert.That(restored.GetStatus("q_added_in_patch"), Is.EqualTo(QuestStatus.Available), "新增任务无需迁移脚本即处于正确状态。");
            restored.Dispose();
        }

        /// <summary>配置被删除的任务在读档时只记警告并丢弃，不污染整个读档流程。</summary>
        [Test]
        public void Snapshot_DropsUnknownQuestWithWarning()
        {
            quest.RegisterDefinition(NewQuest("q_gone"));
            quest.Accept("q_gone");
            string json = quest.CaptureSnapshot();

            QuestSystem restored = new QuestSystem(new VariableStore());
            _ = new StoryVariables(restored.Variables.Store);
            restored.RegisterDefinition(NewQuest("q_kept"));
            LogAssert.Expect(LogType.Warning, new System.Text.RegularExpressions.Regex("q_gone"));
            Assert.DoesNotThrow(() => restored.RestoreSnapshot(json));
            Assert.That(restored.GetStatus("q_kept"), Is.EqualTo(QuestStatus.Available));
            restored.Dispose();
        }

        /// <summary>对话解析按优先级降序取胜出者，同优先级时当前追踪的任务优先。</summary>
        [Test]
        public void ResolveDialogue_PrefersHigherPriorityThenTrackedQuest()
        {
            QuestDefinition low = NewQuest("q_low");
            low.AddDialogueBinding(new DialogueBinding("b_low", "elder", "Story_Low", QuestMarker.QuestInProgress, priority: 1));
            QuestDefinition highA = NewQuest("q_a");
            highA.AddDialogueBinding(new DialogueBinding("b_a", "elder", "Story_A", QuestMarker.QuestTurnIn, priority: 5));
            QuestDefinition highB = NewQuest("q_b");
            highB.AddDialogueBinding(new DialogueBinding("b_b", "elder", "Story_B", QuestMarker.QuestAvailable, priority: 5));
            quest.RegisterDefinition(low);
            quest.RegisterDefinition(highA);
            quest.RegisterDefinition(highB);
            quest.Accept("q_low");
            quest.Accept("q_a");
            quest.Accept("q_b");

            quest.TrackedQuestId = "q_b";
            Assert.That(quest.ResolveDialogue("elder", out QuestDialogueResolution tracked), Is.True);
            Assert.That(tracked.QuestId, Is.EqualTo("q_b"), "同优先级时应当优先说当前追踪任务的话。");
            Assert.That(tracked.Marker, Is.EqualTo(QuestMarker.QuestAvailable), "标记与对话来自同一次解析，必须同源。");

            quest.TrackedQuestId = "q_a";
            Assert.That(quest.ResolveDialogue("elder", out QuestDialogueResolution switched), Is.True);
            Assert.That(switched.QuestId, Is.EqualTo("q_a"));

            Assert.That(quest.ResolveDialogue("nobody", out _), Is.False, "无绑定命中时应当回落给调用方。");
        }

        /// <summary>可接取但尚未接取的任务同样贡献对话绑定，这正是「接取入口」所在。</summary>
        [Test]
        public void ResolveDialogue_IncludesAvailableQuestsSoTheyCanBeOffered()
        {
            QuestDefinition definition = NewQuest("q_offer", mode: QuestAcceptMode.Manual);
            definition.AddDialogueBinding(new DialogueBinding("b_offer", "elder", "Story_Offer", QuestMarker.QuestAvailable).WithOffer("q_offer"));
            quest.RegisterDefinition(definition);

            Assert.That(quest.GetStatus("q_offer"), Is.EqualTo(QuestStatus.Available));
            Assert.That(quest.ResolveDialogue("elder", out QuestDialogueResolution resolution), Is.True);
            Assert.That(resolution.OffersQuestId, Is.EqualTo("q_offer"));
        }

        /// <summary>校验器拒绝不可达步骤。</summary>
        [Test]
        public void Validator_RejectsUnreachableStep()
        {
            QuestDefinition definition = NewQuest("q_unreachable");
            definition.AddStep(new QuestStep("s_orphan").WithTransition(new QuestTransition(null, QuestTransitionKind.CompleteQuest)));
            Assert.That(() => quest.RegisterDefinition(definition), Throws.InvalidOperationException.With.Message.Contains("不可达"));
        }

        /// <summary>校验器拒绝未登记的事件名——这是「字符串事件名」这条取舍的静态兜底。</summary>
        [Test]
        public void Validator_RejectsUnknownEventName()
        {
            QuestDefinition definition = NewQuest("q_badevent");
            definition.Steps[0].WithTrigger(new QuestTrigger("combat.typo_here", "quest.q_badevent.count"));
            Assert.That(() => quest.RegisterDefinition(definition), Throws.InvalidOperationException.With.Message.Contains("未登记的事件名"));
        }

        /// <summary>校验器拒绝条件恒真的自环，否则帧末推进永远无法收敛。</summary>
        [Test]
        public void Validator_RejectsAlwaysTrueSelfLoop()
        {
            QuestDefinition definition = NewQuest("q_loop");
            definition.Steps[0].WithTransition(new QuestTransition(null, QuestTransitionKind.ToStep, "s1"));
            Assert.That(() => quest.RegisterDefinition(definition), Throws.InvalidOperationException.With.Message.Contains("自环"));
        }

        /// <summary>校验器拒绝写入投影命名空间的触发器。</summary>
        [Test]
        public void Validator_RejectsTriggerWritingOutsideQuestNamespace()
        {
            QuestDefinition definition = NewQuest("q_badpath");
            definition.Steps[0].WithTrigger(new QuestTrigger(QuestEventNames.EnemyDefeated, "flag.not_mine"));
            Assert.That(() => quest.RegisterDefinition(definition), Throws.InvalidOperationException.With.Message.Contains("quest."));
        }

        /// <summary>放弃任务回到可接取状态并清空该任务的变量。</summary>
        [Test]
        public void Abandon_ReturnsToAvailableAndClearsProgress()
        {
            QuestDefinition definition = NewQuest("q_drop", mode: QuestAcceptMode.Manual);
            quest.RegisterDefinition(definition);
            quest.Accept("q_drop");
            quest.Variables.Set("quest.q_drop.progress", new StoryValue(7d));

            Assert.That(quest.Abandon("q_drop"), Is.True);
            Assert.That(quest.GetStatus("q_drop"), Is.EqualTo(QuestStatus.Available), "放弃不是终态，而是回到可接取。");
            Assert.That(quest.Variables.Get("quest.q_drop.progress").AsNumber(), Is.EqualTo(0d), "重置后该任务的变量应当清空，未写入的计数器读作零。");
            Assert.That(quest.Accept("q_drop"), Is.True, "放弃后应当可以重新接取。");
        }

        /// <summary>接下第一个任务时自动开始追踪它，玩家不必手动去任务界面点一下。</summary>
        [Test]
        public void Tracking_FollowsTheFirstAcceptedQuestAndRetargetsWhenItEnds()
        {
            quest.RegisterDefinition(NewQuest("q_first", mode: QuestAcceptMode.Manual));
            // 第二个任务用另一个完成条件，否则一次 flag.done 会把两个一起结掉。
            QuestDefinition second = NewQuest("q_second", mode: QuestAcceptMode.Manual, withDefaultStep: false);
            second.AddStep(new QuestStep("s1").WithTransition(new QuestTransition("flag.done_second", QuestTransitionKind.CompleteQuest)));
            quest.RegisterDefinition(second);

            Assert.That(quest.TrackedQuestId, Is.Null.Or.Empty, "还没接任务时不应有追踪目标。");

            quest.Accept("q_first");
            Assert.That(quest.TrackedQuestId, Is.EqualTo("q_first"));

            quest.Accept("q_second");
            Assert.That(quest.TrackedQuestId, Is.EqualTo("q_first"), "已经在追一条线时，接新任务不应抢走追踪。");

            storyVariables.SetFlag("done", true);
            quest.FlushNow();

            Assert.That(quest.GetStatus("q_first"), Is.EqualTo(QuestStatus.Completed));
            Assert.That(quest.TrackedQuestId, Is.EqualTo("q_second"), "被追的任务结束后应自动改追另一条仍在进行的线。");
        }

        /// <summary>追踪摘要一次取齐标题、步骤与导航，避免 UI 读到彼此不一致的中间状态。</summary>
        [Test]
        public void TrackedSummary_ReportsTitleStepAndGuideTogether()
        {
            QuestDefinition definition = NewQuest("q_guide", mode: QuestAcceptMode.Manual, withDefaultStep: false);
            definition.AddStep(new QuestStep("s_go", "key.step.go")
                .WithGuide(QuestGuide.ToNpc("elder"))
                .WithTransition(new QuestTransition("flag.done", QuestTransitionKind.CompleteQuest)));
            quest.RegisterDefinition(definition);

            Assert.That(quest.TryGetTrackedSummary(out _), Is.False, "未接取的任务不应产生追踪摘要。");

            quest.Accept("q_guide");

            Assert.That(quest.TryGetTrackedSummary(out QuestTrackSummary summary), Is.True);
            Assert.That(summary.QuestId, Is.EqualTo("q_guide"));
            Assert.That(summary.StepId, Is.EqualTo("s_go"));
            Assert.That(summary.StepDescTextKey, Is.EqualTo("key.step.go"));
            Assert.That(summary.Guide.Kind, Is.EqualTo(QuestGuideKind.Npc));
            Assert.That(summary.Guide.TargetId, Is.EqualTo("elder"));

            storyVariables.SetFlag("done", true);
            quest.FlushNow();

            Assert.That(quest.TryGetTrackedSummary(out _), Is.False, "任务完成后不应再产生追踪摘要。");
        }

        /// <summary>剧情可以通过只读投影读到任务状态，任务系统不需要认识剧情系统。</summary>
        [Test]
        public void StoryExpressions_CanReadQuestStatusThroughProjection()
        {
            quest.RegisterDefinition(NewQuest("q_seen", mode: QuestAcceptMode.Manual));
            quest.Accept("q_seen");

            Assert.That(StoryExpression.Compile("quest.q_seen.status == \"Active\"").EvaluateBool(storyVariables), Is.True);
        }

        /// <summary>
        /// 构造一个最小任务配置。
        /// 默认带一个以 <c>flag.done</c> 完成的单步骤，绝大多数用例不必自己搭步骤；
        /// 需要自定义步骤结构的用例传 <paramref name="withDefaultStep"/> 为 false。
        /// </summary>
        private QuestDefinition NewQuest(string questId, string unlock = null, QuestAcceptMode mode = QuestAcceptMode.Auto, bool withDefaultStep = true)
        {
            QuestDefinition definition = ScriptableObject.CreateInstance<QuestDefinition>();
            definition.name = questId;
            created.Add(definition);
            definition.Configure(questId, QuestCategory.World, mode, unlock);
            if (withDefaultStep) definition.AddStep(new QuestStep("s1").WithTransition(new QuestTransition("flag.done", QuestTransitionKind.CompleteQuest)));
            return definition;
        }
    }
}
