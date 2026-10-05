namespace Xuan.Prometheus
{
    /// <summary>
    /// 对话面板：走 UIKit 的预制体与代码生成流程。
    /// 旧对话链路已删除，对话系统待重新设计，当前只保留生成绑定。
    /// </summary>
    [UIPanelConfig("DialoguePanel", UIPanelLayer.Popup, UIPanelClosePolicy.Cache)]
    public sealed class DialoguePanel : DialoguePanelBase
    {
        /// <summary>选项模板只作为克隆源，初始化时隐藏。</summary>
        protected override void OnInitialize()
        {
            ChoiceTemplate.gameObject.SetActive(false);
        }

        /// <summary>推进按钮点击；对话系统重新设计后接入。</summary>
        protected override void OnAdvanceBtnClick()
        {
        }
    }
}
