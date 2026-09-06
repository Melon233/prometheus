using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;

namespace Xuan.Prometheus.Narrative
{
    /// <summary>
    /// 并发组合子：同时启动全部子节点，按 ParallelMode 决定何时返回。
    /// 处于跳过状态时不启动任何子节点，直接对全部子节点调用 Settle。
    /// </summary>
    public sealed class ParallelAction : StoryAction
    {
        private readonly IStoryAction[] children;
        private readonly ParallelMode mode;

        /// <summary>创建一个并发组合子。</summary>
        /// <param name="mode">完成条件。</param>
        /// <param name="children">并发演绎的子节点；不允许包含空引用。</param>
        public ParallelAction(ParallelMode mode, params IStoryAction[] children)
        {
            if (children == null) throw new ArgumentNullException(nameof(children));
            for (int index = 0; index < children.Length; index++)
            {
                if (children[index] == null) throw new ArgumentException($"Parallel contains a null child at index {index}.", nameof(children));
            }
            this.mode = mode;
            this.children = children;
        }

        /// <inheritdoc />
        public override IReadOnlyList<IStoryAction> Children => children;

        /// <summary>获取当前组合子的完成条件。</summary>
        public ParallelMode Mode => mode;

        /// <inheritdoc />
        public override async UniTask PlayAsync(StoryContext context, CancellationToken cancellationToken)
        {
            if (context.IsSkipping)
            {
                Settle(context);
                return;
            }
            if (children.Length == 0) return;
            if (context.IsResuming)
            {
                await ResumeAsync(context, cancellationToken);
                return;
            }
            if (mode == ParallelMode.Detached)
            {
                // 分离子节点随本次演绎的取消令牌一并结束，不阻塞父节点推进。
                for (int index = 0; index < children.Length; index++) RunDetachedAsync(children[index], context, cancellationToken).Forget();
                return;
            }
            using (CancellationTokenSource linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken))
            {
                List<UniTask> tasks = new List<UniTask>(children.Length);
                for (int index = 0; index < children.Length; index++) tasks.Add(children[index].PlayAsync(context, linked.Token));
                try
                {
                    if (mode == ParallelMode.WhenAll) await UniTask.WhenAll(tasks);
                    else await UniTask.WhenAny(tasks);
                }
                catch (OperationCanceledException)
                {
                    if (!context.IsSkipping) throw;
                    Settle(context);
                    return;
                }
                finally
                {
                    linked.Cancel();
                }
            }
        }

        /// <inheritdoc />
        public override void Settle(StoryContext context)
        {
            for (int index = 0; index < children.Length; index++) children[index].Settle(context);
        }

        /// <summary>
        /// 快进到续演点。
        /// 并发子节点之间没有先后关系，因此不含续演点的子节点一律落终态，
        /// 只有包含续演点的那一个继续向下演绎。
        /// </summary>
        private async UniTask ResumeAsync(StoryContext context, CancellationToken cancellationToken)
        {
            int targetIndex = -1;
            for (int index = 0; index < children.Length; index++)
            {
                if (!context.ContainsResumeTarget(children[index])) continue;
                targetIndex = index;
                break;
            }
            if (targetIndex < 0)
            {
                Settle(context);
                return;
            }
            for (int index = 0; index < children.Length; index++)
            {
                if (index != targetIndex) children[index].Settle(context);
            }
            context.DecideResume(children[targetIndex]);
            await children[targetIndex].PlayAsync(context, cancellationToken);
        }

        /// <summary>演绎一个分离子节点并吞掉取消异常，避免后台任务把异常抛到主循环。</summary>
        private static async UniTaskVoid RunDetachedAsync(IStoryAction child, StoryContext context, CancellationToken cancellationToken)
        {
            try
            {
                await child.PlayAsync(context, cancellationToken);
            }
            catch (OperationCanceledException)
            {
            }
        }
    }
}
