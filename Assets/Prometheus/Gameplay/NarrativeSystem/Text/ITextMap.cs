using System;

namespace Xuan.Prometheus.Narrative
{
    /// <summary>定义按当前语言解析文本键的查询入口。</summary>
    public interface ITextMap
    {
        /// <summary>当前语言发生变化时触发，已打开的 UI 应据此重新解析显示文本。</summary>
        event Action LanguageChanged;

        /// <summary>获取当前语言代码。</summary>
        string Language { get; }

        /// <summary>
        /// 解析文本键。
        /// 缺失的键返回可见占位串而不是抛出异常，避免整段剧情因为一条未导入的文案而中断。
        /// </summary>
        /// <param name="key">待解析的文本键；空键返回空字符串。</param>
        /// <returns>当前语言下的文案，或形如 <c>#key#</c> 的占位串。</returns>
        string Get(TextKey key);

        /// <summary>判断当前语言下是否存在指定键。</summary>
        bool Contains(TextKey key);

        /// <summary>切换当前语言并通知监听者刷新。</summary>
        /// <param name="languageCode">目标语言代码，例如 <c>zh-CN</c>。</param>
        void SetLanguage(string languageCode);
    }
}
