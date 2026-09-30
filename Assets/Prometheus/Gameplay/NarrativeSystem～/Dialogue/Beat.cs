using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;

namespace Xuan.Prometheus.Narrative
{
    /// <summary>
    /// 一次对话节拍：一句台词及其伴随的表现。
    /// 表现挂在句子上而不是绝对秒数上，因此增删台词、修改文案或更换语言都不会破坏任何编排时序。
    /// </summary>
    public sealed class Beat : StoryAction
    {
        /// <summary>保存进入本节拍时并发触发、不阻塞推进的表现动作。</summary>
        private readonly List<IStoryAction> concurrent = new List<IStoryAction>();

        /// <summary>保存必须先完成才允许推进的动作。</summary>
        private readonly List<IStoryAction> blocking = new List<IStoryAction>();

        /// <summary>保存对外暴露的合并子节点视图，按并发在前、阻塞在后的稳定顺序排列。</summary>
        private readonly List<IStoryAction> children = new List<IStoryAction>();

        /// <summary>创建一句台词节拍。</summary>
        /// <param name="speaker">说话人；传入空引用表示黑屏叙述。</param>
        /// <param name="text">台词文本键。</param>
        public Beat(ActorRef speaker, TextKey text)
        {
            if (text.IsEmpty) throw new ArgumentException("Beat requires a non-empty text key.", nameof(text));
            Speaker = speaker;
            Text = text;
            Advance = AdvancePolicy.Click;
        }

        /// <summary>获取说话人引用。</summary>
        public ActorRef Speaker { get; }

        /// <summary>获取台词文本键。</summary>
        public TextKey Text { get; }

        /// <summary>获取配音事件键；本阶段仅透传给视图，不驱动播放。</summary>
        public string VoiceKey { get; private set; }

        /// <summary>获取本节拍的推进策略。</summary>
        public AdvancePolicy Advance { get; private set; }

        /// <inheritdoc />
        public override IReadOnlyList<IStoryAction> Children => children;

        /// <summary>挂载一个并发表现动作；该动作不阻塞节拍推进。</summary>
        /// <param name="action">镜头、动画、特效或音效等表现动作。</param>
        /// <returns>当前节拍，便于链式书写。</returns>
        public Beat With(IStoryAction action)
        {
            if (action == null) throw new ArgumentNullException(nameof(action));
            concurrent.Add(action);
            RebuildChildren();
            return this;
        }

        /// <summary>挂载一个阻塞动作；该动作完成之前玩家无法推进到下一节拍。</summary>
        /// <param name="action">需要先演完的动作，例如转身、走位。</param>
        /// <returns>当前节拍，便于链式书写。</returns>
        public Beat Await(IStoryAction action)
        {
            if (action == null) throw new ArgumentNullException(nameof(action));
            blocking.Add(action);
            RebuildChildren();
            return this;
        }

        /// <summary>指定本节拍的配音事件键。</summary>
        public Beat Voice(string voiceKey)
        {
            VoiceKey = voiceKey;
            return this;
        }

        /// <summary>指定本节拍的推进策略。</summary>
        public Beat AdvanceOn(AdvancePolicy policy)
        {
            Advance = policy;
            return this;
        }

        /// <summary>为节拍指定可读标识，使其路径在存档与日志中稳定可辨认。</summary>
        public new Beat Id(string id)
        {
            base.Id(id);
            return this;
        }

        /// <inheritdoc />
        public override async UniTask PlayAsync(StoryContext context, CancellationToken cancellationToken)
        {
            DialogueLine line = BuildLine(context);
            context.ReportBeatEntered(Path);
            context.View.BeginLine(line);

            // 并发表现与台词同时开始，且不随本节拍结束而中断，以便播放循环待机一类的持续动作。
            for (int index = 0; index < concurrent.Count; index++) PlayConcurrentAsync(concurrent[index], context, cancellationToken).Forget();

            await context.View.ShowLineAsync(line, cancellationToken);

            if (blocking.Count > 0)
            {
                List<UniTask> tasks = new List<UniTask>(blocking.Count);
                for (int index = 0; index < blocking.Count; index++) tasks.Add(blocking[index].PlayAsync(context, cancellationToken));
                await UniTask.WhenAll(tasks);
            }

            await context.View.WaitForAdvanceAsync(ResolveAdvance(context, line.Content), cancellationToken);
            context.View.EndLine();
        }

        /// <inheritdoc />
        public override void Settle(StoryContext context)
        {
            // 台词本身是纯表现，跳过时不显示；但挂载的动作可能携带世界状态，必须逐个落终态。
            for (int index = 0; index < children.Count; index++) children[index].Settle(context);
        }

        /// <summary>按当前文本表解析说话人名称与正文，组装交给视图的台词数据。</summary>
        private DialogueLine BuildLine(StoryContext context)
        {
            bool isNarration = Speaker.IsNone;
            string speakerName = isNarration ? string.Empty : context.Resolve(Speaker.DisplayNameKey);
            return new DialogueLine(Path, Speaker, speakerName, context.Resolve(Text), VoiceKey, isNarration);
        }

        /// <summary>
        /// 解析本节拍实际生效的推进策略。
        /// 自动播放把点击降级为自动推进；配音推进在没有配音服务的当前阶段退化为按文本长度推进。
        /// </summary>
        private AdvancePolicy ResolveAdvance(StoryContext context, string content)
        {
            if (context.Mode == StoryPlayMode.Preview) return AdvancePolicy.Immediate;
            AdvancePolicy policy = Advance;
            // 自动播放不需要在数据里单独配置：运行时把点击推进整体降级为自动推进即可。
            if (context.AutoPlay && policy.Kind == AdvanceKind.Click) policy = AdvancePolicy.VoiceEnd;
            if (policy.Kind == AdvanceKind.VoiceEnd) policy = new AdvancePolicy(AdvanceKind.TextDuration, context.EstimateReadingSeconds(content));
            if (policy.Kind == AdvanceKind.TextDuration && policy.Seconds <= 0f) policy = new AdvancePolicy(AdvanceKind.TextDuration, context.EstimateReadingSeconds(content));
            return policy;
        }

        /// <summary>演绎一个并发表现动作并吞掉取消异常，避免后台任务把异常抛到主循环。</summary>
        private static async UniTaskVoid PlayConcurrentAsync(IStoryAction action, StoryContext context, CancellationToken cancellationToken)
        {
            try
            {
                await action.PlayAsync(context, cancellationToken);
            }
            catch (OperationCanceledException)
            {
            }
        }

        /// <summary>重建对外暴露的子节点视图，使路径绑定顺序保持稳定。</summary>
        private void RebuildChildren()
        {
            children.Clear();
            children.AddRange(concurrent);
            children.AddRange(blocking);
        }
    }
}
