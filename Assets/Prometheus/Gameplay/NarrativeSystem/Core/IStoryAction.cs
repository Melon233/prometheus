using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;

namespace Xuan.Prometheus.Narrative
{
    /// <summary>
    /// 剧情系统的唯一执行原语。
    /// 镜头、角色动画、物体动画、特效、音效、对话与流程控制全部实现该接口，并由 Seq 与 Par 两个组合子编排。
    /// </summary>
    public interface IStoryAction
    {
        /// <summary>获取该节点在所属剧情树中的稳定路径；未绑定到剧情树时为空路径。</summary>
        StoryPath Path { get; }

        /// <summary>获取该节点的直接子节点；叶子节点返回空集合。</summary>
        IReadOnlyList<IStoryAction> Children { get; }

        /// <summary>正常演绎该节点；实现必须响应取消，并保证取消后不残留半程副作用。</summary>
        UniTask PlayAsync(StoryContext context, CancellationToken cancellationToken);

        /// <summary>
        /// 不经过程直接把该节点的终态应用到世界。
        /// 跳过、断点续演和编辑器预览共用该入口，因此实现必须幂等：重复调用与调用一次结果一致。
        /// </summary>
        void Settle(StoryContext context);
    }

    /// <summary>
    /// 剧情动作的抽象基类，提供路径绑定与子节点默认实现。
    /// 系统内置的全部动作都从该类派生；外部自定义叶子建议同样派生，以便参与路径绑定。
    /// </summary>
    public abstract class StoryAction : IStoryAction
    {
        /// <summary>空子节点集合，避免叶子节点为每次查询分配数组。</summary>
        protected static readonly IReadOnlyList<IStoryAction> NoChildren = Array.Empty<IStoryAction>();

        /// <inheritdoc />
        public StoryPath Path { get; private set; }

        /// <inheritdoc />
        public virtual IReadOnlyList<IStoryAction> Children => NoChildren;

        /// <summary>获取作者显式指定的节点标识；为空时路径使用子节点序号。</summary>
        public string ExplicitId { get; private set; }

        /// <inheritdoc />
        public abstract UniTask PlayAsync(StoryContext context, CancellationToken cancellationToken);

        /// <inheritdoc />
        public abstract void Settle(StoryContext context);

        /// <summary>为节点指定一个可读标识，使其路径在存档与日志中保持稳定且可辨认。</summary>
        /// <param name="id">在同级节点中唯一的标识。</param>
        /// <returns>当前节点，便于在 DSL 中链式书写。</returns>
        public StoryAction Id(string id)
        {
            if (string.IsNullOrWhiteSpace(id)) throw new ArgumentException("Story action id cannot be empty.", nameof(id));
            ExplicitId = id;
            return this;
        }

        /// <summary>由 StoryTree 在绑定阶段写入节点路径。</summary>
        internal void BindPath(StoryPath path)
        {
            Path = path;
        }
    }
}
