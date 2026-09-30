using System.Collections.Generic;
using NUnit.Framework;
using static Xuan.Prometheus.Narrative.Story;
using Xuan.Prometheus.Expression;

namespace Xuan.Prometheus.Narrative.Tests
{
    /// <summary>
    /// 验证设计规则 R2：任何动作的 Settle 必须幂等，且「完整演绎」与「落终态」得到的世界状态一致。
    /// 这是跳过、断点续演与编辑器预览三项功能共用同一条代码路径的前提，也是本系统最关键的一组回归测试。
    /// </summary>
    public sealed class SettleParityTests
    {
        /// <summary>验证完整演绎与仅落终态在同样的选择结果下产生完全一致的变量状态。</summary>
        [Test]
        public void FullPlay_AndSettleOnly_ProduceIdenticalVariables()
        {
            FakeDialogueView playedView = new FakeDialogueView();
            StoryContext played = NarrativeTestKit.CreateContext(playedView, StoryTextKeys);
            IStoryAction playedRoot = BuildBranchingStory();
            StoryTree.Bind(playedRoot, StoryId);
            NarrativeTestKit.AwaitSync(playedRoot.PlayAsync(played, default));

            FakeDialogueView settledView = new FakeDialogueView();
            StoryContext settled = NarrativeTestKit.CreateContext(settledView, StoryTextKeys);
            // 重建剧情树模拟新会话；路径由结构决定，因此两次构建的寻址完全一致。
            IStoryAction settledRoot = BuildBranchingStory();
            StoryTree.Bind(settledRoot, StoryId);
            settled.RestoreChoices(played.CaptureChoices());
            settledRoot.Settle(settled);

            AssertVariablesEqual(played, settled);
            Assert.That(settledView.Lines, Is.Empty, "落终态不应产生任何对话表现。");
        }

        /// <summary>验证重复落终态不会改变结果，即 Settle 幂等。</summary>
        [Test]
        public void Settle_IsIdempotent()
        {
            FakeDialogueView view = new FakeDialogueView();
            StoryContext once = NarrativeTestKit.CreateContext(view, StoryTextKeys);
            IStoryAction root = BuildBranchingStory();
            StoryTree.Bind(root, StoryId);
            once.RecordChoice(new StoryPath($"{StoryId}/choice"), 0);

            root.Settle(once);
            IReadOnlyDictionary<string, StoryValue> afterFirst = once.Variables.Capture();
            root.Settle(once);
            IReadOnlyDictionary<string, StoryValue> afterSecond = once.Variables.Capture();

            AssertDictionariesEqual(afterFirst, afterSecond);
        }

        /// <summary>验证演绎途中跳过后的世界状态与完整演绎完全一致。</summary>
        [Test]
        public void Skip_ProducesSameWorldStateAsFullPlay()
        {
            FakeDialogueView fullView = new FakeDialogueView();
            StoryContext full = NarrativeTestKit.CreateContext(fullView, StoryTextKeys);
            StoryRunner fullRunner = new StoryRunner();
            StoryResult fullResult = NarrativeTestKit.AwaitSync(fullRunner.RunAsync(BuildLinearStory(), full, StoryId));

            FakeDialogueView skipView = new FakeDialogueView();
            StoryContext skipped = NarrativeTestKit.CreateContext(skipView, StoryTextKeys);
            StoryRunner skipRunner = new StoryRunner();
            // 在第一句台词进入时立即请求跳过，剩余节点全部走落终态。
            skipView.BeginLineHook = line => skipRunner.RequestSkip(skipped);
            StoryResult skipResult = NarrativeTestKit.AwaitSync(skipRunner.RunAsync(BuildLinearStory(), skipped, StoryId));

            Assert.That(fullResult, Is.EqualTo(StoryResult.Completed));
            Assert.That(skipResult, Is.EqualTo(StoryResult.Skipped));
            Assert.That(skipView.Lines.Count, Is.EqualTo(1), "跳过之后不应再展示任何台词。");
            AssertVariablesEqual(full, skipped);
        }

