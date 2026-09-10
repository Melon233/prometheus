using Xuan.Prometheus.Narrative;

namespace Xuan.Prometheus
{
    /// <summary>
    /// <see cref="IDialogueHost"/> 的 UI 侧实现：把对话面板的开闭接到 UIKit。
    ///
    /// 这个适配器存在的唯一理由是 asmdef 的依赖方向——玩法层定义端口但无法命名
    /// <see cref="DialoguePanel"/>，UI 层能命名它但不该反过来驱动剧情流程。
    /// 由组合根把本实现交给玩法层，两侧都不必知道对方的具体类型。
    /// </summary>
    public sealed class DialoguePanelHost : IDialogueHost
    {
        /// <summary>打开对话面板并把它作为对话表现端口交给剧情系统。</summary>
        public IDialogueView Open()
        {
            return Core.UI.OpenPanel<DialoguePanel>();
        }

        /// <summary>关闭对话面板；面板按缓存策略保留实例，重复调用保持幂等。</summary>
        public void Close()
        {
            Core.UI.ClosePanel<DialoguePanel>();
        }
    }
}
