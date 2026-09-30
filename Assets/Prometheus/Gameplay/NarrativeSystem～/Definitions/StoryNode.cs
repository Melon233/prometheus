using System;
using System.Collections.Generic;
using UnityEngine;
using Xuan.Prometheus.Expression;

namespace Xuan.Prometheus.Narrative
{
    /// <summary>可序列化的角色引用，供图资产使用。</summary>
    [Serializable]
    public struct ActorRefData
    {
        [Tooltip("角色引用类别。")] public ActorKind kind;
        [Tooltip("该类别下的稳定标识。")] public string id;

        /// <summary>转换为运行时角色引用。</summary>
        public ActorRef ToRuntime()
        {
            return kind == ActorKind.None || string.IsNullOrWhiteSpace(id) ? ActorRef.None : new ActorRef(kind, id);
        }
    }

    /// <summary>汇总一棵剧情树对舞台声明的要求，供编辑期交叉校验。</summary>
    public sealed class StoryNodeRequirements
    {
        /// <summary>获取剧情引用到的演出片段地址；必须出现在 StageSpec.Cinematics 中。</summary>
        public List<string> Cinematics { get; } = new List<string>();

        /// <summary>获取剧情引用到、且被状态类叶子同步读取的资源地址；必须出现在 StageSpec.Preload 中。</summary>
        public List<string> Preload { get; } = new List<string>();

        /// <summary>获取剧情需要在舞台上解析的角色；必须出现在 StageSpec.Actors 中。</summary>
        public List<ActorRef> Actors { get; } = new List<ActorRef>();

        /// <summary>登记一个需要在舞台上解析的角色。</summary>
        public void RequireActor(ActorRefData actor)
        {
            ActorRef reference = actor.ToRuntime();
            if (!reference.IsNone && !Actors.Contains(reference)) Actors.Add(reference);
        }
    }

    /// <summary>把图资产转换为运行时剧情树时携带的诊断信息。</summary>
    public sealed class StoryGraphBuildContext
    {
        /// <summary>获取构建过程中收集到的错误。</summary>
        public List<string> Errors { get; } = new List<string>();

        /// <summary>记录一条构建错误。</summary>
        public void Error(StoryNode node, string message)
        {
            Errors.Add($"[{(node == null ? "?" : node.DisplayName)}] {message}");
        }

        /// <summary>获取当前是否已经收集到错误。</summary>
        public bool HasErrors => Errors.Count > 0;
    }

    /// <summary>
    /// 图资产中的一个节点。
    /// <para>
    /// 节点树用 <c>[SerializeReference]</c> 做多态序列化，因此结构与运行时的 <see cref="IStoryAction"/> 树一一对应，
    /// 不需要额外的连线表或索引表。<see cref="Build"/> 把节点转换为运行时动作，是图资产与运行时之间唯一的转换点。
    /// </para>
    /// </summary>
    [Serializable]
    public abstract class StoryNode
    {
        [SerializeField] [Tooltip("节点在同级中的稳定标识；留空则用序号，填写后存档路径更可读也更稳定。")]
        private string id;

        [SerializeField] [HideInInspector] [Tooltip("编辑器中的布局位置。")]
        private Vector2 editorPosition;

        /// <summary>获取或设置节点的稳定标识。</summary>
        public string Id { get => id; set => id = value; }

        /// <summary>获取或设置节点在编辑器中的布局位置。</summary>
        public Vector2 EditorPosition { get => editorPosition; set => editorPosition = value; }

        /// <summary>获取节点在编辑器中显示的类型名。</summary>
        public abstract string DisplayName { get; }

        /// <summary>获取该节点的子节点列表；叶子节点返回空集合。</summary>
        public virtual IReadOnlyList<StoryNode> Children => Array.Empty<StoryNode>();

        /// <summary>获取该节点是否接受任意数量的子节点，供编辑器决定是否显示「添加子节点」。</summary>
        public virtual bool AcceptsChildren => false;

        /// <summary>把节点转换为运行时动作；转换失败时向上下文记录错误并返回 null。</summary>
        public abstract IStoryAction Build(StoryGraphBuildContext context);

        /// <summary>
        /// 收集该节点引用的条件表达式，供编辑期校验。
        /// 新增带表达式字段的节点类型时必须一并覆写，否则该表达式不会被校验到。
        /// </summary>
        public virtual void CollectExpressions(ICollection<string> expressions)
        {
        }

        /// <summary>
        /// 收集该节点引用的文本键，供编辑期校验文案是否已导入。
        /// 新增带文本键字段的节点类型时必须一并覆写。
        /// </summary>
        public virtual void CollectTextKeys(ICollection<string> keys)
        {
        }

        /// <summary>
        /// 收集该节点对舞台声明的要求（演出片段、预加载资源、参演角色）。
        /// 这些要求在运行时是硬性的：缺失会在演绎途中抛错，因此必须在编辑期就交叉校验出来。
        /// </summary>
        public virtual void CollectRequirements(StoryNodeRequirements requirements)
        {
        }

