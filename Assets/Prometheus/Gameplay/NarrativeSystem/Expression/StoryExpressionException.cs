using System;

namespace Xuan.Prometheus.Narrative
{
    /// <summary>表示剧情表达式在编译或求值阶段发生的错误；编辑期校验与运行期求值共用该异常。</summary>
    public sealed class StoryExpressionException : Exception
    {
        /// <summary>创建一条不带源表达式上下文的表达式错误。</summary>
        public StoryExpressionException(string message) : base(message)
        {
        }

        /// <summary>创建一条带源表达式与位置信息的表达式错误。</summary>
        /// <param name="message">错误描述。</param>
        /// <param name="source">出错的原始表达式文本。</param>
        /// <param name="position">出错位置在源文本中的字符下标；未知时传入负值。</param>
        public StoryExpressionException(string message, string source, int position)
            : base(position >= 0 ? $"{message} (expression: \"{source}\", position: {position})" : $"{message} (expression: \"{source}\")")
        {
            ExpressionText = source;
            Position = position;
        }

        /// <summary>获取出错的原始表达式文本。</summary>
        public string ExpressionText { get; }

        /// <summary>获取出错位置的字符下标；未知时为负值。</summary>
        public int Position { get; } = -1;
    }
}
