using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;
using UnityEngine.Playables;
using UnityEngine.Timeline;

namespace Xuan.Prometheus.Narrative
{
    /// <summary>
    /// 演出片段动作：播放一段纯表现 Timeline。
    /// <para>
    /// 遵循设计规则 R1，Timeline 只承载可插值、可 <c>Evaluate</c> 的连续表现，
    /// 不得包含任何一次性副作用。正因如此，落终态可以简单地写成
    /// 「把时间推到片段末尾并 Evaluate 一次」，而不需要补执行任何被跳过的逻辑。
    /// </para>
    /// <para>
    /// 同一段 Timeline 被拆成多段穿插对话时，各段复用舞台上的同一个 <see cref="PlayableDirector"/>，
    /// 因此镜头与姿态在段与段之间保持连续。
    /// </para>
    /// </summary>
    public sealed class CinematicAction : StoryAction
    {
        private readonly string location;
        private readonly string fromMarker;
        private readonly string toMarker;

        /// <summary>创建一个播放整段 Timeline 的演出动作。</summary>
        /// <param name="location">Timeline 资源地址；必须在 StageSpec.Cinematics 中声明。</param>
        public CinematicAction(string location)
        {
            if (string.IsNullOrWhiteSpace(location)) throw new ArgumentException("Cinematic location cannot be empty.", nameof(location));
            this.location = location;
        }

        /// <summary>创建一个播放指定标记区间的演出动作。</summary>
        /// <param name="location">Timeline 资源地址。</param>
        /// <param name="fromMarker">起始标记名；为空表示从当前时间继续。</param>
        /// <param name="toMarker">结束标记名；为空表示播到片段结尾。</param>
        public CinematicAction(string location, string fromMarker, string toMarker) : this(location)
        {
            this.fromMarker = fromMarker;
            this.toMarker = toMarker;
        }

        /// <inheritdoc />
        public override async UniTask PlayAsync(StoryContext context, CancellationToken cancellationToken)
        {
            StageScope stage = context.RequireStage();
            PlayableDirector director = stage.RequireDirector(location);
            ResolveRange(director, out double start, out double end);

            director.time = start;
            director.Evaluate();
            director.Play();
            try
            {
                while (director.time < end)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    await UniTask.Yield(PlayerLoopTiming.Update, cancellationToken);
                    if (director == null) return;
                    // Director 自然播到结尾会停止，此时不再等待。
                    if (director.state != PlayState.Playing) break;
                }
            }
            finally
            {
                if (director != null)
                {
                    // 无论正常结束、取消还是跳过，都把时间推到区间末尾并求值一次，
                    // 保证世界不会停在半程姿态。
                    director.time = end;
                    director.Evaluate();
                    director.Pause();
                }
            }
        }

        /// <inheritdoc />
        public override void Settle(StoryContext context)
        {
            StageScope stage = context.RequireStage();
            if (!stage.TryGetDirector(location, out PlayableDirector director) || director == null) return;
            ResolveRange(director, out double start, out double end);
            if (director.time < start) director.time = start;
            director.time = end;
            director.Evaluate();
            director.Pause();
        }

        /// <summary>把标记名解析为时间区间；标记缺失时给出可诊断的错误。</summary>
        private void ResolveRange(PlayableDirector director, out double start, out double end)
        {
            double duration = director.duration;
            start = string.IsNullOrEmpty(fromMarker) ? director.time : RequireMarkerTime(director, fromMarker);
            end = string.IsNullOrEmpty(toMarker) ? duration : RequireMarkerTime(director, toMarker);
            if (end < start) throw new InvalidOperationException($"Cinematic '{location}' range '{fromMarker}'..'{toMarker}' is inverted.");
            start = Math.Max(0d, Math.Min(duration, start));
            end = Math.Max(0d, Math.Min(duration, end));
        }

        /// <summary>按名称查找 Timeline 标记轨道上的时间点。</summary>
        private double RequireMarkerTime(PlayableDirector director, string markerName)
        {
            if (!(director.playableAsset is TimelineAsset timeline)) throw new InvalidOperationException($"Cinematic '{location}' is not a TimelineAsset and cannot use markers.");
            MarkerTrack markerTrack = timeline.markerTrack;
            if (markerTrack != null)
            {
                foreach (IMarker marker in markerTrack.GetMarkers())
                {
                    if (marker is CinematicCueMarker cue && string.Equals(cue.CueName, markerName, StringComparison.Ordinal)) return cue.time;
                }
            }
            throw new InvalidOperationException($"Cinematic '{location}' does not contain a cue marker named '{markerName}'.");
        }
    }

    /// <summary>
    /// 演出片段上的具名时间点。
    /// 该标记只用于把一段 Timeline 切成可被剧情树分段引用的区间，本身不触发任何逻辑，
    /// 因此不违反「Timeline 无副作用」的规则。
    /// </summary>
    public sealed class CinematicCueMarker : Marker
    {
        [SerializeField] [Tooltip("供剧情树引用的区间名。")] private string cueName;

        /// <summary>获取或设置区间名。</summary>
        public string CueName { get => cueName; set => cueName = value; }
    }
}
