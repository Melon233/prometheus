using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;

namespace Xuan.Prometheus.Narrative
{
    /// <summary>
    /// 一次性音效动作。
    /// 音效是纯表现，落终态为空实现：跳过一段剧情不应把其中的音效补放出来。
    /// </summary>
    public sealed class SfxAction : StoryAction
    {
        private readonly string eventKey;
        private readonly VfxPlacement placement;
        private readonly bool positional;

        /// <summary>创建一个在指定位置播放的一次性音效。</summary>
        public SfxAction(string eventKey, VfxPlacement placement)
        {
            if (string.IsNullOrWhiteSpace(eventKey)) throw new ArgumentException("Audio event key cannot be empty.", nameof(eventKey));
            this.eventKey = eventKey;
            this.placement = placement;
            positional = true;
        }

        /// <summary>创建一个不带位置的一次性音效。</summary>
        public SfxAction(string eventKey)
        {
            if (string.IsNullOrWhiteSpace(eventKey)) throw new ArgumentException("Audio event key cannot be empty.", nameof(eventKey));
            this.eventKey = eventKey;
            positional = false;
        }

        /// <inheritdoc />
        public override UniTask PlayAsync(StoryContext context, CancellationToken cancellationToken)
        {
            StageScope stage = context.RequireStage();
            Vector3 position = Vector3.zero;
            if (positional)
            {
                position = placement.FollowsActor
                    ? stage.RequireActor(placement.Actor).Transform.TransformPoint(placement.Offset)
                    : placement.Anchor.Resolve(stage.Services.Actors) + placement.Offset;
            }
            stage.Services.RequireAudio().PlayOneShot(eventKey, position);
            return UniTask.CompletedTask;
        }

        /// <inheritdoc />
        public override void Settle(StoryContext context)
        {
            // 纯表现动作：跳过时不补放音效。
        }
    }

    /// <summary>
    /// 持续音频动作，用于背景音乐与环境音。
    /// 事件句柄登记在舞台作用域上并随舞台退出停止，因此本动作同样按纯表现处理；
    /// 需要跨越演出持续存在的音乐属于世界状态，应由显式的状态动作表达。
    /// </summary>
    public sealed class AmbienceAction : StoryAction
    {
        private readonly string eventKey;
        private readonly float fadeInSeconds;
        private readonly float fadeOutSeconds;

        /// <summary>创建一个持续音频动作。</summary>
        public AmbienceAction(string eventKey, float fadeInSeconds = 0f, float fadeOutSeconds = 1f)
        {
            if (string.IsNullOrWhiteSpace(eventKey)) throw new ArgumentException("Audio event key cannot be empty.", nameof(eventKey));
            this.eventKey = eventKey;
            this.fadeInSeconds = Mathf.Max(0f, fadeInSeconds);
            this.fadeOutSeconds = Mathf.Max(0f, fadeOutSeconds);
        }

        /// <inheritdoc />
        public override UniTask PlayAsync(StoryContext context, CancellationToken cancellationToken)
        {
            StageScope stage = context.RequireStage();
            INarrativeAudioPort audio = stage.Services.RequireAudio();
            INarrativeAudioHandle handle = audio.PlayPersistent(eventKey, fadeInSeconds);
            if (handle == null) return UniTask.CompletedTask;
            stage.Track($"audio:{eventKey}", () =>
            {
                audio.StopPersistent(handle, fadeOutSeconds);
                return UniTask.CompletedTask;
            });
            return UniTask.CompletedTask;
        }

        /// <inheritdoc />
        public override void Settle(StoryContext context)
        {
            // 纯表现动作：跳过时不启动持续音频。
        }
    }
}
