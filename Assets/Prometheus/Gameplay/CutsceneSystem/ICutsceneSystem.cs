namespace Xuan.Prometheus.Cutscene
{
    /// <summary>
    /// 演出系统契约：同一时间至多播放一段演出。
    /// 播放期间独占演出镜头与玩家输入；UI 通过 <see cref="CurrentLine"/> 读取台词，并通过 <see cref="Advance"/> / <see cref="Choose"/> 提交玩家操作。
    /// </summary>
    public interface ICutsceneSystem : ISystem
    {
        /// <summary>是否正在播放。</summary>
        bool IsPlaying { get; }

        /// <summary>正在播放的演出；未播放时为空。</summary>
        CutsceneDefinition Current { get; }

        /// <summary>当前显示的台词；没有台词时为空。</summary>
        CutsceneLine CurrentLine { get; }

        /// <summary>
        /// 开始播放一段演出。正在播放时再调用属于调用方的逻辑错误，直接抛出；
        /// 资产校验失败或场景缺少所需标记同样直接抛出。
        /// </summary>
        /// <param name="definition">要播放的演出。</param>
        void Play(CutsceneDefinition definition);

        /// <summary>点击推进当前台词；非对话步骤或等待选择时忽略。</summary>
        void Advance();

        /// <summary>选择当前台词的一个选项；选项不影响后续流程。</summary>
        /// <param name="optionIndex">选项下标。</param>
        void Choose(int optionIndex);

        /// <summary>跳过当前演出；演出配置为不可跳过时忽略。</summary>
        void Skip();
    }
}
