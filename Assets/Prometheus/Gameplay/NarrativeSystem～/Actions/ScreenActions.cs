using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;

namespace Xuan.Prometheus.Narrative
{
    /// <summary>
    /// 黑幕过渡动作。
    /// 黑幕不透明度属于世界状态，因此落终态直接写入目标值。
    /// </summary>
    public sealed class ScreenFadeAction : StoryAction
    {
        private readonly float target;
        private readonly float duration;

        /// <summary>创建一个黑幕过渡动作。</summary>
        public ScreenFadeAction(float target, float duration)
        {
            this.target = Mathf.Clamp01(target);
            this.duration = Mathf.Max(0f, duration);
        }

        /// <inheritdoc />
        public override UniTask PlayAsync(StoryContext context, CancellationToken cancellationToken)
        {
            return context.RequireStage().Services.Screen.FadeAsync(target, duration, cancellationToken);
        }

        /// <inheritdoc />
        public override void Settle(StoryContext context)
        {
            context.RequireStage().Services.Screen.FadeAlpha = target;
        }
    }

    /// <summary>黑边过渡动作；黑边比例属于世界状态。</summary>
    public sealed class ScreenLetterboxAction : StoryAction
    {
        private readonly float target;
        private readonly float duration;

        /// <summary>创建一个黑边过渡动作。</summary>
        public ScreenLetterboxAction(float target, float duration)
        {
            this.target = Mathf.Clamp01(target);
            this.duration = Mathf.Max(0f, duration);
        }

        /// <inheritdoc />
        public override UniTask PlayAsync(StoryContext context, CancellationToken cancellationToken)
        {
            return context.RequireStage().Services.Screen.LetterboxAsync(target, duration, cancellationToken);
        }

        /// <inheritdoc />
        public override void Settle(StoryContext context)
        {
            context.RequireStage().Services.Screen.LetterboxRatio = target;
        }
    }

    /// <summary>HUD 显隐动作。</summary>
    public sealed class ScreenHudAction : StoryAction
    {
        private readonly bool visible;

        /// <summary>创建一个 HUD 显隐动作。</summary>
        public ScreenHudAction(bool visible)
        {
            this.visible = visible;
        }

        /// <inheritdoc />
        public override UniTask PlayAsync(StoryContext context, CancellationToken cancellationToken)
        {
            Settle(context);
            return UniTask.CompletedTask;
        }

        /// <inheritdoc />
        public override void Settle(StoryContext context)
        {
            context.RequireStage().Services.Screen.HudVisible = visible;
        }
    }
}
