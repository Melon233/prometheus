using TMPro;
using UnityEngine;
using UnityEngine.UI;
using Xuan.Prometheus.Protocol;

namespace Xuan.Prometheus
{
    /// <summary>
    /// 背包格子项：把物品数据写入格子的各个显示节点。
    /// 该 Prefab 由 UI 原型构建器整体重建，任何序列化字段都会在重建时丢失，
    /// 因此这里在首次使用时按节点名称解析引用，而不是依赖 Inspector 拖拽。
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class BagItemMono : MonoBehaviour
    {
        private Image background;
        private TextMeshProUGUI countLabel;
        private TextMeshProUGUI levelLabel;
        private bool resolved;

        /// <summary>把物品信息写入格子项；图标与品质底色需要美术资源，目前只写文本。</summary>
        /// <param name="item">要显示的背包物品，为空时不做任何修改。</param>
        public void Apply(Item item)
        {
            if (item == null) return;
            Resolve();
            if (countLabel != null) countLabel.text = item.Quantity.ToString();
            if (levelLabel != null) levelLabel.text = item.ItemId;
        }

        /// <summary>设置格子的品质底色。</summary>
        /// <param name="color">品质对应的底色。</param>
        public void SetQualityColor(Color color)
        {
            Resolve();
            if (background != null) background.color = color;
        }

        /// <summary>按节点名称解析一次显示节点引用。</summary>
        private void Resolve()
        {
            if (resolved) return;
            resolved = true;
            background = FindComponent<Image>("Bg");
            countLabel = FindComponent<TextMeshProUGUI>("ItemMarks/ItemCount");
            levelLabel = FindComponent<TextMeshProUGUI>("ItemLevel");
        }

        /// <summary>按相对路径查找子节点上的组件。</summary>
        private TComponent FindComponent<TComponent>(string path) where TComponent : UnityEngine.Component
        {
            Transform child = transform.Find(path);
            return child == null ? null : child.GetComponent<TComponent>();
        }
    }
}
