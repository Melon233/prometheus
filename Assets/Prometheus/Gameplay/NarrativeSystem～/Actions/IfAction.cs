using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;

namespace Xuan.Prometheus.Narrative
{
    /// <summary>
    /// 条件分支叶子：按表达式求值结果选择一个分支演绎。
    /// 分支活在剧情树上而不是时间轴上，因此不需要任何时间跳转。
    /// </summary>
    public sealed class IfAction : StoryAction
    {
        private readonly string expression;
        private readonly IStoryAction thenBranch;
        private readonly IStoryAction elseBranch;
        private readonly IStoryAction[] children;

        /// <summary>创建一个条件分支。</summary>
        /// <param name="expression">条件表达式文本。</param>
        /// <param name="thenBranch">条件成立时演绎的分支。</param>
        /// <param name="elseBranch">条件不成立时演绎的分支；可为空。</param>
        public IfAction(string expression, IStoryAction thenBranch, IStoryAction elseBranch = null)
        {
            if (string.IsNullOrWhiteSpace(expression)) throw new ArgumentException("If action requires a condition expression.", nameof(expression));
            this.expression = expression;
            this.thenBranch = thenBranch ?? throw new ArgumentNullException(nameof(thenBranch));
            this.elseBranch = elseBranch;
            children = elseBranch == null ? new[] { thenBranch } : new[] { thenBranch, elseBranch };
        }

        /// <inheritdoc />
        public override IReadOnlyList<IStoryAction> Children => children;

        /// <summary>获取条件表达式文本。</summary>
        public string Expression => expression;

        /// <inheritdoc />
        public override UniTask PlayAsync(StoryContext context, CancellationToken cancellationToken)
        {
            IStoryAction branch = Select(context);
            if (branch == null) return UniTask.CompletedTask;
            if (context.IsResuming)
            {
                // 续演点不在命中的分支里，说明该分支整体位于续演点之前：只落终态。
                if (!context.ContainsResumeTarget(branch))
                {
                    branch.Settle(context);
                    return UniTask.CompletedTask;
                }
                context.DecideResume(branch);
            }
            return branch.PlayAsync(context, cancellationToken);
        }

        /// <inheritdoc />
        public override void Settle(StoryContext context)
        {
            // 只对当前条件选中的分支落终态，未选中的分支不应产生任何世界状态。
            Select(context)?.Settle(context);
        }

        /// <summary>按当前变量求值并选择分支。</summary>
        private IStoryAction Select(StoryContext context)
        {
            return context.Evaluate(expression) ? thenBranch : elseBranch;
        }
    }
}
