using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;

namespace Xuan.Prometheus.Narrative
{
    /// <summary>一个玩家选项及其后续分支。</summary>
    public sealed class ChoiceOption : StoryAction
    {
        private IStoryAction[] children = Array.Empty<IStoryAction>();

        /// <summary>创建一个选项。</summary>
        /// <param name="text">选项文案的文本键。</param>
        public ChoiceOption(TextKey text)
        {
            if (text.IsEmpty) throw new ArgumentException("Choice option requires a non-empty text key.", nameof(text));
            Text = text;
        }

        /// <summary>获取选项文案的文本键。</summary>
        public TextKey Text { get; }

        /// <summary>获取选项的解锁条件表达式；为空表示无条件可选。</summary>
        public string Condition { get; private set; }

        /// <summary>获取条件不成立时的锁定原因文案键；为空表示条件不成立时直接隐藏。</summary>
        public TextKey LockedReason { get; private set; }

        /// <summary>获取该选项是否只能被选择一次。</summary>
        public bool IsOnce { get; private set; }

        /// <summary>获取选中后演绎的分支；未配置时选中该项不产生任何后续内容。</summary>
        public IStoryAction Branch { get; private set; }

        /// <inheritdoc />
        public override IReadOnlyList<IStoryAction> Children => children;

        /// <summary>指定选项的解锁条件。</summary>
        public ChoiceOption When(string expression)
        {
            if (string.IsNullOrWhiteSpace(expression)) throw new ArgumentException("Choice condition cannot be empty.", nameof(expression));
            Condition = expression;
            return this;
        }

        /// <summary>指定条件不成立时以灰显锁定态展示，而不是直接隐藏。</summary>
        public ChoiceOption Locked(TextKey reason)
        {
            LockedReason = reason;
            return this;
        }

        /// <summary>标记该选项选择一次之后永久隐藏。</summary>
        public ChoiceOption Once()
        {
            IsOnce = true;
            return this;
        }

        /// <summary>指定选中该项后演绎的分支。</summary>
        public ChoiceOption Then(IStoryAction branch)
        {
            Branch = branch ?? throw new ArgumentNullException(nameof(branch));
            children = new[] { branch };
            return this;
        }

        /// <summary>为选项指定可读标识，使其路径在存档中稳定可辨认。</summary>
        public new ChoiceOption Id(string id)
        {
            base.Id(id);
            return this;
        }

        /// <summary>判断该选项在当前上下文中是否满足解锁条件。</summary>
        public bool IsUnlocked(StoryContext context)
        {
            return string.IsNullOrWhiteSpace(Condition) || context.Evaluate(Condition);
        }

        /// <summary>判断该选项在当前上下文中是否应当出现在选项列表里。</summary>
        public bool IsVisible(StoryContext context)
        {
            if (IsOnce && context.IsOptionChosen(Path)) return false;
            return IsUnlocked(context) || !LockedReason.IsEmpty;
        }

        /// <inheritdoc />
        public override UniTask PlayAsync(StoryContext context, CancellationToken cancellationToken)
        {
            return Branch == null ? UniTask.CompletedTask : Branch.PlayAsync(context, cancellationToken);
        }

        /// <inheritdoc />
        public override void Settle(StoryContext context)
        {
            Branch?.Settle(context);
        }
    }

    /// <summary>
    /// 选项分支叶子：展示可选项并按玩家选择分派。
    /// 跳过时不会替玩家做出尚未做过的选择；只有本次演绎中已经选过的选项才会被落终态。
    /// </summary>
    public sealed class ChooseAction : StoryAction
    {
        private readonly ChoiceOption[] options;

        /// <summary>创建一个选项分支。</summary>
        /// <param name="options">全部候选项；不允许为空集合。</param>
        public ChooseAction(params ChoiceOption[] options)
        {
            if (options == null) throw new ArgumentNullException(nameof(options));
            if (options.Length == 0) throw new ArgumentException("Choose requires at least one option.", nameof(options));
            for (int index = 0; index < options.Length; index++)
            {
                if (options[index] == null) throw new ArgumentException($"Choose contains a null option at index {index}.", nameof(options));
            }
            this.options = options;
        }

        /// <inheritdoc />
        public override IReadOnlyList<IStoryAction> Children => options;

        /// <inheritdoc />
        public override async UniTask PlayAsync(StoryContext context, CancellationToken cancellationToken)
        {
            if (context.IsResuming)
            {
                await ResumeAsync(context, cancellationToken);
                return;
            }
            List<DialogueChoice> visible = new List<DialogueChoice>(options.Length);
            bool hasSelectable = false;
            for (int index = 0; index < options.Length; index++)
            {
                ChoiceOption option = options[index];
                if (!option.IsVisible(context)) continue;
                bool unlocked = option.IsUnlocked(context);
                hasSelectable |= unlocked;
                visible.Add(new DialogueChoice(index, context.Resolve(option.Text), !unlocked, unlocked ? null : context.Resolve(option.LockedReason)));
            }
            if (!hasSelectable) throw new InvalidOperationException($"Story choice '{Path}' has no selectable option under the current conditions.");

            int selected = await context.View.ShowChoicesAsync(new DialogueChoiceRequest(Path, visible), cancellationToken);
            if (selected < 0 || selected >= options.Length) throw new InvalidOperationException($"Story choice '{Path}' received an out-of-range option index {selected}.");

            ChoiceOption chosen = options[selected];
            if (!chosen.IsUnlocked(context)) throw new InvalidOperationException($"Story choice '{Path}' received locked option index {selected}.");
            context.RecordChoice(Path, selected);
            if (chosen.IsOnce) context.MarkOptionChosen(chosen.Path);
            await chosen.PlayAsync(context, cancellationToken);
        }

        /// <inheritdoc />
        public override void Settle(StoryContext context)
        {
            // 跳过不得替玩家做决定：只有已经产生过选择结果的节点才落终态，否则视为该分支从未发生。
            if (!context.TryGetChoice(Path, out int selected)) return;
            if (selected < 0 || selected >= options.Length) return;
            options[selected].Settle(context);
        }

        /// <summary>
        /// 快进到续演点。
        /// 续演不会重新向玩家提问，而是沿用存档中记录的选择结果；缺少记录时立即报错，
        /// 因为凭空替玩家做一次选择会让存档与实际剧情走向产生分歧。
        /// </summary>
        private async UniTask ResumeAsync(StoryContext context, CancellationToken cancellationToken)
        {
            if (!context.TryGetChoice(Path, out int selected) || selected < 0 || selected >= options.Length)
            {
                throw new InvalidOperationException($"Story choice '{Path}' cannot be resumed because the snapshot contains no recorded result for it.");
            }
            ChoiceOption chosen = options[selected];
            if (!context.ContainsResumeTarget(chosen))
            {
                chosen.Settle(context);
                return;
            }
            context.DecideResume(chosen);
            await chosen.PlayAsync(context, cancellationToken);
        }
    }
}
