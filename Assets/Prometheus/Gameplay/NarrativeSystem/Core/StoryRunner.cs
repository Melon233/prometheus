using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;

namespace Xuan.Prometheus.Narrative
{
    /// <summary>
    /// 驱动一棵剧情树的执行，并对外暴露跳过、中止与节拍进度观察。
    /// 同一个 Runner 同时只能驱动一次演绎。
    /// </summary>
    public sealed class StoryRunner
    {
        /// <summary>控制当前演绎取消与跳过的令牌源；空表示当前没有演绎在进行。</summary>
        private CancellationTokenSource runSource;

        /// <summary>进入一个新节拍时触发，存档层可据此记录续演位置。</summary>
        public event Action<StoryPath> BeatEntered;

        /// <summary>获取当前是否有演绎正在进行。</summary>
        public bool IsRunning => runSource != null;

        /// <summary>获取最近进入的节拍路径。</summary>
        public StoryPath CurrentBeat { get; private set; }

        /// <summary>
        /// 演绎一棵剧情树。
        /// 根节点尚未绑定路径时会先按 rootId 绑定，并校验全树路径唯一。
        /// </summary>
        /// <param name="root">剧情树根节点。</param>
        /// <param name="context">本次演绎使用的上下文。</param>
        /// <param name="rootId">根节点稳定标识，用于生成节点路径。</param>
        /// <param name="cancellationToken">外部取消令牌。</param>
        /// <returns>本次演绎的结束原因。</returns>
        public UniTask<StoryResult> RunAsync(IStoryAction root, StoryContext context, string rootId, CancellationToken cancellationToken = default)
        {
            return RunAsync(root, context, rootId, StoryPath.None, StoryPlayMode.Normal, cancellationToken);
        }

        /// <summary>
        /// 从指定节拍继续演绎一棵剧情树。
        /// <para>
        /// 续演点之前的节点全部走 <c>Settle</c>，因此世界状态与完整演绎一致，但不会重复播放已经看过的表现。
        /// 这与跳过、编辑器预览共用同一条落终态代码路径。
        /// </para>
        /// </summary>
        /// <param name="root">剧情树根节点。</param>
        /// <param name="context">本次演绎使用的上下文；续演前应先从存档恢复变量与选择结果。</param>
        /// <param name="rootId">根节点稳定标识，用于生成节点路径。</param>
        /// <param name="resumeAt">续演点路径；空路径表示从头演绎。</param>
        /// <param name="mode">演绎模式；预览模式下节拍不等待玩家输入。</param>
        /// <param name="cancellationToken">外部取消令牌。</param>
        public async UniTask<StoryResult> RunAsync(IStoryAction root, StoryContext context, string rootId, StoryPath resumeAt, StoryPlayMode mode, CancellationToken cancellationToken = default)
        {
            if (root == null) throw new ArgumentNullException(nameof(root));
            if (context == null) throw new ArgumentNullException(nameof(context));
            if (IsRunning) throw new InvalidOperationException("Story runner is already running a story.");

            if (root.Path.IsEmpty)
            {
                StoryTree.Bind(root, rootId);
                StoryTree.ValidateUniquePaths(root);
            }
            // 续演点必须真实存在于当前剧情树中，否则整棵树会被静默落终态而看不出问题。
            if (!resumeAt.IsEmpty && StoryTree.Find(root, resumeAt) == null) throw new InvalidOperationException($"Story '{rootId}' does not contain resume path '{resumeAt}'. The snapshot and the current story tree are out of sync.");

            runSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            context.SetMode(mode == StoryPlayMode.Preview ? StoryPlayMode.Preview : StoryPlayMode.Normal);
            context.SetResumeTarget(resumeAt);
            context.BeatEnteredCallback = OnBeatEntered;
            CurrentBeat = StoryPath.None;
            try
            {
                await root.PlayAsync(context, runSource.Token);
                if (context.IsResuming)
                {
                    // 快进走完整棵树都没碰到续演点，说明存档记录的分支与当前变量或选择结果不一致。
                    Debug.LogError($"[Narrative] 剧情 '{rootId}' 未能到达续演点 '{resumeAt}'：存档中的分支与当前状态不一致。");
                    return StoryResult.Failed;
                }
                // 组合子会就地消化跳过引发的取消，因此正常返回并不代表演绎完整；以是否请求过跳过为准。
                return context.IsSkipping ? StoryResult.Skipped : StoryResult.Completed;
            }
            catch (OperationCanceledException)
            {
                if (!context.IsSkipping) return StoryResult.Aborted;
                // 组合子会自行消化跳过引发的取消；异常能到达此处说明根节点本身是叶子，补一次落终态即可。
                root.Settle(context);
                return StoryResult.Skipped;
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
                return StoryResult.Failed;
            }
            finally
            {
                context.ConsumeBreak();
                context.SetResumeTarget(StoryPath.None);
                context.SetMode(StoryPlayMode.Normal);
                context.BeatEnteredCallback = null;
                context.View.Hide();
                runSource.Dispose();
                runSource = null;
            }
        }

        /// <summary>
        /// 请求跳过：中断当前节点的等待，并让剩余节点全部走落终态。
        /// 调用方应先确认该段剧情已经被完整看过。
        /// </summary>
        /// <param name="context">正在演绎的上下文。</param>
        public void RequestSkip(StoryContext context)
        {
            if (context == null) throw new ArgumentNullException(nameof(context));
            if (runSource == null) return;
            context.SetMode(StoryPlayMode.Skipping);
            runSource.Cancel();
        }

        /// <summary>中止当前演绎；世界状态可能停在中途，仅供关卡卸载一类的强制路径使用。</summary>
        public void Abort()
        {
            runSource?.Cancel();
        }

        /// <summary>记录并转发节拍进入通知。</summary>
        private void OnBeatEntered(StoryPath path)
        {
            CurrentBeat = path;
            BeatEntered?.Invoke(path);
        }
    }
}
