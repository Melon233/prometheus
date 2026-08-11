namespace Xuan.Prometheus
{
    public class Core
    {
        public static IUIKit UI { get; set; }
        public static IEventKit Event { get; set; }
        /// <summary>获取当前正式运行的玩法世界，使 UI 能通过公共 System 读取指定 Entity 的可监听字段。</summary>
        public static IGameplayKit Gameplay { get; set; }
    }
}
