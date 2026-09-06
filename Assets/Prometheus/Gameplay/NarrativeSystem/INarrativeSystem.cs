using System;
using System.Threading;
using Cysharp.Threading.Tasks;

namespace Xuan.Prometheus.Narrative
{
    /// <summary>定义剧情演绎、跳过、断点续演与存档的公共入口。</summary>
    public interface INarrativeSystem : ISystemContract
    {
        /// <summary>进入一个新节拍时触发，存档层可据此记录续演位置。</summary>
        event Action<StoryPath> BeatEntered;

        /// <summary>获取当前是否有剧情正在演绎。</summary>
        bool IsPlaying { get; }

        /// <summary>获取最近进入的节拍路径。</summary>
        StoryPath CurrentBeat { get; }

        /// <summary>获取当前正在演绎的剧情标识；没有进行中的剧情时为空。</summary>
        string CurrentStoryId { get; }

        /// <summary>获取存档中记录的续演剧情标识；没有可续演的剧情时为空。</summary>
        string ResumeStoryId { get; }

        /// <summary>获取存档中记录的续演节拍路径；没有可续演的剧情时为空路径。</summary>
        StoryPath ResumePath { get; }

        /// <summary>获取剧情变量与旗标存储。</summary>
        StoryVariables Variables { get; }

        /// <summary>获取运行时文本表。</summary>
        TextMap Text { get; }

        /// <summary>获取或设置自动播放开关。</summary>
        bool AutoPlay { get; set; }

        /// <summary>
        /// 获取或设置当前生效的舞台作用域。
        /// 调用方用 <c>await using</c> 进入舞台后写入本属性，退出作用域前置空；
        /// 纯对话剧情可以不设置，此时任何依赖舞台的动作都会给出明确错误。
        /// </summary>
        StageScope Stage { get; set; }

        /// <summary>
        /// 获取当前剧情是否允许跳过。
        /// 与原神一致：只有完整看过一次的剧情才开放跳过。
        /// </summary>
        bool CanSkip { get; }

        /// <summary>判断一段剧情是否已经被完整看过。</summary>
        bool HasSeen(string storyId);

        /// <summary>设置对话表现端口；必须在首次演绎之前注入。</summary>
        void SetView(IDialogueView view);

        /// <summary>从头演绎一棵剧情树。</summary>
        UniTask<StoryResult> PlayAsync(IStoryAction root, string storyId, CancellationToken cancellationToken = default);

        /// <summary>
        /// 从指定节拍继续演绎一棵剧情树。
        /// 续演点之前的节点全部走落终态，因此世界状态与完整演绎一致，但不重复播放已看过的表现。
        /// </summary>
        UniTask<StoryResult> ResumeAsync(IStoryAction root, string storyId, StoryPath resumeAt, CancellationToken cancellationToken = default);

        /// <summary>
        /// 以预览模式从指定节拍开始演绎，供编辑器工具使用。
        /// 预览模式下节拍不等待玩家输入，前置节点同样只落终态。
        /// </summary>
        UniTask<StoryResult> PreviewAsync(IStoryAction root, string storyId, StoryPath startAt, CancellationToken cancellationToken = default);

        /// <summary>请求跳过当前演绎；剧情尚未被完整看过时会被拒绝并返回 false。</summary>
        bool RequestSkip();

        /// <summary>中止当前演绎；世界状态可能停在中途。</summary>
        void Abort();

        /// <summary>捕获剧情存档。</summary>
        string CaptureSnapshot();

        /// <summary>从存档恢复剧情状态；恢复后可用 ResumeStoryId 与 ResumePath 继续上次的剧情。</summary>
        void RestoreSnapshot(string json);
    }
}
