using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;

namespace Xuan.Prometheus.Narrative
{
    /// <summary>
    /// 顺序组合子：依次演绎子节点。
    /// 处于跳过状态时不再等待任何子节点，而是对剩余节点逐个调用 Settle，使世界状态与完整演绎一致。
    /// </summary>
    public sealed class SequenceAction : StoryAction
    {
        private readonly IStoryAction[] children;

        /// <summary>创建一个顺序组合子。</summary>
        /// <param name="children">按演绎顺序排列的子节点；不允许包含空引用。</param>
        public SequenceAction(params IStoryAction[] children)
        {
            if (children == null) throw new ArgumentNullException(nameof(children));
            for (int index = 0; index < children.Length; index++)
            {
                if (children[index] == null) throw new ArgumentException($"Sequence contains a null child at index {index}.", nameof(children));
            }
            this.children = children;
        }

        /// <inheritdoc />
        public override IReadOnlyList<IStoryAction> Children => children;

        /// <inheritdoc />
        public override async UniTask PlayAsync(StoryContext context, CancellationToken cancellationToken)
        {
            for (int index = 0; index < children.Length; index++)
            {
                IStoryAction child = children[index];
                if (context.IsSkipping)
                {
                    child.Settle(context);
                    continue;
                }
                // 断点续演：续演点之前的子节点只落终态，包含续演点的子节点向下递归。
                if (context.DecideResume(child) == StoryResumeDecision.Settle)
                {
                    child.Settle(context);
                    if (context.ConsumeBreak()) return;
                    continue;
                }
                try
                {
                    await child.PlayAsync(context, cancellationToken);
                }
                catch (OperationCanceledException)
                {
                    // 跳过引发的取消不是错误：把被打断的子节点补到终态后继续处理剩余节点。
                    if (!context.IsSkipping) throw;
                    child.Settle(context);
                    continue;
                }
                if (context.ConsumeBreak()) return;
            }
        }

        /// <inheritdoc />
        public override void Settle(StoryContext context)
        {
            // 落终态必须与正常演绎走同一条控制流，否则 Break 之后的节点会在跳过时被错误应用。
            for (int index = 0; index < children.Length; index++)
            {
                children[index].Settle(context);
                if (context.ConsumeBreak()) return;
            }
        }
    }
}
