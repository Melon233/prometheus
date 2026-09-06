using NUnit.Framework;

namespace Xuan.Prometheus.Narrative.Tests
{
    /// <summary>验证剧情条件表达式的解析、优先级、短路、类型语义与编辑期校验。</summary>
    public sealed class StoryExpressionTests
    {
        /// <summary>每个用例都从干净的编译缓存开始，避免用例之间互相影响。</summary>
        [SetUp]
        public void SetUp()
        {
            StoryExpression.ClearCache();
        }

        /// <summary>验证逻辑与优先于逻辑或。</summary>
        [Test]
        public void Evaluate_AndBindsTighterThanOr()
        {
            StoryVariables variables = new StoryVariables();
            Assert.That(StoryExpression.Compile("true || false && false").EvaluateBool(variables), Is.True);
            Assert.That(StoryExpression.Compile("(true || false) && false").EvaluateBool(variables), Is.False);
        }

        /// <summary>验证逻辑与短路，右侧的未定义变量不会被求值。</summary>
        [Test]
        public void Evaluate_AndShortCircuitsBeforeUndefinedVariable()
        {
            StoryVariables variables = new StoryVariables();
            Assert.That(StoryExpression.Compile("false && var.missing").EvaluateBool(variables), Is.False);
            Assert.Throws<StoryExpressionException>(() => StoryExpression.Compile("true && var.missing").EvaluateBool(variables));
        }

        /// <summary>验证未写入的旗标按 false 参与判断，而未定义的变量直接报错。</summary>
        [Test]
        public void Resolve_UnknownFlagIsFalseAndUnknownVariableThrows()
        {
            StoryVariables variables = new StoryVariables();
            Assert.That(StoryExpression.Compile("!flag.never_set").EvaluateBool(variables), Is.True);
            Assert.Throws<StoryExpressionException>(() => StoryExpression.Compile("var.never_set").EvaluateBool(variables));
        }

        /// <summary>验证数值比较与相等比较。</summary>
        [Test]
        public void Evaluate_ComparesNumbers()
        {
            StoryVariables variables = new StoryVariables();
            variables.Set("var.trust", 3);
            Assert.That(StoryExpression.Compile("var.trust >= 3").EvaluateBool(variables), Is.True);
            Assert.That(StoryExpression.Compile("var.trust > 3").EvaluateBool(variables), Is.False);
            Assert.That(StoryExpression.Compile("var.trust == 3").EvaluateBool(variables), Is.True);
            Assert.That(StoryExpression.Compile("var.trust != 3").EvaluateBool(variables), Is.False);
        }

        /// <summary>验证字符串按序数比较，使枚举状态可以直接与字符串常量比对。</summary>
        [Test]
        public void Evaluate_ComparesTextOrdinally()
        {
            StoryVariables variables = new StoryVariables();
            variables.Set("var.state", "Done");
            Assert.That(StoryExpression.Compile("var.state == \"Done\"").EvaluateBool(variables), Is.True);
            Assert.That(StoryExpression.Compile("var.state == \"done\"").EvaluateBool(variables), Is.False);
        }

        /// <summary>验证文本参与大小比较时立即报错，使配置错误尽早暴露。</summary>
        [Test]
        public void Evaluate_OrderComparisonRejectsText()
        {
            StoryVariables variables = new StoryVariables();
            variables.Set("var.state", "Done");
            Assert.Throws<StoryExpressionException>(() => StoryExpression.Compile("var.state > 1").EvaluateBool(variables));
        }

        /// <summary>验证组合条件在真实数据下的求值结果。</summary>
        [Test]
        public void Evaluate_CombinedCondition()
        {
            StoryVariables variables = new StoryVariables();
            variables.SetFlag("seen_intro", true);
            variables.Set("var.trust", 2);
            Assert.That(StoryExpression.Compile("flag.seen_intro && var.trust >= 2 && !flag.left_early").EvaluateBool(variables), Is.True);
            variables.SetFlag("left_early", true);
            Assert.That(StoryExpression.Compile("flag.seen_intro && var.trust >= 2 && !flag.left_early").EvaluateBool(variables), Is.False);
        }

        /// <summary>验证语法错误在编译阶段抛出且带有位置信息。</summary>
        [Test]
        public void Compile_RejectsMalformedInput()
        {
            Assert.Throws<StoryExpressionException>(() => StoryExpression.Compile("var.a &&"));
            Assert.Throws<StoryExpressionException>(() => StoryExpression.Compile("(var.a"));
            Assert.Throws<StoryExpressionException>(() => StoryExpression.Compile("var.a = 1"));
            Assert.Throws<StoryExpressionException>(() => StoryExpression.Compile("var.a # 1"));
            Assert.Throws<StoryExpressionException>(() => StoryExpression.Compile(" "));
        }

        /// <summary>验证编辑期校验能识别未被承认的标识符命名空间。</summary>
        [Test]
        public void Validate_RejectsUnknownNamespace()
        {
            StoryVariables variables = new StoryVariables();
            Assert.That(StoryExpression.Validate("flag.a && var.b == 1", variables, out string knownError), Is.True, knownError);
            Assert.That(StoryExpression.Validate("quest.mondstadt == 1", variables, out string unknownError), Is.False);
            Assert.That(unknownError, Does.Contain("quest"));
        }

        /// <summary>验证外部只读投影可以扩展被承认的命名空间。</summary>
        [Test]
        public void Projection_ExtendsResolvableNamespaces()
        {
            StoryVariables variables = new StoryVariables();
            variables.AddProjection(new FakeQuestProjection());
            Assert.That(StoryExpression.Validate("quest.demo == \"Done\"", variables, out string error), Is.True, error);
            Assert.That(StoryExpression.Compile("quest.demo == \"Done\"").EvaluateBool(variables), Is.True);
        }

        /// <summary>验证同一段表达式文本复用编译缓存。</summary>
        [Test]
        public void Compile_ReusesCachedInstance()
        {
            StoryExpression first = StoryExpression.Compile("flag.a || flag.b");
            StoryExpression second = StoryExpression.Compile("flag.a || flag.b");
            Assert.That(ReferenceEquals(first, second), Is.True);
        }

        /// <summary>模拟任务系统的只读投影。</summary>
        private sealed class FakeQuestProjection : IStoryVariableResolver
        {
            /// <inheritdoc />
            public bool TryResolve(string path, out StoryValue value)
            {
                if (path == "quest.demo")
                {
                    value = new StoryValue("Done");
                    return true;
                }
                value = StoryValue.None;
                return false;
            }

            /// <inheritdoc />
            public bool IsKnownRoot(string root)
            {
                return root == "quest";
            }
        }
    }
}
