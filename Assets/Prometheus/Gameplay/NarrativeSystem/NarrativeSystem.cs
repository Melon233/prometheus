using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;

namespace Xuan.Prometheus.Narrative
{
    /// <summary>
    /// 单局剧情系统：持有文本表、变量存储与剧情执行器，向业务层提供演绎、跳过、断点续演与存档入口。
    /// 舞台接管由调用方用 <c>await using</c> 持有并写入 <see cref="StoryContext.Stage"/>，
    /// 使纯对话剧情不必进入舞台也能演绎。
    /// </summary>
    internal sealed class NarrativeSystem : XSystem, INarrativeSystem
    {
        /// <summary>保存单局唯一的剧情执行器。</summary>
        private readonly StoryRunner runner = new StoryRunner();

        /// <summary>保存已完整看过的剧情标识；只有其中的剧情才允许跳过。</summary>
        private readonly HashSet<string> seenStories = new HashSet<string>(StringComparer.Ordinal);

        /// <summary>保存尚未构建上下文时暂存的自动播放设置。</summary>
        private bool pendingAutoPlay;

        /// <summary>保存上下文重建期间需要保留的选择结果，避免更换视图导致存档级状态丢失。</summary>
        private IReadOnlyDictionary<string, int> pendingChoices;

        /// <summary>保存上下文重建期间需要保留的一次性选项记录。</summary>
        private List<string> pendingChosenOptions;

        private IDialogueView view;
        private StoryContext context;

        /// <inheritdoc />
        public event Action<StoryPath> BeatEntered;

        /// <summary>创建剧情系统并建立空的文本表与变量存储。</summary>
        public NarrativeSystem()
        {
            Text = new TextMap();
            Variables = new StoryVariables();
        }

        /// <inheritdoc />
        public bool IsPlaying => runner.IsRunning;

        /// <inheritdoc />
        public StoryPath CurrentBeat => runner.CurrentBeat;

        /// <inheritdoc />
        public string CurrentStoryId { get; private set; }

        /// <inheritdoc />
        public string ResumeStoryId { get; private set; }

        /// <inheritdoc />
        public StoryPath ResumePath { get; private set; }

        /// <inheritdoc />
        public StoryVariables Variables { get; }

        /// <inheritdoc />
        public TextMap Text { get; }

        /// <inheritdoc />
        public bool CanSkip => IsPlaying && !string.IsNullOrEmpty(CurrentStoryId) && seenStories.Contains(CurrentStoryId);

        /// <inheritdoc />
        public StageScope Stage
        {
            get => context != null ? context.Stage : null;
            set => EnsureContext().Stage = value;
        }

        /// <inheritdoc />
        public bool AutoPlay
        {
            get => context != null ? context.AutoPlay : pendingAutoPlay;
            set
            {
                pendingAutoPlay = value;
                if (context != null) context.AutoPlay = value;
                if (view != null) view.AutoPlay = value;
            }
        }

        /// <summary>订阅执行器的节拍通知。</summary>
        public override void AfterNew()
        {
            runner.BeatEntered += OnBeatEntered;
        }

        /// <inheritdoc />
        public bool HasSeen(string storyId)
        {
            return !string.IsNullOrEmpty(storyId) && seenStories.Contains(storyId);
        }

        /// <inheritdoc />
        public void SetView(IDialogueView view)
        {
            if (runner.IsRunning) throw new InvalidOperationException("Narrative view cannot be replaced while a story is playing.");
            this.view = view ?? throw new ArgumentNullException(nameof(view));
            this.view.AutoPlay = pendingAutoPlay;
            // 更换视图会重建上下文，因此先把属于存档层的选择结果暂存下来，避免随上下文一起丢失。
            CacheContextState();
            context = null;
        }

        /// <inheritdoc />
        public UniTask<StoryResult> PlayAsync(IStoryAction root, string storyId, CancellationToken cancellationToken = default)
        {
            return RunAsync(root, storyId, StoryPath.None, StoryPlayMode.Normal, cancellationToken);
        }

        /// <inheritdoc />
        public UniTask<StoryResult> ResumeAsync(IStoryAction root, string storyId, StoryPath resumeAt, CancellationToken cancellationToken = default)
        {
            return RunAsync(root, storyId, resumeAt, StoryPlayMode.Normal, cancellationToken);
        }

        /// <inheritdoc />
        public UniTask<StoryResult> PreviewAsync(IStoryAction root, string storyId, StoryPath startAt, CancellationToken cancellationToken = default)
        {
            return RunAsync(root, storyId, startAt, StoryPlayMode.Preview, cancellationToken);
        }

