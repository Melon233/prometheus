namespace Xuan.Prometheus.Cutscene
{
    /// <summary>一段演出开始播放的事实。UI 据此隐藏 HUD 并打开对话面板。</summary>
    public sealed class CutsceneStartedEvent : IEvent
    {
        /// <summary>创建演出开始事件。</summary>
        /// <param name="definition">开始播放的演出。</param>
        public CutsceneStartedEvent(CutsceneDefinition definition)
        {
            Definition = definition;
        }

        /// <summary>开始播放的演出。</summary>
        public CutsceneDefinition Definition { get; }
    }

    /// <summary>
    /// 一段演出结束的事实（正常播完或被跳过）。
    /// 任务系统据此判定「看完演出」目标——跳过同样算看完，这是演出与任务之间唯一的连接点，演出不知道任务的存在。
    /// </summary>
    public sealed class CutsceneFinishedEvent : IEvent
    {
        /// <summary>创建演出结束事件。</summary>
        /// <param name="definition">结束的演出。</param>
        /// <param name="skipped">是否由玩家跳过。</param>
        public CutsceneFinishedEvent(CutsceneDefinition definition, bool skipped)
        {
            Definition = definition;
            Skipped = skipped;
        }

        /// <summary>结束的演出。</summary>
        public CutsceneDefinition Definition { get; }

        /// <summary>是否由玩家跳过。</summary>
        public bool Skipped { get; }
    }

    /// <summary>当前台词变化的事实；新状态从 <see cref="ICutsceneSystem.CurrentLine"/> 读取。</summary>
    public sealed class CutsceneLineChangedEvent : IEvent
    {
        /// <summary>创建台词变化事件。</summary>
        /// <param name="line">新台词；为空表示清除。</param>
        public CutsceneLineChangedEvent(CutsceneLine line)
        {
            Line = line;
        }

        /// <summary>新台词；为空表示当前没有台词。</summary>
        public CutsceneLine Line { get; }
    }
}
