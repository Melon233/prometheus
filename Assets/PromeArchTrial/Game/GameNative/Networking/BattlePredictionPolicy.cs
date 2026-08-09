namespace PromeArchTrial.Game.Networking
{
    /// <summary>
    /// 声明客户端预测时间线与服务器权威时间线之间必须保持的稳定输入领先量，避免正常网络延迟将下一条命令变成迟到命令。
    /// </summary>
    public static class BattlePredictionPolicy
    {
        /// <summary>客户端始终比最新服务器快照领先四个固定 Tick；在三十赫兹下可吸收约一百三十三毫秒的单向延迟或渲染帧抖动。</summary>
        public const int ClientInputLeadTicks = 4;
    }
}
