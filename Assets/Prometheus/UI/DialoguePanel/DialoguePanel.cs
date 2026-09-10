using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;
using Xuan.Prometheus.Narrative;

namespace Xuan.Prometheus
{
    /// <summary>
    /// 正式对话面板：实现剧情系统的 <see cref="IDialogueView"/>，负责打字机、二段点击与选项交互。
    ///
    /// 与演示用的 <c>SimpleDialogueView</c> 的区别只在构造方式——本面板走 UIKit 的预制体与
    /// 代码生成流程，后者在运行时用代码搭界面。两者共享同一个契约，因此剧情逻辑对它们无差别。
    ///
    /// 正文使用 uGUI 的 <see cref="UnityEngine.UI.Text"/> 而不是 TMP：工程内唯一的 TMP 字体资产
    /// LiberationSans 不含中文字形，中文台词会整片显示为方块。字体在 <see cref="OnInitialize"/> 里
    /// 用 <see cref="NarrativeUiFactory.ResolveFont"/> 解析成系统动态字体，等有了带中文字形的 TMP
    /// 字体资产再整体换回 TMP。
    /// </summary>
    [UIPanelConfig("DialoguePanel", UIPanelLayer.Popup, UIPanelClosePolicy.Cache)]
    public sealed class DialoguePanel : DialoguePanelBase, IDialogueView
    {
        /// <summary>打字机每秒显示的字符数。</summary>
        private const float CharactersPerSecond = 34f;

        /// <summary>保存当前展示的选项列表项，供下一次选择前整体回收。</summary>
        private readonly List<ChoiceOptionMono> choiceItems = new List<ChoiceOptionMono>();

        /// <summary>标记收到过确认点击且尚未被消费。</summary>
        private bool confirmSignal;

        /// <summary>保存玩家已经点下但尚未被读取的选项下标；负值表示尚未选择。</summary>
        private int pendingChoice = -1;

        /// <summary>保存解析出的中文动态字体。</summary>
        private Font font;

        /// <inheritdoc />
        public bool AutoPlay { get; set; }

        /// <summary>解析字体并把它写入静态文本；面板缓存复用时不需要重复解析。</summary>
        protected override void OnInitialize()
        {
            font = NarrativeUiFactory.ResolveFont();
            if (font != null)
            {
                SpeakerText.font = font;
                LineText.font = font;
            }
            ChoiceTemplate.gameObject.SetActive(false);
        }

        /// <summary>面板关闭时清空残留的选项与输入信号，避免下次打开继承上一段剧情的状态。</summary>
        protected override void OnClose()
        {
            ClearChoices();
            confirmSignal = false;
            pendingChoice = -1;
        }

        /// <inheritdoc />
        public void BeginLine(DialogueLine line)
        {
            Root.SetActive(true);
            ChoiceRoot.gameObject.SetActive(false);
            SpeakerText.text = line.IsNarration ? string.Empty : line.SpeakerName;
            SpeakerText.gameObject.SetActive(!line.IsNarration);
            LineText.fontStyle = line.IsNarration ? FontStyle.Italic : FontStyle.Normal;
            LineText.color = line.IsNarration ? new Color(0.78f, 0.82f, 0.9f) : Color.white;
            LineText.text = string.Empty;
        }

        /// <inheritdoc />
        public async UniTask ShowLineAsync(DialogueLine line, CancellationToken cancellationToken)
        {
            // 打字机开始前丢弃早到的点击：它属于上一个节拍，不应把这一句直接跳完。
            confirmSignal = false;
            string full = line.Content;
            float accumulated = 0f;
            int shown = 0;
            while (shown < full.Length)
            {
                cancellationToken.ThrowIfCancellationRequested();
                // 二段点击的第一段：打字过程中的确认输入立即补全全文，而不是推进节拍。
                if (ConsumeConfirm()) break;
                accumulated += Time.deltaTime * CharactersPerSecond;
                int target = Mathf.Min(full.Length, Mathf.FloorToInt(accumulated));
                if (target != shown)
                {
                    shown = target;
                    LineText.text = full.Substring(0, shown);
                }
                await UniTask.Yield(PlayerLoopTiming.Update, cancellationToken);
            }
            LineText.text = full;
        }

        /// <inheritdoc />
        public async UniTask WaitForAdvanceAsync(AdvancePolicy policy, CancellationToken cancellationToken)
        {
            if (policy.Kind == AdvanceKind.Immediate) return;
            // 契约要求丢弃本方法被调用之前收到的确认输入，这正是「先播完阻塞动作再允许推进」不被点穿的保证。
            confirmSignal = false;
            if (policy.Kind == AdvanceKind.Click)
            {
                while (!ConsumeConfirm())
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    await UniTask.Yield(PlayerLoopTiming.Update, cancellationToken);
                }
                return;
            }
            // 自动推进期间仍然允许玩家点击提前推进。
            float remaining = Mathf.Max(0f, policy.Seconds);
            while (remaining > 0f && !ConsumeConfirm())
            {
                cancellationToken.ThrowIfCancellationRequested();
                await UniTask.Yield(PlayerLoopTiming.Update, cancellationToken);
                remaining -= Time.deltaTime;
            }
        }

        /// <inheritdoc />
        public void EndLine()
        {
        }

        /// <inheritdoc />
        public async UniTask<int> ShowChoicesAsync(DialogueChoiceRequest request, CancellationToken cancellationToken)
        {
            Root.SetActive(true);
            BuildChoices(request);
            pendingChoice = -1;
            ChoiceRoot.gameObject.SetActive(true);
            try
            {
                while (pendingChoice < 0)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    await UniTask.Yield(PlayerLoopTiming.Update, cancellationToken);
                }
                return pendingChoice;
            }
            finally
            {
                ChoiceRoot.gameObject.SetActive(false);
                ClearChoices();
                // 选中选项的那次点击不应残留成为下一节拍的推进输入。
                confirmSignal = false;
            }
        }

        /// <inheritdoc />
        public void Hide()
        {
            ClearChoices();
            confirmSignal = false;
            pendingChoice = -1;
            if (Root != null) Root.SetActive(false);
        }

        /// <summary>响应全屏推进热区的点击，记录一次尚未被消费的确认输入。</summary>
        protected override void OnAdvanceBtnClick()
        {
            confirmSignal = true;
        }

        /// <summary>读取并清除一次确认输入。</summary>
        private bool ConsumeConfirm()
        {
            if (!confirmSignal) return false;
            confirmSignal = false;
            return true;
        }

        /// <summary>按当前请求克隆模板重建选项列表项。</summary>
        private void BuildChoices(DialogueChoiceRequest request)
        {
            ClearChoices();
            for (int index = 0; index < request.Choices.Count; index++)
            {
                ChoiceOptionMono item = UnityEngine.Object.Instantiate(ChoiceTemplate, ChoiceRoot);
                item.gameObject.SetActive(true);
                item.ApplyFont(font);
                item.Apply(request.Choices[index], OnChoiceSelected);
                choiceItems.Add(item);
            }
        }

        /// <summary>记录玩家选中的原始选项下标，由等待中的 ShowChoicesAsync 读取。</summary>
        private void OnChoiceSelected(int optionIndex)
        {
            pendingChoice = optionIndex;
        }

        /// <summary>销毁全部已生成的选项列表项；模板本身始终保留。</summary>
        private void ClearChoices()
        {
            for (int index = 0; index < choiceItems.Count; index++)
            {
                if (choiceItems[index] != null) UnityEngine.Object.Destroy(choiceItems[index].gameObject);
            }
            choiceItems.Clear();
        }
    }
}
