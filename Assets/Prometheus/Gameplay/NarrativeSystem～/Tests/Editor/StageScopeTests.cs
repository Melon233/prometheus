using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Xuan.Prometheus.Narrative.Tests
{
    /// <summary>
    /// 验证设计规则 R3：环境接管必须通过舞台作用域获得，且退出时严格逆序还原。
    /// 这组测试是「剧情播完玩家卡住 / HUD 不见了 / 输入锁没解开」这类事故的回归保护。
    /// </summary>
    public sealed class StageScopeTests
    {
        private FakeStageEnvironment environment;

        /// <summary>为每个用例准备一组干净的伪端口。</summary>
        [SetUp]
        public void SetUp()
        {
            environment = new FakeStageEnvironment();
        }

        /// <summary>销毁用例期间创建的场景对象。</summary>
        [TearDown]
        public void TearDown()
        {
            environment.Dispose();
            LogAssert.ignoreFailingMessages = false;
        }

        /// <summary>验证进入舞台按文档规定的顺序接管环境。</summary>
        [Test]
        public void Enter_AppliesTakeoverInDocumentedOrder()
        {
            StageScope scope = NarrativeTestKit.AwaitSync(Stage.EnterAsync(BuildSpec(), environment.Services));

            Assert.That(environment.Log, Is.EqualTo(new[]
            {
                "fade:1",
                "input-lock",
                "ai-freeze",
                "camera-acquire",
                "resolve:hero",
                "load:preload.a",
                "fade:0"
            }));
            NarrativeTestKit.AwaitSync(scope.DisposeAsync());
        }

        /// <summary>验证退出舞台严格按进入顺序的逆序还原。</summary>
        [Test]
        public void Dispose_RestoresInExactReverseOrder()
        {
            StageScope scope = NarrativeTestKit.AwaitSync(Stage.EnterAsync(BuildSpec(), environment.Services));
            environment.Log.Clear();

            NarrativeTestKit.AwaitSync(scope.DisposeAsync());

            Assert.That(environment.Log, Is.EqualTo(new[]
            {
                "fade:1",
                "unload:preload.a",
                "release:hero",
                "camera-release",
                "ai-unfreeze",
                "input-unlock",
                "fade:0"
            }));
        }

        /// <summary>验证进入过程中途失败时，已经生效的接管会被完整还原。</summary>
        [Test]
        public void Enter_RollsBackEverythingWhenAStepFails()
        {
            environment.Actors.ThrowOnResolve = true;

            Assert.Throws<System.InvalidOperationException>(() => NarrativeTestKit.AwaitSync(Stage.EnterAsync(BuildSpec(), environment.Services)));

            Assert.That(environment.Log, Does.Contain("input-unlock"), "进入失败后必须解开输入锁，否则玩家会永久卡住。");
            Assert.That(environment.Log, Does.Contain("ai-unfreeze"));
            Assert.That(environment.Log, Does.Contain("camera-release"));
            Assert.That(environment.Screen.HudVisible, Is.True, "进入失败后必须恢复 HUD。");
            Assert.That(environment.Screen.FadeAlpha, Is.EqualTo(0f), "进入失败后必须把黑幕淡回去。");
        }

        /// <summary>验证 HUD 与黑边被还原到接管前的取值，而不是硬编码的默认值。</summary>
        [Test]
        public void Dispose_RestoresScreenDressingToPreviousValues()
        {
            environment.Screen.HudVisible = false;
            environment.Screen.LetterboxRatio = 0.05f;

            StageScope scope = NarrativeTestKit.AwaitSync(Stage.EnterAsync(BuildSpec(), environment.Services));
            Assert.That(environment.Screen.LetterboxRatio, Is.EqualTo(0.2f).Within(0.0001f));

            NarrativeTestKit.AwaitSync(scope.DisposeAsync());

            Assert.That(environment.Screen.HudVisible, Is.False);
            Assert.That(environment.Screen.LetterboxRatio, Is.EqualTo(0.05f).Within(0.0001f));
        }

        /// <summary>验证参演角色在退出舞台后回到进入前的位置。</summary>
        [Test]
        public void Dispose_RestoresActorTransform()
        {
            StageSpec spec = BuildSpec();
            environment.Actors.AddAnchor("altar", new Vector3(5f, 0f, 5f));
            spec.Placements[ActorRef.Npc("hero")] = Anchor.Named("altar");

            StageScope scope = NarrativeTestKit.AwaitSync(Stage.EnterAsync(spec, environment.Services));
            ActorHandle hero = scope.RequireActor(ActorRef.Npc("hero"));
            Assert.That(hero.Transform.position, Is.EqualTo(new Vector3(5f, 0f, 5f)));

            NarrativeTestKit.AwaitSync(scope.DisposeAsync());

            Assert.That(hero.Transform.position, Is.EqualTo(Vector3.zero), "演出是纯表现，角色应回到进入舞台前的位置。");
        }

        /// <summary>验证重复退出保持幂等。</summary>
        [Test]
        public void Dispose_IsIdempotent()
        {
            StageScope scope = NarrativeTestKit.AwaitSync(Stage.EnterAsync(BuildSpec(), environment.Services));
            NarrativeTestKit.AwaitSync(scope.DisposeAsync());
            int count = environment.Log.Count;

            NarrativeTestKit.AwaitSync(scope.DisposeAsync());

            Assert.That(environment.Log.Count, Is.EqualTo(count));
            Assert.That(scope.IsDisposed, Is.True);
        }

        /// <summary>验证单个还原步骤失败不会阻断其余步骤，避免一个异常导致输入或镜头永久卡死。</summary>
        [Test]
        public void Dispose_ContinuesAfterAFailingTeardownStep()
        {
            LogAssert.ignoreFailingMessages = true;
            StageScope scope = NarrativeTestKit.AwaitSync(Stage.EnterAsync(BuildSpec(), environment.Services));
            scope.Track("boom", () => throw new System.InvalidOperationException("teardown failure"));
            environment.Log.Clear();

            NarrativeTestKit.AwaitSync(scope.DisposeAsync());

            Assert.That(environment.Log, Does.Contain("input-unlock"));
            Assert.That(environment.Log, Does.Contain("camera-release"));
        }

        /// <summary>验证特效句柄随舞台退出统一回收。</summary>
        [Test]
        public void Dispose_ReleasesVfxSpawnedDuringTheStage()
        {
            StageScope scope = NarrativeTestKit.AwaitSync(Stage.EnterAsync(BuildSpec(), environment.Services));
            StoryContext context = NarrativeTestKit.CreateContext(new FakeDialogueView());
            context.Stage = scope;
            IStoryAction vfx = Story.Vfx("fx.test", VfxPlacement.On(ActorRef.Npc("hero")));
            StoryTree.Bind(vfx, "test");
            NarrativeTestKit.AwaitSync(vfx.PlayAsync(context, default));
            Assert.That(environment.Vfx.AliveCount, Is.EqualTo(1));

            NarrativeTestKit.AwaitSync(scope.DisposeAsync());

            Assert.That(environment.Vfx.AliveCount, Is.EqualTo(0), "舞台退出后不应残留特效实例。");
        }

        /// <summary>验证未进入舞台时，依赖舞台的动作给出可诊断的错误而不是空引用。</summary>
        [Test]
        public void StageActions_ReportMissingStageClearly()
        {
            StoryContext context = NarrativeTestKit.CreateContext(new FakeDialogueView());
            IStoryAction fade = Stage.FadeOut(0.2f);
            StoryTree.Bind(fade, "test");

            System.InvalidOperationException error = Assert.Throws<System.InvalidOperationException>(() => fade.Settle(context));
            Assert.That(error.Message, Does.Contain("narrative stage"));
        }

        /// <summary>验证缺省的能力端口在被用到时报出端口名，而不是空引用。</summary>
        [Test]
        public void MissingPort_IsReportedByName()
        {
            StageServices services = new StageServices(environment.Screen, environment.Actors);
            System.InvalidOperationException error = Assert.Throws<System.InvalidOperationException>(() => services.RequireCamera());
            Assert.That(error.Message, Does.Contain(nameof(INarrativeCameraPort)));
        }

        /// <summary>验证未在 StageSpec.Cinematics 中声明的演出片段会被明确拒绝。</summary>
        [Test]
        public void Cinematic_ReportsUnpreparedDirector()
        {
            StageScope scope = NarrativeTestKit.AwaitSync(Stage.EnterAsync(BuildSpec(), environment.Services));
            System.InvalidOperationException error = Assert.Throws<System.InvalidOperationException>(() => scope.RequireDirector("seq.missing"));
            Assert.That(error.Message, Does.Contain("StageSpec.Cinematics"));
            NarrativeTestKit.AwaitSync(scope.DisposeAsync());
        }

        /// <summary>构造一份开启了全部接管项的舞台声明。</summary>
        private static StageSpec BuildSpec()
        {
            StageSpec spec = new StageSpec
            {
                FadeSeconds = 0f,
                HideHud = true,
                LetterboxRatio = 0.2f,
                LockInput = true,
                FreezeAiRadius = 30f,
                TakeCameraControl = true
            };
            spec.WithActor(ActorRef.Npc("hero"));
            spec.WithPreload("preload.a");
            return spec;
        }
    }

    /// <summary>验证第二、三期新增叶子同样满足 R2：落终态与完整演绎产生一致的世界状态。</summary>
    public sealed class StageActionParityTests
    {
        private FakeStageEnvironment environment;
        private StageScope scope;
        private StoryContext context;

        /// <summary>为每个用例进入一个带角色与锚点的舞台。</summary>
        [SetUp]
        public void SetUp()
        {
            environment = new FakeStageEnvironment();
            environment.Actors.AddAnchor("target", new Vector3(3f, 0f, 7f));
            StageSpec spec = new StageSpec { FadeSeconds = 0f, LetterboxRatio = 0f, LockInput = false, TakeCameraControl = false };
            spec.WithActor(ActorRef.Npc("hero"));
            scope = NarrativeTestKit.AwaitSync(Stage.EnterAsync(spec, environment.Services));
            context = NarrativeTestKit.CreateContext(new FakeDialogueView());
            context.Stage = scope;
        }

        /// <summary>退出舞台并清理场景对象。</summary>
        [TearDown]
        public void TearDown()
        {
            NarrativeTestKit.AwaitSync(scope.DisposeAsync());
            environment.Dispose();
        }

        /// <summary>验证位移动作落终态直接把角色放到终点。</summary>
        [Test]
        public void ActorMove_SettleMatchesEndOfFullPlay()
        {
            ActorHandle hero = scope.RequireActor(ActorRef.Npc("hero"));
            IStoryAction move = Story.MoveTo(ActorRef.Npc("hero"), Anchor.Named("target"), 0f);
            StoryTree.Bind(move, "test");

            NarrativeTestKit.AwaitSync(move.PlayAsync(context, default));
            Vector3 afterPlay = hero.Transform.position;
            hero.Transform.position = Vector3.zero;
            move.Settle(context);

            Assert.That(hero.Transform.position, Is.EqualTo(afterPlay));
            Assert.That(hero.Transform.position, Is.EqualTo(new Vector3(3f, 0f, 7f)));
        }

        /// <summary>验证显隐动作与镜头切换动作的落终态直接写入终值。</summary>
        [Test]
        public void VisibilityAndCamera_SettleWriteFinalState()
        {
            ActorHandle hero = scope.RequireActor(ActorRef.Npc("hero"));
            IStoryAction hide = Story.Show(ActorRef.Npc("hero"), false);
            IStoryAction camera = Story.CameraTo("cam_a", 1.5f);
            StoryTree.Bind(Story.Seq(hide, camera), "test");

            hide.Settle(context);
            camera.Settle(context);

            Assert.That(hero.GameObject.activeSelf, Is.False);
            Assert.That(environment.Camera.CurrentCamera, Is.EqualTo("cam_a"));
        }

        /// <summary>验证纯表现叶子的落终态不产生任何世界状态。</summary>
        [Test]
        public void PresentationLeaves_SettleProducesNoState()
        {
            List<IStoryAction> presentation = new List<IStoryAction>
            {
                Story.CameraShake(),
                Story.Vfx("fx.test", VfxPlacement.On(ActorRef.Npc("hero"))),
                Story.Sfx("sfx.test"),
                Story.Ambience("bgm.test")
            };
            environment.Log.Clear();
            for (int index = 0; index < presentation.Count; index++)
            {
                StoryTree.Bind(presentation[index], $"test{index}");
                presentation[index].Settle(context);
            }

            Assert.That(environment.Log, Is.Empty, "纯表现叶子在跳过时不应触发任何端口调用。");
            Assert.That(environment.Vfx.AliveCount, Is.EqualTo(0));
        }

        /// <summary>验证黑幕动作的落终态直接写入目标不透明度。</summary>
        [Test]
        public void ScreenFade_SettleWritesFinalAlpha()
        {
            IStoryAction fade = Stage.FadeOut(2f);
            StoryTree.Bind(fade, "test");

            fade.Settle(context);

            Assert.That(environment.Screen.FadeAlpha, Is.EqualTo(1f));
        }
    }
}
