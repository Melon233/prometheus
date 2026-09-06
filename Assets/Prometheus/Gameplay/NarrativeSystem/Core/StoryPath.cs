using System;

namespace Xuan.Prometheus.Narrative
{
    /// <summary>
    /// 剧情树中一个节点的稳定路径标识。
    /// 路径由树结构静态生成，是跳过、断点续演和编辑器任意节拍预览三项功能的共同寻址基础。
    /// </summary>
    public readonly struct StoryPath : IEquatable<StoryPath>
    {
        /// <summary>路径分段之间的分隔符。</summary>
        public const char Separator = '/';

        /// <summary>获取空路径；表示节点尚未绑定到任何剧情树。</summary>
        public static readonly StoryPath None = default;

        /// <summary>创建一个指定文本的路径。</summary>
        public StoryPath(string value)
        {
            Value = string.IsNullOrWhiteSpace(value) ? null : value;
        }

        /// <summary>获取路径文本；空路径为 null。</summary>
        public string Value { get; }

        /// <summary>获取当前路径是否为空。</summary>
        public bool IsEmpty => string.IsNullOrEmpty(Value);

        /// <summary>创建一条根路径。</summary>
        /// <param name="rootId">剧情树的稳定标识，例如 <c>ch1_s3</c>。</param>
        public static StoryPath Root(string rootId)
        {
            if (string.IsNullOrWhiteSpace(rootId)) throw new ArgumentException("Story root id cannot be empty.", nameof(rootId));
            return new StoryPath(rootId);
        }

        /// <summary>在当前路径下追加一个子节点分段。</summary>
        /// <param name="segment">子节点分段，通常是显式标识或子节点序号。</param>
        public StoryPath Append(string segment)
        {
            if (string.IsNullOrWhiteSpace(segment)) throw new ArgumentException("Story path segment cannot be empty.", nameof(segment));
            return new StoryPath(IsEmpty ? segment : string.Concat(Value, Separator.ToString(), segment));
        }

        /// <summary>在当前路径下追加一个序号分段。</summary>
        public StoryPath Append(int index)
        {
            return Append(index.ToString());
        }

        /// <summary>判断当前路径是否是指定路径本身或其祖先。</summary>
        public bool IsAncestorOfOrSame(StoryPath other)
        {
            if (IsEmpty || other.IsEmpty) return false;
            if (string.Equals(Value, other.Value, StringComparison.Ordinal)) return true;
            return other.Value.Length > Value.Length && other.Value[Value.Length] == Separator && other.Value.StartsWith(Value, StringComparison.Ordinal);
        }

        /// <inheritdoc />
        public bool Equals(StoryPath other)
        {
            return string.Equals(Value, other.Value, StringComparison.Ordinal);
        }

        /// <inheritdoc />
        public override bool Equals(object obj)
        {
            return obj is StoryPath other && Equals(other);
        }

        /// <inheritdoc />
        public override int GetHashCode()
        {
            return Value != null ? StringComparer.Ordinal.GetHashCode(Value) : 0;
        }

        /// <inheritdoc />
        public override string ToString()
        {
            return Value ?? string.Empty;
        }
    }
}
