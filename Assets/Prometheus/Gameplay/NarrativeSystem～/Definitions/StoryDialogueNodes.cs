using System;
using System.Collections.Generic;
using UnityEngine;

namespace Xuan.Prometheus.Narrative
{
    /// <summary>
    /// 挂在节拍上的一个表现动作。
    /// 用一个带标记的列表而不是两个列表，编辑器就只需要在条目上切换「阻塞」开关，
    /// 顺序也一目了然。
    /// </summary>
    [Serializable]
    public sealed class BeatAttachment
    {
        [SerializeField] [Tooltip("勾选后该动作必须先播完才允许推进节拍；不勾选则与台词并发且不阻塞。")]
        private bool blocking;

        [SerializeReference] [Tooltip("要挂载的表现动作。")]
        private StoryNode node;

        /// <summary>获取或设置该动作是否阻塞节拍推进。</summary>
        public bool Blocking { get => blocking; set => blocking = value; }

        /// <summary>获取或设置挂载的动作节点。</summary>
        public StoryNode Node { get => node; set => node = value; }
    }

    /// <summary>
    /// 对话节拍节点。
    /// 表现挂在句子上而不是绝对秒数上，因此增删台词、修改文案或更换语言都不会破坏编排时序。
    /// </summary>
    [Serializable]
    public sealed class BeatNode : StoryNode
    {
        [SerializeField] [Tooltip("说话人；类别选 None 表示这是一句没有说话人的叙述。")]
        private ActorRefData speaker;

        [SerializeField] [Tooltip("台词文本键；实际文案由 TextMap 按当前语言解析。")]
        private string textKey;

        [SerializeField] [Tooltip("配音事件键；当前阶段仅作为数据透传。")]
        private string voiceKey;

        [SerializeField] [Tooltip("推进方式。")]
        private AdvanceKind advance = AdvanceKind.Click;

        [SerializeField] [Tooltip("Delay 推进使用的秒数；其他推进方式忽略该值。")]
        private float advanceSeconds = 1f;

        [SerializeField] [Tooltip("随本句一起触发的表现动作。")]
        private List<BeatAttachment> attachments = new List<BeatAttachment>();

        /// <summary>获取或设置说话人。</summary>
        public ActorRefData Speaker { get => speaker; set => speaker = value; }

        /// <summary>获取或设置台词文本键。</summary>
        public string TextKey { get => textKey; set => textKey = value; }

        /// <summary>获取或设置配音事件键。</summary>
        public string VoiceKey { get => voiceKey; set => voiceKey = value; }

        /// <summary>获取或设置推进方式。</summary>
        public AdvanceKind Advance { get => advance; set => advance = value; }

        /// <summary>获取或设置固定延迟推进使用的秒数。</summary>
        public float AdvanceSeconds { get => advanceSeconds; set => advanceSeconds = value; }

        /// <summary>获取挂载的表现动作列表。</summary>
        public List<BeatAttachment> Attachments => attachments;

        /// <inheritdoc />
        public override string DisplayName => speaker.kind == ActorKind.None ? $"叙述 「{textKey}」" : $"台词 {speaker.id}：「{textKey}」";

        /// <inheritdoc />
        public override void CollectTextKeys(ICollection<string> keys)
        {
            if (!string.IsNullOrWhiteSpace(textKey)) keys.Add(textKey);
            // 说话人显示名同样要有文案，否则界面上会出现占位串。
            if (speaker.kind != ActorKind.None && !string.IsNullOrWhiteSpace(speaker.id)) keys.Add($"actor.{speaker.id}.name");
        }

        /// <inheritdoc />
        public override bool AcceptsChildren => true;

        /// <inheritdoc />
        public override IReadOnlyList<StoryNode> Children
        {
            get
            {
                List<StoryNode> list = new List<StoryNode>(attachments.Count);
                for (int index = 0; index < attachments.Count; index++)
                {
                    if (attachments[index] != null && attachments[index].Node != null) list.Add(attachments[index].Node);
                }
                return list;
            }
        }

