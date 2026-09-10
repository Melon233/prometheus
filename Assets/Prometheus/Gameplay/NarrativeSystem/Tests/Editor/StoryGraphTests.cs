using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using static Xuan.Prometheus.Narrative.Story;
using Xuan.Prometheus.Expression;

namespace Xuan.Prometheus.Narrative.Tests
{
    /// <summary>
    /// 验证图资产与 C# DSL 产出同一棵运行时剧情树，并验证编辑期校验能拦住运行时的硬性前提。
    /// 「两种作者入口、同一个运行时表示」是第五期的核心主张，这组用例就是它的回归保护。
    /// </summary>
    public sealed class StoryGraphTests
    {
        /// <summary>测试剧情的稳定标识。</summary>
        private const string StoryId = "graph_test";

        private StoryGraph graph;

        /// <summary>为每个用例准备一份等价于 DSL 版本的图资产。</summary>
        [SetUp]
        public void SetUp()
        {
            graph = BuildGraph();
        }

        /// <summary>销毁临时资产。</summary>
        [TearDown]
        public void TearDown()
        {
            if (graph != null) Object.DestroyImmediate(graph);
        }

        /// <summary>验证图资产构建出的剧情树与 DSL 版本具有完全相同的节点路径。</summary>
        [Test]
        public void Build_ProducesSamePathsAsTheDslTree()
        {
            IStoryAction fromGraph = graph.Build(out IReadOnlyList<string> errors);
            Assert.That(fromGraph, Is.Not.Null, errors.Count == 0 ? string.Empty : string.Join("\n", errors));

            IStoryAction fromDsl = BuildDsl();
            StoryTree.Bind(fromDsl, StoryId);

            Assert.That(CollectPaths(fromGraph), Is.EqualTo(CollectPaths(fromDsl)));
        }

        /// <summary>验证两种作者入口演绎出的世界状态与展示内容完全一致。</summary>
        [Test]
        public void Build_ProducesSameRuntimeBehaviourAsTheDslTree()
        {
            RunResult graphRun = Run(graph.Build(out _));
            RunResult dslRun = Run(BuildDsl());

            Assert.That(graphRun.Result, Is.EqualTo(StoryResult.Completed));
            Assert.That(dslRun.Result, Is.EqualTo(StoryResult.Completed));
            Assert.That(graphRun.Lines, Is.EqualTo(dslRun.Lines));
            Assert.That(Describe(graphRun.Variables), Is.EqualTo(Describe(dslRun.Variables)));
        }

        /// <summary>验证缺少标识或根节点时构建失败并给出可读原因。</summary>
        [Test]
        public void Build_ReportsMissingStoryIdAndRoot()
        {
            StoryGraph empty = ScriptableObject.CreateInstance<StoryGraph>();
            try
            {
                Assert.That(empty.Build(out IReadOnlyList<string> noId), Is.Null);
                Assert.That(string.Join("\n", noId), Does.Contain("StoryId"));

                empty.StoryId = "x";
                Assert.That(empty.Build(out IReadOnlyList<string> noRoot), Is.Null);
                Assert.That(string.Join("\n", noRoot), Does.Contain("根节点"));
            }
            finally
            {
                Object.DestroyImmediate(empty);
            }
        }

        /// <summary>验证节点字段缺失时构建失败，并指出是哪个节点。</summary>
        [Test]
        public void Build_ReportsIncompleteNode()
        {
            StoryGraph broken = ScriptableObject.CreateInstance<StoryGraph>();
            try
            {
                broken.StoryId = StoryId;
                SequenceNode root = new SequenceNode();
                root.AddChild(new BeatNode());
                broken.Root = root;

                Assert.That(broken.Build(out IReadOnlyList<string> errors), Is.Null);
                Assert.That(string.Join("\n", errors), Does.Contain("文本键"));
            }
            finally
            {
                Object.DestroyImmediate(broken);
            }
        }

        /// <summary>验证校验能识别写错的条件表达式。</summary>
        [Test]
        public void Validate_ReportsBrokenExpression()
        {
            IfNode branch = FindNode<IfNode>(graph);
            branch.Expression = "flag.asked &&";

            List<string> problems = new List<string>();
            Assert.That(StoryGraphValidator.Validate(graph, new StoryVariables(), null, problems), Is.False);
            Assert.That(string.Join("\n", problems), Does.Contain("表达式"));
        }

        /// <summary>验证校验能识别未被承认的标识符命名空间。</summary>
        [Test]
        public void Validate_ReportsUnknownNamespace()
        {
            IfNode branch = FindNode<IfNode>(graph);
            branch.Expression = "quest.not_registered == 1";

            List<string> problems = new List<string>();
            Assert.That(StoryGraphValidator.Validate(graph, new StoryVariables(), null, problems), Is.False);
            Assert.That(string.Join("\n", problems), Does.Contain("quest"));
        }

