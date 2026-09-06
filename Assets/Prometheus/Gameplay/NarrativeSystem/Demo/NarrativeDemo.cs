using System.Collections.Generic;
using System.Text;
using Cysharp.Threading.Tasks;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace Xuan.Prometheus.Narrative.Demo
{
    /// <summary>
    /// 剧情系统演示场景的驱动器。
    /// <para>
    /// 直接组装 <see cref="INarrativeSystem"/>、舞台端口与对话视图来演绎一段用 C# DSL 书写的演出，
    /// 不依赖 Core 启动流程，因此该场景可以单独按下 Play 就跑起来，
    /// 作为剧情与演出系统的常驻测试场景。
    /// </para>
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class NarrativeDemo : MonoBehaviour
    {
        [Header("舞台端口")]
        [SerializeField] private NarrativeScreenView screen;
        [SerializeField] private SceneActorResolver actorResolver;
        [SerializeField] private SceneCameraPort cameraPort;
        [SerializeField] private SceneAssetPort assetPort;
        [SerializeField] private PrefabVfxPort vfxPort;
        [SerializeField] private SceneWorldPort worldPort;

        [Header("剧情来源")]
        [SerializeField] [Tooltip("留空则演绎 DemoStory 里用 C# DSL 写的剧情；指定图资产则改为演绎该资产。两者产出同一种运行时表示。")]
        private StoryGraph storyGraph;

        [Header("运行参数")]
        [SerializeField] [Tooltip("进入播放模式后自动开始演绎。")] private bool playOnStart = true;
        [SerializeField] [Tooltip("初始信任值；小于 2 时「西风的旧名」选项以锁定态展示。")] private int initialTrust = 1;
        [SerializeField] [Tooltip("勾选后音频走工程的 FMOD 事件表；未加载 Bank 时保持关闭，音频请求只写日志。")] private bool useFmodAudio;

        private SimpleDialogueView view;
        private NarrativeSystem narrative;
        private StageServices services;
        private Text statusText;
        private Text autoButtonLabel;
        private string savedSnapshot;
        private StoryResult lastResult = StoryResult.Completed;
        private bool hasResult;
        private bool isBusy;
        private string hint = string.Empty;

        /// <summary>准备视图、端口、剧情系统与控制条，并按配置自动开始演绎。</summary>
        private void Start()
        {
            view = GetComponent<SimpleDialogueView>();
            if (view == null) view = gameObject.AddComponent<SimpleDialogueView>();

            BuildControlBar();
            services = BuildServices();

            narrative = new NarrativeSystem();
            narrative.AfterNew();
            narrative.SetView(view);
            DemoTexts.LoadInto(narrative.Text);
            narrative.BeatEntered += OnBeatEntered;

            if (playOnStart) Replay();
        }

        /// <summary>释放剧情系统与事件订阅。</summary>
        private void OnDestroy()
        {
            if (narrative == null) return;
            narrative.BeatEntered -= OnBeatEntered;
            narrative.Dispose();
        }

        /// <summary>处理演示用的键盘快捷键并刷新状态显示。</summary>
        private void Update()
        {
            Keyboard keyboard = Keyboard.current;
            if (keyboard != null)
            {
                if (keyboard.rKey.wasPressedThisFrame) Replay();
                if (keyboard.aKey.wasPressedThisFrame) ToggleAutoPlay();
                if (keyboard.sKey.wasPressedThisFrame) Skip();
                if (keyboard.f5Key.wasPressedThisFrame) Save();
                if (keyboard.f9Key.wasPressedThisFrame) LoadAndResume();
                if (keyboard.escapeKey.wasPressedThisFrame) narrative?.Abort();
            }
            RefreshStatus();
        }

        /// <summary>复位变量并从头演绎一遍剧情。</summary>
        public void Replay()
        {
            RunAsync(StoryPath.None, true).Forget();
        }

        /// <summary>捕获一份剧情存档。</summary>
        public void Save()
        {
            if (narrative == null) return;
            savedSnapshot = narrative.CaptureSnapshot();
            hint = narrative.ResumePath.IsEmpty ? "已存档（无续演点：剧情未中断）" : $"已存档，续演点 {narrative.ResumePath}";
            Debug.Log($"[Narrative] 存档：{savedSnapshot}");
        }

        /// <summary>读取存档；存档中记录了续演点时从该节拍继续，否则从头演绎。</summary>
        public void LoadAndResume()
        {
            if (narrative == null) return;
            if (string.IsNullOrEmpty(savedSnapshot))
            {
                hint = "还没有存档，先按 F5";
                return;
            }
            if (narrative.IsPlaying)
            {
                hint = "演绎中无法读档，先按 Esc 中止";
                return;
            }
            narrative.RestoreSnapshot(savedSnapshot);
            StoryPath resumeAt = narrative.ResumePath;
            hint = resumeAt.IsEmpty ? "存档无续演点，从头演绎" : $"从 {resumeAt} 续演";
            RunAsync(resumeAt, false).Forget();
        }

        /// <summary>跳过当前演绎；剧情尚未被完整看过时会被系统拒绝。</summary>
        public void Skip()
        {
            if (narrative == null || !narrative.IsPlaying) return;
            hint = narrative.RequestSkip() ? "已请求跳过" : "该剧情还没完整看过一遍，不能跳过";
        }

        /// <summary>切换自动播放。</summary>
        public void ToggleAutoPlay()
        {
            if (narrative == null) return;
            narrative.AutoPlay = !narrative.AutoPlay;
            if (autoButtonLabel != null) autoButtonLabel.text = narrative.AutoPlay ? "自动播放：开 (A)" : "自动播放：关 (A)";
        }

        /// <summary>
        /// 进入舞台并演绎剧情。
        /// 舞台用 <c>await using</c> 持有，因此无论正常结束、跳过、中止还是抛异常，
        /// 接管过的输入、镜头、HUD、角色位置都会被完整还原。
        /// </summary>
        /// <param name="resumeAt">续演点；空路径表示从头演绎。</param>
        /// <param name="resetVariables">是否在演绎前复位剧情变量。</param>
        private async UniTaskVoid RunAsync(StoryPath resumeAt, bool resetVariables)
        {
            if (isBusy) return;
            if (narrative.IsPlaying)
            {
                narrative.Abort();
                await UniTask.WaitUntil(() => !narrative.IsPlaying);
            }
            if (resetVariables) ResetProgress();
            hasResult = false;

            isBusy = true;
            try
            {
                await using (StageScope stage = await Stage.EnterAsync(BuildStageSpec(), services))
                {
                    isBusy = false;
                    narrative.Stage = stage;
                    lastResult = resumeAt.IsEmpty
                        ? await narrative.PlayAsync(BuildStory(), StoryId)
                        : await narrative.ResumeAsync(BuildStory(), StoryId, resumeAt);
                }
                hasResult = true;
                Debug.Log($"[Narrative] 演绎结束：{lastResult}");
            }
            catch (System.OperationCanceledException)
            {
                // 取消是正常的中止路径，但演示场景仍然应当把它显示出来，而不是静默结束。
                Debug.Log("[Narrative] 演绎被取消。");
            }
            catch (System.Exception exception)
            {
                // UniTaskVoid 的未观测异常默认只走调度器，演示驱动器必须自己把失败暴露到 Console。
                Debug.LogException(exception);
            }
            finally
            {
                isBusy = false;
                if (narrative != null) narrative.Stage = null;
            }
        }

        /// <summary>获取当前演绎的剧情标识：优先取图资产，否则用 DSL 版本。</summary>
        private string StoryId => storyGraph != null ? storyGraph.StoryId : DemoStory.StoryId;

        /// <summary>构建当前剧情的舞台声明。</summary>
        private StageSpec BuildStageSpec()
        {
            return storyGraph != null ? storyGraph.BuildStage() : DemoStory.BuildStage();
        }

        /// <summary>
        /// 构建当前剧情树。
        /// 图资产与 C# DSL 产出的是同一种运行时表示，因此跳过、续演、预览等能力对两者一视同仁。
        /// </summary>
        private IStoryAction BuildStory()
        {
            return storyGraph != null ? storyGraph.BuildOrThrow() : DemoStory.Build();
        }

        /// <summary>
        /// 把剧情进度复位到「从没演过」的状态。
        /// 变量、一次性选项记录与选择结果都要一起清掉：只清变量会让重播沿用上一遍的选择，
        /// 跳过时又会按那次旧选择落终态，看起来像是玩家没选也走进了分支。
        /// 已看过的剧情记录被保留，以便继续演示跳过权限。
        /// </summary>
        private void ResetProgress()
        {
            NarrativeSnapshot fresh = new NarrativeSnapshot();
            if (narrative.HasSeen(StoryId)) fresh.seenStories.Add(StoryId);
            narrative.RestoreSnapshot(fresh.ToJson());
            // var.* 读取未定义变量会直接报错，因此条件用到的剧情变量必须显式初始化。
            narrative.Variables.Set("var.trust", initialTrust);
        }

        /// <summary>组装舞台使用的全部能力端口。</summary>
        private StageServices BuildServices()
        {
            if (screen == null) screen = GetComponentInChildren<NarrativeScreenView>();
            if (screen == null) screen = gameObject.AddComponent<NarrativeScreenView>();
            if (actorResolver == null) actorResolver = FindAnyObjectByType<SceneActorResolver>();
            if (cameraPort == null) cameraPort = FindAnyObjectByType<SceneCameraPort>();
            if (assetPort == null) assetPort = FindAnyObjectByType<SceneAssetPort>();
            if (vfxPort == null) vfxPort = FindAnyObjectByType<PrefabVfxPort>();
            if (worldPort == null) worldPort = FindAnyObjectByType<SceneWorldPort>();

            return new StageServices(screen, actorResolver)
            {
                Camera = cameraPort,
                Assets = assetPort,
                Vfx = vfxPort,
                World = worldPort,
                Audio = useFmodAudio ? (INarrativeAudioPort)new FmodNarrativeAudioPort() : new LoggingNarrativeAudioPort()
            };
        }

        /// <summary>记录节拍进入日志，模拟存档层对续演位置的观察。</summary>
        private void OnBeatEntered(StoryPath path)
        {
            Debug.Log($"[Narrative] 进入节拍 {path}");
        }

        /// <summary>刷新左上角状态面板。</summary>
        private void RefreshStatus()
        {
            if (statusText == null || narrative == null) return;
            StringBuilder builder = new StringBuilder();
            builder.AppendLine($"剧情：{StoryId}（{(storyGraph != null ? "图资产" : "C# DSL")}）");
            builder.AppendLine($"状态：{DescribeState()}");
            builder.AppendLine($"当前节拍：{(narrative.CurrentBeat.IsEmpty ? "-" : narrative.CurrentBeat.Value)}");
            builder.AppendLine($"舞台：{(narrative.Stage != null && !narrative.Stage.IsDisposed ? "已接管" : "未接管")}");
            builder.AppendLine($"自动播放：{(narrative.AutoPlay ? "开" : "关")}");
            builder.AppendLine($"已看过：{(narrative.HasSeen(StoryId) ? "是" : "否")}　可跳过：{(narrative.CanSkip ? "是" : "否")}");
            builder.AppendLine($"续演点：{(narrative.ResumePath.IsEmpty ? "-" : narrative.ResumePath.Value)}");
            builder.AppendLine($"存档：{(string.IsNullOrEmpty(savedSnapshot) ? "无" : savedSnapshot.Length + " 字节")}");
            if (!string.IsNullOrEmpty(hint)) builder.AppendLine($"提示：{hint}");
            builder.AppendLine();
            builder.AppendLine("变量：");
            foreach (KeyValuePair<string, StoryValue> pair in narrative.Variables.Capture()) builder.AppendLine($"  {pair.Key} = {pair.Value}");
            statusText.text = builder.ToString();
        }

        /// <summary>把当前运行状态描述成一行可读文本。</summary>
        private string DescribeState()
        {
            if (narrative.IsPlaying) return "演绎中";
            return hasResult ? $"已结束 ({lastResult})" : "未开始";
        }

        /// <summary>构建演示控制条与状态面板。</summary>
        private void BuildControlBar()
        {
            Font font = NarrativeUiFactory.ResolveFont();
            NarrativeUiFactory.EnsureEventSystem();
            Canvas canvas = NarrativeUiFactory.CreateCanvas("NarrativeDemoHudCanvas", transform, 480);

            GameObject bar = NarrativeUiFactory.CreateUiObject("ControlBar", canvas.transform);
            RectTransform barRect = (RectTransform)bar.transform;
            barRect.anchorMin = new Vector2(1f, 1f);
            barRect.anchorMax = new Vector2(1f, 1f);
            barRect.pivot = new Vector2(1f, 1f);
            barRect.anchoredPosition = new Vector2(-24f, -24f);
            barRect.sizeDelta = new Vector2(280f, 10f);
            VerticalLayoutGroup layout = bar.AddComponent<VerticalLayoutGroup>();
            layout.spacing = 10f;
            layout.childControlHeight = true;
            layout.childControlWidth = true;
            layout.childForceExpandHeight = false;
            ContentSizeFitter fitter = bar.AddComponent<ContentSizeFitter>();
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            AddBarButton(bar.transform, font, "重播 (R)", Replay);
            Button autoButton = AddBarButton(bar.transform, font, "自动播放：关 (A)", ToggleAutoPlay);
            autoButtonLabel = autoButton.GetComponentInChildren<Text>();
            AddBarButton(bar.transform, font, "跳过 (S)", Skip);
            AddBarButton(bar.transform, font, "存档 (F5)", Save);
            AddBarButton(bar.transform, font, "读档续演 (F9)", LoadAndResume);

            GameObject statusPanel = NarrativeUiFactory.CreateUiObject("StatusPanel", canvas.transform);
            RectTransform statusRect = (RectTransform)statusPanel.transform;
            statusRect.anchorMin = new Vector2(0f, 1f);
            statusRect.anchorMax = new Vector2(0f, 1f);
            statusRect.pivot = new Vector2(0f, 1f);
            statusRect.anchoredPosition = new Vector2(24f, -24f);
            statusRect.sizeDelta = new Vector2(470f, 420f);
            Image statusBackground = statusPanel.AddComponent<Image>();
            statusBackground.color = new Color(0.03f, 0.04f, 0.08f, 0.72f);
            statusText = NarrativeUiFactory.CreateText(statusRect, font, 20, TextAnchor.UpperLeft);
            NarrativeUiFactory.Stretch(statusText.rectTransform, 16f, 12f);
        }

        /// <summary>向控制条追加一个固定高度的按钮。</summary>
        private static Button AddBarButton(Transform parent, Font font, string label, UnityEngine.Events.UnityAction onClick)
        {
            Button button = NarrativeUiFactory.CreateButton(parent, font, label, new Color(0.12f, 0.16f, 0.26f, 0.94f), onClick);
            LayoutElement layout = button.gameObject.AddComponent<LayoutElement>();
            layout.minHeight = 42f;
            return button;
        }
    }
}
