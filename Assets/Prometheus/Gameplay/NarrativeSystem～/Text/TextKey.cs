using System;

namespace Xuan.Prometheus.Narrative
{
    /// <summary>
    /// 表示一条本地化文本的稳定键。
    /// 当前直接保存可读字符串；若将来改用哈希，改动范围限于本结构内部，调用方代码不受影响。
    /// </summary>
    [Serializable]
    public readonly struct TextKey : IEquatable<TextKey>
    {
        /// <summary>获取空键；空键解析结果为空字符串。</summary>
        public static readonly TextKey Empty = default;

        /// <summary>创建一个文本键。</summary>
        /// <param name="value">在文本表中唯一的键字符串。</param>
        public TextKey(string value)
        {
            Value = string.IsNullOrWhiteSpace(value) ? null : value;
        }

        /// <summary>获取键字符串；空键为 null。</summary>
        public string Value { get; }

        /// <summary>获取当前键是否为空键。</summary>
        public bool IsEmpty => string.IsNullOrEmpty(Value);

        /// <inheritdoc />
        public bool Equals(TextKey other)
        {
            return string.Equals(Value, other.Value, StringComparison.Ordinal);
        }

        /// <inheritdoc />
        public override bool Equals(object obj)
        {
            return obj is TextKey other && Equals(other);
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

        /// <summary>把字符串隐式转换为文本键，使 DSL 书写保持简洁。</summary>
        public static implicit operator TextKey(string value)
        {
            return new TextKey(value);
        }
    }
}
