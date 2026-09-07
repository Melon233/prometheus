using TMPro;
using UnityEngine;
using UnityEngine.UI;
using Xuan.Prometheus.Protocol;

namespace Xuan.Prometheus
{
    /// <summary>背包格子项：显示物品图标、品质、名称与数量。</summary>
    public class ItemMono : MonoBehaviour
    {
        public Image icon;
        public Image quality;
        public TextMeshProUGUI itemName;
        public TextMeshProUGUI quantity;

        /// <summary>把物品信息写入格子项；图标/品质需要美术资源暂不赋值，名称与数量写入文本。</summary>
        public void Apply(Item item)
        {
            if (item == null) return;
            if (itemName != null) itemName.text = $"{item.ItemId} x{item.Quantity}";
            if (quantity != null) quantity.text = item.Quantity.ToString();
        }
    }
}
