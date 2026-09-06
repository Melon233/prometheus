using System;
using System.Collections.Generic;

namespace Xuan.Prometheus.Narrative
{
    /// <summary>
    /// 承载一次剧情演绎的运行时上下文：变量、条件求值、文本解析与对话视图。
    /// 舞台接管与角色解析将在第二期加入本类型，当前阶段的剧情只依赖对话与流程能力。
    /// </summary>
    public sealed class StoryContext
    {
        /// <summary>保存本次演绎中已选择过的一次性选项路径。</summary>
        private readonly HashSet<string> chosenOptions = new HashSet<string>(StringComparer.Ordinal);

        /// <summary>按选项节点路径保存玩家已经做出的选择结果，使跳过与断点续演不会替玩家重新决定分支。</summary>
        private readonly Dictionary<string, int> choiceResults = new Dictionary<string, int>(StringComparer.Ordinal);

        /// <summary>创建一个剧情上下文。</summary>
        /// <param name="text">文本表；对话文案由该表按当前语言解析。</param>
        /// <param name="view">对话视图端口；纯逻辑测试可传入伪实现。</param>
        /// <param name="variables">变量存储；为空时自动创建。</param>
        /// <param name="functions">表达式函数解析器；可为空。</param>
        public StoryContext(ITextMap text, IDialogueView view, StoryVariables variables = null, IStoryFunctionResolver functions = null)
        {
            Text = text ?? throw new ArgumentNullException(nameof(text));
            View = view ?? throw new ArgumentNullException(nameof(view));
            Variables = variables ?? new StoryVariables();
            Functions = functions;
        }

        /// <summary>获取变量与旗标存储。</summary>
        public StoryVariables Variables { get; }

        /// <summary>获取表达式函数解析器；可为空。</summary>
        public IStoryFunctionResolver Functions { get; }

        /// <summary>获取当前文本表。</summary>
        public ITextMap Text { get; }

        /// <summary>获取当前对话视图。</summary>
        public IDialogueView View { get; }

        /// <summary>
        /// 获取或设置当前生效的舞台作用域。
        /// 由进入舞台的一方写入；纯对话剧情可以不设置，此时任何依赖舞台的动作都会给出明确错误。
        /// </summary>
        public StageScope Stage { get; set; }

        /// <summary>取用当前舞台作用域；未进入舞台时抛出可诊断的错误。</summary>
        public StageScope RequireStage()
        {
            if (Stage == null || Stage.IsDisposed) throw new InvalidOperationException("This story action requires an active narrative stage. Enter a stage with Stage.EnterAsync and assign it to StoryContext.Stage.");
            return Stage;
        }

        /// <summary>取用当前舞台的能力端口集合。</summary>
        public StageServices RequireServices()
        {
            return RequireStage().Services;
        }

        /// <summary>获取本次演绎所处的模式。</summary>
        public StoryPlayMode Mode { get; private set; } = StoryPlayMode.Normal;

        /// <summary>获取当前是否处于跳过状态；处于该状态时组合子只对剩余节点调用 Settle。</summary>
        public bool IsSkipping => Mode == StoryPlayMode.Skipping;

        /// <summary>获取或设置自动播放开关；开启后需要点击推进的节拍会降级为按时长自动推进。</summary>
        public bool AutoPlay { get; set; }

        /// <summary>获取或设置按文本长度估算阅读时长时每个字符占用的秒数。</summary>
        public float ReadingSecondsPerChar { get; set; } = 0.09f;

        /// <summary>获取或设置按文本长度估算阅读时长时的最小秒数。</summary>
        public float MinReadingSeconds { get; set; } = 1.2f;

        /// <summary>获取或设置按文本长度估算阅读时长时的最大秒数。</summary>
        public float MaxReadingSeconds { get; set; } = 12f;

        /// <summary>获取已选择过的一次性选项路径集合。</summary>
        public IReadOnlyCollection<string> ChosenOptions => chosenOptions;

        /// <summary>由 StoryRunner 写入的节拍进入回调，用于向存档层报告续演位置。</summary>
        internal Action<StoryPath> BeatEnteredCallback { get; set; }

        /// <summary>获取当前是否存在尚未被最近的 Seq 消费的提前结束请求。</summary>
        internal bool BreakRequested { get; private set; }

        /// <summary>
        /// 获取断点续演的目标节拍路径；非空表示正在快进到该节拍。
        /// 快进过程中，位于目标之前的节点只落终态、不产生表现。
        /// </summary>
        public StoryPath ResumeTarget { get; private set; }

        /// <summary>获取当前是否处于快进到续演点的过程中。</summary>
        public bool IsResuming => !ResumeTarget.IsEmpty;

        /// <summary>设置断点续演目标；由 StoryRunner 在开始演绎前调用。</summary>
        internal void SetResumeTarget(StoryPath target)
        {
            ResumeTarget = target;
        }

        /// <summary>判断续演点是否位于指定节点的子树中；该查询不改变快进状态。</summary>
        public bool ContainsResumeTarget(IStoryAction action)
        {
            return IsResuming && action != null && action.Path.IsAncestorOfOrSame(ResumeTarget);
        }

