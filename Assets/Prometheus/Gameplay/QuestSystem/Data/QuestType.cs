namespace Xuan.Prometheus.Quest
{
    /// <summary>
    /// 任务类型。当前只实现主线；资产按整数下标序列化，只允许在末尾追加，并同步更新 SerializedEnumLayoutTests 的黄金名单。
    /// </summary>
    public enum QuestType
    {
        /// <summary>主线任务。</summary>
        Main = 0,
    }
}
