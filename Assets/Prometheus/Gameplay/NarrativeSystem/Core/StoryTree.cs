using System;
using System.Collections.Generic;

namespace Xuan.Prometheus.Narrative
{
    /// <summary>提供剧情树的路径绑定与遍历工具。</summary>
    public static class StoryTree
    {
        /// <summary>
        /// 自顶向下为整棵剧情树分配稳定路径。
        /// 同一棵树重复绑定同一根标识是幂等的，便于断点续演时重建结构后再次寻址。
        /// </summary>
        /// <param name="root">剧情树根节点。</param>
        /// <param name="rootId">根节点的稳定标识。</param>
        public static void Bind(IStoryAction root, string rootId)
        {
            if (root == null) throw new ArgumentNullException(nameof(root));
            BindRecursive(root, StoryPath.Root(rootId));
        }

        /// <summary>按深度优先顺序枚举剧情树中的全部节点，包含根节点自身。</summary>
        public static IEnumerable<IStoryAction> Enumerate(IStoryAction root)
        {
            if (root == null) yield break;
            yield return root;
            IReadOnlyList<IStoryAction> children = root.Children;
            for (int index = 0; index < children.Count; index++)
            {
                foreach (IStoryAction descendant in Enumerate(children[index])) yield return descendant;
            }
        }

        /// <summary>按路径查找剧情树中的节点；未找到时返回 null。</summary>
        public static IStoryAction Find(IStoryAction root, StoryPath path)
        {
            if (path.IsEmpty) return null;
            foreach (IStoryAction action in Enumerate(root))
            {
                if (action.Path.Equals(path)) return action;
            }
            return null;
        }

        /// <summary>校验整棵树的路径唯一，避免同级节点使用重复的显式标识导致存档寻址歧义。</summary>
        /// <param name="root">已完成绑定的剧情树根节点。</param>
        public static void ValidateUniquePaths(IStoryAction root)
        {
            HashSet<string> seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (IStoryAction action in Enumerate(root))
            {
                if (action.Path.IsEmpty) throw new InvalidOperationException($"Story action '{action.GetType().Name}' was not bound to a story tree.");
                if (!seen.Add(action.Path.Value)) throw new InvalidOperationException($"Story tree contains duplicate path '{action.Path.Value}'.");
            }
        }

        /// <summary>递归写入节点路径；非 StoryAction 派生的自定义实现会被跳过绑定但仍继续向下遍历。</summary>
        private static void BindRecursive(IStoryAction action, StoryPath path)
        {
            if (action is StoryAction bindable) bindable.BindPath(path);
            IReadOnlyList<IStoryAction> children = action.Children;
            for (int index = 0; index < children.Count; index++)
            {
                IStoryAction child = children[index];
                string segment = child is StoryAction typed && !string.IsNullOrEmpty(typed.ExplicitId) ? typed.ExplicitId : index.ToString();
                BindRecursive(child, path.Append(segment));
            }
        }
    }
}
