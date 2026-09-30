using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Cysharp.Threading.Tasks;
using UnityEngine;
using UnityEngine.Playables;
using UnityEngine.Timeline;

namespace Xuan.Prometheus.Narrative
{
    /// <summary>
    /// 一次舞台接管的作用域。
    /// <para>
    /// 进入阶段按固定顺序压入还原动作，退出阶段严格逆序弹出执行，
    /// 因此「忘记还原」在语法上不可能发生：只要写了 <c>await using</c>，异常与取消路径同样会走完整套还原。
    /// </para>
    /// </summary>
    public sealed class StageScope : IAsyncDisposable
    {
        /// <summary>保存按进入顺序压入的还原动作，退出时逆序执行。</summary>
        private readonly Stack<StageTeardown> teardowns = new Stack<StageTeardown>();

        /// <summary>保存本次舞台已解析的角色。</summary>
        private readonly Dictionary<ActorRef, ActorHandle> actors = new Dictionary<ActorRef, ActorHandle>();

        /// <summary>保存本次舞台创建的 Timeline 播放器，按资源地址复用以保证分段演出的连续性。</summary>
        private readonly Dictionary<string, PlayableDirector> directors = new Dictionary<string, PlayableDirector>(StringComparer.Ordinal);

        /// <summary>按角色保存当前动画通道的取消源，保证同一副骨架同时只有一段剧情动画在驱动。</summary>
        private readonly Dictionary<ActorRef, CancellationTokenSource> animationChannels = new Dictionary<ActorRef, CancellationTokenSource>();

        /// <summary>保存承载本次舞台运行时对象的根节点。</summary>
        private GameObject runtimeRoot;

        private bool disposed;

        /// <summary>由 Stage 创建舞台作用域。</summary>
        internal StageScope(StageSpec spec, StageServices services)
        {
            Spec = spec ?? throw new ArgumentNullException(nameof(spec));
            Services = services ?? throw new ArgumentNullException(nameof(services));
        }

        /// <summary>获取本次接管使用的声明。</summary>
        public StageSpec Spec { get; }

        /// <summary>获取本次接管使用的能力端口。</summary>
        public StageServices Services { get; }

        /// <summary>获取本次舞台已解析的角色只读视图。</summary>
        public IReadOnlyDictionary<ActorRef, ActorHandle> Actors => actors;

        /// <summary>获取本次舞台是否已经退出。</summary>
        public bool IsDisposed => disposed;

        /// <summary>获取承载本次舞台运行时对象的根节点，按需创建。</summary>
        public Transform RuntimeRoot
        {
            get
            {
                if (runtimeRoot == null)
                {
                    runtimeRoot = new GameObject("[NarrativeStage]");
                    PushTeardown("runtime-root", () =>
                    {
                        DestroyObject(runtimeRoot);
                        runtimeRoot = null;
                        return UniTask.CompletedTask;
                    });
                }
                return runtimeRoot.transform;
            }
        }

        /// <summary>
        /// 为一个角色开启新的动画通道，并中止该角色上一段仍在播放的动画。
        /// <para>
        /// 每个角色同时只允许一段剧情动画驱动骨架，否则两段循环动画会在同一帧互相覆盖，
        /// 结果取决于任务调度顺序而不可复现。
        /// </para>
        /// </summary>
        /// <param name="actor">目标角色。</param>
        /// <param name="outer">外层取消令牌，跳过与中止仍然可以打断动画。</param>
        /// <returns>本段动画应当使用的取消令牌。</returns>
        public CancellationToken BeginActorAnimation(ActorRef actor, CancellationToken outer)
        {
            if (animationChannels.TryGetValue(actor, out CancellationTokenSource previous))
            {
                previous.Cancel();
                previous.Dispose();
            }
            CancellationTokenSource created = CancellationTokenSource.CreateLinkedTokenSource(outer);
            animationChannels[actor] = created;
            return created.Token;
        }

