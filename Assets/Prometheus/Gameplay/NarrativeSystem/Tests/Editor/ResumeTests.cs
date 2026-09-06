using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine.TestTools;
using static Xuan.Prometheus.Narrative.Story;

namespace Xuan.Prometheus.Narrative.Tests
{
    /// <summary>
    /// 验证断点续演：从任意节拍继续演绎，得到的世界状态必须与完整演绎完全一致。
    /// <para>
    /// 这组用例按剧情树中的每个节拍自动参数化展开，是设计文档 §20 要求的「遍历每个节拍对拍」的实现，
    /// 也是 <c>Settle</c> 幂等契约在续演路径上的回归保护。
    /// </para>
    /// </summary>
    public sealed class ResumeTests
    {
        /// <summary>测试剧情的稳定标识。</summary>
        private const string StoryId = "resume_test";

        /// <summary>枚举测试剧情中的全部节拍路径，供参数化用例展开。</summary>
        public static IEnumerable<string> BeatPaths()
        {
            IStoryAction root = BuildStory();
            StoryTree.Bind(root, StoryId);
            List<string> paths = new List<string>();
            foreach (IStoryAction action in StoryTree.Enumerate(root))
            {
                if (action is Beat) paths.Add(action.Path.Value);
            }
            return paths;
        }

        /// <summary>验证从任意一个节拍续演，最终变量状态与完整演绎一致。</summary>
        [Test]
        [TestCaseSource(nameof(BeatPaths))]
        public void Resume_FromAnyBeat_MatchesFullPlayFinalState(string beatPath)
        {
            RunResult full = RunFull();
            RunResult resumed = RunResume(new StoryPath(beatPath), full);

            Assert.That(resumed.Result, Is.EqualTo(StoryResult.Completed), $"从 '{beatPath}' 续演未能正常结束。");
            AssertVariablesEqual(full.Variables, resumed.Variables, beatPath);
        }

        /// <summary>验证续演确实跳过了续演点之前的表现，而不是重新演一遍。</summary>
        [Test]
        public void Resume_SkipsPresentationBeforeTheResumePoint()
        {
            RunResult full = RunFull();
            RunResult resumed = RunResume(new StoryPath(StoryId + "/tail"), full);

            Assert.That(resumed.Result, Is.EqualTo(StoryResult.Completed));
            Assert.That(resumed.LineCount, Is.LessThan(full.LineCount), "续演不应重复播放续演点之前的台词。");
            Assert.That(resumed.ChoiceRequestCount, Is.EqualTo(0), "续演不应重新向玩家提问已经做过的选择。");
            AssertVariablesEqual(full.Variables, resumed.Variables, "tail");
        }

        /// <summary>验证从第一个节拍续演等价于完整演绎。</summary>
        [Test]
        public void Resume_FromFirstBeat_EqualsFullPlay()
        {
            RunResult full = RunFull();
            RunResult resumed = RunResume(new StoryPath(StoryId + "/b1"), full);

            Assert.That(resumed.LineCount, Is.EqualTo(full.LineCount));
            AssertVariablesEqual(full.Variables, resumed.Variables, "b1");
        }

        /// <summary>验证续演点不存在于当前剧情树时立即报错，而不是静默把整棵树落终态。</summary>
        [Test]
        public void Resume_RejectsUnknownPath()
        {
            FakeDialogueView view = new FakeDialogueView();
            StoryContext context = CreateContext(view);
            IStoryAction root = BuildStory();

            System.InvalidOperationException error = Assert.Throws<System.InvalidOperationException>(
                () => NarrativeTestKit.AwaitSync(new StoryRunner().RunAsync(root, context, StoryId, new StoryPath(StoryId + "/does_not_exist"), StoryPlayMode.Normal)));
            Assert.That(error.Message, Does.Contain("out of sync"));
        }