        /// <summary>
        /// 判断能否接纳一个子节点。
        /// 这是一个<b>不改变任何状态</b>的查询，编辑器用它来决定创建菜单里哪些项可用；
        /// 绝不能用「先加再删」来试探，那会真的改动数据。
        /// </summary>
        public virtual bool CanAddChild(StoryNode child)
        {
            return false;
        }

        /// <summary>在子节点列表末尾追加一个节点；不接受该子节点时返回 false。</summary>
        public virtual bool AddChild(StoryNode child)
        {
            return false;
        }

        /// <summary>移除一个子节点；不包含该子节点时返回 false。</summary>
        public virtual bool RemoveChild(StoryNode child)
        {
            return false;
        }

        /// <summary>在子节点列表中移动一个子节点；越界或不包含该子节点时返回 false。</summary>
        public virtual bool MoveChild(StoryNode child, int delta)
        {
            return false;
        }

        /// <summary>把已构建的运行时动作打上稳定标识。</summary>
        protected IStoryAction WithId(IStoryAction action)
        {
            if (action is StoryAction typed && !string.IsNullOrWhiteSpace(id)) typed.Id(id);
            return action;
        }

        /// <summary>构建一组子节点；任一子节点构建失败即返回 null。</summary>
        protected static IStoryAction[] BuildAll(IReadOnlyList<StoryNode> nodes, StoryGraphBuildContext context)
        {
            IStoryAction[] built = new IStoryAction[nodes.Count];
            for (int index = 0; index < nodes.Count; index++)
            {
                if (nodes[index] == null)
                {
                    context.Errors.Add($"节点列表第 {index} 项为空。");
                    return null;
                }
                built[index] = nodes[index].Build(context);
                if (built[index] == null) return null;
            }
            return built;
        }
    }

    /// <summary>可以持有任意数量有序子节点的组合节点基类。</summary>
    [Serializable]
    public abstract class StoryCompositeNode : StoryNode
    {
        [SerializeReference] [Tooltip("按顺序排列的子节点。")]
        private List<StoryNode> children = new List<StoryNode>();

        /// <inheritdoc />
        public override IReadOnlyList<StoryNode> Children => children;

        /// <inheritdoc />
        public override bool AcceptsChildren => true;

        /// <inheritdoc />
        public override bool CanAddChild(StoryNode child)
        {
            return child != null;
        }

        /// <inheritdoc />
        public override bool AddChild(StoryNode child)
        {
            if (!CanAddChild(child)) return false;
            children.Add(child);
            return true;
        }

        /// <inheritdoc />
        public override bool RemoveChild(StoryNode child)
        {
            return children.Remove(child);
        }

        /// <inheritdoc />
        public override bool MoveChild(StoryNode child, int delta)
        {
            int index = children.IndexOf(child);
            int target = index + delta;
            if (index < 0 || target < 0 || target >= children.Count) return false;
            children.RemoveAt(index);
            children.Insert(target, child);
            return true;
        }
    }

    /// <summary>顺序组合节点。</summary>
    [Serializable]
    public sealed class SequenceNode : StoryCompositeNode
    {
        /// <inheritdoc />
        public override string DisplayName => "顺序 Seq";

        /// <inheritdoc />
        public override IStoryAction Build(StoryGraphBuildContext context)
        {
            IStoryAction[] built = BuildAll(Children, context);
            if (built == null) return null;
            if (built.Length == 0)
            {
                context.Error(this, "顺序节点至少需要一个子节点。");
                return null;
            }
            return WithId(new SequenceAction(built));
        }
    }

    /// <summary>并发组合节点。</summary>
    [Serializable]
    public sealed class ParallelNode : StoryCompositeNode
    {
        [SerializeField] [Tooltip("并发完成条件。")] private ParallelMode mode = ParallelMode.WhenAll;

        /// <summary>获取或设置并发完成条件。</summary>
        public ParallelMode Mode { get => mode; set => mode = value; }

        /// <inheritdoc />
        public override string DisplayName => $"并发 Par ({mode})";

        /// <inheritdoc />
        public override IStoryAction Build(StoryGraphBuildContext context)
        {
            IStoryAction[] built = BuildAll(Children, context);
            if (built == null) return null;
            if (built.Length == 0)
            {
                context.Error(this, "并发节点至少需要一个子节点。");
                return null;
            }
            return WithId(new ParallelAction(mode, built));
        }
    }

    /// <summary>条件分支节点。</summary>
    [Serializable]
    public sealed class IfNode : StoryNode
    {
        [SerializeField] [Tooltip("条件表达式，例如 flag.asked && var.trust >= 2。")] private string expression;
        [SerializeReference] [Tooltip("条件成立时演绎的分支。")] private StoryNode thenBranch;
        [SerializeReference] [Tooltip("条件不成立时演绎的分支；可留空。")] private StoryNode elseBranch;

        /// <summary>获取或设置条件表达式。</summary>
        public string Expression { get => expression; set => expression = value; }

        /// <summary>获取或设置条件成立分支。</summary>
        public StoryNode ThenBranch { get => thenBranch; set => thenBranch = value; }

        /// <summary>获取或设置条件不成立分支。</summary>
        public StoryNode ElseBranch { get => elseBranch; set => elseBranch = value; }

