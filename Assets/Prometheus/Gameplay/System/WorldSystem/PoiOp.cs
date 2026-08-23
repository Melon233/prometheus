namespace Xuan.Prometheus.World
{
    /// <summary>客户端向服务器提交的交互请求类型。数值与协议 PoiOp（Xuan.Prometheus.Protocol）一一对应。</summary>
    public enum PoiOp
    {
        Unlock,        // 解锁类：传送锚点 / 七天神像 / 副本
        OpenChest,     // 开启宝箱
        CollectCore,   // 收集神瞳
        Gather,        // 采集（可刷新，重复成功）
        Defeat,        // 击败地图 Boss（可刷新，重复成功）
        OfferStatue    // 七天神像供奉：消耗风神瞳推进进度，升级发长剑
    }
}
