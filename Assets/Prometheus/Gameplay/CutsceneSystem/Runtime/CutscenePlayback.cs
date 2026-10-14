using System.Collections.Generic;

namespace Xuan.Prometheus.Cutscene
{
    /// <summary>
    /// 一次演出播放的步骤推进器：只负责「何时开始哪个 Action、何时进入下一步」，不认识任何 System。
    ///
    /// 推进规则：
    /// <list type="bullet">
    /// <item>进入步骤时开始该步全部 Action；同通道上仍在运行的旧 Action 先被 Finish。</item>
    /// <item>含对话的步骤等待 <see cref="Advance"/>（无选项）或 <see cref="Choose"/>（有选项）；否则计满时长自动进入下一步。</item>
    /// <item>已开始的持续型 Action 跨步骤继续运行，直到自然结束、被同通道顶替或演出收尾。</item>
    /// <item>跳过：Finish 全部运行中的 Action，再把剩余步骤的 Action 依次落到最终状态。</item>
    /// </list>
    /// </summary>
    internal sealed class CutscenePlayback
    {
        /// <summary>被播放的演出。</summary>
        private readonly CutsceneDefinition definition;

        /// <summary>本次播放上下文。</summary>
        private readonly CutsceneContext context;

        /// <summary>仍在运行的持续型 Action 及其通道（通道可为空）。</summary>
        private readonly List<(string Channel, CutsceneActionRun Run)> running = new List<(string, CutsceneActionRun)>();

        /// <summary>当前步骤下标。</summary>
        private int stepIndex = -1;

        /// <summary>当前步骤已经经过的时间（只对非对话步骤有意义）。</summary>
        private float stepElapsed;

        /// <summary>创建推进器；调用 <see cref="Start"/> 后才开始第一步。</summary>
        /// <param name="definition">要播放的演出。</param>
        /// <param name="context">本次播放上下文。</param>
        public CutscenePlayback(CutsceneDefinition definition, CutsceneContext context)
        {
            this.definition = definition;
            this.context = context;
        }

        /// <summary>全部步骤是否已经走完（或已被跳过）。</summary>
        public bool IsFinished { get; private set; }

        /// <summary>当前步骤的对话；非对话步骤为空。</summary>
        public DialogAction CurrentDialog => IsFinished ? null : definition.Steps[stepIndex].Dialog;

        /// <summary>进入第一步。</summary>
        public void Start() => EnterStep(0);

        /// <summary>推进一帧：驱动运行中的 Action，并在非对话步骤计满时长时进入下一步。</summary>
        /// <param name="dt">帧时间（秒）。</param>
        public void Tick(float dt)
        {
            for (int index = running.Count - 1; index >= 0; index--)
            {
                if (!running[index].Run.Tick(dt)) continue;
                running[index].Run.Finish();
                running.RemoveAt(index);
            }
            if (CurrentDialog != null) return;
            stepElapsed += dt;
            if (stepElapsed >= definition.Steps[stepIndex].Duration) EnterStep(stepIndex + 1);
        }

        /// <summary>对话步骤的点击推进；非对话步骤与带选项的对话步骤忽略点击（点击背景是合法操作）。</summary>
        public void Advance()
        {
            if (CurrentDialog == null || HasPendingOptions) return;
            EnterStep(stepIndex + 1);
        }

        /// <summary>当前台词是否在等待选择。</summary>
        public bool HasPendingOptions { get; private set; }

        /// <summary>选择当前台词的一个选项。选项不影响流程，任何选择都进入下一步。</summary>
        public void Choose()
        {
            HasPendingOptions = false;
            EnterStep(stepIndex + 1);
        }

        /// <summary>跳过剩余部分：运行中的 Action 落位，未开始的步骤只应用最终状态。</summary>
        public void Skip()
        {
            FinishRunning();
            for (int index = stepIndex + 1; index < definition.Steps.Count; index++)
            {
                IReadOnlyList<CutsceneAction> actions = definition.Steps[index].Actions;
                for (int actionIndex = 0; actionIndex < actions.Count; actionIndex++) actions[actionIndex].ApplyFinalState(context);
            }
            IsFinished = true;
        }

        /// <summary>收尾：Finish 全部仍在运行的 Action（停声音、删特效、镜头与移动落位）。</summary>
        public void FinishRunning()
        {
            for (int index = running.Count - 1; index >= 0; index--) running[index].Run.Finish();
            running.Clear();
        }

        /// <summary>进入指定步骤；越过最后一步即标记完成。</summary>
        /// <param name="index">目标步骤下标。</param>
        private void EnterStep(int index)
        {
            stepIndex = index;
            stepElapsed = 0f;
            if (index >= definition.Steps.Count)
            {
                IsFinished = true;
                return;
            }
            CutsceneStep step = definition.Steps[index];
            // 非对话步骤清掉上一句台词，对话面板随之只保留镜头画面。
            if (step.Dialog == null) context.ShowLine(null);
            for (int actionIndex = 0; actionIndex < step.Actions.Count; actionIndex++)
            {
                CutsceneAction action = step.Actions[actionIndex];
                if (action.Channel != null) FinishChannel(action.Channel);
                CutsceneActionRun run = action.Begin(context);
                if (run != null) running.Add((action.Channel, run));
            }
            HasPendingOptions = step.Dialog != null && step.Dialog.HasOptions;
        }

        /// <summary>Finish 同一通道上仍在运行的旧 Action。</summary>
        /// <param name="channel">互斥通道。</param>
        private void FinishChannel(string channel)
        {
            for (int index = running.Count - 1; index >= 0; index--)
            {
                if (running[index].Channel != channel) continue;
                running[index].Run.Finish();
                running.RemoveAt(index);
            }
        }
    }
}