        /// <summary>
        /// 验证中止与跳过是两种不同的结束方式：
        /// 中止让剩余节点既不演绎也不落终态，跳过则让剩余节点全部落终态。
        /// </summary>
        [Test]
        public void Abort_LeavesRemainingNodesUntouchedUnlikeSkip()
        {
            FakeDialogueView abortView = new FakeDialogueView();
            StoryContext abortContext = NarrativeTestKit.CreateContext(abortView, StoryTextKeys);
            StoryRunner abortRunner = new StoryRunner();
            List<string> abortLog = new List<string>();
            ProbeAction abortTail = new ProbeAction(abortLog, "tail");
            abortView.BeginLineHook = line => abortRunner.Abort();
            StoryResult abortResult = NarrativeTestKit.AwaitSync(abortRunner.RunAsync(BuildStoryWithTail(abortTail), abortContext, StoryId));

            Assert.That(abortResult, Is.EqualTo(StoryResult.Aborted));
            Assert.That(abortContext.IsSkipping, Is.False);
            Assert.That(abortTail.PlayCount, Is.EqualTo(0));
            Assert.That(abortTail.SettleCount, Is.EqualTo(0));

            FakeDialogueView skipView = new FakeDialogueView();
            StoryContext skipContext = NarrativeTestKit.CreateContext(skipView, StoryTextKeys);
            StoryRunner skipRunner = new StoryRunner();
            List<string> skipLog = new List<string>();
            ProbeAction skipTail = new ProbeAction(skipLog, "tail");
            skipView.BeginLineHook = line => skipRunner.RequestSkip(skipContext);
            StoryResult skipResult = NarrativeTestKit.AwaitSync(skipRunner.RunAsync(BuildStoryWithTail(skipTail), skipContext, StoryId));

            Assert.That(skipResult, Is.EqualTo(StoryResult.Skipped));
            Assert.That(skipTail.PlayCount, Is.EqualTo(0));
            Assert.That(skipTail.SettleCount, Is.EqualTo(1));
        }

        /// <summary>构建一段以指定探针结尾的线性剧情，用于区分中止与跳过的收尾行为。</summary>
        private static IStoryAction BuildStoryWithTail(IStoryAction tail)
        {
            return Seq(Say(ActorRef.Npc("a"), "line.1").Id("b1"), tail);
        }

        /// <summary>剧情树的稳定标识。</summary>
        private const string StoryId = "parity";

        /// <summary>测试剧情使用的文本键。</summary>
        private static readonly string[] StoryTextKeys = { "line.1", "line.2", "opt.a", "opt.b" };

        /// <summary>构建一段包含选项与条件分支的测试剧情。</summary>
        private static IStoryAction BuildBranchingStory()
        {
            ActorRef speaker = ActorRef.Npc("a");
            return Seq(
                Say(speaker, "line.1").With(SetFlag("greeted")).Id("b1"),
                Choose(
                    Option("opt.a").Then(Seq(Say(speaker, "line.2"), SetFlag("asked"))).Id("a"),
                    Option("opt.b").Then(SetFlag("left")).Id("b")).Id("choice"),
                If("flag.asked", SetVar("var.result", "asked"), SetVar("var.result", "left")).Id("branch"),
                SetVar("var.done", 1).Id("done"));
        }

        /// <summary>构建一段不含选项的线性测试剧情。</summary>
        private static IStoryAction BuildLinearStory()
        {
            ActorRef speaker = ActorRef.Npc("a");
            return Seq(
                Say(speaker, "line.1").With(SetFlag("m1")).Id("b1"),
                Say(speaker, "line.2").With(SetVar("var.n", 5)).Id("b2"),
                SetFlag("done").Id("done"));
        }

        /// <summary>断言两个上下文的变量状态完全一致。</summary>
        private static void AssertVariablesEqual(StoryContext expected, StoryContext actual)
        {
            AssertDictionariesEqual(expected.Variables.Capture(), actual.Variables.Capture());
        }

        /// <summary>断言两份变量快照完全一致。</summary>
        private static void AssertDictionariesEqual(IReadOnlyDictionary<string, StoryValue> expected, IReadOnlyDictionary<string, StoryValue> actual)
        {
            Assert.That(actual.Count, Is.EqualTo(expected.Count), $"变量数量不一致：期望 {Describe(expected)}，实际 {Describe(actual)}");
            foreach (KeyValuePair<string, StoryValue> pair in expected)
            {
                Assert.That(actual.ContainsKey(pair.Key), Is.True, $"缺少变量 '{pair.Key}'。");
                Assert.That(actual[pair.Key], Is.EqualTo(pair.Value), $"变量 '{pair.Key}' 不一致。");
            }
        }

        /// <summary>把变量快照拼成便于阅读的诊断文本。</summary>
        private static string Describe(IReadOnlyDictionary<string, StoryValue> values)
        {
            List<string> parts = new List<string>(values.Count);
            foreach (KeyValuePair<string, StoryValue> pair in values) parts.Add($"{pair.Key}={pair.Value}");
            parts.Sort();
            return string.Join(", ", parts);
        }
    }
}