        /// <inheritdoc />
        public override bool CanAddChild(StoryNode child)
        {
            return child != null && !(child is ChoiceOptionNode);
        }

        /// <inheritdoc />
        public override bool AddChild(StoryNode child)
        {
            if (!CanAddChild(child)) return false;
            attachments.Add(new BeatAttachment { Node = child });
            return true;
        }

        /// <inheritdoc />
        public override bool RemoveChild(StoryNode child)
        {
            for (int index = 0; index < attachments.Count; index++)
            {
                if (attachments[index] == null || !ReferenceEquals(attachments[index].Node, child)) continue;
                attachments.RemoveAt(index);
                return true;
            }
            return false;
        }

        /// <inheritdoc />
        public override bool MoveChild(StoryNode child, int delta)
        {
            int index = attachments.FindIndex(entry => entry != null && ReferenceEquals(entry.Node, child));
            int target = index + delta;
            if (index < 0 || target < 0 || target >= attachments.Count) return false;
            BeatAttachment moved = attachments[index];
            attachments.RemoveAt(index);
            attachments.Insert(target, moved);
            return true;
        }

        /// <inheritdoc />
        public override IStoryAction Build(StoryGraphBuildContext context)
        {
            if (string.IsNullOrWhiteSpace(textKey))
            {
                context.Error(this, "节拍需要一个台词文本键。");
                return null;
            }
            Beat beat = new Beat(speaker.ToRuntime(), new TextKey(textKey));
            if (!string.IsNullOrWhiteSpace(voiceKey)) beat.Voice(voiceKey);
            beat.AdvanceOn(advance == AdvanceKind.Delay ? AdvancePolicy.Delay(advanceSeconds) : new AdvancePolicy(advance, 0f));
            for (int index = 0; index < attachments.Count; index++)
            {
                BeatAttachment attachment = attachments[index];
                if (attachment == null || attachment.Node == null)
                {
                    context.Error(this, $"第 {index} 个挂载动作为空。");
                    return null;
                }
                IStoryAction action = attachment.Node.Build(context);
                if (action == null) return null;
                if (attachment.Blocking) beat.Await(action);
                else beat.With(action);
            }
            return WithId(beat);
        }
    }

    /// <summary>一个玩家选项节点。</summary>
    [Serializable]
    public sealed class ChoiceOptionNode : StoryNode
    {
        [SerializeField] [Tooltip("选项文案的文本键。")] private string textKey;
        [SerializeField] [Tooltip("解锁条件表达式；留空表示无条件可选。")] private string condition;
        [SerializeField] [Tooltip("条件不成立时的锁定原因文本键；留空表示条件不成立时直接隐藏该项。")] private string lockedReasonKey;
        [SerializeField] [Tooltip("勾选后该项被选择一次即永久隐藏。")] private bool once;
        [SerializeReference] [Tooltip("选中该项后演绎的分支。")] private StoryNode branch;

        /// <summary>获取或设置选项文案的文本键。</summary>
        public string TextKey { get => textKey; set => textKey = value; }

        /// <summary>获取或设置解锁条件表达式。</summary>
        public string Condition { get => condition; set => condition = value; }

        /// <summary>获取或设置条件不成立时的锁定原因文本键。</summary>
        public string LockedReasonKey { get => lockedReasonKey; set => lockedReasonKey = value; }

        /// <summary>获取或设置该选项是否只能被选择一次。</summary>
        public bool Once { get => once; set => once = value; }

        /// <summary>获取或设置选中后演绎的分支。</summary>
        public StoryNode Branch { get => branch; set => branch = value; }

        /// <inheritdoc />
        public override string DisplayName => $"选项 「{textKey}」{(once ? " (一次性)" : string.Empty)}";

        /// <inheritdoc />
        public override void CollectExpressions(ICollection<string> expressions)
        {
            if (!string.IsNullOrWhiteSpace(condition)) expressions.Add(condition);
        }

