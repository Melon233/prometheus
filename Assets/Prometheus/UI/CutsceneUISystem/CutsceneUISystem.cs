using Cysharp.Threading.Tasks;
using Xuan.Prometheus.Cutscene;

namespace Xuan.Prometheus
{
    /// <summary>
    /// 跟随演出开始与结束切换界面：开始时若 HUD 打开则关闭它并记下，随后打开对话面板；
    /// 结束时关闭对话面板，并只在开始前 HUD 确实打开过时重新打开（不显示 HUD 的世界不会因演出而多出 HUD）。
    /// </summary>
    internal sealed class CutsceneUISystem : ICutsceneUISystem
    {
        /// <summary>演出开始时 HUD 是否处于打开状态。</summary>
        private bool hudWasOpen;

        /// <inheritdoc />
        public UniTask AfterNewAsync() => UniTask.CompletedTask;

        /// <summary>订阅演出开始与结束。</summary>
        public void AfterNew()
        {
            Core.Event.AddListener<CutsceneStartedEvent>(OnCutsceneStarted);
            Core.Event.AddListener<CutsceneFinishedEvent>(OnCutsceneFinished);
        }

        /// <summary>本系统不需要逐帧驱动。</summary>
        /// <param name="dt">帧时间（秒）。</param>
        public void OnUpdate(float dt)
        {
        }

        /// <summary>对称退订。面板随 UIKit 释放，这里不主动关闭。</summary>
        public void Dispose()
        {
            Core.Event.RemoveListener<CutsceneStartedEvent>(OnCutsceneStarted);
            Core.Event.RemoveListener<CutsceneFinishedEvent>(OnCutsceneFinished);
        }

        /// <summary>演出开始：收起 HUD，打开对话面板。</summary>
        private void OnCutsceneStarted(CutsceneStartedEvent eventData)
        {
            hudWasOpen = Core.UI.IsPanelOpen<HudPanel>();
            if (hudWasOpen) Core.UI.ClosePanel<HudPanel>();
            Core.UI.OpenPanel<DialoguePanel>();
        }

        /// <summary>演出结束：关闭对话面板，按开始前的状态还原 HUD。</summary>
        private void OnCutsceneFinished(CutsceneFinishedEvent eventData)
        {
            Core.UI.ClosePanel<DialoguePanel>();
            if (hudWasOpen) Core.UI.OpenPanel<HudPanel>();
            hudWasOpen = false;
        }
    }
}
