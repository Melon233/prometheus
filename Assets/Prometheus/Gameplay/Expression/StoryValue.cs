using System;
using System.Globalization;

namespace Xuan.Prometheus.Expression
{
    /// <summary>定义剧情表达式支持的值类别。</summary>
    public enum StoryValueKind
    {
        /// <summary>空值；读取未定义变量时的中性结果。</summary>
        None,

        /// <summary>布尔值。</summary>
        Bool,

        /// <summary>数值，统一以双精度保存。</summary>
        Number,

        /// <summary>字符串。</summary>
        Text
    }

    /// <summary>剧情表达式的统一值类型；不依赖任何 Unity 类型，可在纯 C# 环境求值。</summary>
    public readonly struct StoryValue : IEquatable<StoryValue>
    {
        /// <summary>获取空值单例。</summary>
        public static readonly StoryValue None = default;

        /// <summary>获取布尔真值。</summary>
        public static readonly StoryValue True = new StoryValue(true);

        /// <summary>获取布尔假值。</summary>
        public static readonly StoryValue False = new StoryValue(false);

        private readonly double number;
        private readonly string text;

        /// <summary>创建一个布尔值。</summary>
        public StoryValue(bool value)
        {
            Kind = StoryValueKind.Bool;
            number = value ? 1d : 0d;
            text = null;
        }

        /// <summary>创建一个数值。</summary>
        public StoryValue(double value)
        {
            Kind = StoryValueKind.Number;
            number = value;
            text = null;
        }

        /// <summary>创建一个字符串值；传入空引用时视为空字符串。</summary>
        public StoryValue(string value)
        {
            Kind = StoryValueKind.Text;
            number = 0d;
            text = value ?? string.Empty;
        }

        /// <summary>获取当前值的类别。</summary>
        public StoryValueKind Kind { get; }

        /// <summary>获取当前值是否为空值。</summary>
        public bool IsNone => Kind == StoryValueKind.None;

        /// <summary>按剧情条件的宽松语义把当前值折叠为布尔：空值与零为假，空字符串为假，其余为真。</summary>
        public bool AsBool()
        {
            switch (Kind)
            {
                case StoryValueKind.Bool:
                    return number != 0d;
                case StoryValueKind.Number:
                    return number != 0d;
                case StoryValueKind.Text:
                    return !string.IsNullOrEmpty(text);
                default:
                    return false;
            }
        }

        /// <summary>读取数值；布尔按 1 与 0 折算，其余类别抛出类型错误。</summary>
        public double AsNumber()
        {
            if (Kind == StoryValueKind.Number || Kind == StoryValueKind.Bool) return number;
            throw new StoryExpressionException($"Story value of kind '{Kind}' cannot be used as a number.");
        }

        /// <summary>读取字符串；其余类别返回其规范化文本形式。</summary>
        public string AsText()
        {
            return ToString();
        }

        /// <inheritdoc />
        public bool Equals(StoryValue other)
        {
            if (Kind != other.Kind) return false;
            if (Kind == StoryValueKind.Text) return string.Equals(text, other.text, StringComparison.Ordinal);
            return number.Equals(other.number);
        }

        /// <inheritdoc />
        public override bool Equals(object obj)
        {
            return obj is StoryValue other && Equals(other);
        }

        /// <inheritdoc />
        public override int GetHashCode()
        {
            unchecked
            {
                int hash = (int)Kind * 397;
                return Kind == StoryValueKind.Text ? hash ^ (text != null ? StringComparer.Ordinal.GetHashCode(text) : 0) : hash ^ number.GetHashCode();
            }
        }

        /// <summary>返回值的规范化文本形式，供日志、比较与文本插值使用。</summary>
        public override string ToString()
        {
            switch (Kind)
            {
                case StoryValueKind.Bool:
                    return number != 0d ? "true" : "false";
                case StoryValueKind.Number:
                    return number.ToString("R", CultureInfo.InvariantCulture);
                case StoryValueKind.Text:
                    return text ?? string.Empty;
                default:
                    return string.Empty;
            }
        }

        /// <summary>
        /// 按类别把文本解析为剧情值。
        /// 存档反序列化与图资产的常量字段共用该入口，保证两处的解析规则一致。
        /// </summary>
        /// <param name="kind">目标值类别。</param>
        /// <param name="text">值的文本形式。</param>
        public static StoryValue Parse(StoryValueKind kind, string text)
        {
            switch (kind)
            {
                case StoryValueKind.Bool:
                    return new StoryValue(string.Equals(text, "true", StringComparison.OrdinalIgnoreCase));
                case StoryValueKind.Number:
                    return new StoryValue(double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out double parsed) ? parsed : 0d);
                case StoryValueKind.Text:
                    return new StoryValue(text ?? string.Empty);
                default:
                    return None;
            }
        }

        /// <summary>把布尔隐式转换为剧情值。</summary>
        public static implicit operator StoryValue(bool value)
        {
            return new StoryValue(value);
        }

        /// <summary>把数值隐式转换为剧情值。</summary>
        public static implicit operator StoryValue(double value)
        {
            return new StoryValue(value);
        }

        /// <summary>把整数隐式转换为剧情值。</summary>
        public static implicit operator StoryValue(int value)
        {
            return new StoryValue((double)value);
        }

        /// <summary>把字符串隐式转换为剧情值。</summary>
        public static implicit operator StoryValue(string value)
        {
            return new StoryValue(value);
        }
    }
}
