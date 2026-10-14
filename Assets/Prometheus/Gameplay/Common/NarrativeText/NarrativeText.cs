namespace Xuan.Prometheus
{
    /// <summary>
    /// 叙事文本查询：对话台词、说话人、选项与任务文案统一存放在 Luban 表 TbText 中，SO 只保存整数 id。
    /// 文本与结构分离是为了本地化：换语言只换表，不动任何演出或任务资产。
    /// </summary>
    public static class NarrativeText
    {
        /// <summary>id 为 0 表示「没有文本」（例如旁白没有说话人），这是唯一允许的空值。</summary>
        public const int None = 0;

        /// <summary>按 id 取文本；id 为 0 返回空串，表中不存在的 id 属于配置错误，由生成代码直接抛出。</summary>
        /// <param name="textId">TbText 主键。</param>
        public static string Get(int textId)
        {
            return textId == None ? string.Empty : Core.Config.Tables.TbText.Get(textId).Content;
        }
    }
}