        /// <inheritdoc />
        public bool RequestSkip()
        {
            if (!IsPlaying) return false;
            if (!CanSkip)
            {
                Debug.Log($"[Narrative] 剧情 '{CurrentStoryId}' 尚未被完整看过，不允许跳过。");
                return false;
            }
            runner.RequestSkip(RequireContext());
            return true;
        }

        /// <inheritdoc />
        public void Abort()
        {
            runner.Abort();
        }

        /// <inheritdoc />
        public string CaptureSnapshot()
        {
            return NarrativeSnapshot.Capture(EnsureContext(), seenStories, ResumeStoryId, ResumePath).ToJson();
        }

        /// <inheritdoc />
        public void RestoreSnapshot(string json)
        {
            if (runner.IsRunning) throw new InvalidOperationException("Narrative snapshot cannot be restored while a story is playing.");
            NarrativeSnapshot snapshot = NarrativeSnapshot.FromJson(json);
            StoryContext target = EnsureContext();
            snapshot.RestoreTo(target);
            CacheContextState();
            seenStories.Clear();
            for (int index = 0; index < snapshot.seenStories.Count; index++) seenStories.Add(snapshot.seenStories[index]);
            ResumeStoryId = snapshot.resumeStoryId;
            ResumePath = snapshot.GetResumePath();
        }

        /// <summary>释放订阅与运行时引用。</summary>
        public override void Dispose()
        {
            runner.Abort();
            runner.BeatEntered -= OnBeatEntered;
            BeatEntered = null;
            context = null;
            view = null;
            pendingChoices = null;
            pendingChosenOptions = null;
            seenStories.Clear();
            CurrentStoryId = null;
            ResumeStoryId = null;
            ResumePath = StoryPath.None;
            Text.Clear();
            Variables.Clear();
        }

        /// <summary>统一的演绎入口：维护当前剧情标识、已看过集合与续演点。</summary>
        private async UniTask<StoryResult> RunAsync(IStoryAction root, string storyId, StoryPath resumeAt, StoryPlayMode mode, CancellationToken cancellationToken)
        {
            if (string.IsNullOrWhiteSpace(storyId)) throw new ArgumentException("Story id cannot be empty.", nameof(storyId));
            StoryContext current = RequireContext();
            CurrentStoryId = storyId;
            try
            {
                StoryResult result = await runner.RunAsync(root, current, storyId, resumeAt, mode, cancellationToken);
                if (mode == StoryPlayMode.Preview) return result;
                if (result == StoryResult.Completed || result == StoryResult.Skipped)
                {
                    // 跳过同样意味着这段剧情已经走完，续演点随之清空。
                    ResumeStoryId = null;
                    ResumePath = StoryPath.None;
                    // 只有完整演绎才算「看过」；跳过不应把尚未看过的剧情标记为已看过。
                    if (result == StoryResult.Completed) seenStories.Add(storyId);
                }
                else
                {
                    // 中止或失败：把当前节拍记为续演点，下次进入时从这里继续。
                    ResumeStoryId = storyId;
                    ResumePath = runner.CurrentBeat;
                }
                return result;
            }
            finally
            {
                CurrentStoryId = null;
            }
        }

        /// <summary>取用用于演绎的上下文；缺少视图时拒绝，避免剧情在无人可见的情况下跑完。</summary>
        private StoryContext RequireContext()
        {
            if (view == null) throw new InvalidOperationException("NarrativeSystem requires a dialogue view before playing a story.");
            return EnsureContext();
        }

        /// <summary>
        /// 取用上下文；尚未注入视图时用空视图占位，使存档读写不必依赖界面。
        /// 新建上下文会把暂存的选择结果写回，保证更换视图不丢失存档级状态。
        /// </summary>
        private StoryContext EnsureContext()
        {
            if (context != null) return context;
            context = new StoryContext(Text, view ?? (IDialogueView)NullDialogueView.Instance, Variables) { AutoPlay = pendingAutoPlay };
            if (pendingChoices != null) context.RestoreChoices(pendingChoices);
            if (pendingChosenOptions != null) context.RestoreChosenOptions(pendingChosenOptions);
            return context;
        }

        /// <summary>把上下文中属于存档层的状态暂存到系统，供上下文重建后写回。</summary>
        private void CacheContextState()
        {
            if (context == null) return;
            pendingChoices = context.CaptureChoices();
            pendingChosenOptions = new List<string>(context.ChosenOptions);
        }

        /// <summary>转发执行器的节拍通知。</summary>
        private void OnBeatEntered(StoryPath path)
        {
            BeatEntered?.Invoke(path);
        }
    }
}