        /// <inheritdoc />
        public override string DisplayName => $"条件 If ({expression})";

        /// <inheritdoc />
        public override void CollectExpressions(ICollection<string> expressions)
        {
            if (!string.IsNullOrWhiteSpace(expression)) expressions.Add(expression);
        }

        /// <inheritdoc />
        public override IReadOnlyList<StoryNode> Children
        {
            get
            {
                List<StoryNode> list = new List<StoryNode>(2);
                if (thenBranch != null) list.Add(thenBranch);
                if (elseBranch != null) list.Add(elseBranch);
                return list;
            }
        }

        /// <inheritdoc />
        public override bool CanAddChild(StoryNode child)
        {
            return child != null && (thenBranch == null || elseBranch == null);
        }

        /// <inheritdoc />
        public override bool AddChild(StoryNode child)
        {
            if (!CanAddChild(child)) return false;
            if (thenBranch == null)
            {
                thenBranch = child;
                return true;
            }
            elseBranch = child;
            return true;
        }

        /// <inheritdoc />
        public override bool RemoveChild(StoryNode child)
        {
            if (ReferenceEquals(thenBranch, child))
            {
                thenBranch = elseBranch;
                elseBranch = null;
                return true;
            }
            if (!ReferenceEquals(elseBranch, child)) return false;
            elseBranch = null;
            return true;
        }

        /// <inheritdoc />
        public override IStoryAction Build(StoryGraphBuildContext context)
        {
            if (string.IsNullOrWhiteSpace(expression))
            {
                context.Error(this, "条件节点需要一个表达式。");
                return null;
            }
            if (thenBranch == null)
            {
                context.Error(this, "条件节点需要一个成立分支。");
                return null;
            }
            IStoryAction thenAction = thenBranch.Build(context);
            if (thenAction == null) return null;
            IStoryAction elseAction = elseBranch != null ? elseBranch.Build(context) : null;
            if (elseBranch != null && elseAction == null) return null;
            return WithId(new IfAction(expression, thenAction, elseAction));
        }
    }

    /// <summary>变量赋值节点。</summary>
    [Serializable]
    public sealed class SetVariableNode : StoryNode
    {
        [SerializeField] [Tooltip("变量完整路径，例如 flag.asked 或 var.trust。")] private string path;
        [SerializeField] [Tooltip("值类别。")] private StoryValueKind kind = StoryValueKind.Bool;
        [SerializeField] [Tooltip("值的文本形式；布尔填 true/false，数值填数字。")] private string value = "true";

        /// <summary>获取或设置变量路径。</summary>
        public string Path { get => path; set => path = value; }

        /// <summary>获取或设置值类别。</summary>
        public StoryValueKind Kind { get => kind; set => kind = value; }

        /// <summary>获取或设置值的文本形式。</summary>
        public string Value { get => value; set => this.value = value; }

        /// <inheritdoc />
        public override string DisplayName => $"赋值 {path} = {value}";

        /// <inheritdoc />
        public override IStoryAction Build(StoryGraphBuildContext context)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                context.Error(this, "赋值节点需要一个变量路径。");
                return null;
            }
            return WithId(new SetVariableAction(path, StoryValue.Parse(kind, value)));
        }
    }

    /// <summary>固定时长等待节点。</summary>
    [Serializable]
    public sealed class WaitNode : StoryNode
    {
        [SerializeField] [Tooltip("等待秒数。")] private float seconds = 1f;

        /// <inheritdoc />
        public override string DisplayName => $"等待 {seconds:0.##}s";

        /// <inheritdoc />
        public override IStoryAction Build(StoryGraphBuildContext context)
        {
            return WithId(new WaitAction(seconds));
        }
    }

    /// <summary>条件等待节点。</summary>
    [Serializable]
    public sealed class WaitForNode : StoryNode
    {
        [SerializeField] [Tooltip("等待成立的条件表达式。")] private string expression;
        [SerializeField] [Tooltip("超时秒数；小于等于零表示无限等待。")] private float timeoutSeconds;

        /// <summary>获取或设置条件表达式。</summary>
        public string Expression { get => expression; set => expression = value; }

        /// <inheritdoc />
        public override string DisplayName => $"等待条件 {expression}";

        /// <inheritdoc />
        public override void CollectExpressions(ICollection<string> expressions)
        {
            if (!string.IsNullOrWhiteSpace(expression)) expressions.Add(expression);
        }

        /// <inheritdoc />
        public override IStoryAction Build(StoryGraphBuildContext context)
        {
            if (string.IsNullOrWhiteSpace(expression))
            {
                context.Error(this, "条件等待节点需要一个表达式。");
                return null;
            }
            return WithId(new WaitForAction(expression, timeoutSeconds));
        }
    }

    /// <summary>提前结束节点。</summary>
    [Serializable]
    public sealed class BreakNode : StoryNode
    {
        /// <inheritdoc />
        public override string DisplayName => "提前结束 Break";

        /// <inheritdoc />
        public override IStoryAction Build(StoryGraphBuildContext context)
        {
            return WithId(new BreakAction());
        }
    }
}
