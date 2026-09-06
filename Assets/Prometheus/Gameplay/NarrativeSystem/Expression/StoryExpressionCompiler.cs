using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace Xuan.Prometheus.Narrative
{
    /// <summary>剧情表达式求值时携带的解析环境。</summary>
    internal readonly struct StoryEvalScope
    {
        /// <summary>创建一个求值环境。</summary>
        internal StoryEvalScope(IStoryVariableResolver variables, IStoryFunctionResolver functions, string source)
        {
            Variables = variables;
            Functions = functions;
            Source = source;
        }

        /// <summary>获取标识符解析器。</summary>
        internal IStoryVariableResolver Variables { get; }

        /// <summary>获取函数解析器；可为空。</summary>
        internal IStoryFunctionResolver Functions { get; }

        /// <summary>获取原始表达式文本，用于组装错误信息。</summary>
        internal string Source { get; }
    }

    /// <summary>剧情表达式语法树节点基类。</summary>
    internal abstract class StoryExpressionNode
    {
        /// <summary>在给定环境下求值。</summary>
        internal abstract StoryValue Evaluate(in StoryEvalScope scope);

        /// <summary>递归收集该子树引用的全部标识符与函数名。</summary>
        internal virtual void CollectIdentifiers(ICollection<string> identifiers, ICollection<string> functions)
        {
        }
    }

    /// <summary>常量节点。</summary>
    internal sealed class StoryLiteralNode : StoryExpressionNode
    {
        private readonly StoryValue value;

        /// <summary>创建一个常量节点。</summary>
        internal StoryLiteralNode(StoryValue value)
        {
            this.value = value;
        }

        /// <inheritdoc />
        internal override StoryValue Evaluate(in StoryEvalScope scope)
        {
            return value;
        }
    }

    /// <summary>标识符节点，按点分路径从变量解析器取值。</summary>
    internal sealed class StoryIdentifierNode : StoryExpressionNode
    {
        private readonly string path;

        /// <summary>创建一个标识符节点。</summary>
        internal StoryIdentifierNode(string path)
        {
            this.path = path;
        }

        /// <summary>获取标识符的完整点分路径。</summary>
        internal string Path => path;

        /// <inheritdoc />
        internal override StoryValue Evaluate(in StoryEvalScope scope)
        {
            if (scope.Variables == null) throw new StoryExpressionException($"Story expression references '{path}' but no variable resolver was provided.", scope.Source, -1);
            if (!scope.Variables.TryResolve(path, out StoryValue value)) throw new StoryExpressionException($"Story expression references undefined identifier '{path}'.", scope.Source, -1);
            return value;
        }

        /// <inheritdoc />
        internal override void CollectIdentifiers(ICollection<string> identifiers, ICollection<string> functions)
        {
            identifiers.Add(path);
        }
    }

    /// <summary>函数调用节点。</summary>
    internal sealed class StoryCallNode : StoryExpressionNode
    {
        private readonly string name;
        private readonly StoryExpressionNode[] arguments;

        /// <summary>创建一个函数调用节点。</summary>
        internal StoryCallNode(string name, StoryExpressionNode[] arguments)
        {
            this.name = name;
            this.arguments = arguments;
        }

        /// <inheritdoc />
        internal override StoryValue Evaluate(in StoryEvalScope scope)
        {
            if (scope.Functions == null) throw new StoryExpressionException($"Story expression calls '{name}' but no function resolver was provided.", scope.Source, -1);
            StoryValue[] evaluated = new StoryValue[arguments.Length];
            for (int index = 0; index < arguments.Length; index++) evaluated[index] = arguments[index].Evaluate(scope);
            if (!scope.Functions.TryInvoke(name, evaluated, out StoryValue value)) throw new StoryExpressionException($"Story expression calls undefined function '{name}'.", scope.Source, -1);
            return value;
        }

        /// <inheritdoc />
        internal override void CollectIdentifiers(ICollection<string> identifiers, ICollection<string> functions)
        {
            functions.Add(name);
            for (int index = 0; index < arguments.Length; index++) arguments[index].CollectIdentifiers(identifiers, functions);
        }
    }

    /// <summary>定义一元运算符。</summary>
    internal enum StoryUnaryOperator
    {
        /// <summary>逻辑非。</summary>
        Not,

        /// <summary>数值取负。</summary>
        Negate
    }

    /// <summary>一元运算节点。</summary>
    internal sealed class StoryUnaryNode : StoryExpressionNode
    {
        private readonly StoryUnaryOperator op;
        private readonly StoryExpressionNode operand;

        /// <summary>创建一个一元运算节点。</summary>
        internal StoryUnaryNode(StoryUnaryOperator op, StoryExpressionNode operand)
        {
            this.op = op;
            this.operand = operand;
        }

        /// <inheritdoc />
        internal override StoryValue Evaluate(in StoryEvalScope scope)
        {
            StoryValue value = operand.Evaluate(scope);
            return op == StoryUnaryOperator.Not ? new StoryValue(!value.AsBool()) : new StoryValue(-value.AsNumber());
        }

        /// <inheritdoc />
        internal override void CollectIdentifiers(ICollection<string> identifiers, ICollection<string> functions)
        {
            operand.CollectIdentifiers(identifiers, functions);
        }
    }

    /// <summary>定义二元运算符。</summary>
    internal enum StoryBinaryOperator
    {
        /// <summary>逻辑或，短路求值。</summary>
        Or,

        /// <summary>逻辑与，短路求值。</summary>
        And,

        /// <summary>相等。</summary>
        Equal,

        /// <summary>不等。</summary>
        NotEqual,

        /// <summary>小于。</summary>
        Less,

        /// <summary>小于等于。</summary>
        LessEqual,

        /// <summary>大于。</summary>
        Greater,

        /// <summary>大于等于。</summary>
        GreaterEqual
    }

    /// <summary>二元运算节点。</summary>
    internal sealed class StoryBinaryNode : StoryExpressionNode
    {
        private readonly StoryBinaryOperator op;
        private readonly StoryExpressionNode left;
        private readonly StoryExpressionNode right;

        /// <summary>创建一个二元运算节点。</summary>
        internal StoryBinaryNode(StoryBinaryOperator op, StoryExpressionNode left, StoryExpressionNode right)
        {
            this.op = op;
            this.left = left;
            this.right = right;
        }

        /// <inheritdoc />
        internal override StoryValue Evaluate(in StoryEvalScope scope)
        {
            if (op == StoryBinaryOperator.And) return new StoryValue(left.Evaluate(scope).AsBool() && right.Evaluate(scope).AsBool());
            if (op == StoryBinaryOperator.Or) return new StoryValue(left.Evaluate(scope).AsBool() || right.Evaluate(scope).AsBool());
            StoryValue leftValue = left.Evaluate(scope);
            StoryValue rightValue = right.Evaluate(scope);
            switch (op)
            {
                case StoryBinaryOperator.Equal:
                    return new StoryValue(AreEqual(leftValue, rightValue));
                case StoryBinaryOperator.NotEqual:
                    return new StoryValue(!AreEqual(leftValue, rightValue));
                case StoryBinaryOperator.Less:
                    return new StoryValue(Compare(leftValue, rightValue, scope) < 0);
                case StoryBinaryOperator.LessEqual:
                    return new StoryValue(Compare(leftValue, rightValue, scope) <= 0);
                case StoryBinaryOperator.Greater:
                    return new StoryValue(Compare(leftValue, rightValue, scope) > 0);
                default:
                    return new StoryValue(Compare(leftValue, rightValue, scope) >= 0);
            }
        }

        /// <inheritdoc />
        internal override void CollectIdentifiers(ICollection<string> identifiers, ICollection<string> functions)
        {
            left.CollectIdentifiers(identifiers, functions);
            right.CollectIdentifiers(identifiers, functions);
        }

        /// <summary>数值与布尔按数值比较，其余按序数文本比较，使枚举状态可以直接与字符串常量比对。</summary>
        private static bool AreEqual(StoryValue left, StoryValue right)
        {
            if (IsNumeric(left) && IsNumeric(right)) return Math.Abs(left.AsNumber() - right.AsNumber()) < double.Epsilon;
            return string.Equals(left.AsText(), right.AsText(), StringComparison.Ordinal);
        }

        /// <summary>大小比较只接受数值与布尔，文本参与比较时立即报错以暴露配置问题。</summary>
        private static int Compare(StoryValue left, StoryValue right, in StoryEvalScope scope)
        {
            if (!IsNumeric(left) || !IsNumeric(right)) throw new StoryExpressionException($"Story expression cannot order-compare '{left.Kind}' with '{right.Kind}'.", scope.Source, -1);
            return left.AsNumber().CompareTo(right.AsNumber());
        }

        /// <summary>判断值是否可以参与数值运算。</summary>
        private static bool IsNumeric(StoryValue value)
        {
            return value.Kind == StoryValueKind.Number || value.Kind == StoryValueKind.Bool;
        }
    }

    /// <summary>定义词法单元类别。</summary>
    internal enum StoryTokenKind
    {
        End,
        Identifier,
        Number,
        Text,
        LeftParen,
        RightParen,
        Comma,
        Not,
        And,
        Or,
        Equal,
        NotEqual,
        Less,
        LessEqual,
        Greater,
        GreaterEqual,
        Minus
    }

    /// <summary>一个词法单元。</summary>
    internal readonly struct StoryToken
    {
        /// <summary>创建一个词法单元。</summary>
        internal StoryToken(StoryTokenKind kind, string text, double number, int position)
        {
            Kind = kind;
            Text = text;
            Number = number;
            Position = position;
        }

        /// <summary>获取词法单元类别。</summary>
        internal StoryTokenKind Kind { get; }

        /// <summary>获取标识符或字符串字面量的文本。</summary>
        internal string Text { get; }

        /// <summary>获取数值字面量。</summary>
        internal double Number { get; }

        /// <summary>获取该单元在源文本中的起始下标。</summary>
        internal int Position { get; }
    }

    /// <summary>把剧情表达式文本编译为语法树；词法与语法分析不依赖任何 Unity 类型。</summary>
    internal static class StoryExpressionCompiler
    {
        /// <summary>编译一段表达式文本并返回根节点。</summary>
        internal static StoryExpressionNode Compile(string source)
        {
            if (string.IsNullOrWhiteSpace(source)) throw new StoryExpressionException("Story expression cannot be empty.", source, -1);
            List<StoryToken> tokens = Tokenize(source);
            int cursor = 0;
            StoryExpressionNode root = ParseOr(source, tokens, ref cursor);
            if (tokens[cursor].Kind != StoryTokenKind.End) throw new StoryExpressionException("Story expression contains trailing content.", source, tokens[cursor].Position);
            return root;
        }

        /// <summary>把源文本切分为词法单元序列，末尾追加一个 End 单元。</summary>
        private static List<StoryToken> Tokenize(string source)
        {
            List<StoryToken> tokens = new List<StoryToken>();
            int index = 0;
            while (index < source.Length)
            {
                char current = source[index];
                if (char.IsWhiteSpace(current))
                {
                    index++;
                    continue;
                }
                int start = index;
                if (char.IsLetter(current) || current == '_')
                {
                    while (index < source.Length && (char.IsLetterOrDigit(source[index]) || source[index] == '_' || source[index] == '.')) index++;
                    tokens.Add(new StoryToken(StoryTokenKind.Identifier, source.Substring(start, index - start), 0d, start));
                    continue;
                }
                if (char.IsDigit(current))
                {
                    while (index < source.Length && (char.IsDigit(source[index]) || source[index] == '.')) index++;
                    string literal = source.Substring(start, index - start);
                    if (!double.TryParse(literal, NumberStyles.Float, CultureInfo.InvariantCulture, out double parsed)) throw new StoryExpressionException($"Story expression contains an invalid number '{literal}'.", source, start);
                    tokens.Add(new StoryToken(StoryTokenKind.Number, literal, parsed, start));
                    continue;
                }
                if (current == '"' || current == '\'')
                {
                    char quote = current;
                    index++;
                    StringBuilder builder = new StringBuilder();
                    while (index < source.Length && source[index] != quote)
                    {
                        if (source[index] == '\\' && index + 1 < source.Length) index++;
                        builder.Append(source[index]);
                        index++;
                    }
                    if (index >= source.Length) throw new StoryExpressionException("Story expression contains an unterminated string literal.", source, start);
                    index++;
                    tokens.Add(new StoryToken(StoryTokenKind.Text, builder.ToString(), 0d, start));
                    continue;
                }
                switch (current)
                {
                    case '(':
                        tokens.Add(new StoryToken(StoryTokenKind.LeftParen, "(", 0d, start));
                        index++;
                        continue;
                    case ')':
                        tokens.Add(new StoryToken(StoryTokenKind.RightParen, ")", 0d, start));
                        index++;
                        continue;
                    case ',':
                        tokens.Add(new StoryToken(StoryTokenKind.Comma, ",", 0d, start));
                        index++;
                        continue;
                    case '-':
                        tokens.Add(new StoryToken(StoryTokenKind.Minus, "-", 0d, start));
                        index++;
                        continue;
                    case '!':
                        index++;
                        if (index < source.Length && source[index] == '=')
                        {
                            index++;
                            tokens.Add(new StoryToken(StoryTokenKind.NotEqual, "!=", 0d, start));
                        }
                        else tokens.Add(new StoryToken(StoryTokenKind.Not, "!", 0d, start));
                        continue;
                    case '=':
                        index++;
                        if (index >= source.Length || source[index] != '=') throw new StoryExpressionException("Story expression uses '=' where '==' is required.", source, start);
                        index++;
                        tokens.Add(new StoryToken(StoryTokenKind.Equal, "==", 0d, start));
                        continue;
                    case '<':
                        index++;
                        if (index < source.Length && source[index] == '=')
                        {
                            index++;
                            tokens.Add(new StoryToken(StoryTokenKind.LessEqual, "<=", 0d, start));
                        }
                        else tokens.Add(new StoryToken(StoryTokenKind.Less, "<", 0d, start));
                        continue;
                    case '>':
                        index++;
                        if (index < source.Length && source[index] == '=')
                        {
                            index++;
                            tokens.Add(new StoryToken(StoryTokenKind.GreaterEqual, ">=", 0d, start));
                        }
                        else tokens.Add(new StoryToken(StoryTokenKind.Greater, ">", 0d, start));
                        continue;
                    case '&':
                        index++;
                        if (index >= source.Length || source[index] != '&') throw new StoryExpressionException("Story expression uses '&' where '&&' is required.", source, start);
                        index++;
                        tokens.Add(new StoryToken(StoryTokenKind.And, "&&", 0d, start));
                        continue;
                    case '|':
                        index++;
                        if (index >= source.Length || source[index] != '|') throw new StoryExpressionException("Story expression uses '|' where '||' is required.", source, start);
                        index++;
                        tokens.Add(new StoryToken(StoryTokenKind.Or, "||", 0d, start));
                        continue;
                    default:
                        throw new StoryExpressionException($"Story expression contains an unexpected character '{current}'.", source, start);
                }
            }
            tokens.Add(new StoryToken(StoryTokenKind.End, string.Empty, 0d, source.Length));
            return tokens;
        }

        /// <summary>解析逻辑或层级。</summary>
        private static StoryExpressionNode ParseOr(string source, List<StoryToken> tokens, ref int cursor)
        {
            StoryExpressionNode node = ParseAnd(source, tokens, ref cursor);
            while (tokens[cursor].Kind == StoryTokenKind.Or)
            {
                cursor++;
                node = new StoryBinaryNode(StoryBinaryOperator.Or, node, ParseAnd(source, tokens, ref cursor));
            }
            return node;
        }

        /// <summary>解析逻辑与层级。</summary>
        private static StoryExpressionNode ParseAnd(string source, List<StoryToken> tokens, ref int cursor)
        {
            StoryExpressionNode node = ParseEquality(source, tokens, ref cursor);
            while (tokens[cursor].Kind == StoryTokenKind.And)
            {
                cursor++;
                node = new StoryBinaryNode(StoryBinaryOperator.And, node, ParseEquality(source, tokens, ref cursor));
            }
            return node;
        }

        /// <summary>解析相等比较层级。</summary>
        private static StoryExpressionNode ParseEquality(string source, List<StoryToken> tokens, ref int cursor)
        {
            StoryExpressionNode node = ParseComparison(source, tokens, ref cursor);
            while (tokens[cursor].Kind == StoryTokenKind.Equal || tokens[cursor].Kind == StoryTokenKind.NotEqual)
            {
                StoryBinaryOperator op = tokens[cursor].Kind == StoryTokenKind.Equal ? StoryBinaryOperator.Equal : StoryBinaryOperator.NotEqual;
                cursor++;
                node = new StoryBinaryNode(op, node, ParseComparison(source, tokens, ref cursor));
            }
            return node;
        }

        /// <summary>解析大小比较层级。</summary>
        private static StoryExpressionNode ParseComparison(string source, List<StoryToken> tokens, ref int cursor)
        {
            StoryExpressionNode node = ParseUnary(source, tokens, ref cursor);
            while (true)
            {
                StoryTokenKind kind = tokens[cursor].Kind;
                StoryBinaryOperator op;
                if (kind == StoryTokenKind.Less) op = StoryBinaryOperator.Less;
                else if (kind == StoryTokenKind.LessEqual) op = StoryBinaryOperator.LessEqual;
                else if (kind == StoryTokenKind.Greater) op = StoryBinaryOperator.Greater;
                else if (kind == StoryTokenKind.GreaterEqual) op = StoryBinaryOperator.GreaterEqual;
                else return node;
                cursor++;
                node = new StoryBinaryNode(op, node, ParseUnary(source, tokens, ref cursor));
            }
        }

        /// <summary>解析一元运算层级。</summary>
        private static StoryExpressionNode ParseUnary(string source, List<StoryToken> tokens, ref int cursor)
        {
            if (tokens[cursor].Kind == StoryTokenKind.Not)
            {
                cursor++;
                return new StoryUnaryNode(StoryUnaryOperator.Not, ParseUnary(source, tokens, ref cursor));
            }
            if (tokens[cursor].Kind == StoryTokenKind.Minus)
            {
                cursor++;
                return new StoryUnaryNode(StoryUnaryOperator.Negate, ParseUnary(source, tokens, ref cursor));
            }
            return ParsePrimary(source, tokens, ref cursor);
        }

        /// <summary>解析字面量、标识符、函数调用与括号表达式。</summary>
        private static StoryExpressionNode ParsePrimary(string source, List<StoryToken> tokens, ref int cursor)
        {
            StoryToken token = tokens[cursor];
            switch (token.Kind)
            {
                case StoryTokenKind.Number:
                    cursor++;
                    return new StoryLiteralNode(new StoryValue(token.Number));
                case StoryTokenKind.Text:
                    cursor++;
                    return new StoryLiteralNode(new StoryValue(token.Text));
                case StoryTokenKind.LeftParen:
                {
                    cursor++;
                    StoryExpressionNode inner = ParseOr(source, tokens, ref cursor);
                    if (tokens[cursor].Kind != StoryTokenKind.RightParen) throw new StoryExpressionException("Story expression is missing a closing parenthesis.", source, tokens[cursor].Position);
                    cursor++;
                    return inner;
                }
                case StoryTokenKind.Identifier:
                {
                    cursor++;
                    if (string.Equals(token.Text, "true", StringComparison.Ordinal)) return new StoryLiteralNode(StoryValue.True);
                    if (string.Equals(token.Text, "false", StringComparison.Ordinal)) return new StoryLiteralNode(StoryValue.False);
                    if (tokens[cursor].Kind != StoryTokenKind.LeftParen) return new StoryIdentifierNode(token.Text);
                    cursor++;
                    List<StoryExpressionNode> arguments = new List<StoryExpressionNode>();
                    if (tokens[cursor].Kind != StoryTokenKind.RightParen)
                    {
                        while (true)
                        {
                            arguments.Add(ParseOr(source, tokens, ref cursor));
                            if (tokens[cursor].Kind != StoryTokenKind.Comma) break;
                            cursor++;
                        }
                    }
                    if (tokens[cursor].Kind != StoryTokenKind.RightParen) throw new StoryExpressionException($"Story expression call to '{token.Text}' is missing a closing parenthesis.", source, tokens[cursor].Position);
                    cursor++;
                    return new StoryCallNode(token.Text, arguments.ToArray());
                }
                default:
                    throw new StoryExpressionException("Story expression is missing an operand.", source, token.Position);
            }
        }
    }
}