        /// <summary>验证选项分支缺少选择记录时拒绝续演，而不是替玩家重新选一次。</summary>
        [Test]
        public void Resume_IntoChoiceBranchRequiresRecordedChoice()
        {
            LogAssert.ignoreFailingMessages = true;
            try
            {
                FakeDialogueView view = new FakeDialogueView();
                StoryContext context = CreateContext(view);
                IStoryAction root = BuildStory();

                // 没有恢复任何选择结果就试图续演到选项分支内部。
                StoryResult result = NarrativeTestKit.AwaitSync(
                    new StoryRunner().RunAsync(root, context, StoryId, new StoryPath(StoryId + "/choice/ask/0/asked_line"), StoryPlayMode.Normal));

                Assert.That(result, Is.EqualTo(StoryResult.Failed));
            }
            finally
            {
                LogAssert.ignoreFailingMessages = false;
            }
        }

        /// <summary>验证预览模式下节拍不等待玩家输入。</summary>
        [Test]
        public void Preview_DoesNotWaitForPlayerInput()
        {
            FakeDialogueView view = new FakeDialogueView();
            StoryContext context = CreateContext(view);
            IStoryAction root = BuildStory();
            StoryResult result = NarrativeTestKit.AwaitSync(new StoryRunner().RunAsync(root, context, StoryId, StoryPath.None, StoryPlayMode.Preview));

            Assert.That(result, Is.EqualTo(StoryResult.Completed));
            Assert.That(view.Policies, Is.Not.Empty);
            for (int index = 0; index < view.Policies.Count; index++)
            {
                Assert.That(view.Policies[index].Kind, Is.EqualTo(AdvanceKind.Immediate), "预览模式下所有节拍都应立即推进。");
            }
        }

        /// <summary>验证存档可以完整往返：变量、已选选项、选择结果与已看过剧情都被保留。</summary>
        [Test]
        public void Snapshot_RoundTripsAllPersistentState()
        {
            RunResult full = RunFull();

            NarrativeSnapshot snapshot = NarrativeSnapshot.Capture(full.Context, new[] { StoryId }, StoryId, new StoryPath(StoryId + "/tail"));
            string json = snapshot.ToJson();

            FakeDialogueView view = new FakeDialogueView();
            StoryContext restored = CreateContext(view);
            NarrativeSnapshot parsed = NarrativeSnapshot.FromJson(json);
            parsed.RestoreTo(restored);

            AssertVariablesEqual(full.Variables, restored.Variables.Capture(), "snapshot");
            Assert.That(parsed.seenStories, Does.Contain(StoryId));
            Assert.That(parsed.HasResumePoint, Is.True);
            Assert.That(parsed.GetResumePath().Value, Is.EqualTo(StoryId + "/tail"));
            Assert.That(restored.TryGetChoice(new StoryPath(StoryId + "/choice"), out int option), Is.True);
            Assert.That(option, Is.EqualTo(0));
        }

        /// <summary>验证快照能够还原各种类型的变量值。</summary>
        [Test]
        public void Snapshot_PreservesValueKinds()
        {
            FakeDialogueView view = new FakeDialogueView();
            StoryContext source = CreateContext(view);
            source.Variables.SetFlag("done", true);
            source.Variables.Set("var.count", 42);
            source.Variables.Set("var.state", "Done");

            NarrativeSnapshot snapshot = NarrativeSnapshot.FromJson(NarrativeSnapshot.Capture(source, null, null, StoryPath.None).ToJson());
            StoryContext target = CreateContext(new FakeDialogueView());
            snapshot.RestoreTo(target);

            Assert.That(target.Variables.GetFlag("done"), Is.True);
            Assert.That(target.Variables.Get("var.count").AsNumber(), Is.EqualTo(42d));
            Assert.That(target.Variables.Get("var.state").AsText(), Is.EqualTo("Done"));
            Assert.That(target.EvaluateValue("var.count >= 42").AsBool(), Is.True);
        }

        /// <summary>完整演绎一遍测试剧情。</summary>
        private static RunResult RunFull()
        {
            FakeDialogueView view = new FakeDialogueView();
            StoryContext context = CreateContext(view);
            IStoryAction root = BuildStory();
            StoryResult result = NarrativeTestKit.AwaitSync(new StoryRunner().RunAsync(root, context, StoryId));
            return new RunResult(context, view, result);
        }