        /// <inheritdoc />
        public override void CollectTextKeys(ICollection<string> keys)
        {
            if (!string.IsNullOrWhiteSpace(textKey)) keys.Add(textKey);
            if (!string.IsNullOrWhiteSpace(lockedReasonKey)) keys.Add(lockedReasonKey);
        }

        /// <inheritdoc />
        public override bool AcceptsChildren => branch == null;

        /// <inheritdoc />
        public override IReadOnlyList<StoryNode> Children => branch == null ? Array.Empty<StoryNode>() : new[] { branch };

        /// <inheritdoc />
        public override bool CanAddChild(StoryNode child)
        {
            return child != null && branch == null && !(child is ChoiceOptionNode);
        }

        /// <inheritdoc />
        public override bool AddChild(StoryNode child)
        {
            if (!CanAddChild(child)) return false;
            branch = child;
            return true;
        }

        /// <inheritdoc />
        public override bool RemoveChild(StoryNode child)
        {
            if (!ReferenceEquals(branch, child)) return false;
            branch = null;
            return true;
        }

        /// <summary>构建为运行时选项。</summary>
        public ChoiceOption BuildOption(StoryGraphBuildContext context)
        {
            if (string.IsNullOrWhiteSpace(textKey))
            {
                context.Error(this, "选项需要一个文案文本键。");
                return null;
            }
            ChoiceOption option = new ChoiceOption(new TextKey(textKey));
            if (!string.IsNullOrWhiteSpace(condition)) option.When(condition);
            if (!string.IsNullOrWhiteSpace(lockedReasonKey)) option.Locked(new TextKey(lockedReasonKey));
            if (once) option.Once();
            if (branch != null)
            {
                IStoryAction branchAction = branch.Build(context);
                if (branchAction == null) return null;
                option.Then(branchAction);
            }
            if (!string.IsNullOrWhiteSpace(Id)) option.Id(Id);
            return option;
        }

        /// <inheritdoc />
        public override IStoryAction Build(StoryGraphBuildContext context)
        {
            context.Error(this, "选项节点只能作为选项分支节点的直接子节点使用。");
            return null;
        }
    }

    /// <summary>选项分支节点。</summary>
    [Serializable]
    public sealed class ChooseNode : StoryNode
    {
        [SerializeReference] [Tooltip("全部候选项。")]
        private List<StoryNode> options = new List<StoryNode>();

        /// <inheritdoc />
        public override string DisplayName => $"选项分支 Choose ({options.Count})";

        /// <inheritdoc />
        public override bool AcceptsChildren => true;

        /// <inheritdoc />
        public override IReadOnlyList<StoryNode> Children => options;

        /// <inheritdoc />
        public override bool CanAddChild(StoryNode child)
        {
            // 选项分支只接受选项节点，避免在编辑器里挂出无法构建的结构。
            return child is ChoiceOptionNode;
        }

        /// <inheritdoc />
        public override bool AddChild(StoryNode child)
        {
            if (!CanAddChild(child)) return false;
            options.Add(child);
            return true;
        }

        /// <inheritdoc />
        public override bool RemoveChild(StoryNode child)
        {
            return options.Remove(child);
        }

        /// <inheritdoc />
        public override bool MoveChild(StoryNode child, int delta)
        {
            int index = options.IndexOf(child);
            int target = index + delta;
            if (index < 0 || target < 0 || target >= options.Count) return false;
            options.RemoveAt(index);
            options.Insert(target, child);
            return true;
        }

        /// <inheritdoc />
        public override IStoryAction Build(StoryGraphBuildContext context)
        {
            if (options.Count == 0)
            {
                context.Error(this, "选项分支至少需要一个选项。");
                return null;
            }
            ChoiceOption[] built = new ChoiceOption[options.Count];
            for (int index = 0; index < options.Count; index++)
            {
                if (!(options[index] is ChoiceOptionNode optionNode))
                {
                    context.Error(this, $"第 {index} 个子节点不是选项节点。");
                    return null;
                }
                built[index] = optionNode.BuildOption(context);
                if (built[index] == null) return null;
            }
            return WithId(new ChooseAction(built));
        }
    }
}
