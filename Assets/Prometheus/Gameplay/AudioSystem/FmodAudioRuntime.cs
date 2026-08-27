using System;
using System.Collections.Generic;
using FMODUnity;
using UnityEngine;

namespace Xuan.Prometheus
{
    /// <summary>集中解析 AnimationLine 音效标记并以 FMOD GUID 播放一次性三维事件。</summary>
    public static class FmodAudioRuntime
    {
        /// <summary>定义注入 Spine EventTimeline 的保留事件名，普通玩法事件不得使用该名称。</summary>
        public const string AnimationMarkerEventName = "__prometheus_fmod_audio";

        private static readonly HashSet<FmodAudioEvent> ReportedFailures = new HashSet<FmodAudioEvent>();

        /// <summary>判断并消费一个 AnimationLine FMOD 标记；普通 Spine 事件返回 false 并继续交给玩法订阅者。</summary>
        public static bool TryConsumeAnimationMarker(Spine.Event animationEvent, Vector3 worldPosition)
        {
            if (animationEvent == null || animationEvent.Data == null || !string.Equals(animationEvent.Data.Name, AnimationMarkerEventName, StringComparison.Ordinal)) return false;
            PlayOneShot((FmodAudioEvent)animationEvent.Int, worldPosition);
            return true;
        }

        /// <summary>在世界坐标播放一个生成枚举对应的 FMOD 一次性事件；None 或未生成映射会安全忽略。</summary>
        public static bool PlayOneShot(FmodAudioEvent audioEvent, Vector3 worldPosition)
        {
            if (audioEvent == FmodAudioEvent.None || !FmodAudioEventCatalog.TryGetGuid(audioEvent, out FMOD.GUID guid) || guid.IsNull) return false;
#if UNITY_EDITOR
            // 编辑模式动画测试仍会推进 Spine 事件，但 FMOD RuntimeManager 只允许在播放模式访问，因此此处仅消费标记而不启动音频系统。
            if (!Application.isPlaying) return false;
#endif
            try
            {
                RuntimeManager.PlayOneShot(guid, worldPosition);
                return true;
            }
            catch (Exception exception)
            {
                if (ReportedFailures.Add(audioEvent)) Debug.LogWarning($"FMOD 音频事件 '{audioEvent}' 播放失败，请确认 Master Bank 与事件所在 Bank 已加载。\n{exception.Message}");
                return false;
            }
        }
    }
}
