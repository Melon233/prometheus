using System;
using System.Collections.Generic;
using UnityEngine;

namespace Xuan.Prometheus.Narrative
{
    /// <summary>单条文案的可序列化条目。</summary>
    [Serializable]
    public sealed class TextMapEntry
    {
        [SerializeField] private string key;
        [SerializeField] [TextArea(1, 6)] private string value;

        /// <summary>创建一条文案条目，供编辑器工具与测试构造数据使用。</summary>
        public TextMapEntry(string key, string value)
        {
            this.key = key;
            this.value = value;
        }

        /// <summary>提供 Unity 序列化器创建数组元素所需的无参构造入口。</summary>
        public TextMapEntry()
        {
        }

        /// <summary>获取文本键。</summary>
        public string Key => key;

        /// <summary>获取当前语言下的文案。</summary>
        public string Value => value;
    }

    /// <summary>
    /// 一个语言的文案资产。
    /// 正式流程由表格导出工具生成，运行时按当前语言只加载对应的一份资产。
    /// </summary>
    [CreateAssetMenu(fileName = "TextMap", menuName = "Prometheus/Narrative/Text Map")]
    public sealed class TextMapAsset : ScriptableObject
    {
        [SerializeField] private string languageCode = "zh-CN";
        [SerializeField] private List<TextMapEntry> entries = new List<TextMapEntry>();

        /// <summary>获取该资产所属的语言代码。</summary>
        public string LanguageCode => languageCode;

        /// <summary>获取全部文案条目。</summary>
        public IReadOnlyList<TextMapEntry> Entries => entries;

        /// <summary>把本资产的全部条目并入运行时文本表。</summary>
        public void ApplyTo(TextMap textMap)
        {
            if (textMap == null) throw new ArgumentNullException(nameof(textMap));
            Validate();
            List<KeyValuePair<string, string>> pairs = new List<KeyValuePair<string, string>>(entries.Count);
            for (int index = 0; index < entries.Count; index++) pairs.Add(new KeyValuePair<string, string>(entries[index].Key, entries[index].Value));
            textMap.Load(languageCode, pairs);
        }

        /// <summary>校验语言代码与键的唯一性，使重复键在导入阶段而非运行期暴露。</summary>
        public void Validate()
        {
            if (string.IsNullOrWhiteSpace(languageCode)) throw new InvalidOperationException($"TextMapAsset '{name}' requires a language code.");
            HashSet<string> seen = new HashSet<string>(StringComparer.Ordinal);
            for (int index = 0; index < entries.Count; index++)
            {
                TextMapEntry entry = entries[index];
                if (entry == null || string.IsNullOrWhiteSpace(entry.Key)) throw new InvalidOperationException($"TextMapAsset '{name}' contains an empty key at index {index}.");
                if (!seen.Add(entry.Key)) throw new InvalidOperationException($"TextMapAsset '{name}' contains duplicate key '{entry.Key}'.");
            }
        }
    }
}
