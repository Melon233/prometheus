using System.Collections.Generic;

namespace Xuan.Prometheus.Cutscene
{
    /// <summary>
    /// 当前正在显示的一句台词的只读快照（文本已从 TbText 解析完毕），供对话面板直接显示。
    /// 选项只是让玩家表态，不影响后续流程：无论选哪一项都进入下一步。
    /// </summary>
    public sealed class CutsceneLine
    {
        /// <summary>创建台词快照。</summary>
        /// <param name="speaker">说话人名称；旁白为空串。</param>
        /// <param name="text">台词内容。</param>
        /// <param name="options">选项文案；没有选项时为空列表。</param>
        public CutsceneLine(string speaker, string text, IReadOnlyList<string> options)
        {
            Speaker = speaker;
            Text = text;
            Options = options;
        }

        /// <summary>说话人名称；旁白为空串。</summary>
        public string Speaker { get; }

        /// <summary>台词内容。</summary>
        public string Text { get; }

        /// <summary>选项文案；非空时本句等待选择而不是点击。</summary>
        public IReadOnlyList<string> Options { get; }

        /// <summary>本句是否带选项。</summary>
        public bool HasOptions => Options.Count > 0;
    }
}
