using System;
using UnityEngine;
using UnityEngine.UI;

namespace Xuan.Prometheus
{
    /// <summary>
    /// 对话选项列表项：显示一个选项的文案，点击时回传它在原始选项列表中的下标。
    ///
    /// 锁定选项按原神的做法保持可见但不可点，并把锁定原因附在文案后，
    /// 使玩家知道「这里有一条我暂时够不到的分支」而不是看到一个空列表。
    /// </summary>
    public class ChoiceOptionMono : MonoBehaviour
    {
        public Button optionBtn;
        public Text text;

        /// <summary>保存本项当前代表的原始选项下标，避免回调闭包捕获列表状态。</summary>
        private int optionIndex;

        private Action<int> onClick;

        /// <summary>把一个选项写入列表项并绑定点击回调；不在 UI 内保存或修改任何剧情状态。</summary>
        /// <param name="choice">已完成本地化解析的选项。</param>
        /// <param name="onClick">选中时回传原始选项下标的回调。</param>
        public void Apply(Narrative.DialogueChoice choice, Action<int> onClick)
        {
            if (optionBtn == null || text == null) throw new InvalidOperationException($"{nameof(ChoiceOptionMono)} on '{name}' requires optionBtn and text references.");
            this.onClick = onClick;
            optionIndex = choice.OptionIndex;
            text.text = choice.IsLocked && !string.IsNullOrEmpty(choice.LockedReason) ? $"{choice.Content}（{choice.LockedReason}）" : choice.Content;
            optionBtn.interactable = !choice.IsLocked;
            optionBtn.onClick.RemoveAllListeners();
            optionBtn.onClick.AddListener(OnClick);
        }

        /// <summary>设置选项文案使用的字体；工程内的 TMP 字体不含中文字形，正文一律走动态系统字体。</summary>
        /// <param name="font">已解析的动态字体。</param>
        public void ApplyFont(Font font)
        {
            if (text != null && font != null) text.font = font;
        }

        /// <summary>回传当前项代表的原始选项下标。</summary>
        private void OnClick()
        {
            onClick?.Invoke(optionIndex);
        }
    }
}
