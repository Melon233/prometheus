using UnityEngine;
using static Xuan.Prometheus.Narrative.Story;

namespace Xuan.Prometheus.Narrative.Demo
{
    /// <summary>
    /// 演示剧情：用 C# DSL 书写的一段完整演出。
    /// <para>
    /// 覆盖第一到第三期的全部能力：叙述与台词、并发与阻塞表现、并发组合子、自动推进、
    /// 带条件与一次性限制的选项、条件分支、变量写入、舞台接管、镜头切换、特效、音效、
    /// Spine 角色动画，以及被 cue 切成两段、中间穿插对话的 Timeline 演出片段。
    /// </para>
    /// </summary>
    public static class DemoStory
    {
        /// <summary>剧情树的稳定标识，节点路径以此为根。</summary>
        public const string StoryId = "demo_altar";

        /// <summary>演示剧情引用的角色。</summary>
        public static class Cast
        {
            /// <summary>主角。</summary>
            public static readonly ActorRef Hero = ActorRef.Npc("hero");

            /// <summary>同伴。</summary>
            public static readonly ActorRef Paimon = ActorRef.Companion("paimon");

            /// <summary>长老，使用 Spine 骨骼动画。</summary>
            public static readonly ActorRef Elder = ActorRef.Npc("elder");

            /// <summary>祭坛，作为场景物体参演。</summary>
            public static readonly ActorRef Altar = ActorRef.SceneProp("altar");
        }

        /// <summary>演示剧情引用的资源地址。</summary>
        public static class Locations
        {
            /// <summary>火花特效预制体。</summary>
            public const string Spark = "fx_spark";

            /// <summary>祭坛升起的演出片段。</summary>
            public const string AltarSequence = "seq_altar";
        }

        /// <summary>演示剧情引用的机位名。</summary>
        public static class Cameras
        {
            /// <summary>全景机位。</summary>
            public const string Wide = "cam_wide";

            /// <summary>同伴特写。</summary>
            public const string Paimon = "cam_paimon";

            /// <summary>主角过肩。</summary>
            public const string Hero = "cam_hero";

            /// <summary>祭坛机位。</summary>
            public const string Altar = "cam_altar";
        }

        /// <summary>构建本段演出的舞台声明。</summary>
        public static StageSpec BuildStage()
        {
            StageSpec spec = new StageSpec
            {
                FadeSeconds = 0.35f,
                HideHud = true,
                LetterboxRatio = 0.11f,
                LockInput = true,
                TakeCameraControl = true
            };
            spec.WithActor(Cast.Hero)
                .WithActor(Cast.Paimon)
                .WithActor(Cast.Elder)
                .WithActor(Cast.Altar);
            spec.WithPreload(Locations.Spark);
            spec.WithCinematics(Locations.AltarSequence);
            return spec;
        }

        /// <summary>构建演示剧情树。</summary>
        public static IStoryAction Build()
        {
            return Seq(
                CameraCut(Cameras.Wide).Id("open_camera"),
                Ambience("demo_ambience", 1.5f).Id("ambience"),
                Narration("demo.intro").Id("intro"),

                // 并发表现挂在句子上：特效与音效随台词一起触发，不阻塞推进。
                CameraTo(Cameras.Paimon, 0.8f).Id("to_paimon"),
                Say(Cast.Paimon, "demo.p1")
                    .With(Vfx(Locations.Spark, VfxPlacement.On(Cast.Paimon, new Vector3(0f, 0.7f, 0f)), 2f))
                    .With(Sfx("demo_poof", VfxPlacement.On(Cast.Paimon)))
                    .Id("paimon_first"),

                // 阻塞表现：走位播完之前，玩家的确认输入会被视图丢弃，点不穿。
                CameraTo(Cameras.Hero, 0.8f).Id("to_hero"),
                Say(Cast.Hero, "demo.h1")
                    .Await(MoveTo(Cast.Hero, Anchor.Named("hero_stand"), 1.4f, true))
                    .Id("hero_approach"),

                Narration("demo.h1_hint").AdvanceOn(AdvancePolicy.TextDuration).Id("hero_hint"),

                // 循环动画用分离组合子启动：它不会自然结束，因此不能参与 WhenAll 等待。
                Par(ParallelMode.Detached, SpineAnim(Cast.Elder, ElderRunAnimation, true)).Id("elder_run"),
                Par(
                    MoveTo(Cast.Elder, Anchor.Named("elder_stand"), 1.6f, true),
                    CameraTo(Cameras.Altar, 1.6f)).Id("elder_enter"),
                // 同一角色开启新动画会自动顶掉上一段，无需显式停止。
                Par(ParallelMode.Detached, SpineAnim(Cast.Elder, ElderIdleAnimation, true)).Id("elder_idle"),

                // 第三期：一段 Timeline 演出被 cue 切成两半，中间插一句对话。
                // 两段复用同一个 PlayableDirector，因此姿态与镜头在段与段之间保持连续。
                Cinematic(Locations.AltarSequence, null, "cue_mid").Id("altar_rise_a"),
                Say(Cast.Elder, "demo.e1").AdvanceOn(AdvancePolicy.TextDuration).Id("elder_first"),
                Cinematic(Locations.AltarSequence).Id("altar_rise_b"),
                CameraShake(0.12f, 0.35f).Id("altar_shake"),

                Choose(
                    Option("demo.opt_ask")
                        .Once()
                        .Then(Seq(
                            Say(Cast.Hero, "demo.h2"),
                            Say(Cast.Elder, "demo.e2"),
                            SetFlag("asked_about_wind")))
                        .Id("opt_ask"),

                    // 条件不满足且配置了锁定原因，因此以灰显锁定态展示而不是隐藏。
                    Option("demo.opt_secret")
                        .When("var.trust >= 2")
                        .Locked("demo.opt_secret_locked")
                        .Then(Seq(
                            Say(Cast.Elder, "demo.e_secret"),
                            SetFlag("knows_old_name")))
                        .Id("opt_secret"),

                    Option("demo.opt_leave")
                        .Then(Say(Cast.Elder, "demo.e3"))
                        .Id("opt_leave")).Id("elder_choice"),

                // 条件分支活在剧情树上，不需要任何时间轴跳转。
                If("flag.asked_about_wind",
                    Say(Cast.Paimon, "demo.p2").Id("paimon_learned"),
                    Say(Cast.Paimon, "demo.p3").Id("paimon_missed")).Id("wind_branch"),

                SetVar("var.trust", 2).Id("grant_trust"),
                CameraTo(Cameras.Wide, 1.2f).Id("close_camera"),
                Narration("demo.outro").Id("outro"));
        }

        /// <summary>长老使用的跑步动画名；与场景中挂载的 Spine 骨骼保持一致。</summary>
        public const string ElderRunAnimation = "run";

        /// <summary>长老使用的待机动画名；与场景中挂载的 Spine 骨骼保持一致。</summary>
        public const string ElderIdleAnimation = "idle";
    }
}
