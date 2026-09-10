using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEngine;
using Xuan.Prometheus.Bootstrap;
using Xuan.Prometheus.Effects;

namespace Xuan.Prometheus.Tests.Architecture
{
    /// <summary>
    /// 把 ArchSpec 中无法由编译器表达的硬约束固化为可执行断言。
    /// 分层依赖本身已经由四个 asmdef 强制，这里只负责编译器管不到的部分：
    /// 实现类型的可见性、唯一性，以及若干源码级禁令。
    /// 未被执行的规范只是注释，因此这些断言与 ArchSpec 条目一一对应。
    /// </summary>
    public sealed class ArchitectureConstraintTests
    {
        /// <summary>框架层装配名。</summary>
        private const string FrameworkAssembly = "Prometheus.Framework";

        /// <summary>玩法层装配名。</summary>
        private const string GameplayAssembly = "Prometheus.Gameplay";

        /// <summary>界面层装配名。</summary>
        private const string UIAssembly = "Prometheus.UI";

        /// <summary>唯一组合根装配名；只有它允许认识全部具体实现。</summary>
        private const string CompositionAssembly = "Runtime";

        /// <summary>本规范覆盖的全部运行时装配。</summary>
        private static readonly string[] RuntimeAssemblies = { FrameworkAssembly, GameplayAssembly, UIAssembly, CompositionAssembly };

        /// <summary>ArchSpec 覆盖的源码根目录；独立渲染插件和第三方代码不在范围内。</summary>
        private static readonly string[] SourceRoots = { "Framework", "Gameplay", "UI", "Bootstrap" };

        /// <summary>
        /// ARCH-LAYER-001：下层装配不得引用上层装配。
        /// 该约束平时由 asmdef 强制；这里额外断言一次，使有人放宽 asmdef 时测试立即失败。
        /// </summary>
        [TestCase(FrameworkAssembly, new[] { GameplayAssembly, UIAssembly, CompositionAssembly })]
        [TestCase(GameplayAssembly, new[] { UIAssembly, CompositionAssembly })]
        [TestCase(UIAssembly, new[] { CompositionAssembly })]
        public void Layer_DoesNotReferenceUpperLayers(string layerAssemblyName, string[] forbiddenAssemblyNames)
        {
            Assembly layerAssembly = ResolveAssembly(layerAssemblyName);
            HashSet<string> referencedNames = new HashSet<string>(layerAssembly.GetReferencedAssemblies().Select(reference => reference.Name), StringComparer.Ordinal);
            string[] violations = forbiddenAssemblyNames.Where(referencedNames.Contains).ToArray();
            Assert.That(violations, Is.Empty, $"装配 '{layerAssemblyName}' 反向引用了上层装配：{string.Join("、", violations)}。");
        }

        /// <summary>ARCH-SYS-002：System 具体实现必须 internal sealed，外部只允许持有 I*System 契约。</summary>
        [Test]
        public void SystemImplementations_AreInternalSealed()
        {
            string[] violations = ConcreteImplementationsOf(typeof(ISystemContract))
                .Where(type => type.IsPublic || !type.IsSealed)
                .Select(type => $"{type.FullName}（public={type.IsPublic}, sealed={type.IsSealed}）")
                .ToArray();
            Assert.That(violations, Is.Empty, $"以下 System 实现违反 internal sealed 约束：{string.Join("；", violations)}。");
        }

        /// <summary>ARCH-KIT-002：正式 Kit 的具体实现必须 internal sealed。</summary>
        [Test]
        public void KitImplementations_AreInternalSealed()
        {
            string[] violations = ConcreteImplementationsOf(typeof(IKitContract))
                .Where(type => type.IsPublic || !type.IsSealed)
                .Select(type => $"{type.FullName}（public={type.IsPublic}, sealed={type.IsSealed}）")
                .ToArray();
            Assert.That(violations, Is.Empty, $"以下 Kit 实现违反 internal sealed 约束：{string.Join("；", violations)}。");
        }

        /// <summary>ARCH-SYS-006：单局只允许存在一个实体容器，因此实现实体驱动端口的类型必须唯一。</summary>
        [Test]
        public void EntityDriver_IsUnique()
        {
            Type[] drivers = ConcreteImplementationsOf(typeof(IEntityDriver)).ToArray();
            Assert.That(drivers.Length, Is.EqualTo(1), $"实体驱动端口必须唯一，当前实现：{string.Join("、", drivers.Select(type => type.FullName))}。");
        }

