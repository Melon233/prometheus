using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;

namespace Xuan.Prometheus.Narrative
{
    /// <summary>
    /// 运行时屏幕端口。
    ///
    /// 黑幕与黑边直接复用 <see cref="NarrativeScreenView"/>：它在 Awake 里自建两层画布，
    /// 与宿主场景无关，玩法流程与演示场景需要的是同一套表现，没有理由复制一份。
    /// 唯一需要替换的是 HUD 显隐——<see cref="NarrativeScreenView"/> 依赖 Inspector 上的
    /// 场景节点引用，而玩法层拿不到 UI 程序集里的面板，只能发 <see cref="NarrativeHudVisibilityEvent"/>
    /// 由 UI 层自行响应。
    /// </summary>
    internal sealed class NarrativeRuntimeScreen : INarrativeScreen, IDisposable
    {
        /// <summary>承载黑幕与黑边画布的运行时宿主节点名。</summary>
        private const string HostName = "[NarrativeScreen]";

        private GameObject host;
        private NarrativeScreenView view;
        private bool hudVisible = true;

        /// <summary>在常驻根节点下创建屏幕表现宿主；黑幕与黑边随之建立。</summary>
        internal NarrativeRuntimeScreen()
        {
            host = new GameObject(HostName);
            host.transform.SetParent(PersistentRoot.Shared, false);
            view = host.AddComponent<NarrativeScreenView>();
        }

        /// <inheritdoc />
        public float FadeAlpha
        {
            get => Require().FadeAlpha;
            set => Require().FadeAlpha = value;
        }

        /// <inheritdoc />
        public float LetterboxRatio
        {
            get => Require().LetterboxRatio;
            set => Require().LetterboxRatio = value;
        }

        /// <summary>
        /// 获取或设置玩法 HUD 是否可见。
        /// 状态保存在本端口内，使落终态与还原不依赖 UI 层是否已经响应过上一条事件。
        /// </summary>
        public bool HudVisible
        {
            get => hudVisible;
            set
            {
                if (hudVisible == value) return;
                hudVisible = value;
                Core.Event.Invoke(new NarrativeHudVisibilityEvent(value));
            }
        }

        /// <inheritdoc />
        public UniTask FadeAsync(float target, float duration, CancellationToken cancellationToken)
        {
            return Require().FadeAsync(target, duration, cancellationToken);
        }

        /// <inheritdoc />
        public UniTask LetterboxAsync(float target, float duration, CancellationToken cancellationToken)
        {
            return Require().LetterboxAsync(target, duration, cancellationToken);
        }

        /// <summary>销毁宿主节点并把 HUD 恢复为可见；重复释放保持幂等。</summary>
        public void Dispose()
        {
            if (host == null) return;
            HudVisible = true;
            StageScope.DestroyObject(host);
            host = null;
            view = null;
        }

        /// <summary>取用屏幕视图；已释放后继续使用属于调用方错误，不做兜底。</summary>
        private NarrativeScreenView Require()
        {
            if (view == null) throw new ObjectDisposedException(nameof(NarrativeRuntimeScreen));
            return view;
        }
    }
}