        /// <summary>读取一个已解析角色；未参演的角色会给出明确错误。</summary>
        public ActorHandle RequireActor(ActorRef actor)
        {
            if (actors.TryGetValue(actor, out ActorHandle handle) && handle.IsAlive) return handle;
            throw new InvalidOperationException($"Narrative stage does not contain actor '{actor}'. Declare it in StageSpec.Actors before entering the stage.");
        }

        /// <summary>读取一个已解析角色；不存在时返回 false。</summary>
        public bool TryGetActor(ActorRef actor, out ActorHandle handle)
        {
            return actors.TryGetValue(actor, out handle) && handle.IsAlive;
        }

        /// <summary>
        /// 取得一个按资源地址复用的 Timeline 播放器。
        /// 同一段演出被拆成多段穿插对话时，复用同一个播放器可以保持镜头与姿态的连续性。
        /// </summary>
        public async UniTask<PlayableDirector> AcquireDirectorAsync(string location, CancellationToken cancellationToken)
        {
            if (string.IsNullOrWhiteSpace(location)) throw new ArgumentException("Cinematic location cannot be empty.", nameof(location));
            if (directors.TryGetValue(location, out PlayableDirector existing) && existing != null) return existing;

            PlayableAsset asset = await Services.RequireAssets().LoadAsync<PlayableAsset>(location, cancellationToken);
            if (asset == null) throw new InvalidOperationException($"Cinematic asset '{location}' could not be loaded.");

            GameObject directorObject = new GameObject($"Cinematic_{location}");
            directorObject.transform.SetParent(RuntimeRoot, false);
            PlayableDirector director = directorObject.AddComponent<PlayableDirector>();
            director.playableAsset = asset;
            director.extrapolationMode = DirectorWrapMode.None;
            director.timeUpdateMode = DirectorUpdateMode.GameTime;
            director.playOnAwake = false;
            BindOutputs(director, location);
            directors[location] = director;
            PushTeardown($"cinematic:{location}", () =>
            {
                if (director != null)
                {
                    director.Stop();
                    DestroyObject(director.gameObject);
                }
                return UniTask.CompletedTask;
            });
            return director;
        }

        /// <summary>读取一个已创建的 Timeline 播放器。</summary>
        public bool TryGetDirector(string location, out PlayableDirector director)
        {
            return directors.TryGetValue(location, out director) && director != null;
        }

        /// <summary>取用一个已创建的 Timeline 播放器；未在 StageSpec.Cinematics 中声明时给出明确错误。</summary>
        public PlayableDirector RequireDirector(string location)
        {
            if (TryGetDirector(location, out PlayableDirector director)) return director;
            throw new InvalidOperationException($"Cinematic '{location}' was not prepared. Declare it in StageSpec.Cinematics so that both playback and skipping can address the same director.");
        }

        /// <summary>
        /// 按输出轨道名把 Timeline 绑定到已解析的角色。
        /// 约定：输出轨道名与 <see cref="ActorRef.Id"/> 相同即自动绑定，绑定目标按轨道要求的组件类型解析。
        /// </summary>
        private void BindOutputs(PlayableDirector director, string location)
        {
            if (!(director.playableAsset is TimelineAsset timeline)) return;
            foreach (PlayableBinding output in timeline.outputs)
            {
                if (string.IsNullOrWhiteSpace(output.streamName) || output.sourceObject == null) continue;
                if (!TryFindActorById(output.streamName, out ActorHandle handle)) continue;
                Type required = output.outputTargetType;
                UnityEngine.Object target = required == null || required == typeof(GameObject)
                    ? handle.GameObject
                    : handle.GameObject.GetComponentInChildren(required);
                if (target == null)
                {
                    Debug.LogWarning($"[Narrative] 演出 '{location}' 的轨道 '{output.streamName}' 需要 {required?.Name}，但角色 '{handle.Actor}' 上找不到该组件。");
                    continue;
                }
                director.SetGenericBinding(output.sourceObject, target);
            }
            WarnOnSideEffectTracks(timeline, location);
        }

