namespace Xuan.Prometheus.Narrative
{
    /// <summary>描述一次剧情演绎所处的模式；叶子实现可据此省略非必要表现。</summary>
    public enum StoryPlayMode
    {
        /// <summary>正常演绎。</summary>
        Normal,

        /// <summary>跳过中：剩余节点只应用终态，不产生表现。</summary>
        Skipping,

        /// <summary>编辑器预览：允许省略音频与耗时等待。</summary>
        Preview
    }

    /// <summary>描述一次剧情演绎的结束原因。</summary>
    public enum StoryResult
    {
        /// <summary>完整演绎结束。</summary>
        Completed,

        /// <summary>玩家跳过，剩余节点已经落到终态。</summary>
        Skipped,

        /// <summary>被外部中止，世界状态可能停在中途。</summary>
        Aborted,

        /// <summary>演绎过程中抛出异常。</summary>
        Failed
    }

    /// <summary>描述断点续演快进过程中对一个子节点的处理方式。</summary>
    public enum StoryResumeDecision
    {
        /// <summary>正常演绎；未处于快进状态，或该节点就是续演点。</summary>
        Play,

        /// <summary>续演点位于该节点的子树中，向下递归继续快进。</summary>
        Descend,

        /// <summary>该节点整体位于续演点之前，只落终态不产生表现。</summary>
        Settle
    }

    /// <summary>定义并发组合子的完成条件。</summary>
    public enum ParallelMode
    {
        /// <summary>等待全部子节点完成。</summary>
        WhenAll,

        /// <summary>任一子节点完成即结束，其余子节点被取消。</summary>
        WhenAny,

        /// <summary>启动后立即返回；子节点在后台继续，并随本次演绎结束一并取消。</summary>
        Detached
    }
}
