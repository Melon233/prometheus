namespace Xuan.Prometheus
{
    /// <summary>
    /// 演出期间的界面切换：演出开始时收起 HUD、打开对话面板，结束时还原。
    /// 它是 UI 层拥有的 System（ARCH-SYS-008）：玩法层的演出系统不能引用 UI，
    /// 而面板之间互相开关又会让 HUD 认识对话面板，因此由这个系统集中监听演出事件。
    /// 契约本身不公开任何操作，存在的意义只是作为注册键。
    /// </summary>
    public interface ICutsceneUISystem : ISystem
    {
    }
}
