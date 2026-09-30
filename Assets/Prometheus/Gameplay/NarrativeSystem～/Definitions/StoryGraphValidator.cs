using System;
using System.Collections.Generic;
using Xuan.Prometheus.Expression;

namespace Xuan.Prometheus.Narrative
{
    /// <summary>
    /// 图资产的编辑期校验。
    /// <para>
    /// 设计文档 §11 要求「资产导入时解析全部表达式，未定义标识符直接报错」。这里在此基础上把
    /// 运行时的硬性前提也一并前移到编辑期：演出片段必须在舞台声明中、状态类叶子引用的资源必须预加载、
    /// 被动作操作的角色必须参演。这些缺失在运行时只会在演到那一步时才抛错，代价高得多。
    /// </para>
    /// </summary>
    public static class StoryGraphValidator
    {
        /// <summary>
        /// 校验一份图资产。
        /// </summary>
        /// <param name="graph">待校验的图资产。</param>
        /// <param name="variables">用于承认表达式标识符命名空间的解析器；可为空则跳过表达式命名空间校验。</param>
        /// <param name="text">用于检查文案是否已导入的文本表；可为空则跳过文本校验。</param>
        /// <param name="problems">接收逐条问题描述。</param>
        /// <returns>没有发现任何问题时返回 true。</returns>
        public static bool Validate(StoryGraph graph, IStoryVariableResolver variables, ITextMap text, List<string> problems)
        {
            if (graph == null) throw new ArgumentNullException(nameof(graph));
            if (problems == null) throw new ArgumentNullException(nameof(problems));
            int before = problems.Count;

            ValidateStructure(graph, problems);
            ValidateExpressions(graph, variables, problems);
            ValidateTextKeys(graph, text, problems);
            ValidateStageRequirements(graph, problems);

            return problems.Count == before;
        }

        /// <summary>校验图能够构建为运行时剧情树，且全树路径唯一。</summary>
        private static void ValidateStructure(StoryGraph graph, List<string> problems)
        {
            IStoryAction action = graph.Build(out IReadOnlyList<string> errors);
            if (action != null) return;
            for (int index = 0; index < errors.Count; index++) problems.Add($"结构：{errors[index]}");
        }

        /// <summary>校验全部条件表达式可以编译，且标识符命名空间被承认。</summary>
        private static void ValidateExpressions(StoryGraph graph, IStoryVariableResolver variables, List<string> problems)
        {
            List<string> expressions = new List<string>();
            foreach (StoryNode node in graph.EnumerateNodes())
            {
                int before = expressions.Count;
                node.CollectExpressions(expressions);
                for (int index = before; index < expressions.Count; index++)
                {
                    if (StoryExpression.Validate(expressions[index], variables, out string error)) continue;
                    problems.Add($"表达式（{node.DisplayName}）：{error}");
                }
            }
        }

        /// <summary>校验全部文本键在当前文本表中存在。</summary>
        private static void ValidateTextKeys(StoryGraph graph, ITextMap text, List<string> problems)
        {
            if (text == null) return;
            List<string> keys = new List<string>();
            foreach (StoryNode node in graph.EnumerateNodes())
            {
                int before = keys.Count;
                node.CollectTextKeys(keys);
                for (int index = before; index < keys.Count; index++)
                {
                    if (text.Contains(new TextKey(keys[index]))) continue;
                    problems.Add($"文案（{node.DisplayName}）：文本表中缺少键 '{keys[index]}'。");
                }
            }
        }

        /// <summary>交叉校验剧情对舞台声明的要求。</summary>
        private static void ValidateStageRequirements(StoryGraph graph, List<string> problems)
        {
            StoryNodeRequirements requirements = new StoryNodeRequirements();
            foreach (StoryNode node in graph.EnumerateNodes()) node.CollectRequirements(requirements);

            StageSpec spec = graph.BuildStage();
            for (int index = 0; index < requirements.Cinematics.Count; index++)
            {
                string location = requirements.Cinematics[index];
                if (spec.Cinematics.Contains(location)) continue;
                problems.Add($"舞台：演出片段 '{location}' 未在舞台声明的 Cinematics 中列出，运行时会拒绝播放。");
            }
            for (int index = 0; index < requirements.Preload.Count; index++)
            {
                string location = requirements.Preload[index];
                if (spec.Preload.Contains(location)) continue;
                problems.Add($"舞台：资源 '{location}' 未在舞台声明的 Preload 中列出；落终态是同步过程，跳过时会取不到该资源。");
            }
            for (int index = 0; index < requirements.Actors.Count; index++)
            {
                ActorRef actor = requirements.Actors[index];
                if (spec.Actors.Contains(actor)) continue;
                problems.Add($"舞台：角色 '{actor}' 被动作引用但未在舞台声明的 Actors 中列出，运行时会解析失败。");
            }
        }
    }
}
