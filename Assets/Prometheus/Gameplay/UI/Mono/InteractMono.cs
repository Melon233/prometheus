using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using Xuan.Prometheus.World;

namespace Xuan.Prometheus
{
    /// <summary>交互栏列表项：显示交互物类型，点击时触发对应交互。</summary>
    public class InteractMono : MonoBehaviour
    {
        public Button interactBtn;
        public TextMeshProUGUI text;

        private Action<PoiConfig> onClick;

        /// <summary>把交互物配置写入列表项，并绑定点击回调；不在 UI 内保存或修改玩法状态。</summary>
        public void Apply(PoiConfig config, Action<PoiConfig> onClick)
        {
            if (config == null) throw new ArgumentNullException(nameof(config));
            if (interactBtn == null || text == null) throw new InvalidOperationException($"{nameof(InteractMono)} on '{name}' requires interactBtn and text references.");
            this.onClick = onClick;
            text.text = config.PoiType.ToString();
            interactBtn.onClick.RemoveAllListeners();
            interactBtn.onClick.AddListener(() => this.onClick?.Invoke(config));
        }
    }
}