        /// <summary>验证校验能识别文本表里缺失的文案。</summary>
        [Test]
        public void Validate_ReportsMissingTextKey()
        {
            TextMap text = new TextMap();
            text.Load("zh-CN", new[] { new KeyValuePair<string, string>("line.1", "一") });

            List<string> problems = new List<string>();
            Assert.That(StoryGraphValidator.Validate(graph, new StoryVariables(), text, problems), Is.False);
            Assert.That(string.Join("\n", problems), Does.Contain("line.2"));
        }

        /// <summary>验证校验能识别未在舞台声明中列出的演出片段。</summary>
        [Test]
        public void Validate_ReportsUndeclaredCinematic()
        {
            SequenceNode root = (SequenceNode)graph.Root;
            root.AddChild(new CinematicNode { Location = "seq.undeclared" });

            List<string> problems = new List<string>();
            Assert.That(StoryGraphValidator.Validate(graph, new StoryVariables(), null, problems), Is.False);
            Assert.That(string.Join("\n", problems), Does.Contain("Cinematics"));
        }

        /// <summary>验证校验能识别未预加载的动画片段。</summary>
        [Test]
        public void Validate_ReportsUndeclaredPreload()
        {
            SequenceNode root = (SequenceNode)graph.Root;
            root.AddChild(new PropAnimNode { ClipLocation = "clip.undeclared" });

            List<string> problems = new List<string>();
            StoryGraphValidator.Validate(graph, new StoryVariables(), null, problems);
            Assert.That(string.Join("\n", problems), Does.Contain("Preload"));
        }

        /// <summary>验证校验能识别未参演却被动作引用的角色。</summary>
        [Test]
        public void Validate_ReportsUndeclaredActor()
        {
            SequenceNode root = (SequenceNode)graph.Root;
            MoveToNode move = new MoveToNode
            {
                Actor = new ActorRefData { kind = ActorKind.Npc, id = "ghost" },
                Destination = new AnchorData { useWorldPosition = true }
            };
            root.AddChild(move);

            List<string> problems = new List<string>();
            Assert.That(StoryGraphValidator.Validate(graph, new StoryVariables(), null, problems), Is.False);
            Assert.That(string.Join("\n", problems), Does.Contain("ghost"));
        }

        /// <summary>验证舞台声明能正确转换为运行时形式。</summary>
        [Test]
        public void BuildStage_ConvertsDeclaration()
        {
            graph.Stage.AddActor(new ActorRefData { kind = ActorKind.Npc, id = "a" }, new AnchorData { anchorId = "spot" });
            graph.Stage.AddCinematic("seq.x");
            graph.Stage.AddPreload("clip.x");

            StageSpec spec = graph.BuildStage();

            Assert.That(spec.Actors, Does.Contain(ActorRef.Npc("a")));
            Assert.That(spec.Cinematics, Does.Contain("seq.x"));
            Assert.That(spec.Preload, Does.Contain("clip.x"));
            Assert.That(spec.Placements.ContainsKey(ActorRef.Npc("a")), Is.True);
        }

        /// <summary>
        /// 验证接纳判定不会改动数据。
        /// 编辑器的创建菜单要逐个判断节点类型能否挂上去，如果用「先加再删」试探，
        /// IfNode 会把 else 分支顶到 then 上，静默改坏剧情。
        /// </summary>
        [Test]
        public void CanAddChild_DoesNotMutate()
        {
            IfNode branch = new IfNode { Expression = "flag.a" };
            SetVariableNode thenNode = new SetVariableNode { Path = "flag.then" };
            SetVariableNode elseNode = new SetVariableNode { Path = "flag.else" };
            branch.AddChild(thenNode);
            branch.AddChild(elseNode);

            Assert.That(branch.CanAddChild(new WaitNode()), Is.False);
            Assert.That(branch.ThenBranch, Is.SameAs(thenNode));
            Assert.That(branch.ElseBranch, Is.SameAs(elseNode));
        }

        /// <summary>验证选项分支只接纳选项节点。</summary>
        [Test]
        public void ChooseNode_OnlyAcceptsOptionNodes()
        {
            ChooseNode choose = new ChooseNode();
            Assert.That(choose.CanAddChild(new WaitNode()), Is.False);
            Assert.That(choose.CanAddChild(new ChoiceOptionNode()), Is.True);
            Assert.That(new SequenceNode().CanAddChild(new ChoiceOptionNode()), Is.True, "顺序节点本身接纳任意子节点；非法组合在构建时报错。");
        }

