using System.Collections.Generic;
using NUnit.Framework;
using static Xuan.Prometheus.Narrative.Story;

namespace Xuan.Prometheus.Narrative.Tests
{
    /// <summary>验证组合子的执行顺序、分支选择、提前结束与路径绑定。</summary>
    public sealed class StoryActionTests
    {
        /// <summary>验证顺序组合子按声明顺序演绎子节点。</summary>
        [Test]
        public void Sequence_PlaysChildrenInOrder()
        {
            List<string> log = new List<string>();
            FakeDialogueView view = new FakeDialogueView();
            StoryContext context = NarrativeTestKit.CreateContext(view);
            IStoryAction root = Seq(new ProbeAction(log, "a"), new ProbeAction(log, "b"), new ProbeAction(log, "c"));
            StoryTree.Bind(root, "test");

            NarrativeTestKit.AwaitSync(root.PlayAsync(context, default));

            Assert.That(log, Is.EqualTo(new[] { "play:a", "play:b", "play:c" }));
        }

        /// <summary>验证并发组合子会启动全部子节点。</summary>
        [Test]
        public void Parallel_PlaysAllChildren()
        {
            List<string> log = new List<string>();
            FakeDialogueView view = new FakeDialogueView();
            StoryContext context = NarrativeTestKit.CreateContext(view);
            ProbeAction first = new ProbeAction(log, "a");
            ProbeAction second = new ProbeAction(log, "b");
            IStoryAction root = Par(first, second);
            StoryTree.Bind(root, "test");

            NarrativeTestKit.AwaitSync(root.PlayAsync(context, default));

            Assert.That(first.PlayCount, Is.EqualTo(1));
            Assert.That(second.PlayCount, Is.EqualTo(1));
        }

        /// <summary>验证条件分支只演绎命中的一侧。</summary>
        [Test]
        public void If_PlaysOnlySelectedBranch()
        {
            List<string> log = new List<string>();
            FakeDialogueView view = new FakeDialogueView();
            StoryContext context = NarrativeTestKit.CreateContext(view);
            ProbeAction thenProbe = new ProbeAction(log, "then");
            ProbeAction elseProbe = new ProbeAction(log, "else");
            IStoryAction root = If("flag.ok", thenProbe, elseProbe);
            StoryTree.Bind(root, "test");

            NarrativeTestKit.AwaitSync(root.PlayAsync(context, default));
            Assert.That(elseProbe.PlayCount, Is.EqualTo(1));
            Assert.That(thenProbe.PlayCount, Is.EqualTo(0));

            context.Variables.SetFlag("ok", true);
            NarrativeTestKit.AwaitSync(root.PlayAsync(context, default));
            Assert.That(thenProbe.PlayCount, Is.EqualTo(1));
            Assert.That(elseProbe.PlayCount, Is.EqualTo(1));
        }

        /// <summary>验证条件分支落终态时同样只作用于命中的一侧。</summary>
        [Test]
        public void If_SettlesOnlySelectedBranch()
        {
            List<string> log = new List<string>();
            FakeDialogueView view = new FakeDialogueView();
            StoryContext context = NarrativeTestKit.CreateContext(view);
            ProbeAction thenProbe = new ProbeAction(log, "then");
            ProbeAction elseProbe = new ProbeAction(log, "else");
            IStoryAction root = If("flag.ok", thenProbe, elseProbe);
            StoryTree.Bind(root, "test");

            root.Settle(context);

            Assert.That(elseProbe.SettleCount, Is.EqualTo(1));
            Assert.That(thenProbe.SettleCount, Is.EqualTo(0));
        }

        /// <summary>验证提前结束只作用于最近的一层顺序组合子。</summary>
        [Test]
        public void Break_EndsNearestSequenceOnly()
        {
            List<string> log = new List<string>();
            FakeDialogueView view = new FakeDialogueView();
            StoryContext context = NarrativeTestKit.CreateContext(view);
            IStoryAction root = Seq(
                Seq(new ProbeAction(log, "inner1"), Break(), new ProbeAction(log, "inner2")),
                new ProbeAction(log, "outer"));
            StoryTree.Bind(root, "test");

            NarrativeTestKit.AwaitSync(root.PlayAsync(context, default));

            Assert.That(log, Is.EqualTo(new[] { "play:inner1", "play:outer" }));
        }

        /// <summary>验证落终态与正常演绎在提前结束上的行为一致。</summary>
        [Test]
        public void Break_IsRespectedWhileSettling()
        {
            List<string> log = new List<string>();
            FakeDialogueView view = new FakeDialogueView();
            StoryContext context = NarrativeTestKit.CreateContext(view);
            IStoryAction root = Seq(new ProbeAction(log, "a"), Break(), new ProbeAction(log, "b"));
            StoryTree.Bind(root, "test");

            root.Settle(context);

            Assert.That(log, Is.EqualTo(new[] { "settle:a" }));
        }

        /// <summary>验证路径按显式标识与子节点序号自顶向下生成。</summary>
        [Test]
        public void Bind_GeneratesStablePaths()
        {
            List<string> log = new List<string>();
            ProbeAction first = new ProbeAction(log, "a");
            ProbeAction second = new ProbeAction(log, "b");
            second.Id("named");
            IStoryAction root = Seq(first, Seq(second));
            StoryTree.Bind(root, "ch1");

            Assert.That(root.Path.Value, Is.EqualTo("ch1"));
            Assert.That(first.Path.Value, Is.EqualTo("ch1/0"));
            Assert.That(second.Path.Value, Is.EqualTo("ch1/1/named"));
        }

        /// <summary>验证重复路径在绑定校验阶段被拒绝。</summary>
        [Test]
        public void ValidateUniquePaths_RejectsDuplicateExplicitIds()
        {
            List<string> log = new List<string>();
            ProbeAction first = new ProbeAction(log, "a");
            ProbeAction second = new ProbeAction(log, "b");
            first.Id("same");
            second.Id("same");
            IStoryAction root = Seq(first, second);
            StoryTree.Bind(root, "ch1");

            Assert.Throws<System.InvalidOperationException>(() => StoryTree.ValidateUniquePaths(root));
        }

        /// <summary>验证祖先判定用于按前缀定位子树。</summary>
        [Test]
        public void StoryPath_AncestorCheckUsesSegmentBoundary()
        {
            StoryPath parent = StoryPath.Root("ch1").Append("scene");
            Assert.That(parent.IsAncestorOfOrSame(parent), Is.True);
            Assert.That(parent.IsAncestorOfOrSame(parent.Append("beat")), Is.True);
            Assert.That(parent.IsAncestorOfOrSame(StoryPath.Root("ch1").Append("scenery")), Is.False);
        }
    }
}
