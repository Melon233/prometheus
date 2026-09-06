using System.Threading;
using Cysharp.Threading.Tasks;

namespace Xuan.Prometheus.Narrative
{
    /// <summary>
    /// 对话表现端口。
    /// 剧情逻辑只依赖该接口，因此对话流程可以在没有任何 UI 的 EditMode 测试中完整驱动。
    /// 实现方负责打字机、选项交互与输入采集；本地化解析已在调用前完成。
    /// </summary>
    public interface IDialogueView
    {
        /// <summary>获取或设置自动播放开关；开启后视图不应等待玩家确认。</summary>
        bool AutoPlay { get; set; }

        /// <summary>进入一句新台词的显示状态；实现应更新说话人并清空正文。</summary>
        void BeginLine(DialogueLine line);

        /// <summary>
        /// 逐字显示台词正文，并在全文显示完成后返回。
        /// 这是二段点击的第一段：显示过程中的确认输入应立即补全全文而不是推进节拍。
        /// </summary>
        UniTask ShowLineAsync(DialogueLine line, CancellationToken cancellationToken);

        /// <summary>
        /// 等待节拍推进信号。
        /// 这是二段点击的第二段；在本方法被调用之前收到的确认输入必须被丢弃，
        /// 以保证「先播完阻塞动作再允许推进」的编排不会被玩家点穿。
        /// </summary>
        UniTask WaitForAdvanceAsync(AdvancePolicy policy, CancellationToken cancellationToken);

        /// <summary>结束当前台词的显示状态。</summary>
        void EndLine();

        /// <summary>展示选项并等待玩家选择，返回被选中选项的原始下标。</summary>
        UniTask<int> ShowChoicesAsync(DialogueChoiceRequest request, CancellationToken cancellationToken);

        /// <summary>隐藏整个对话界面；一次剧情演绎结束时调用。</summary>
        void Hide();
    }
}