        /// <summary>构建测试用的图资产。</summary>
        private static StoryGraph BuildGraph()
        {
            StoryGraph created = ScriptableObject.CreateInstance<StoryGraph>();
            created.StoryId = StoryId;

            ActorRefData speaker = new ActorRefData { kind = ActorKind.Npc, id = "a" };

            BeatNode first = new BeatNode { Speaker = speaker, TextKey = "line.1" };
            first.Id = "b1";
            first.AddChild(new SetVariableNode { Path = "flag.greeted", Kind = StoryValueKind.Bool, Value = "true" });

            ChoiceOptionNode ask = new ChoiceOptionNode { TextKey = "opt.a" };
            ask.Id = "ask";
            SequenceNode askBranch = new SequenceNode();
            BeatNode askedLine = new BeatNode { Speaker = speaker, TextKey = "line.2" };
            askedLine.Id = "asked_line";
            askBranch.AddChild(askedLine);
            askBranch.AddChild(new SetVariableNode { Path = "flag.asked", Kind = StoryValueKind.Bool, Value = "true" });
            ask.AddChild(askBranch);

            ChoiceOptionNode leave = new ChoiceOptionNode { TextKey = "opt.b" };
            leave.Id = "leave";
            leave.AddChild(new SetVariableNode { Path = "flag.left", Kind = StoryValueKind.Bool, Value = "true" });

            ChooseNode choose = new ChooseNode();
            choose.Id = "choice";
            choose.AddChild(ask);
            choose.AddChild(leave);

            IfNode branch = new IfNode { Expression = "flag.asked" };
            branch.Id = "branch";
            branch.AddChild(new SetVariableNode { Path = "var.result", Kind = StoryValueKind.Text, Value = "asked" });
            branch.AddChild(new SetVariableNode { Path = "var.result", Kind = StoryValueKind.Text, Value = "left" });

            BeatNode tail = new BeatNode { Speaker = speaker, TextKey = "line.3" };
            tail.Id = "tail";

            SequenceNode root = new SequenceNode();
            root.AddChild(first);
            root.AddChild(choose);
            root.AddChild(branch);
            root.AddChild(tail);
            created.Root = root;
            return created;
        }

        /// <summary>用 C# DSL 构建等价的剧情树。</summary>
        private static IStoryAction BuildDsl()
        {
            ActorRef speaker = ActorRef.Npc("a");
            return Seq(
                Say(speaker, "line.1").With(SetFlag("greeted")).Id("b1"),
                Choose(
                    Option("opt.a").Then(Seq(Say(speaker, "line.2").Id("asked_line"), SetFlag("asked"))).Id("ask"),
                    Option("opt.b").Then(SetFlag("left")).Id("leave")).Id("choice"),
                If("flag.asked", SetVar("var.result", "asked"), SetVar("var.result", "left")).Id("branch"),
                Say(speaker, "line.3").Id("tail"));
        }

        /// <summary>演绎一棵剧情树并采集可观察结果。</summary>
        private static RunResult Run(IStoryAction root)
        {
            FakeDialogueView view = new FakeDialogueView();
            StoryContext context = NarrativeTestKit.CreateContext(view, "line.1", "line.2", "line.3", "opt.a", "opt.b");
            StoryResult result = NarrativeTestKit.AwaitSync(new StoryRunner().RunAsync(root, context, StoryId));
            List<string> lines = new List<string>(view.Lines.Count);
            for (int index = 0; index < view.Lines.Count; index++) lines.Add(view.Lines[index].Content);
            return new RunResult(result, lines, context.Variables.Capture());
        }

        /// <summary>收集一棵剧情树的全部节点路径。</summary>
        private static List<string> CollectPaths(IStoryAction root)
        {
            List<string> paths = new List<string>();
            foreach (IStoryAction action in StoryTree.Enumerate(root)) paths.Add(action.Path.Value);
            return paths;
        }

        /// <summary>在图中查找第一个指定类型的节点。</summary>
        private static TNode FindNode<TNode>(StoryGraph target) where TNode : StoryNode
        {
            foreach (StoryNode node in target.EnumerateNodes())
            {
                if (node is TNode typed) return typed;
            }
            return null;
        }

        /// <summary>把变量快照拼成可比较的文本。</summary>
        private static string Describe(IReadOnlyDictionary<string, StoryValue> values)
        {
            List<string> parts = new List<string>(values.Count);
            foreach (KeyValuePair<string, StoryValue> pair in values) parts.Add($"{pair.Key}={pair.Value}");
            parts.Sort();
            return string.Join(", ", parts);
        }

        /// <summary>一次演绎的可观察结果。</summary>
        private readonly struct RunResult
        {
            /// <summary>记录一次演绎的结果。</summary>
            internal RunResult(StoryResult result, List<string> lines, IReadOnlyDictionary<string, StoryValue> variables)
            {
                Result = result;
                Lines = lines;
                Variables = variables;
            }

            /// <summary>获取结束原因。</summary>
            internal StoryResult Result { get; }

            /// <summary>获取展示过的台词内容。</summary>
            internal List<string> Lines { get; }

            /// <summary>获取结束时的变量快照。</summary>
            internal IReadOnlyDictionary<string, StoryValue> Variables { get; }
        }
    }
}
