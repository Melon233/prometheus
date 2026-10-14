using System;
using System.Collections.Generic;
using UnityEngine;
using Xuan.Prometheus.Cutscene;

namespace Xuan.Prometheus
{
    /// <summary>
    /// 对话面板：显示演出当前台词与选项，并把点击与选择提交给演出系统。
    /// 面板不保存对话状态，只按 <see cref="ICutsceneSystem.CurrentLine"/> 渲染；
    /// 打开与关闭由 CutsceneUISystem 跟随演出开始与结束管理。
    /// 非对话步骤没有台词，此时隐藏对话框只保留全屏点击层（点击会被演出系统忽略）。
    /// </summary>
    [UIPanelConfig("DialoguePanel", UIPanelLayer.Popup, UIPanelClosePolicy.Cache)]
    public sealed class DialoguePanel : DialoguePanelBase
    {
        /// <summary>当前台词生成的选项项，刷新时整体销毁重建。</summary>
        private readonly List<ChoiceOptionMono> choiceItems = new List<ChoiceOptionMono>();

        /// <summary>选项点击回调；保存为字段，避免每次刷新都分配新委托。</summary>
        private Action<int> onChoose;

        /// <summary>打开期间使用的演出系统。</summary>
        private ICutsceneSystem cutsceneSystem;

        /// <summary>选项模板只作为克隆源，初始化时隐藏。</summary>
        protected override void OnInitialize()
        {
            ChoiceTemplate.gameObject.SetActive(false);
            onChoose = OnChoose;
        }

        /// <summary>打开时解析演出系统、订阅台词变化，并立即按当前台词渲染一次。</summary>
        protected override void OnOpen()
        {
            if (!Core.Gameplay.TryGetSystem(out cutsceneSystem)) throw new InvalidOperationException($"{nameof(DialoguePanel)} requires {nameof(ICutsceneSystem)}.");
            Core.Event.AddListener<CutsceneLineChangedEvent>(OnLineChanged);
            Render(cutsceneSystem.CurrentLine);
        }

        /// <summary>关闭时退订并清空选项。</summary>
        protected override void OnClose()
        {
            Core.Event.RemoveListener<CutsceneLineChangedEvent>(OnLineChanged);
            ClearChoices();
            cutsceneSystem = null;
        }

        /// <summary>点击推进；没有台词或台词带选项时由演出系统忽略。</summary>
        protected override void OnAdvanceBtnClick()
        {
            cutsceneSystem.Advance();
        }

        /// <summary>台词变化：直接用事件携带的新台词重绘。</summary>
        private void OnLineChanged(CutsceneLineChangedEvent eventData) => Render(eventData.Line);

        /// <summary>按台词渲染说话人、内容与选项；台词为空时隐藏对话框。</summary>
        /// <param name="line">当前台词，可为空。</param>
        private void Render(CutsceneLine line)
        {
            // 对话框背景是两段文本的共同父节点，生成绑定里没有单独暴露它。
            LineText.transform.parent.gameObject.SetActive(line != null);
            ClearChoices();
            if (line == null) return;
            SpeakerText.text = line.Speaker;
            LineText.text = line.Text;
            for (int index = 0; index < line.Options.Count; index++)
            {
                ChoiceOptionMono item = UnityEngine.Object.Instantiate(ChoiceTemplate, ChoiceRoot);
                item.gameObject.SetActive(true);
                item.Apply(index, line.Options[index], onChoose);
                choiceItems.Add(item);
            }
        }

        /// <summary>选择一个选项；选项不影响流程，演出系统收到后进入下一步。</summary>
        private void OnChoose(int optionIndex) => cutsceneSystem.Choose(optionIndex);

        /// <summary>销毁当前全部选项项。</summary>
        private void ClearChoices()
        {
            for (int index = 0; index < choiceItems.Count; index++) UnityEngine.Object.Destroy(choiceItems[index].gameObject);
            choiceItems.Clear();
        }
    }
}