        /// <summary>按角色标识查找已解析角色。</summary>
        private bool TryFindActorById(string actorId, out ActorHandle handle)
        {
            foreach (KeyValuePair<ActorRef, ActorHandle> pair in actors)
            {
                if (string.Equals(pair.Key.Id, actorId, StringComparison.Ordinal))
                {
                    handle = pair.Value;
                    return true;
                }
            }
            handle = null;
            return false;
        }

        /// <summary>
        /// 对含有一次性副作用的轨道给出警告。
        /// 设计规则 R1 要求这类逻辑上移到剧情树；留在 Timeline 里会让跳过与断点续演产生不一致。
        /// </summary>
        private static void WarnOnSideEffectTracks(TimelineAsset timeline, string location)
        {
            foreach (TrackAsset track in timeline.GetOutputTracks())
            {
                string typeName = track.GetType().Name;
                if (typeName != "SignalTrack" && typeName != "ControlTrack") continue;
                Debug.LogWarning($"[Narrative] 演出 '{location}' 含有 {typeName}（轨道 '{track.name}'）。剧情演出禁止在 Timeline 中产生一次性副作用，请把该逻辑上移到剧情树。");
            }
        }

        /// <summary>登记一个需要随舞台退出一并释放的资源。</summary>
        public void Track(string label, Func<UniTask> release)
        {
            PushTeardown(label, release);
        }

        /// <summary>登记一个需要随舞台退出一并释放的同步资源。</summary>
        public void Track(string label, Action release)
        {
            if (release == null) throw new ArgumentNullException(nameof(release));
            PushTeardown(label, () =>
            {
                release();
                return UniTask.CompletedTask;
            });
        }

        /// <summary>按进入顺序压入一个还原动作。</summary>
        internal void PushTeardown(string label, Func<UniTask> action)
        {
            if (action == null) throw new ArgumentNullException(nameof(action));
            teardowns.Push(new StageTeardown(label, action));
        }

        /// <summary>登记一个已解析角色。</summary>
        internal void RegisterActor(ActorHandle handle)
        {
            actors[handle.Actor] = handle;
        }

        /// <summary>
        /// 逆序执行全部还原动作。
        /// 单个还原动作失败不会阻断其余动作，避免一个异常导致输入或镜头永久卡死。
        /// </summary>
        public async UniTask DisposeAsync()
        {
            if (disposed) return;
            disposed = true;
            foreach (KeyValuePair<ActorRef, CancellationTokenSource> channel in animationChannels)
            {
                channel.Value.Cancel();
                channel.Value.Dispose();
            }
            animationChannels.Clear();
            actors.Clear();
            directors.Clear();
            while (teardowns.Count > 0)
            {
                StageTeardown teardown = teardowns.Pop();
                try
                {
                    await teardown.Action();
                }
                catch (Exception exception)
                {
                    Debug.LogError($"[Narrative] 舞台还原步骤 '{teardown.Label}' 失败：{exception}");
                }
            }
        }

        /// <inheritdoc />
        ValueTask IAsyncDisposable.DisposeAsync()
        {
            return new ValueTask(DisposeAsync().AsTask());
        }

        /// <summary>在编辑器与运行时之间选择正确的销毁方式。</summary>
        internal static void DestroyObject(UnityEngine.Object target)
        {
            if (target == null) return;
            if (Application.isPlaying) UnityEngine.Object.Destroy(target);
            else UnityEngine.Object.DestroyImmediate(target);
        }

        /// <summary>一个带标签的还原动作，标签只用于诊断日志。</summary>
        private readonly struct StageTeardown
        {
            /// <summary>创建一个还原动作。</summary>
            internal StageTeardown(string label, Func<UniTask> action)
            {
                Label = label;
                Action = action;
            }

            /// <summary>获取还原步骤名称。</summary>
            internal string Label { get; }

            /// <summary>获取还原逻辑。</summary>
            internal Func<UniTask> Action { get; }
        }
    }
}
