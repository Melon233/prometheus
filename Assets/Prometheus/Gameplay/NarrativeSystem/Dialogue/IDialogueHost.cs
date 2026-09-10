namespace Xuan.Prometheus.Narrative
{
    /// <summary>
    /// 对话界面宿主端口。
    ///
    /// <see cref="IDialogueView"/> 描述的是「一句台词怎么显示」，本端口描述的是
    /// 「这套界面什么时候存在」。两者分开是因为界面的创建与销毁归 UI 程序集，
    /// 而玩法层只能在 asmdef 允许的方向上提出需求：玩法定义端口，UI 实现端口，
    /// 组合根把实现交给玩法。
    ///
    /// 实现方必须保证 <see cref="Open"/> 返回的视图在 <see cref="Close"/> 之前一直可用。
    /// </summary>
    public interface IDialogueHost
    {
        /// <summary>打开对话界面并返回它的表现端口。</summary>
        IDialogueView Open();

        /// <summary>关闭对话界面；重复调用保持幂等。</summary>
        void Close();
    }
}
