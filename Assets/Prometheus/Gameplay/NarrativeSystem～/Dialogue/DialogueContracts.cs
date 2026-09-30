using System;
using System.Collections.Generic;

namespace Xuan.Prometheus.Narrative
{
    /// <summary>定义对话节拍的推进方式。</summary>
    public enum AdvanceKind
    {
        /// <summary>等待玩家确认输入。</summary>
        Click,

        /// <summary>配音播放结束后自动推进；本阶段尚无配音服务，退化为 TextDuration。</summary>
        VoiceEnd,

        /// <summary>按文本长度估算的阅读时长后自动推进。</summary>
        TextDuration,

        /// <summary>固定延迟后自动推进。</summary>
        Delay,

        /// <summary>不等待，立即推进；用于把多句合并成一段连续独白。</summary>
        Immediate
    }

    /// <summary>描述一个节拍的推进策略。</summary>
    public readonly struct AdvancePolicy
    {
        /// <summary>获取默认的点击推进策略。</summary>
        public static readonly AdvancePolicy Click = new AdvancePolicy(AdvanceKind.Click, 0f);

        /// <summary>获取立即推进策略。</summary>
        public static readonly AdvancePolicy Immediate = new AdvancePolicy(AdvanceKind.Immediate, 0f);

        /// <summary>获取按配音结束推进的策略。</summary>
        public static readonly AdvancePolicy VoiceEnd = new AdvancePolicy(AdvanceKind.VoiceEnd, 0f);

        /// <summary>获取按文本长度自动推进的策略。</summary>
        public static readonly AdvancePolicy TextDuration = new AdvancePolicy(AdvanceKind.TextDuration, 0f);

        /// <summary>创建一个推进策略。</summary>
        public AdvancePolicy(AdvanceKind kind, float seconds)
        {
            Kind = kind;
            Seconds = seconds;
        }

        /// <summary>获取推进方式。</summary>
        public AdvanceKind Kind { get; }

        /// <summary>获取固定延迟或已解析出的自动推进秒数。</summary>
        public float Seconds { get; }

        /// <summary>创建一个固定延迟推进策略。</summary>
        public static AdvancePolicy Delay(float seconds)
        {
            return new AdvancePolicy(AdvanceKind.Delay, seconds);
        }
    }

    /// <summary>
    /// 交给对话视图显示的一句台词。
    /// 文案与说话人名称在交给视图之前已经由 TextMap 解析完毕，视图不需要再感知本地化。
    /// </summary>
    public readonly struct DialogueLine
    {
        /// <summary>创建一句已完成本地化解析的台词。</summary>
        public DialogueLine(StoryPath path, ActorRef speaker, string speakerName, string content, string voiceKey, bool isNarration)
        {
            Path = path;
            Speaker = speaker;
            SpeakerName = speakerName ?? string.Empty;
            Content = content ?? string.Empty;
            VoiceKey = voiceKey;
            IsNarration = isNarration;
        }

        /// <summary>获取该台词所属节拍的稳定路径。</summary>
        public StoryPath Path { get; }

        /// <summary>获取说话人引用。</summary>
        public ActorRef Speaker { get; }

        /// <summary>获取已解析的说话人显示名；叙述行为空字符串。</summary>
        public string SpeakerName { get; }

        /// <summary>获取已解析的台词正文。</summary>
        public string Content { get; }

        /// <summary>获取配音事件键；本阶段仅作为数据透传，不驱动播放。</summary>
        public string VoiceKey { get; }

        /// <summary>获取该行是否为无说话人的黑屏叙述。</summary>
        public bool IsNarration { get; }
    }

    /// <summary>交给对话视图显示的一个选项。</summary>
    public readonly struct DialogueChoice
    {
        /// <summary>创建一个已完成本地化解析的选项。</summary>
        public DialogueChoice(int optionIndex, string content, bool isLocked, string lockedReason)
        {
            OptionIndex = optionIndex;
            Content = content ?? string.Empty;
            IsLocked = isLocked;
            LockedReason = lockedReason;
        }

        /// <summary>获取该选项在原始选项列表中的下标；视图选中后必须原样回传该值。</summary>
        public int OptionIndex { get; }

        /// <summary>获取已解析的选项文案。</summary>
        public string Content { get; }

        /// <summary>获取该选项是否处于可见但不可选的锁定状态。</summary>
        public bool IsLocked { get; }

        /// <summary>获取锁定原因文案；未锁定时为空。</summary>
        public string LockedReason { get; }
    }

    /// <summary>一次选项请求。</summary>
    public sealed class DialogueChoiceRequest
    {
        /// <summary>创建一次选项请求。</summary>
        public DialogueChoiceRequest(StoryPath path, IReadOnlyList<DialogueChoice> choices)
        {
            Path = path;
            Choices = choices ?? throw new ArgumentNullException(nameof(choices));
        }

        /// <summary>获取选项节点的稳定路径。</summary>
        public StoryPath Path { get; }

        /// <summary>获取本次展示的全部选项。</summary>
        public IReadOnlyList<DialogueChoice> Choices { get; }
    }
}