        /// <summary>ARCH-SYS-003：禁止按具体实现类型查询 System。</summary>
        [Test]
        public void Sources_DoNotQuerySystemsByConcreteType()
        {
            AssertNoSourceMatches(new Regex(@"(?:Try)?GetSystem<\s*[^I\s][^>]*System\s*>"), "禁止按具体实现查询 System，必须使用 I*System 契约（ARCH-SYS-003）");
        }

        /// <summary>ARCH-EVT-002：禁止新增与 IEventKit 平行的全局静态事件通道。</summary>
        [Test]
        public void Sources_DoNotDeclareStaticEventBuses()
        {
            AssertNoSourceMatches(new Regex(@"\bstatic\s+event\b"), "禁止新增静态事件总线，全局事实必须经由 Core.Event（ARCH-EVT-002）");
        }

        /// <summary>ARCH-KIT-003：业务代码必须通过 Core 的接口入口使用 Kit，GetKit 仅供 Core 自身生命周期管理。</summary>
        [Test]
        public void Sources_DoNotResolveKitsByGetKit()
        {
            AssertNoSourceMatches(new Regex(@"\.GetKit<"), "业务代码禁止使用 Core.GetKit，应使用 Core.Asset / Core.Event / Core.UI / Core.Gameplay（ARCH-KIT-003）");
        }

        /// <summary>ARCH-NET-003：底层网络客户端只允许由 ServiceSystem 持有。</summary>
        [Test]
        public void Sources_DoNotUseNetworkClientOutsideServiceSystem()
        {
            AssertNoSourceMatches(new Regex(@"\b(?:INetworkClient|NetworkClientFactory)\b"),
                "底层网络客户端只允许由 NetworkKit 自身声明、由 ServiceSystem 独占持有（ARCH-NET-003）",
                path =>
                {
                    string normalized = path.Replace('\\', '/');
                    // NetworkKit 是这些类型的声明方；ServiceSystem 是规范允许的唯一持有方。
                    return normalized.Contains("/Framework/NetworkKit/") || normalized.EndsWith("ServiceSystem/ServiceSystem.cs", StringComparison.Ordinal);
                });
        }

        /// <summary>
        /// ARCH-NET-003：通用请求通道只允许由各领域 Gateway 使用。
        /// 这条规则把"谁能组装业务 Packet"限制在明确命名的适配器上，
        /// 使得网络业务的新增只会落在某个 `*Gateway.cs` 中，而不会重新长成一个跨领域的服务上帝接口。
        /// </summary>
        [Test]
        public void Sources_DoNotUseRequestChannelOutsideGateways()
        {
            AssertNoSourceMatches(new Regex(@"\bRequestAsync\s*\("),
                "通用请求通道只允许由各领域 Gateway 调用（ARCH-NET-003）",
                path =>
                {
                    string normalized = path.Replace('\\', '/');
                    // NetworkKit 声明并实现底层请求；ServiceSystem 目录是通道自身的声明与实现；*Gateway.cs 是规范允许的调用方。
                    return normalized.Contains("/Framework/NetworkKit/")
                        || normalized.Contains("/Gameplay/ServiceSystem/")
                        || normalized.EndsWith("Gateway.cs", StringComparison.Ordinal);
                });
        }

        /// <summary>
        /// ARCH-EVT-005：全局事件订阅者必须在同一文件内对称退订。
        /// 只比较事件载荷类型的集合，因此订阅与退订写在不同方法（AfterNew / Dispose）也能通过。
        /// </summary>
        [Test]
        public void Sources_UnsubscribeEveryGlobalEventTheySubscribe()
        {
            Regex addPattern = new Regex(@"Core\.Event\.AddListener<\s*(\w+)\s*>");
            Regex removePattern = new Regex(@"Core\.Event\.RemoveListener<\s*(\w+)\s*>");
            List<string> violations = new List<string>();
            foreach (string sourcePath in EnumerateGovernedSources())
            {
                string source = StripCommentsAndLiterals(File.ReadAllText(sourcePath));
                HashSet<string> subscribed = new HashSet<string>(addPattern.Matches(source).Cast<Match>().Select(match => match.Groups[1].Value), StringComparer.Ordinal);
                if (subscribed.Count == 0) continue;
                subscribed.ExceptWith(removePattern.Matches(source).Cast<Match>().Select(match => match.Groups[1].Value));
                if (subscribed.Count == 0) continue;
                string relativePath = sourcePath.Substring(sourcePath.IndexOf("Assets", StringComparison.Ordinal)).Replace(Path.DirectorySeparatorChar, '/');
                violations.Add($"{relativePath} 订阅后未退订：{string.Join("、", subscribed)}");
            }

            Assert.That(violations, Is.Empty, $"全局事件订阅者必须在自身释放边界对称退订（ARCH-EVT-005）。{string.Join("；", violations)}");
        }

