using System;
using UnityEngine;
using UnityEngine.UI;

namespace Xuan.Prometheus
{
    /// <summary>
    /// 对话选项列表项：显示一个选项的文案，点击时回传它在当前台词选项列表中的下标。
    /// 对话选项没有出现条件，玩家可以任意选择，因此列表项没有锁定状态。
    /// </summary>
    public class ChoiceOptionMono : MonoBehaviour
    {
        public Button optionBtn;
        public Text text;

        /// <summary>保存本项当前代表的选项下标，避免回调闭包捕获列表状态。</summary>
        private int optionIndex;

        /// <summary>选中时回传选项下标的回调。</summary>
        private Action<int> onClick;

        /// <summary>把一个选项写入列表项并绑定点击回调；不在 UI 内保存或修改任何对话状态。</summary>
        /// <param name="index">选项在当前台词选项列表中的下标。</param>
        /// <param name="content">选项显示文案。</param>
        /// <param name="onClick">选中时回传选项下标的回调。</param>
        public void Apply(int index, string content, Action<int> onClick)
        {
            if (optionBtn == null || text == null) throw new InvalidOperationException($"{nameof(ChoiceOptionMono)} on '{name}' requires optionBtn and text references.");
            this.onClick = onClick;
            optionIndex = index;
            text.text = content;
            optionBtn.onClick.RemoveAllListeners();
            optionBtn.onClick.AddListener(OnClick);
        }

        /// <summary>设置选项文案使用的字体。</summary>
        /// <param name="font">要使用的字体。</param>
        public void ApplyFont(Font font)
        {
            if (text != null && font != null) text.font = font;
        }

        /// <summary>回传当前项代表的选项下标。</summary>
        private void OnClick()
        {
            onClick?.Invoke(optionIndex);
        }
    }
}
