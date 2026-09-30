using System;
using System.Collections.Generic;
using FMOD.Studio;
using FMODUnity;
using UnityEngine;

namespace Xuan.Prometheus.Narrative
{
    /// <summary>
    /// 只把请求写进日志的音频端口。
    /// 用于没有加载 FMOD Bank 的测试场景：既能验证剧情树在音频节点上的调用时机，又不会刷出播放失败告警。
    /// </summary>
    public sealed class LoggingNarrativeAudioPort : INarrativeAudioPort
    {
        /// <summary>保存当前仍在“播放”的持续事件。</summary>
        private readonly List<LoggedHandle> active = new List<LoggedHandle>();

        /// <inheritdoc />
        public void PlayOneShot(string eventKey, Vector3 position)
        {
            Debug.Log($"[Narrative][Audio] one-shot '{eventKey}' @ {position}");
        }

        /// <inheritdoc />
        public INarrativeAudioHandle PlayPersistent(string eventKey, float fadeInSeconds)
        {
            Debug.Log($"[Narrative][Audio] start '{eventKey}' (fadeIn {fadeInSeconds}s)");
            LoggedHandle handle = new LoggedHandle(eventKey);
            active.Add(handle);
            return handle;
        }

        /// <inheritdoc />
        public void StopPersistent(INarrativeAudioHandle handle, float fadeOutSeconds)
        {
            if (!(handle is LoggedHandle logged) || !logged.IsAlive) return;
            Debug.Log($"[Narrative][Audio] stop '{logged.EventKey}' (fadeOut {fadeOutSeconds}s)");
            logged.Kill();
            active.Remove(logged);
        }

        /// <inheritdoc />
        public void StopAll()
        {
            for (int index = 0; index < active.Count; index++) active[index].Kill();
            active.Clear();
        }

        /// <summary>日志端口使用的事件句柄。</summary>
        private sealed class LoggedHandle : INarrativeAudioHandle
        {
            /// <summary>创建一个日志事件句柄。</summary>
            internal LoggedHandle(string eventKey)
            {
                EventKey = eventKey;
                IsAlive = true;
            }

            /// <inheritdoc />
            public string EventKey { get; }

            /// <inheritdoc />
            public bool IsAlive { get; private set; }

            /// <summary>标记事件已停止。</summary>
            internal void Kill()
            {
                IsAlive = false;
            }
        }
    }

    /// <summary>
    /// 接入工程既有 FMOD 事件表的音频端口。
    /// <para>
    /// 事件键取 <c>FmodAudioEvent</c> 枚举名；一次性事件复用 <see cref="FmodAudioRuntime.PlayOneShot"/>，
    /// 持续事件创建 <see cref="EventInstance"/> 并由本端口负责释放。
    /// 未能解析的事件键只告警一次，不会中断剧情。
    /// </para>
    /// </summary>
    public sealed class FmodNarrativeAudioPort : INarrativeAudioPort
    {
        /// <summary>保存当前仍在播放的持续事件。</summary>
        private readonly List<FmodHandle> active = new List<FmodHandle>();

        /// <summary>记录已经告警过的事件键，避免同一条配置错误刷屏。</summary>
        private readonly HashSet<string> reported = new HashSet<string>(StringComparer.Ordinal);

        /// <inheritdoc />
        public void PlayOneShot(string eventKey, Vector3 position)
        {
            if (!TryParse(eventKey, out FmodAudioEvent audioEvent)) return;
            FmodAudioRuntime.PlayOneShot(audioEvent, position);
        }

        /// <inheritdoc />
        public INarrativeAudioHandle PlayPersistent(string eventKey, float fadeInSeconds)
        {
            if (!TryParse(eventKey, out FmodAudioEvent audioEvent)) return null;
            if (!FmodAudioEventCatalog.TryGetGuid(audioEvent, out FMOD.GUID guid) || guid.IsNull) return null;
            if (!Application.isPlaying) return null;
            try
            {
                EventInstance instance = RuntimeManager.CreateInstance(guid);
                instance.start();
                FmodHandle handle = new FmodHandle(eventKey, instance);
                active.Add(handle);
                return handle;
            }
            catch (Exception exception)
            {
                Warn(eventKey, exception.Message);
                return null;
            }
        }

        /// <inheritdoc />
        public void StopPersistent(INarrativeAudioHandle handle, float fadeOutSeconds)
        {
            if (!(handle is FmodHandle fmodHandle) || !fmodHandle.IsAlive) return;
            fmodHandle.Stop(fadeOutSeconds > 0f);
            active.Remove(fmodHandle);
        }

        /// <inheritdoc />
        public void StopAll()
        {
            for (int index = 0; index < active.Count; index++) active[index].Stop(false);
            active.Clear();
        }

        /// <summary>把事件键解析为生成的事件枚举。</summary>
        private bool TryParse(string eventKey, out FmodAudioEvent audioEvent)
        {
            if (Enum.TryParse(eventKey, false, out audioEvent) && audioEvent != FmodAudioEvent.None) return true;
            Warn(eventKey, "未在 FmodAudioEvent 中找到同名事件。");
            audioEvent = FmodAudioEvent.None;
            return false;
        }

        /// <summary>对同一个事件键只告警一次。</summary>
        private void Warn(string eventKey, string reason)
        {
            if (reported.Add(eventKey)) Debug.LogWarning($"[Narrative][Audio] 事件 '{eventKey}' 不可用：{reason}");
        }

        /// <summary>FMOD 持续事件句柄。</summary>
        private sealed class FmodHandle : INarrativeAudioHandle
        {
            private EventInstance instance;
            private bool alive;

            /// <summary>创建一个 FMOD 事件句柄。</summary>
            internal FmodHandle(string eventKey, EventInstance instance)
            {
                EventKey = eventKey;
                this.instance = instance;
                alive = true;
            }

            /// <inheritdoc />
            public string EventKey { get; }

            /// <inheritdoc />
            public bool IsAlive => alive;

            /// <summary>停止并释放事件实例；重复调用保持幂等。</summary>
            internal void Stop(bool allowFadeOut)
            {
                if (!alive) return;
                alive = false;
                instance.stop(allowFadeOut ? FMOD.Studio.STOP_MODE.ALLOWFADEOUT : FMOD.Studio.STOP_MODE.IMMEDIATE);
                instance.release();
            }
        }
    }
}
