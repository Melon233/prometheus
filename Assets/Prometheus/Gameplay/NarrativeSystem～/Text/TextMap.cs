using System;
using System.Collections.Generic;

namespace Xuan.Prometheus.Narrative
{
    /// <summary>
    /// 运行时文本表的默认实现。
    /// 只常驻当前语言的一份字典，切换语言时整表替换，未使用语言不占内存。
    /// </summary>
    public sealed class TextMap : ITextMap
    {
        /// <summary>按语言代码保存各自的键值表。</summary>
        private readonly Dictionary<string, Dictionary<string, string>> languages = new Dictionary<string, Dictionary<string, string>>(StringComparer.OrdinalIgnoreCase);

        /// <summary>保存当前语言的键值表；未选择语言时为空表。</summary>
        private Dictionary<string, string> current = new Dictionary<string, string>(StringComparer.Ordinal);

        /// <inheritdoc />
        public event Action LanguageChanged;

        /// <summary>创建一个尚未载入任何语言的文本表。</summary>
        public TextMap()
        {
            Language = string.Empty;
        }

        /// <inheritdoc />
        public string Language { get; private set; }

        /// <summary>获取当前已载入的语言代码集合。</summary>
        public IReadOnlyCollection<string> LoadedLanguages => languages.Keys;

        /// <summary>获取当前语言下的条目数量。</summary>
        public int Count => current.Count;

        /// <summary>
        /// 载入或合并一个语言的文案条目。
        /// 重复键以后写入者为准，便于用增量补丁覆盖基础表。
        /// </summary>
        /// <param name="languageCode">语言代码。</param>
        /// <param name="entries">键值对序列。</param>
        public void Load(string languageCode, IEnumerable<KeyValuePair<string, string>> entries)
        {
            if (string.IsNullOrWhiteSpace(languageCode)) throw new ArgumentException("Text map language code cannot be empty.", nameof(languageCode));
            if (entries == null) throw new ArgumentNullException(nameof(entries));
            if (!languages.TryGetValue(languageCode, out Dictionary<string, string> table))
            {
                table = new Dictionary<string, string>(StringComparer.Ordinal);
                languages.Add(languageCode, table);
            }
            foreach (KeyValuePair<string, string> entry in entries)
            {
                if (string.IsNullOrWhiteSpace(entry.Key)) continue;
                table[entry.Key] = entry.Value ?? string.Empty;
            }
            if (string.Equals(Language, languageCode, StringComparison.OrdinalIgnoreCase)) current = table;
            else if (string.IsNullOrEmpty(Language)) SetLanguage(languageCode);
        }

        /// <summary>写入或替换当前语言下的单条文案，供调试与运行时补丁使用。</summary>
        public void Set(TextKey key, string value)
        {
            if (key.IsEmpty) throw new ArgumentException("Text map key cannot be empty.", nameof(key));
            if (string.IsNullOrEmpty(Language)) throw new InvalidOperationException("Text map requires a language before entries can be written.");
            current[key.Value] = value ?? string.Empty;
        }

        /// <inheritdoc />
        public string Get(TextKey key)
        {
            if (key.IsEmpty) return string.Empty;
            return current.TryGetValue(key.Value, out string value) ? value : $"#{key.Value}#";
        }

        /// <inheritdoc />
        public bool Contains(TextKey key)
        {
            return !key.IsEmpty && current.ContainsKey(key.Value);
        }

        /// <inheritdoc />
        public void SetLanguage(string languageCode)
        {
            if (string.IsNullOrWhiteSpace(languageCode)) throw new ArgumentException("Text map language code cannot be empty.", nameof(languageCode));
            if (string.Equals(Language, languageCode, StringComparison.OrdinalIgnoreCase)) return;
            if (!languages.TryGetValue(languageCode, out Dictionary<string, string> table)) throw new InvalidOperationException($"Text map does not contain language '{languageCode}'.");
            Language = languageCode;
            current = table;
            LanguageChanged?.Invoke();
        }

        /// <summary>清空全部语言数据并复位当前语言。</summary>
        public void Clear()
        {
            languages.Clear();
            current = new Dictionary<string, string>(StringComparer.Ordinal);
            Language = string.Empty;
        }
    }
}
