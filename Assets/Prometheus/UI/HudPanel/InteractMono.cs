using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Xuan.Prometheus
{
    /// <summary>交互栏列表项：显示交互物文案，点击时触发对应交互。</summary>
    public class InteractMono : MonoBehaviour
    {
        public Button interactBtn;
        public TextMeshProUGUI text;

        private Action<IInteractable> onClick;

        /// <summary>把交互物写入列表项，并绑定点击回调；不在 UI 内保存或修改玩法状态。</summary>
        public void Apply(IInteractable interactable, Action<IInteractable> onClick)
        {
            if (interactable == null) throw new ArgumentNullException(nameof(interactable));
            if (interactBtn == null || text == null) throw new InvalidOperationException($"{nameof(InteractMono)} on '{name}' requires interactBtn and text references.");
            this.onClick = onClick;
            text.text = interactable.InteractLabel;
            interactBtn.onClick.RemoveAllListeners();
            interactBtn.onClick.AddListener(() => this.onClick?.Invoke(interactable));
        }
    }
}
