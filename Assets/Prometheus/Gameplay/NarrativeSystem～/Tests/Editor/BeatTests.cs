using NUnit.Framework;
using static Xuan.Prometheus.Narrative.Story;

namespace Xuan.Prometheus.Narrative.Tests
{
    /// <summary>验证对话节拍的执行顺序、推进策略解析、叙述行与选项语义。</summary>
    public sealed class BeatTests
    {
        /// <summary>验证节拍按「进入 → 打字机 → 阻塞动作 → 等待推进 → 结束」的顺序驱动视图。</summary>
        [Test]
        public void Beat_DrivesViewInDocumentedOrder()
        {
            FakeDialogueView view = new FakeDialogueView();
            StoryContext context = NarrativeTestKit.CreateContext(view, "line.1");
            Beat beat = Say(ActorRef.Npc("a"), "line.1")
                .With(new ProbeAction(view.Log, "concurrent"))
                .Await(new ProbeAction(view.Log, "blocking"))
                .Id("only");
            StoryTree.Bind(beat, "test");

            NarrativeTestKit.AwaitSync(beat.PlayAsync(context, default));

            Assert.That(view.Log, Is.EqualTo(new[]
            {
                "begin:test",
                "play:concurrent",
                "show:test",
                "play:blocking",
                "advance:Click",
                "end"
            }));
        }

        /// <summary>验证台词与说话人显示名都经过文本表解析。</summary>
        [Test]
        public void Beat_ResolvesSpeakerNameAndContentFromTextMap()
        {
            FakeDialogueView view = new FakeDialogueView();
            StoryContext context = NarrativeTestKit.CreateContext(view, "line.1");
            Beat beat = Say(ActorRef.Npc("a"), "line.1");
            StoryTree.Bind(beat, "test");

            NarrativeTestKit.AwaitSync(beat.PlayAsync(context, default));

            Assert.That(view.Lines[0].SpeakerName, Is.EqualTo("甲"));
            Assert.That(view.Lines[0].Content, Is.EqualTo("文案-line.1"));
            Assert.That(view.Lines[0].IsNarration, Is.False);
        }

        /// <summary>验证叙述行没有说话人并被标记为叙述。</summary>
        [Test]
        public void Narration_HasNoSpeaker()
        {
            FakeDialogueView view = new FakeDialogueView();
            StoryContext context = NarrativeTestKit.CreateContext(view, "line.n");
            Beat beat = Narration("line.n");
            StoryTree.Bind(beat, "test");

            NarrativeTestKit.AwaitSync(beat.PlayAsync(context, default));

            Assert.That(view.Lines[0].IsNarration, Is.True);
            Assert.That(view.Lines[0].SpeakerName, Is.Empty);
        }

        /// <summary>验证自动播放把点击推进降级为按文本长度自动推进，无需在数据中额外配置。</summary>
        [Test]
        public void AutoPlay_DegradesClickToTextDuration()
        {
            FakeDialogueView view = new FakeDialogueView();
            StoryContext context = NarrativeTestKit.CreateContext(view, "line.1");
            context.AutoPlay = true;
            Beat beat = Say(ActorRef.Npc("a"), "line.1");
            StoryTree.Bind(beat, "test");

            NarrativeTestKit.AwaitSync(beat.PlayAsync(context, default));

            Assert.That(view.Policies[0].Kind, Is.EqualTo(AdvanceKind.TextDuration));
            Assert.That(view.Policies[0].Seconds, Is.GreaterThan(0f));
        }

        /// <summary>验证配音推进在缺少配音服务的当前阶段退化为按文本长度推进。</summary>
        [Test]
        public void VoiceEnd_DegradesToTextDuration()
        {
            FakeDialogueView view = new FakeDialogueView();
            StoryContext context = NarrativeTestKit.CreateContext(view, "line.1");
            Beat beat = Say(ActorRef.Npc("a"), "line.1").AdvanceOn(AdvancePolicy.VoiceEnd);
            StoryTree.Bind(beat, "test");

            NarrativeTestKit.AwaitSync(beat.PlayAsync(context, default));

            Assert.That(view.Policies[0].Kind, Is.EqualTo(AdvanceKind.TextDuration));
        }

        /// <summary>验证节拍落终态时不显示任何界面，但挂载动作仍会落到终态。</summary>
        [Test]
        public void Beat_SettleAppliesAttachedActionsWithoutShowingUi()
        {
            FakeDialogueView view = new FakeDialogueView();
            StoryContext context = NarrativeTestKit.CreateContext(view, "line.1");
            ProbeAction attached = new ProbeAction(view.Log, "attached");
            Beat beat = Say(ActorRef.Npc("a"), "line.1").With(attached);
            StoryTree.Bind(beat, "test");

            beat.Settle(context);

            Assert.That(attached.SettleCount, Is.EqualTo(1));
            Assert.That(view.Lines, Is.Empty);
        }