        /// <summary>按指定续演点演绎一遍，并先从完整演绎的结果恢复选择记录。</summary>
        private static RunResult RunResume(StoryPath resumeAt, RunResult full)
        {
            FakeDialogueView view = new FakeDialogueView();
            StoryContext context = CreateContext(view);
            context.RestoreChoices(full.Context.CaptureChoices());
            context.RestoreChosenOptions(full.Context.ChosenOptions);
            IStoryAction root = BuildStory();
            StoryResult result = NarrativeTestKit.AwaitSync(new StoryRunner().RunAsync(root, context, StoryId, resumeAt, StoryPlayMode.Normal));
            return new RunResult(context, view, result);
        }

        /// <summary>创建一个已经初始化好条件变量的测试上下文。</summary>
        private static StoryContext CreateContext(FakeDialogueView view)
        {
            StoryContext context = NarrativeTestKit.CreateContext(view, "line.1", "line.2", "line.3", "line.4", "opt.a", "opt.b");
            // var.* 读取未定义变量会报错，因此条件用到的变量必须显式初始化。
            context.Variables.Set("var.trust", 1);
            return context;
        }

        /// <summary>
        /// 构建测试剧情：覆盖顺序、并发、选项、条件分支与变量写入。
        /// 每次调用都重新构建，模拟「新会话重建剧情树后按路径续演」的真实场景。
        /// </summary>
        private static IStoryAction BuildStory()
        {
            ActorRef speaker = ActorRef.Npc("a");
            return Seq(
                Say(speaker, "line.1").With(SetFlag("greeted")).Id("b1"),
                Par(
                    Say(speaker, "line.2").With(SetVar("var.n", 5)).Id("p1"),
                    SetFlag("par_done").Id("p2")).Id("par"),
                Choose(
                    Option("opt.a")
                        .Then(Seq(Say(speaker, "line.3").Id("asked_line"), SetFlag("asked")))
                        .Id("ask"),
                    Option("opt.b")
                        .Then(SetFlag("left"))
                        .Id("leave")).Id("choice"),
                If("flag.asked", SetVar("var.result", "asked"), SetVar("var.result", "left")).Id("branch"),
                Say(speaker, "line.4").Id("tail"),
                SetVar("var.done", 1).Id("done"));
        }

        /// <summary>断言两份变量快照完全一致。</summary>
        private static void AssertVariablesEqual(IReadOnlyDictionary<string, StoryValue> expected, IReadOnlyDictionary<string, StoryValue> actual, string label)
        {
            Assert.That(actual.Count, Is.EqualTo(expected.Count), $"[{label}] 变量数量不一致：期望 {Describe(expected)}，实际 {Describe(actual)}");
            foreach (KeyValuePair<string, StoryValue> pair in expected)
            {
                Assert.That(actual.ContainsKey(pair.Key), Is.True, $"[{label}] 缺少变量 '{pair.Key}'。");
                Assert.That(actual[pair.Key], Is.EqualTo(pair.Value), $"[{label}] 变量 '{pair.Key}' 不一致。");
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

        /// <summary>一次演绎的结果与可观察副产物。</summary>
        private sealed class RunResult
        {
            /// <summary>记录一次演绎的结果。</summary>
            internal RunResult(StoryContext context, FakeDialogueView view, StoryResult result)
            {
                Context = context;
                Result = result;
                LineCount = view.Lines.Count;
                ChoiceRequestCount = view.ChoiceRequests.Count;
                Variables = context.Variables.Capture();
            }

            /// <summary>获取本次演绎使用的上下文。</summary>
            internal StoryContext Context { get; }

            /// <summary>获取本次演绎的结束原因。</summary>
            internal StoryResult Result { get; }

            /// <summary>获取本次演绎展示过的台词数量。</summary>
            internal int LineCount { get; }

            /// <summary>获取本次演绎展示过的选项请求数量。</summary>
            internal int ChoiceRequestCount { get; }

            /// <summary>获取本次演绎结束时的变量快照。</summary>
            internal IReadOnlyDictionary<string, StoryValue> Variables { get; }
        }
    }
}