        /// <summary>取得指定名称的已加载装配；缺失说明装配划分被改动，应立即失败而不是静默跳过。</summary>
        private static Assembly ResolveAssembly(string assemblyName)
        {
            Assembly resolved = AppDomain.CurrentDomain.GetAssemblies().FirstOrDefault(assembly => string.Equals(assembly.GetName().Name, assemblyName, StringComparison.Ordinal));
            Assert.That(resolved, Is.Not.Null, $"未找到装配 '{assemblyName}'，装配划分可能已被改动。");
            return resolved;
        }

        /// <summary>枚举全部运行时装配中实现指定契约的具体类型；抽象类型和契约本身不参与约束。</summary>
        private static IEnumerable<Type> ConcreteImplementationsOf(Type contractType)
        {
            foreach (string assemblyName in RuntimeAssemblies)
            {
                foreach (Type type in ResolveAssembly(assemblyName).GetTypes())
                {
                    if (!type.IsClass || type.IsAbstract || type.IsNested) continue;
                    if (!contractType.IsAssignableFrom(type)) continue;
                    yield return type;
                }
            }
        }

        /// <summary>断言 ArchSpec 覆盖范围内的源码不包含指定模式；测试代码本身不受生产约束限制。</summary>
        /// <param name="forbiddenPattern">禁止出现的源码模式。</param>
        /// <param name="ruleDescription">失败信息中展示的规则说明。</param>
        /// <param name="isExempt">可选的豁免判定，用于规则明确允许的唯一位置。</param>
        private static void AssertNoSourceMatches(Regex forbiddenPattern, string ruleDescription, Func<string, bool> isExempt = null)
        {
            List<string> violations = new List<string>();
            foreach (string sourcePath in EnumerateGovernedSources())
            {
                if (isExempt != null && isExempt(sourcePath)) continue;
                string source = StripCommentsAndLiterals(File.ReadAllText(sourcePath));
                if (!forbiddenPattern.IsMatch(source)) continue;
                violations.Add(sourcePath.Substring(sourcePath.IndexOf("Assets", StringComparison.Ordinal)).Replace('\\', '/'));
            }

            Assert.That(violations, Is.Empty, $"{ruleDescription}。违规文件：{string.Join("、", violations)}。");
        }

        /// <summary>枚举 ArchSpec 覆盖的全部生产源码文件；测试目录和生成代码不参与源码级禁令。</summary>
        private static IEnumerable<string> EnumerateGovernedSources()
        {
            string prometheusRoot = Path.Combine(Application.dataPath, "Prometheus");
            foreach (string sourceRoot in SourceRoots)
            {
                string absoluteRoot = Path.Combine(prometheusRoot, sourceRoot);
                if (!Directory.Exists(absoluteRoot)) continue;
                foreach (string sourcePath in Directory.EnumerateFiles(absoluteRoot, "*.cs", SearchOption.AllDirectories))
                {
                    string normalized = sourcePath.Replace('\\', '/');
                    if (normalized.Contains("/Tests/") || normalized.EndsWith(".g.cs", StringComparison.Ordinal)) continue;
                    yield return sourcePath;
                }
            }
        }

        /// <summary>移除注释与字符串字面量，使源码级禁令只作用于真实代码而不是文档和提示文本。</summary>
        private static string StripCommentsAndLiterals(string source)
        {
            source = Regex.Replace(source, @"/\*.*?\*/", " ", RegexOptions.Singleline);
            source = Regex.Replace(source, @"//[^\r\n]*", " ");
            source = Regex.Replace(source, @"@""(?:[^""]|"""")*""", " \"\" ", RegexOptions.Singleline);
            source = Regex.Replace(source, @"""(?:\\.|[^""\\])*""", " \"\" ");
            return source;
        }
    }
}
