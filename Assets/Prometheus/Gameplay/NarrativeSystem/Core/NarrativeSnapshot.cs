using System;
using System.Collections.Generic;
using UnityEngine;
using Xuan.Prometheus.Expression;

namespace Xuan.Prometheus.Narrative
{
    /// <summary>一个剧情变量的可序列化条目。</summary>
    [Serializable]
    public sealed class StoryVariableEntry
    {
        /// <summary>变量的完整点分路径。</summary>
        public string path;

        /// <summary>变量值类别。</summary>
        public int kind;

        /// <summary>变量值的文本形式。</summary>
        public string value;

        /// <summary>创建一个变量条目。</summary>
        public StoryVariableEntry(string path, StoryValue value)
        {
            this.path = path;
            kind = (int)value.Kind;
            this.value = value.ToString();
        }

        /// <summary>提供 Unity 序列化器所需的无参构造入口。</summary>
        public StoryVariableEntry()
        {
        }

        /// <summary>把条目还原为剧情值。</summary>
        public StoryValue ToStoryValue()
        {
            return StoryValue.Parse((StoryValueKind)kind, value);
        }
    }

    /// <summary>一个选项节点上已经产生的选择结果。</summary>
    [Serializable]
    public sealed class StoryChoiceEntry
    {
        /// <summary>选项节点的稳定路径。</summary>
        public string path;

        /// <summary>被选中选项在原始列表中的下标。</summary>
        public int optionIndex;

        /// <summary>创建一个选择结果条目。</summary>
        public StoryChoiceEntry(string path, int optionIndex)
        {
            this.path = path;
            this.optionIndex = optionIndex;
        }

        /// <summary>提供 Unity 序列化器所需的无参构造入口。</summary>
        public StoryChoiceEntry()
        {
        }
    }

    /// <summary>
    /// 剧情系统的可持久化状态。
    /// <para>
    /// <b>不包含任何演出播放进度</b>：既没有 Timeline 时间，也没有镜头位置或特效句柄。
    /// 这些都可以由续演点之前节点的 <c>Settle</c> 重建，因此存档只需要记录到「演到哪个节拍」。
    /// </para>
    /// <para>
    /// 旗标与剧情变量共用同一份存储（旗标即 <c>flag.*</c> 前缀的布尔变量），
    /// 因此这里只有一个 <see cref="variables"/> 列表，不再单列旗标，避免两份数据产生分歧。
    /// </para>
    /// </summary>
    [Serializable]
    public sealed class NarrativeSnapshot
    {
        /// <summary>全部剧情变量与旗标。</summary>
        public List<StoryVariableEntry> variables = new List<StoryVariableEntry>();

        /// <summary>已完整播放过、因而允许跳过的剧情标识。</summary>
        public List<string> seenStories = new List<string>();

        /// <summary>已选择过的一次性选项路径。</summary>
        public List<string> chosenOptions = new List<string>();

        /// <summary>各选项节点上已经产生的选择结果。</summary>
        public List<StoryChoiceEntry> choices = new List<StoryChoiceEntry>();

        /// <summary>中断时所处剧情的稳定标识；为空表示当前没有进行中的剧情。</summary>
        public string resumeStoryId;

        /// <summary>中断时所处的节拍路径；为空表示当前没有进行中的剧情。</summary>
        public string resumePath;

        /// <summary>获取当前快照是否记录了一个可续演的位置。</summary>
        public bool HasResumePoint => !string.IsNullOrEmpty(resumeStoryId) && !string.IsNullOrEmpty(resumePath);

        /// <summary>从运行时上下文捕获一份快照。</summary>
        /// <param name="context">当前剧情上下文。</param>
        /// <param name="seenStories">已完整看过的剧情标识集合；可为空。</param>
        /// <param name="resumeStoryId">中断时所处剧情标识；没有进行中的剧情时传空。</param>
        /// <param name="resumePath">中断时所处节拍路径；没有进行中的剧情时传空路径。</param>
        public static NarrativeSnapshot Capture(StoryContext context, IEnumerable<string> seenStories, string resumeStoryId, StoryPath resumePath)
        {
            if (context == null) throw new ArgumentNullException(nameof(context));
            NarrativeSnapshot snapshot = new NarrativeSnapshot
            {
                resumeStoryId = resumeStoryId,
                resumePath = resumePath.IsEmpty ? null : resumePath.Value
            };
            foreach (KeyValuePair<string, StoryValue> pair in context.Variables.Capture()) snapshot.variables.Add(new StoryVariableEntry(pair.Key, pair.Value));
            foreach (string option in context.ChosenOptions) snapshot.chosenOptions.Add(option);
            foreach (KeyValuePair<string, int> choice in context.CaptureChoices()) snapshot.choices.Add(new StoryChoiceEntry(choice.Key, choice.Value));
            if (seenStories != null)
            {
                foreach (string story in seenStories)
                {
                    if (!string.IsNullOrWhiteSpace(story)) snapshot.seenStories.Add(story);
                }
            }
            return snapshot;
        }

        /// <summary>把快照写回运行时上下文；变量、已选选项与选择结果都会被整体覆盖。</summary>
        public void RestoreTo(StoryContext context)
        {
            if (context == null) throw new ArgumentNullException(nameof(context));
            Dictionary<string, StoryValue> values = new Dictionary<string, StoryValue>(StringComparer.Ordinal);
            for (int index = 0; index < variables.Count; index++)
            {
                StoryVariableEntry entry = variables[index];
                if (entry != null && !string.IsNullOrEmpty(entry.path)) values[entry.path] = entry.ToStoryValue();
            }
            context.Variables.Restore(values);
            context.RestoreChosenOptions(chosenOptions);
            Dictionary<string, int> choiceResults = new Dictionary<string, int>(StringComparer.Ordinal);
            for (int index = 0; index < choices.Count; index++)
            {
                StoryChoiceEntry entry = choices[index];
                if (entry != null && !string.IsNullOrEmpty(entry.path)) choiceResults[entry.path] = entry.optionIndex;
            }
            context.RestoreChoices(choiceResults);
        }

        /// <summary>读取快照中的续演点路径。</summary>
        public StoryPath GetResumePath()
        {
            return new StoryPath(resumePath);
        }

        /// <summary>序列化为 JSON。</summary>
        public string ToJson()
        {
            return JsonUtility.ToJson(this);
        }

        /// <summary>从 JSON 反序列化；内容非法时抛出可诊断的错误。</summary>
        public static NarrativeSnapshot FromJson(string json)
        {
            if (string.IsNullOrWhiteSpace(json)) throw new ArgumentException("Narrative snapshot JSON cannot be empty.", nameof(json));
            NarrativeSnapshot snapshot = JsonUtility.FromJson<NarrativeSnapshot>(json);
            if (snapshot == null) throw new InvalidOperationException("Narrative snapshot JSON is invalid.");
            snapshot.variables = snapshot.variables ?? new List<StoryVariableEntry>();
            snapshot.seenStories = snapshot.seenStories ?? new List<string>();
            snapshot.chosenOptions = snapshot.chosenOptions ?? new List<string>();
            snapshot.choices = snapshot.choices ?? new List<StoryChoiceEntry>();
            return snapshot;
        }
    }
}
