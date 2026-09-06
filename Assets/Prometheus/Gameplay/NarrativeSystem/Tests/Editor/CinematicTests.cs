using NUnit.Framework;
using UnityEngine;
using UnityEngine.Playables;
using UnityEngine.Timeline;

namespace Xuan.Prometheus.Narrative.Tests
{
    /// <summary>
    /// 验证演出片段的区间解析与落终态。
    /// 这里直接构造 TimelineAsset 与 PlayableDirector，因此不依赖演示场景的播放时序，
    /// 可以确定性地断言「按 cue 分段」与「Settle 推到区间末尾」两件事。
    /// </summary>
    public sealed class CinematicTests
    {
        /// <summary>演出片段在测试资源端口中的地址。</summary>
        private const string Location = "seq.test";

        /// <summary>区间中点标记名。</summary>
        private const string MidCue = "cue_mid";

        /// <summary>片段总时长。</summary>
        private const double Duration = 2.4d;

        /// <summary>中点标记所处时刻。</summary>
        private const double MidTime = 1.2d;

        private FakeStageEnvironment environment;
        private TimelineAsset timeline;
        private StageScope scope;
        private StoryContext context;

        /// <summary>为每个用例准备一份带 cue 标记的演出片段与已进入的舞台。</summary>
        [SetUp]
        public void SetUp()
        {
            environment = new FakeStageEnvironment();
            timeline = ScriptableObject.CreateInstance<TimelineAsset>();
            timeline.name = "TestSequence";
            timeline.durationMode = TimelineAsset.DurationMode.FixedLength;
            timeline.fixedDuration = Duration;
            timeline.CreateMarkerTrack();
            CinematicCueMarker cue = timeline.markerTrack.CreateMarker<CinematicCueMarker>(MidTime);
            cue.CueName = MidCue;
            environment.Assets.Register(Location, timeline);

            StageSpec spec = new StageSpec { FadeSeconds = 0f, LetterboxRatio = 0f, LockInput = false, TakeCameraControl = false };
            spec.WithCinematics(Location);
            scope = NarrativeTestKit.AwaitSync(Stage.EnterAsync(spec, environment.Services));
            context = NarrativeTestKit.CreateContext(new FakeDialogueView());
            context.Stage = scope;
        }

        /// <summary>退出舞台并清理临时资源。</summary>
        [TearDown]
        public void TearDown()
        {
            NarrativeTestKit.AwaitSync(scope.DisposeAsync());
            environment.Dispose();
            if (timeline != null) Object.DestroyImmediate(timeline);
        }

        /// <summary>验证舞台在进入阶段就为声明过的片段创建好播放器。</summary>
        [Test]
        public void Stage_PreparesDeclaredCinematics()
        {
            Assert.That(scope.TryGetDirector(Location, out PlayableDirector director), Is.True);
            Assert.That(director.playableAsset, Is.SameAs(timeline));
            Assert.That(director.duration, Is.EqualTo(Duration).Within(0.001d));
        }

        /// <summary>验证按 cue 分段的片段落终态时只推进到该 cue，而不是整段末尾。</summary>
        [Test]
        public void Settle_StopsAtTheDeclaredCue()
        {
            IStoryAction first = Story.Cinematic(Location, null, MidCue);
            StoryTree.Bind(first, "test");

            first.Settle(context);

            scope.TryGetDirector(Location, out PlayableDirector director);
            Assert.That(director.time, Is.EqualTo(MidTime).Within(0.001d));
        }

        /// <summary>验证未指定区间的片段落终态时推进到整段末尾。</summary>
        [Test]
        public void Settle_WithoutCueStopsAtTheEnd()
        {
            IStoryAction whole = Story.Cinematic(Location);
            StoryTree.Bind(whole, "test");

            whole.Settle(context);

            scope.TryGetDirector(Location, out PlayableDirector director);
            Assert.That(director.time, Is.EqualTo(Duration).Within(0.001d));
        }

        /// <summary>
        /// 验证分段落终态是连续的：先落到 cue，再落一次整段，时间从 cue 继续推到末尾。
        /// 这正是「一段演出被拆开穿插对话」在跳过与续演路径上的行为。
        /// </summary>
        [Test]
        public void Settle_SegmentsAreContinuousOnTheSameDirector()
        {
            IStoryAction first = Story.Cinematic(Location, null, MidCue);
            IStoryAction second = Story.Cinematic(Location);
            StoryTree.Bind(Story.Seq(first, second), "test");

            first.Settle(context);
            scope.TryGetDirector(Location, out PlayableDirector director);
            Assert.That(director.time, Is.EqualTo(MidTime).Within(0.001d), "第一段应停在 cue 上。");

            second.Settle(context);
            Assert.That(director.time, Is.EqualTo(Duration).Within(0.001d), "第二段应从 cue 继续推到末尾。");
        }

        /// <summary>验证落终态幂等：重复落终态不会改变播放位置。</summary>
        [Test]
        public void Settle_IsIdempotent()
        {
            IStoryAction first = Story.Cinematic(Location, null, MidCue);
            StoryTree.Bind(first, "test");

            first.Settle(context);
            first.Settle(context);

            scope.TryGetDirector(Location, out PlayableDirector director);
            Assert.That(director.time, Is.EqualTo(MidTime).Within(0.001d));
        }

        /// <summary>验证引用了不存在的 cue 时立即报错，而不是静默播到结尾。</summary>
        [Test]
        public void Settle_RejectsUnknownCue()
        {
            IStoryAction bad = Story.Cinematic(Location, null, "cue_missing");
            StoryTree.Bind(bad, "test");

            System.InvalidOperationException error = Assert.Throws<System.InvalidOperationException>(() => bad.Settle(context));
            Assert.That(error.Message, Does.Contain("cue_missing"));
        }

        /// <summary>验证未在 StageSpec.Cinematics 中声明的片段在演绎时给出明确错误。</summary>
        [Test]
        public void Play_RejectsUndeclaredCinematic()
        {
            IStoryAction missing = Story.Cinematic("seq.not_declared");
            StoryTree.Bind(missing, "test");

            System.InvalidOperationException error = Assert.Throws<System.InvalidOperationException>(
                () => NarrativeTestKit.AwaitSync(missing.PlayAsync(context, default)));
            Assert.That(error.Message, Does.Contain("StageSpec.Cinematics"));
        }

        /// <summary>验证舞台退出后播放器被销毁，不留残留对象。</summary>
        [Test]
        public void Dispose_DestroysTheDirector()
        {
            scope.TryGetDirector(Location, out PlayableDirector director);
            GameObject directorObject = director.gameObject;

            NarrativeTestKit.AwaitSync(scope.DisposeAsync());

            Assert.That(directorObject == null, Is.True, "舞台退出后不应残留演出播放器。");
        }
    }
}