        /// <summary>验证条件不满足但配置了锁定原因的选项以锁定态展示，且不可被选中。</summary>
        [Test]
        public void Choose_ShowsLockedOptionAndRejectsSelectingIt()
        {
            FakeDialogueView view = new FakeDialogueView();
            StoryContext context = NarrativeTestKit.CreateContext(view, "opt.a", "opt.b", "opt.b.locked", "line.1");
            context.Variables.Set("var.trust", 1);
            ChooseAction choose = Choose(
                Option("opt.a").Then(Say(ActorRef.Npc("a"), "line.1")).Id("a"),
                Option("opt.b").When("var.trust >= 2").Locked("opt.b.locked").Then(Say(ActorRef.Npc("a"), "line.1")).Id("b"));
            StoryTree.Bind(choose, "test");

            NarrativeTestKit.AwaitSync(choose.PlayAsync(context, default));

            DialogueChoiceRequest request = view.ChoiceRequests[0];
            Assert.That(request.Choices.Count, Is.EqualTo(2));
            Assert.That(request.Choices[1].IsLocked, Is.True);
            Assert.That(request.Choices[1].LockedReason, Is.EqualTo("文案-opt.b.locked"));
            Assert.That(context.TryGetChoice(choose.Path, out int selected), Is.True);
            Assert.That(selected, Is.EqualTo(0));
        }

        /// <summary>验证条件不满足且未配置锁定原因的选项被直接隐藏。</summary>
        [Test]
        public void Choose_HidesUnconditionallyLockedOptionWithoutReason()
        {
            FakeDialogueView view = new FakeDialogueView();
            StoryContext context = NarrativeTestKit.CreateContext(view, "opt.a", "opt.b", "line.1");
            ChooseAction choose = Choose(
                Option("opt.a").Then(Say(ActorRef.Npc("a"), "line.1")).Id("a"),
                Option("opt.b").When("flag.never").Then(Say(ActorRef.Npc("a"), "line.1")).Id("b"));
            StoryTree.Bind(choose, "test");

            NarrativeTestKit.AwaitSync(choose.PlayAsync(context, default));

            Assert.That(view.ChoiceRequests[0].Choices.Count, Is.EqualTo(1));
        }

        /// <summary>验证一次性选项被选中之后不再出现在后续的选项列表中。</summary>
        [Test]
        public void Choose_HidesOnceOptionAfterItWasChosen()
        {
            FakeDialogueView view = new FakeDialogueView();
            StoryContext context = NarrativeTestKit.CreateContext(view, "opt.a", "opt.b", "line.1");
            ChooseAction choose = Choose(
                Option("opt.a").Once().Then(Say(ActorRef.Npc("a"), "line.1")).Id("a"),
                Option("opt.b").Then(Say(ActorRef.Npc("a"), "line.1")).Id("b"));
            StoryTree.Bind(choose, "test");

            NarrativeTestKit.AwaitSync(choose.PlayAsync(context, default));
            Assert.That(view.ChoiceRequests[0].Choices.Count, Is.EqualTo(2));

            NarrativeTestKit.AwaitSync(choose.PlayAsync(context, default));
            Assert.That(view.ChoiceRequests[1].Choices.Count, Is.EqualTo(1));
            Assert.That(view.ChoiceRequests[1].Choices[0].OptionIndex, Is.EqualTo(1));
        }

        /// <summary>验证没有任何可选项时立即报错，使内容配置问题在演绎现场暴露。</summary>
        [Test]
        public void Choose_ThrowsWhenNoOptionIsSelectable()
        {
            FakeDialogueView view = new FakeDialogueView();
            StoryContext context = NarrativeTestKit.CreateContext(view, "opt.a", "opt.a.locked", "line.1");
            ChooseAction choose = Choose(
                Option("opt.a").When("flag.never").Locked("opt.a.locked").Then(Say(ActorRef.Npc("a"), "line.1")).Id("a"));
            StoryTree.Bind(choose, "test");

            Assert.Throws<System.InvalidOperationException>(() => NarrativeTestKit.AwaitSync(choose.PlayAsync(context, default)));
        }

        /// <summary>验证跳过不会替玩家做出尚未发生的选择。</summary>
        [Test]
        public void Choose_SettleDoesNothingWithoutAPreviousChoice()
        {
            FakeDialogueView view = new FakeDialogueView();
            StoryContext context = NarrativeTestKit.CreateContext(view, "opt.a", "opt.b");
            ProbeAction branchA = new ProbeAction(view.Log, "a");
            ProbeAction branchB = new ProbeAction(view.Log, "b");
            ChooseAction choose = Choose(
                Option("opt.a").Then(branchA).Id("a"),
                Option("opt.b").Then(branchB).Id("b"));
            StoryTree.Bind(choose, "test");

            choose.Settle(context);

            Assert.That(branchA.SettleCount, Is.EqualTo(0));
            Assert.That(branchB.SettleCount, Is.EqualTo(0));
        }

        /// <summary>验证已经发生过的选择在跳过与续演时会被原样落终态。</summary>
        [Test]
        public void Choose_SettleReplaysRecordedChoice()
        {
            FakeDialogueView view = new FakeDialogueView();
            StoryContext context = NarrativeTestKit.CreateContext(view, "opt.a", "opt.b");
            ProbeAction branchA = new ProbeAction(view.Log, "a");
            ProbeAction branchB = new ProbeAction(view.Log, "b");
            ChooseAction choose = Choose(
                Option("opt.a").Then(branchA).Id("a"),
                Option("opt.b").Then(branchB).Id("b"));
            StoryTree.Bind(choose, "test");
            context.RecordChoice(choose.Path, 1);

            choose.Settle(context);

            Assert.That(branchB.SettleCount, Is.EqualTo(1));
            Assert.That(branchA.SettleCount, Is.EqualTo(0));
        }
    }
}
