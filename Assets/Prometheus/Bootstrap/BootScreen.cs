using System;
using Cysharp.Threading.Tasks;
using UnityEngine;
using UnityEngine.UI;
using Xuan.Prometheus.Asset;

namespace Xuan.Prometheus.Bootstrap
{
    /// <summary>
    /// 开屏与热更界面。
    ///
    /// 它**不是 UIPanel**，而且不能是：<c>UIKit.OpenPanel</c> 内部走 <c>Core.Asset.InstantiateSync</c>
    /// 加载面板预制体，而开屏与热更恰好发生在资源包就绪**之前**，此时没有可用清单，任何 OpenPanel 必然失败。
    /// 因此这一层必须随包体直出：要么是启动场景里的对象，要么是 Resources 资源。
    ///
    /// 出于同一个理由，字体走 <see cref="Font.CreateDynamicFontFromOSFont"/> 取系统字体，
    /// 而不是引用 <c>BundleResources/Font</c> 下的字体资产——那份资产在这一刻同样还不可达，
    /// 直接硬引用会把 3MB 字体复制进 Resources。
    ///
    /// 当前整套界面是**占位**：纯色底 + 标题 + 进度条。接入正式开屏动画（视频或 Timeline）时，
    /// 把 <see cref="PlaySplashAsync"/> 的实现换掉即可，其余阶段与进度契约不受影响。
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class BootScreen : MonoBehaviour
    {
        /// <summary>开屏画面至少停留的时长；正式动画接入后由动画自身长度决定。</summary>
        private const float SplashHoldSeconds = 1.2f;

        /// <summary>淡入淡出时长。</summary>
        private const float FadeSeconds = 0.35f;

        /// <summary>启动界面的排序层级；必须高过一切玩法界面，因为它在玩法存在之前就要盖住全屏。</summary>
        private const int SortingOrder = 32000;

        private Canvas canvas;
        private CanvasGroup group;
        private Text titleText;
        private Text statusText;
        private RectTransform progressFill;

        /// <summary>已订阅的资源模块；退订时精确解除。</summary>
        private IAssetKit subscribedAssetKit;

        /// <summary>在任何启动阶段开始前建立界面，使第一帧就有东西盖住全屏。</summary>
        private void Awake()
        {
            BuildUi();
        }

        /// <summary>释放对资源模块的订阅，避免界面销毁后仍被进度通知持有。</summary>
        private void OnDestroy()
        {
            if (subscribedAssetKit != null) subscribedAssetKit.BootProgressChanged -= OnBootProgressChanged;
            subscribedAssetKit = null;
        }

        /// <summary>
        /// 播放开屏画面。
        /// 它与资源包初始化**并行**发生：开屏用来覆盖初始化耗时，而不是叠加在它前面。
        /// </summary>
        public async UniTask PlaySplashAsync()
        {
            titleText.text = "PROMETHEUS";
            statusText.text = string.Empty;
            SetProgress(0f);
            group.alpha = 0f;
            await FadeAsync(0f, 1f);
            await UniTask.Delay(TimeSpan.FromSeconds(SplashHoldSeconds), DelayType.UnscaledDeltaTime);
        }

        /// <summary>
        /// 订阅资源模块的启动进度并显示热更进度条。
        /// 进度来自 <see cref="IAssetKit.BootProgressChanged"/>：热更是资源包初始化内部的一段，
        /// 本界面只负责显示，不驱动任何下载。
        /// </summary>
        /// <param name="assetKit">当前资源模块。</param>
        public void BindProgress(IAssetKit assetKit)
        {
            subscribedAssetKit = assetKit ?? throw new ArgumentNullException(nameof(assetKit));
            subscribedAssetKit.BootProgressChanged += OnBootProgressChanged;
            // 立即用当前值刷新一次：订阅之前可能已经走过若干阶段。
            OnBootProgressChanged(subscribedAssetKit.BootProgress);
        }

        /// <summary>淡出并销毁整个启动界面；登录界面随后由 UIKit 正常打开。</summary>
        public async UniTask DismissAsync()
        {
            await FadeAsync(group.alpha, 0f);
            Destroy(gameObject);
        }

        /// <summary>把最新的启动进度画到进度条与状态文字上。</summary>
        private void OnBootProgressChanged(AssetBootProgress progress)
        {
            SetProgress(progress.OverallRatio);
            statusText.text = DescribePhase(progress);
        }

        /// <summary>把阶段翻译成一句给玩家看的话；下载阶段额外显示字节数。</summary>
        private static string DescribePhase(AssetBootProgress progress)
        {
            switch (progress.Phase)
            {
                case AssetBootPhase.InitializingPackage: return "正在准备资源…";
                case AssetBootPhase.RequestingVersion: return "正在检查更新…";
                case AssetBootPhase.LoadingManifest: return "正在获取资源清单…";
                case AssetBootPhase.DownloadingContent:
                    return progress.HasDownload
                        ? $"正在下载更新… {progress.DownloadedBytes / 1048576f:F1} / {progress.TotalBytes / 1048576f:F1} MB"
                        : "已是最新版本";
                case AssetBootPhase.Ready: return "准备就绪";
                default: return string.Empty;
            }
        }

        /// <summary>设置进度条填充比例。</summary>
        private void SetProgress(float ratio)
        {
            progressFill.anchorMax = new Vector2(Mathf.Clamp01(ratio), 1f);
        }

        /// <summary>按未缩放时间做一次整体淡变；启动阶段不受 Time.timeScale 影响。</summary>
        private async UniTask FadeAsync(float from, float to)
        {
            float elapsed = 0f;
            group.alpha = from;
            while (elapsed < FadeSeconds)
            {
                elapsed += Time.unscaledDeltaTime;
                group.alpha = Mathf.Lerp(from, to, Mathf.Clamp01(elapsed / FadeSeconds));
                await UniTask.Yield();
            }
            group.alpha = to;
        }

        /// <summary>用代码搭出占位界面：纯色底、标题、状态文字和一条进度条。</summary>
        private void BuildUi()
        {
            canvas = gameObject.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = SortingOrder;
            CanvasScaler scaler = gameObject.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.matchWidthOrHeight = 1f;
            gameObject.AddComponent<GraphicRaycaster>();
            group = gameObject.AddComponent<CanvasGroup>();

            CreateStretchedImage("Background", transform, new Color(0.05f, 0.06f, 0.09f, 1f));
            titleText = CreateText("Title", transform, 72, new Vector2(0f, 120f), new Vector2(1200f, 120f), new Color(0.92f, 0.88f, 0.78f, 1f));
            statusText = CreateText("Status", transform, 28, new Vector2(0f, -170f), new Vector2(1200f, 48f), new Color(0.75f, 0.75f, 0.78f, 1f));

            RectTransform track = CreateStretchedImage("ProgressTrack", transform, new Color(1f, 1f, 1f, 0.12f)).rectTransform;
            track.anchorMin = new Vector2(0.5f, 0.5f);
            track.anchorMax = new Vector2(0.5f, 0.5f);
            track.pivot = new Vector2(0.5f, 0.5f);
            track.anchoredPosition = new Vector2(0f, -120f);
            track.sizeDelta = new Vector2(720f, 6f);

            Image fill = CreateStretchedImage("ProgressFill", track, new Color(0.92f, 0.78f, 0.35f, 1f));
            progressFill = fill.rectTransform;
            progressFill.anchorMin = Vector2.zero;
            progressFill.anchorMax = new Vector2(0f, 1f);
            progressFill.pivot = new Vector2(0f, 0.5f);
            progressFill.offsetMin = Vector2.zero;
            progressFill.offsetMax = Vector2.zero;
        }

        /// <summary>创建一张铺满父节点的纯色图。</summary>
        private static Image CreateStretchedImage(string name, Transform parent, Color color)
        {
            GameObject imageObject = new GameObject(name, typeof(RectTransform), typeof(Image));
            imageObject.transform.SetParent(parent, false);
            RectTransform rect = (RectTransform)imageObject.transform;
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
            Image image = imageObject.GetComponent<Image>();
            image.color = color;
            image.raycastTarget = false;
            return image;
        }

        /// <summary>创建一行居中文字；字体取系统字体，因为此刻资源包还不可达。</summary>
        private static Text CreateText(string name, Transform parent, int fontSize, Vector2 anchoredPosition, Vector2 size, Color color)
        {
            GameObject textObject = new GameObject(name, typeof(RectTransform), typeof(Text));
            textObject.transform.SetParent(parent, false);
            RectTransform rect = (RectTransform)textObject.transform;
            rect.anchorMin = new Vector2(0.5f, 0.5f);
            rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = anchoredPosition;
            rect.sizeDelta = size;
            Text text = textObject.GetComponent<Text>();
            text.font = ResolveSystemFont();
            text.fontSize = fontSize;
            text.color = color;
            text.alignment = TextAnchor.MiddleCenter;
            text.horizontalOverflow = HorizontalWrapMode.Overflow;
            text.raycastTarget = false;
            return text;
        }

        /// <summary>
        /// 取一个能显示中文的系统字体。
        /// 逐个尝试常见字体名而不是写死一个：不同平台装的中文字体不同，
        /// 全部取不到时退回 Unity 内置字体——那时中文会显示成方块，但启动流程本身不受影响。
        /// </summary>
        private static Font ResolveSystemFont()
        {
            string[] candidates = { "Microsoft YaHei UI", "Microsoft YaHei", "PingFang SC", "Noto Sans CJK SC", "Source Han Sans" };
            string[] installed = Font.GetOSInstalledFontNames();
            for (int index = 0; index < candidates.Length; index++)
            {
                if (Array.IndexOf(installed, candidates[index]) < 0) continue;
                Font font = Font.CreateDynamicFontFromOSFont(candidates[index], 32);
                if (font != null) return font;
            }
            return Font.CreateDynamicFontFromOSFont(installed.Length > 0 ? installed[0] : "Arial", 32);
        }
    }
}
