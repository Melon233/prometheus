using System;
using Cysharp.Threading.Tasks;
using UnityEngine;

namespace Xuan.Prometheus.Bootstrap
{
    /// <summary>启动流程的阶段；枚举本身就是这台状态机的状态。</summary>
    public enum GameFlowStage
    {
        /// <summary>创建 Core 与启动界面，并让资源包初始化开始跑。</summary>
        Boot = 0,

        /// <summary>播放开屏画面；与资源包初始化**并行**。</summary>
        Splash = 1,

        /// <summary>显示资源准备与热更进度，等待资源包就绪。</summary>
        HotUpdate = 2,

        /// <summary>显示登录界面，等待玩家进入。</summary>
        Login = 3,

        /// <summary>建立玩法会话并进入主世界。</summary>
        EnterWorld = 4,

        /// <summary>启动完成，流程结束。</summary>
        Running = 5
    }

    /// <summary>
    /// 启动流程的状态机。
    ///
    /// 它放在 Bootstrap，因为驱动启动需要同时认识 Kit、UI、场景与玩法会话，
    /// 而 Bootstrap 是唯一被允许认识具体实现的装配。
    ///
    /// 阶段划分对应三段生命周期：<see cref="GameFlowStage.Boot"/> 到 <see cref="GameFlowStage.Login"/>
    /// 全部属于 **App 段**（只有 Kit），<see cref="GameFlowStage.EnterWorld"/> 才建立 **Session 段**（全部 System）
    /// 与 **World 段**（场景与实体）。登录界面因此不需要等待任何玩法配置加载。
    /// </summary>
    public sealed class GameFlow
    {
        /// <summary>本次启动驱动的运行时核心。</summary>
        private readonly Core core;

        /// <summary>开屏与热更界面；进入登录阶段后被销毁。</summary>
        private BootScreen bootScreen;

        /// <summary>
        /// 全部 Kit 的异步初始化任务。
        /// 它在 Boot 阶段就开始跑，直到 HotUpdate 阶段才被等待——开屏因此是用来**覆盖**初始化耗时的，
        /// 而不是叠加在它前面。
        /// </summary>
        private UniTask kitInitialization;

        /// <summary>
        /// 本次启动的世界栈。
        /// 它随流程存活而不是随会话存活：登出重登会重建会话，但「现在在哪个世界、从哪来的」
        /// 由流程负责重置，因此栈归流程持有。
        /// </summary>
        public WorldSession Worlds { get; } = new WorldSession();

        /// <summary>创建启动流程。</summary>
        /// <param name="runtimeCore">已经构造完成、尚未初始化的运行时核心。</param>
        public GameFlow(Core runtimeCore)
        {
            core = runtimeCore ?? throw new ArgumentNullException(nameof(runtimeCore));
        }

        /// <summary>获取当前所处的启动阶段。</summary>
        public GameFlowStage Stage { get; private set; } = GameFlowStage.Boot;

        /// <summary>按阶段依次推进，直到进入 <see cref="GameFlowStage.Running"/>。</summary>
        public async UniTask RunAsync()
        {
            while (Stage != GameFlowStage.Running) Stage = await RunStageAsync(Stage);
        }

        /// <summary>执行一个阶段并返回下一个阶段；每个阶段只回答「做完之后去哪」。</summary>
        /// <param name="stage">要执行的阶段。</param>
        private UniTask<GameFlowStage> RunStageAsync(GameFlowStage stage)
        {
            switch (stage)
            {
                case GameFlowStage.Boot: return RunBootAsync();
                case GameFlowStage.Splash: return RunSplashAsync();
                case GameFlowStage.HotUpdate: return RunHotUpdateAsync();
                case GameFlowStage.Login: return RunLoginAsync();
                case GameFlowStage.EnterWorld: return RunEnterWorldAsync();
                default: throw new InvalidOperationException($"Game flow stage '{stage}' has no runner.");
            }
        }

        /// <summary>建立启动界面并让资源包初始化立刻开始跑。</summary>
        private UniTask<GameFlowStage> RunBootAsync()
        {
            bootScreen = CreateBootScreen();
            // 只启动、不等待：等待发生在 HotUpdate 阶段，中间这段时间用来播开屏。
            kitInitialization = UniTask.WhenAll(core.CreateAfterNewTasks());
            return UniTask.FromResult(GameFlowStage.Splash);
        }

        /// <summary>播放开屏画面。</summary>
        private async UniTask<GameFlowStage> RunSplashAsync()
        {
            await bootScreen.PlaySplashAsync();
            return GameFlowStage.HotUpdate;
        }

        /// <summary>显示资源准备与热更进度，等待全部 Kit 就绪。</summary>
        private async UniTask<GameFlowStage> RunHotUpdateAsync()
        {
            // 进度来自 AssetKit：热更是资源包初始化内部的一段，界面只显示、不驱动下载。
            // BindProgress 会先用当前值刷新一次，因此 Splash 期间已经走过的阶段不会丢失。
            bootScreen.BindProgress(Core.Asset);
            await kitInitialization;
            core.AfterNew();
            return GameFlowStage.Login;
        }

        /// <summary>关闭启动界面，打开登录界面并等待玩家进入。</summary>
        private async UniTask<GameFlowStage> RunLoginAsync()
        {
            // 登录界面**可以**是 UIPanel：此刻资源包已经就绪。开屏与热更则不行（见 BootScreen 的说明）。
            await bootScreen.DismissAsync();
            bootScreen = null;
            LoginPanel loginPanel = Core.UI.OpenPanel<LoginPanel>();
            await loginPanel.WaitForEnterAsync();
            Core.UI.ClosePanel<LoginPanel>();
            return GameFlowStage.EnterWorld;
        }

        /// <summary>建立玩法会话并进入主世界。</summary>
        private async UniTask<GameFlowStage> RunEnterWorldAsync()
        {
            // Session 段：构造全部 System，各自加载自己的配置。
            await Core.Gameplay.CreateSessionAsync(new PrometheusSystemInstaller());
            // World 段：进入栈底世界。具体是哪个场景、出生在哪，由世界目录决定，流程不写死。
            await Worlds.EnterMainWorldAsync();
            return GameFlowStage.Running;
        }

        /// <summary>
        /// 创建启动界面的宿主对象。
        /// 用代码创建而不是摆在启动场景里，是为了让它同样在场景卸载后存活——
        /// 它必须盖住整个启动过程，而这个过程会跨越一次场景加载。
        /// </summary>
        private static BootScreen CreateBootScreen()
        {
            GameObject host = new GameObject(nameof(BootScreen), typeof(RectTransform), typeof(BootScreen));
            UnityEngine.Object.DontDestroyOnLoad(host);
            return host.GetComponent<BootScreen>();
        }
    }
}
