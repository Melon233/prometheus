namespace Xuan.Prometheus
{
    /// <summary>定义 HUD 点击和快捷键共同使用的稳定命令入口。</summary>
    public interface IHudCommandSystem : ISystemContract
    {
        /// <summary>执行一个不携带具体 UI 组件引用的 HUD 命令。</summary>
        void Execute(HudCommandType command);
    }
}