        /// <summary>
        /// 判断快进过程中应当如何处理一个子节点。
        /// 这是断点续演的核心决策：目标节点本身开始正常演绎，其祖先向下递归，其余节点一律落终态。
        /// </summary>
        /// <param name="child">待处理的子节点。</param>
        /// <returns>对该子节点应采取的处理方式。</returns>
        internal StoryResumeDecision DecideResume(IStoryAction child)
        {
            if (!IsResuming) return StoryResumeDecision.Play;
            if (child.Path.Equals(ResumeTarget))
            {
                // 已经到达续演点：清除标记，从这里开始恢复正常演绎。
                ResumeTarget = StoryPath.None;
                return StoryResumeDecision.Play;
            }
            if (child.Path.IsAncestorOfOrSame(ResumeTarget)) return StoryResumeDecision.Descend;
            return StoryResumeDecision.Settle;
        }

        /// <summary>按条件语义求值一段表达式。</summary>
        /// <param name="expression">条件文本；空文本视为恒真，便于把「无条件」写成空配置。</param>
        public bool Evaluate(string expression)
        {
            if (string.IsNullOrWhiteSpace(expression)) return true;
            return StoryExpression.Compile(expression).EvaluateBool(Variables, Functions);
        }

        /// <summary>求值一段表达式并返回原始值。</summary>
        public StoryValue EvaluateValue(string expression)
        {
            if (string.IsNullOrWhiteSpace(expression)) return StoryValue.None;
            return StoryExpression.Compile(expression).Evaluate(Variables, Functions);
        }

        /// <summary>解析一个文本键为当前语言下的文案。</summary>
        public string Resolve(TextKey key)
        {
            return Text.Get(key);
        }

        /// <summary>按文本长度估算一段文案的阅读时长。</summary>
        public float EstimateReadingSeconds(string content)
        {
            int length = string.IsNullOrEmpty(content) ? 0 : content.Length;
            float seconds = length * ReadingSecondsPerChar;
            if (seconds < MinReadingSeconds) seconds = MinReadingSeconds;
            if (seconds > MaxReadingSeconds) seconds = MaxReadingSeconds;
            return seconds;
        }

        /// <summary>记录一个一次性选项已经被选择。</summary>
        public void MarkOptionChosen(StoryPath optionPath)
        {
            if (!optionPath.IsEmpty) chosenOptions.Add(optionPath.Value);
        }

        /// <summary>判断一个一次性选项是否已经被选择过。</summary>
        public bool IsOptionChosen(StoryPath optionPath)
        {
            return !optionPath.IsEmpty && chosenOptions.Contains(optionPath.Value);
        }

        /// <summary>用快照覆盖已选择的一次性选项集合。</summary>
        public void RestoreChosenOptions(IEnumerable<string> paths)
        {
            chosenOptions.Clear();
            if (paths == null) return;
            foreach (string path in paths)
            {
                if (!string.IsNullOrWhiteSpace(path)) chosenOptions.Add(path);
            }
        }

        /// <summary>记录一个选项节点上玩家做出的选择结果。</summary>
        /// <param name="choicePath">选项节点的稳定路径。</param>
        /// <param name="optionIndex">被选中选项在原始列表中的下标。</param>
        public void RecordChoice(StoryPath choicePath, int optionIndex)
        {
            if (choicePath.IsEmpty) return;
            choiceResults[choicePath.Value] = optionIndex;
        }

        /// <summary>读取一个选项节点上已经产生的选择结果。</summary>
        public bool TryGetChoice(StoryPath choicePath, out int optionIndex)
        {
            if (!choicePath.IsEmpty) return choiceResults.TryGetValue(choicePath.Value, out optionIndex);
            optionIndex = -1;
            return false;
        }

        /// <summary>导出全部选择结果的副本，供存档序列化使用。</summary>
        public IReadOnlyDictionary<string, int> CaptureChoices()
        {
            return new Dictionary<string, int>(choiceResults, StringComparer.Ordinal);
        }

        /// <summary>用快照覆盖全部选择结果。</summary>
        public void RestoreChoices(IReadOnlyDictionary<string, int> snapshot)
        {
            choiceResults.Clear();
            if (snapshot == null) return;
            foreach (KeyValuePair<string, int> pair in snapshot) choiceResults[pair.Key] = pair.Value;
        }

        /// <summary>报告进入一个新节拍。</summary>
        internal void ReportBeatEntered(StoryPath path)
        {
            BeatEnteredCallback?.Invoke(path);
        }

        /// <summary>切换本次演绎的模式；由 StoryRunner 调用。</summary>
        internal void SetMode(StoryPlayMode mode)
        {
            Mode = mode;
        }

        /// <summary>请求结束最近的一层顺序组合子。</summary>
        internal void RequestBreak()
        {
            BreakRequested = true;
        }

        /// <summary>消费一次提前结束请求；返回是否确实存在待处理请求。</summary>
        internal bool ConsumeBreak()
        {
            if (!BreakRequested) return false;
            BreakRequested = false;
            return true;
        }
    }
}
