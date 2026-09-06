using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace Xuan.Prometheus.Narrative.Demo
{
    /// <summary>
    /// 演示与测试用的对话视图。
    /// 全部界面在运行时用代码构建，不依赖任何预制体或美术资源，因此可以直接放进任何场景验证剧情流程。
    /// 正式的 DialoguePanel 应走 UIKit 的预制体与代码生成流程，并使用带中文字形的 TMP 字体资产。
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class SimpleDialogueView : MonoBehaviour, IDialogueView
    {
        [Header("打字机")]
        [SerializeField] [Tooltip("每秒显示的字符数。")] private float charactersPerSecond = 34f;

        [Header("字体")]
        [SerializeField] [Tooltip("按优先级尝试的系统字体名；留空则使用 NarrativeUiFactory 的默认列表。")]
        private string[] preferredFonts;

        /// <summary>保存当前展示的选项按钮。</summary>
        private readonly List<Button> choiceButtons = new List<Button>();

        private GameObject dialogueRoot;
        private GameObject choicePanel;
        private Text speakerText;
        private Text contentText;
        private Text hintText;
        private Font font;

        /// <summary>标记收到过确认输入且尚未被消费。</summary>
        private bool confirmSignal;

        /// <summary>记录当前有多少个异步等待正在消费确认输入；为零时确认输入会在帧末被丢弃。</summary>
        private int waiterCount;

        /// <summary>保存玩家已经点下但尚未被读取的选项下标。</summary>
        private int pendingChoice = -1;

        /// <inheritdoc />
        public bool AutoPlay { get; set; }

        /// <summary>获取当前节拍的完整台词正文，供演示界面与自动化验证读取。</summary>
        public string CurrentContent { get; private set; } = string.Empty;

        /// <summary>构建界面层级并确保场景中存在可用的事件系统。</summary>
        private void Awake()
        {
            font = NarrativeUiFactory.ResolveFont(preferredFonts);
            NarrativeUiFactory.EnsureEventSystem();
            BuildUi();
            Hide();
        }

        /// <summary>采样确认输入；键盘空格、回车与鼠标左键都视为确认。</summary>
        private void Update()
        {
            if (WasConfirmPressed()) confirmSignal = true;
        }

        /// <summary>
        /// 在帧末丢弃无人等待的确认输入。
        /// 这是「阻塞动作播放期间点不穿」的实现要点：没有等待者时的点击不会被后续等待误消费。
        /// </summary>
        private void LateUpdate()
        {
            if (waiterCount == 0) confirmSignal = false;
        }

        /// <inheritdoc />
        public void BeginLine(DialogueLine line)
        {
            dialogueRoot.SetActive(true);
            choicePanel.SetActive(false);
            speakerText.text = line.IsNarration ? string.Empty : line.SpeakerName;
            speakerText.gameObject.SetActive(!line.IsNarration);
            contentText.fontStyle = line.IsNarration ? FontStyle.Italic : FontStyle.Normal;
            contentText.color = line.IsNarration ? new Color(0.78f, 0.82f, 0.9f) : Color.white;
            contentText.text = string.Empty;
            hintText.text = string.Empty;
            CurrentContent = line.Content;
        }

        /// <inheritdoc />
        public async UniTask ShowLineAsync(DialogueLine line, CancellationToken cancellationToken)
        {
            string full = line.Content;
            float accumulated = 0f;
            int shown = 0;
            waiterCount++;
            try
            {
                while (shown < full.Length)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    // 二段点击的第一段：打字机进行中的确认输入立即补全全文，而不是推进节拍。
                    if (ConsumeConfirm()) break;
                    accumulated += Time.deltaTime * Mathf.Max(1f, charactersPerSecond);
                    int target = Mathf.Min(full.Length, Mathf.FloorToInt(accumulated));
                    if (target != shown)
                    {
                        shown = target;
                        contentText.text = full.Substring(0, shown);
                    }
                    await UniTask.Yield(PlayerLoopTiming.Update, cancellationToken);
                }
            }
            finally
            {
                waiterCount--;
            }
            contentText.text = full;
        }

        /// <inheritdoc />
        public async UniTask WaitForAdvanceAsync(AdvancePolicy policy, CancellationToken cancellationToken)
        {
            if (policy.Kind == AdvanceKind.Immediate) return;
            hintText.text = policy.Kind == AdvanceKind.Click ? "▼ 点击 / 空格 继续" : "▼ 自动播放中";
            waiterCount++;
            try
            {
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
            finally
            {
                waiterCount--;
                hintText.text = string.Empty;
            }
        }

        /// <inheritdoc />
        public void EndLine()
        {
            hintText.text = string.Empty;
        }

        /// <inheritdoc />
        public async UniTask<int> ShowChoicesAsync(DialogueChoiceRequest request, CancellationToken cancellationToken)
        {
            BuildChoiceButtons(request);
            pendingChoice = -1;
            choicePanel.SetActive(true);
            hintText.text = "请选择";
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
                choicePanel.SetActive(false);
                hintText.text = string.Empty;
                // 选项按钮消费掉的那次点击不应残留成为下一节拍的推进输入。
                confirmSignal = false;
            }
        }

        /// <inheritdoc />
        public void Hide()
        {
            if (dialogueRoot != null) dialogueRoot.SetActive(false);
            if (choicePanel != null) choicePanel.SetActive(false);
            confirmSignal = false;
            pendingChoice = -1;
        }

        /// <summary>读取并清除一次确认输入。</summary>
        private bool ConsumeConfirm()
        {
            if (!confirmSignal) return false;
            confirmSignal = false;
            return true;
        }

        /// <summary>采样本帧是否按下了确认键或鼠标左键。</summary>
        private static bool WasConfirmPressed()
        {
            Keyboard keyboard = Keyboard.current;
            if (keyboard != null && (keyboard.spaceKey.wasPressedThisFrame || keyboard.enterKey.wasPressedThisFrame)) return true;
            Mouse mouse = Mouse.current;
            return mouse != null && mouse.leftButton.wasPressedThisFrame;
        }

        /// <summary>按当前请求重建选项按钮。</summary>
        private void BuildChoiceButtons(DialogueChoiceRequest request)
        {
            for (int index = 0; index < choiceButtons.Count; index++)
            {
                if (choiceButtons[index] != null) Destroy(choiceButtons[index].gameObject);
            }
            choiceButtons.Clear();
            for (int index = 0; index < request.Choices.Count; index++)
            {
                DialogueChoice choice = request.Choices[index];
                int optionIndex = choice.OptionIndex;
                string label = choice.IsLocked && !string.IsNullOrEmpty(choice.LockedReason) ? $"{choice.Content}（{choice.LockedReason}）" : choice.Content;
                Color background = choice.IsLocked ? new Color(0.16f, 0.16f, 0.18f, 0.85f) : new Color(0.10f, 0.14f, 0.24f, 0.94f);
                Button button = NarrativeUiFactory.CreateButton(choicePanel.transform, font, label, background, () => pendingChoice = optionIndex);
                button.interactable = !choice.IsLocked;
                button.GetComponentInChildren<Text>().color = choice.IsLocked ? new Color(0.55f, 0.55f, 0.58f) : Color.white;
                LayoutElement layout = button.gameObject.AddComponent<LayoutElement>();
                layout.minHeight = 48f;
                choiceButtons.Add(button);
            }
        }

        /// <summary>构建对话框与选项容器的完整界面层级。</summary>
        private void BuildUi()
        {
            Canvas canvas = NarrativeUiFactory.CreateCanvas("NarrativeDialogueCanvas", transform, 500);

            dialogueRoot = NarrativeUiFactory.CreateUiObject("DialogueRoot", canvas.transform);
            RectTransform dialogueRect = (RectTransform)dialogueRoot.transform;
            dialogueRect.anchorMin = new Vector2(0.5f, 0f);
            dialogueRect.anchorMax = new Vector2(0.5f, 0f);
            dialogueRect.pivot = new Vector2(0.5f, 0f);
            dialogueRect.sizeDelta = new Vector2(1520f, 290f);
            dialogueRect.anchoredPosition = new Vector2(0f, 56f);
            Image dialogueBackground = dialogueRoot.AddComponent<Image>();
            dialogueBackground.color = new Color(0.04f, 0.05f, 0.09f, 0.88f);

            speakerText = NarrativeUiFactory.CreateText(dialogueRect, font, 34, TextAnchor.UpperLeft);
            speakerText.color = new Color(1f, 0.85f, 0.45f);
            speakerText.fontStyle = FontStyle.Bold;
            RectTransform speakerRect = speakerText.rectTransform;
            speakerRect.anchorMin = new Vector2(0f, 1f);
            speakerRect.anchorMax = new Vector2(1f, 1f);
            speakerRect.pivot = new Vector2(0.5f, 1f);
            speakerRect.offsetMin = new Vector2(44f, -72f);
            speakerRect.offsetMax = new Vector2(-44f, -18f);

            contentText = NarrativeUiFactory.CreateText(dialogueRect, font, 28, TextAnchor.UpperLeft);
            RectTransform contentRect = contentText.rectTransform;
            contentRect.anchorMin = Vector2.zero;
            contentRect.anchorMax = Vector2.one;
            contentRect.offsetMin = new Vector2(44f, 52f);
            contentRect.offsetMax = new Vector2(-44f, -78f);

            hintText = NarrativeUiFactory.CreateText(dialogueRect, font, 22, TextAnchor.LowerRight);
            hintText.color = new Color(0.7f, 0.75f, 0.85f);
            RectTransform hintRect = hintText.rectTransform;
            hintRect.anchorMin = Vector2.zero;
            hintRect.anchorMax = new Vector2(1f, 0f);
            hintRect.pivot = new Vector2(0.5f, 0f);
            hintRect.offsetMin = new Vector2(44f, 12f);
            hintRect.offsetMax = new Vector2(-44f, 44f);

            choicePanel = NarrativeUiFactory.CreateUiObject("ChoicePanel", canvas.transform);
            RectTransform choiceRect = (RectTransform)choicePanel.transform;
            choiceRect.anchorMin = new Vector2(0.5f, 0.5f);
            choiceRect.anchorMax = new Vector2(0.5f, 0.5f);
            choiceRect.pivot = new Vector2(0.5f, 0.5f);
            choiceRect.sizeDelta = new Vector2(920f, 10f);
            choiceRect.anchoredPosition = new Vector2(0f, 30f);
            VerticalLayoutGroup layoutGroup = choicePanel.AddComponent<VerticalLayoutGroup>();
            layoutGroup.spacing = 14f;
            layoutGroup.childControlHeight = true;
            layoutGroup.childControlWidth = true;
            layoutGroup.childForceExpandHeight = false;
            ContentSizeFitter fitter = choicePanel.AddComponent<ContentSizeFitter>();
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
        }
    }
}
