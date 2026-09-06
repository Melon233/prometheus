using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;
using UnityEngine.UI;

namespace Xuan.Prometheus.Narrative
{
    /// <summary>
    /// 屏幕表现端口的默认实现：在运行时构建全屏黑幕与上下黑边。
    /// 全部状态以属性直接读写，因此落终态不需要经过任何过渡动画。
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class NarrativeScreenView : MonoBehaviour, INarrativeScreen
    {
        [Header("HUD")]
        [SerializeField] [Tooltip("演出期间需要隐藏的玩法 HUD 根节点；留空则只记录状态不实际隐藏。")]
        private GameObject hudRoot;

        private Image fadeImage;
        private RectTransform topBar;
        private RectTransform bottomBar;
        private float fadeAlpha;
        private float letterboxRatio;
        private bool hudVisible = true;

        /// <inheritdoc />
        public float FadeAlpha
        {
            get => fadeAlpha;
            set
            {
                fadeAlpha = Mathf.Clamp01(value);
                ApplyFade();
            }
        }

        /// <inheritdoc />
        public float LetterboxRatio
        {
            get => letterboxRatio;
            set
            {
                letterboxRatio = Mathf.Clamp01(value);
                ApplyLetterbox();
            }
        }

        /// <inheritdoc />
        public bool HudVisible
        {
            get => hudVisible;
            set
            {
                hudVisible = value;
                if (hudRoot != null) hudRoot.SetActive(value);
            }
        }

        /// <summary>
        /// 构建黑边与黑幕层级。
        /// 两者分属不同画布：黑边压在对话界面之下（它是取景框，不应挡住台词），
        /// 黑幕位于最上层（它要盖住包括界面在内的一切）。
        /// </summary>
        private void Awake()
        {
            Canvas letterboxCanvas = NarrativeUiFactory.CreateCanvas("NarrativeLetterboxCanvas", transform, LetterboxSortingOrder);
            topBar = CreateBar(letterboxCanvas.transform, "LetterboxTop", new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0.5f, 1f));
            bottomBar = CreateBar(letterboxCanvas.transform, "LetterboxBottom", new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(0.5f, 0f));

            Canvas fadeCanvas = NarrativeUiFactory.CreateCanvas("NarrativeFadeCanvas", transform, FadeSortingOrder);
            GameObject fadeObject = NarrativeUiFactory.CreateUiObject("Fade", fadeCanvas.transform);
            fadeImage = fadeObject.AddComponent<Image>();
            fadeImage.color = new Color(0f, 0f, 0f, 0f);
            fadeImage.raycastTarget = false;
            NarrativeUiFactory.Stretch((RectTransform)fadeObject.transform, 0f, 0f);

            ApplyFade();
            ApplyLetterbox();
        }

        /// <summary>黑边画布的排序层级；低于对话界面。</summary>
        public const int LetterboxSortingOrder = 400;

        /// <summary>黑幕画布的排序层级；高于全部界面。</summary>
        public const int FadeSortingOrder = 2000;

        /// <inheritdoc />
        public async UniTask FadeAsync(float target, float duration, CancellationToken cancellationToken)
        {
            float clamped = Mathf.Clamp01(target);
            if (duration <= 0f)
            {
                FadeAlpha = clamped;
                return;
            }
            float start = fadeAlpha;
            float elapsed = 0f;
            while (elapsed < duration)
            {
                cancellationToken.ThrowIfCancellationRequested();
                await UniTask.Yield(PlayerLoopTiming.Update, cancellationToken);
                elapsed += Time.deltaTime;
                FadeAlpha = Mathf.Lerp(start, clamped, Mathf.Clamp01(elapsed / duration));
            }
            FadeAlpha = clamped;
        }

        /// <inheritdoc />
        public async UniTask LetterboxAsync(float target, float duration, CancellationToken cancellationToken)
        {
            float clamped = Mathf.Clamp01(target);
            if (duration <= 0f)
            {
                LetterboxRatio = clamped;
                return;
            }
            float start = letterboxRatio;
            float elapsed = 0f;
            while (elapsed < duration)
            {
                cancellationToken.ThrowIfCancellationRequested();
                await UniTask.Yield(PlayerLoopTiming.Update, cancellationToken);
                elapsed += Time.deltaTime;
                LetterboxRatio = Mathf.Lerp(start, clamped, Mathf.Clamp01(elapsed / duration));
            }
            LetterboxRatio = clamped;
        }

        /// <summary>把当前黑幕不透明度写入图像。</summary>
        private void ApplyFade()
        {
            if (fadeImage != null) fadeImage.color = new Color(0f, 0f, 0f, fadeAlpha);
        }

        /// <summary>把当前黑边比例写入上下两条黑边。</summary>
        private void ApplyLetterbox()
        {
            if (topBar == null || bottomBar == null) return;
            float height = letterboxRatio * 1080f;
            topBar.sizeDelta = new Vector2(0f, height);
            bottomBar.sizeDelta = new Vector2(0f, height);
        }

        /// <summary>创建一条贴边的黑边条。</summary>
        private static RectTransform CreateBar(Transform parent, string name, Vector2 anchorMin, Vector2 anchorMax, Vector2 pivot)
        {
            GameObject barObject = NarrativeUiFactory.CreateUiObject(name, parent);
            Image image = barObject.AddComponent<Image>();
            image.color = Color.black;
            image.raycastTarget = false;
            RectTransform rect = (RectTransform)barObject.transform;
            rect.anchorMin = anchorMin;
            rect.anchorMax = anchorMax;
            rect.pivot = pivot;
            rect.sizeDelta = new Vector2(0f, 0f);
            rect.anchoredPosition = Vector2.zero;
            return rect;
        }
    }
}
