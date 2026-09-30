using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;
using Xuan.Prometheus.Expression;

namespace Xuan.Prometheus.Narrative
{
    /// <summary>
    /// 变量写入叶子。
    /// 这是「状态类叶子」的范式实现：Settle 与 PlayAsync 执行完全相同的写入，因而天然幂等。
    /// </summary>
    public sealed class SetVariableAction : StoryAction
    {
        private readonly string path;
        private readonly StoryValue literal;
        private readonly string valueExpression;

        /// <summary>创建一个写入常量值的变量赋值节点。</summary>
        public SetVariableAction(string path, StoryValue value)
        {
            if (string.IsNullOrWhiteSpace(path)) throw new ArgumentException("SetVariable requires a variable path.", nameof(path));
            this.path = path;
            literal = value;
            valueExpression = null;
        }

        /// <summary>创建一个写入表达式求值结果的变量赋值节点。</summary>
        public SetVariableAction(string path, string valueExpression)
        {
            if (string.IsNullOrWhiteSpace(path)) throw new ArgumentException("SetVariable requires a variable path.", nameof(path));
            if (string.IsNullOrWhiteSpace(valueExpression)) throw new ArgumentException("SetVariable requires a value expression.", nameof(valueExpression));
            this.path = path;
            this.valueExpression = valueExpression;
            literal = StoryValue.None;
        }

        /// <inheritdoc />
        public override UniTask PlayAsync(StoryContext context, CancellationToken cancellationToken)
        {
            Apply(context);
            return UniTask.CompletedTask;
        }

        /// <inheritdoc />
        public override void Settle(StoryContext context)
        {
            Apply(context);
        }

        /// <summary>执行一次写入；重复调用结果一致。</summary>
        private void Apply(StoryContext context)
        {
            context.Variables.Set(path, valueExpression == null ? literal : context.EvaluateValue(valueExpression));
        }
    }

    /// <summary>固定时长等待叶子；跳过时不消耗任何时间。</summary>
    public sealed class WaitAction : StoryAction
    {
        private readonly float seconds;

        /// <summary>创建一个等待指定秒数的节点。</summary>
        public WaitAction(float seconds)
        {
            if (seconds < 0f) throw new ArgumentOutOfRangeException(nameof(seconds), seconds, "Wait seconds cannot be negative.");
            this.seconds = seconds;
        }

        /// <inheritdoc />
        public override UniTask PlayAsync(StoryContext context, CancellationToken cancellationToken)
        {
            if (seconds <= 0f || context.Mode == StoryPlayMode.Preview) return UniTask.CompletedTask;
            return UniTask.Delay(TimeSpan.FromSeconds(seconds), DelayType.DeltaTime, PlayerLoopTiming.Update, cancellationToken);
        }

        /// <inheritdoc />
        public override void Settle(StoryContext context)
        {
            // 等待本身不产生世界状态，跳过时直接略过。
        }
    }

    /// <summary>条件等待叶子：轮询表达式直到成立。</summary>
    public sealed class WaitForAction : StoryAction
    {
        private readonly string expression;
        private readonly float timeoutSeconds;

        /// <summary>创建一个条件等待节点。</summary>
        /// <param name="expression">等待成立的条件表达式。</param>
        /// <param name="timeoutSeconds">超时秒数；小于等于零表示无限等待。</param>
        public WaitForAction(string expression, float timeoutSeconds = 0f)
        {
            if (string.IsNullOrWhiteSpace(expression)) throw new ArgumentException("WaitFor requires a condition expression.", nameof(expression));
            this.expression = expression;
            this.timeoutSeconds = timeoutSeconds;
        }

        /// <inheritdoc />
        public override async UniTask PlayAsync(StoryContext context, CancellationToken cancellationToken)
        {
            float elapsed = 0f;
            while (!context.Evaluate(expression))
            {
                cancellationToken.ThrowIfCancellationRequested();
                await UniTask.Yield(PlayerLoopTiming.Update, cancellationToken);
                if (timeoutSeconds <= 0f) continue;
                elapsed += Time.deltaTime;
                if (elapsed >= timeoutSeconds) throw new TimeoutException($"Story wait for condition '{expression}' timed out after {timeoutSeconds} seconds.");
            }
        }

        /// <inheritdoc />
        public override void Settle(StoryContext context)
        {
            // 条件等待不产生世界状态；跳过时不再等待外部条件。
        }
    }

    /// <summary>
    /// 通用回调叶子。
    /// 通过分别提供演绎与落终态两个委托，强制作者显式区分「表现」与「状态」，避免破坏 Settle 幂等契约。
    /// </summary>
    public sealed class CallbackAction : StoryAction
    {
        private readonly Func<StoryContext, CancellationToken, UniTask> play;
        private readonly Action<StoryContext> settle;

        /// <summary>创建一个通用回调节点。</summary>
        /// <param name="play">演绎逻辑；不可为空。</param>
        /// <param name="settle">落终态逻辑；传入空表示该节点是纯表现，跳过时不产生任何状态。</param>
        public CallbackAction(Func<StoryContext, CancellationToken, UniTask> play, Action<StoryContext> settle)
        {
            this.play = play ?? throw new ArgumentNullException(nameof(play));
            this.settle = settle;
        }

        /// <inheritdoc />
        public override UniTask PlayAsync(StoryContext context, CancellationToken cancellationToken)
        {
            return play(context, cancellationToken);
        }

        /// <inheritdoc />
        public override void Settle(StoryContext context)
        {
            settle?.Invoke(context);
        }
    }

    /// <summary>提前结束最近一层顺序组合子的叶子。</summary>
    public sealed class BreakAction : StoryAction
    {
        /// <inheritdoc />
        public override UniTask PlayAsync(StoryContext context, CancellationToken cancellationToken)
        {
            context.RequestBreak();
            return UniTask.CompletedTask;
        }

        /// <inheritdoc />
        public override void Settle(StoryContext context)
        {
            // 落终态阶段同样需要中断，否则跳过时会把本不该执行的后续节点一并应用。
            context.RequestBreak();
        }
    }
}
