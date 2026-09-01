using Cysharp.Threading.Tasks;
using SuperScrollView;
using UnityEngine;
using Xuan.Prometheus.Component;

namespace Xuan.Prometheus
{
    /// <summary>背包面板：打开时请求背包数据，并通过 BagSystem 的修订号响应式刷新物品网格。</summary>
    [UIPanelConfig("BagPanel", UIPanelLayer.Normal, UIPanelClosePolicy.Destroy)]
    public sealed class BagPanel : BagPanelBase
    {
        private BagSystem bagSystem;
        private ListenHandle listenHandle;

        /// <summary>背包网格每行显示的物品格数（固定列数，避免列数为 0 触发除零异常）。</summary>
        private const int BagColumnCount = 4;

        /// <summary>首次创建时初始化物品网格（只执行一次）。</summary>
        protected override void OnInitialize()
        {
            // 预制体未配置固定行列数，LoopGridView 会以 0 列计算行数导致 DivideByZeroException，这里显式传入固定列数。
            LoopGridViewSettingParam setting = new LoopGridViewSettingParam { mGridFixedType = GridFixedType.ColumnCountFixed, mFixedRowOrColumnCount = BagColumnCount };
            BagGrid.InitGridView(0, OnGetBagItemByRowColumn, setting);
        }

        /// <summary>每次进入显示状态时：监听背包修订号并请求服务器刷新背包数据。</summary>
        protected override void OnOpen()
        {
            if (!Core.Gameplay.TryGetSystem(out bagSystem)) throw new System.InvalidOperationException($"{nameof(BagPanel)} requires {nameof(BagSystem)}.");
            listenHandle = bagSystem.RevisionProperty.Listen(OnItemsChanged);
            bagSystem.RequestItemsAsync().Forget();
        }

        /// <summary>面板关闭时释放监听，避免缓存面板与旧数据互相持有。</summary>
        protected override void OnClose()
        {
            listenHandle?.Dispose();
            listenHandle = null;
            bagSystem = null;
        }

        /// <summary>响应 CloseBtn 点击：关闭本面板。</summary>
        protected override void OnCloseBtnClick()
        {
            Debug.Log("CloseBag");
            Close();
        }

        /// <summary>背包数据变化时刷新网格项数量。</summary>
        private void OnItemsChanged()
        {
            BagGrid.SetListItemCount(bagSystem.Items.Count, false);
            BagGrid.RefreshAllShownItem();
        }

        /// <summary>按行列索引返回背包格子项，并把物品信息写入 ItemMono。</summary>
        private LoopGridViewItem OnGetBagItemByRowColumn(LoopGridView gridView, int itemIndex, int row, int column)
        {
            if (itemIndex < 0 || itemIndex >= bagSystem.Items.Count) return null;
            LoopGridViewItem item = gridView.NewListViewItem("ItemCard");
            if (item == null) throw new System.InvalidOperationException("BagPanel BagGrid requires an item prefab named 'ItemCard'.");
            ItemMono mono = item.GetComponent<ItemMono>();
            if (mono == null) throw new System.InvalidOperationException("BagPanel BagGrid item prefab requires ItemMono.");
            mono.Apply(bagSystem.Items[itemIndex]);
            return item;
        }
    }
}
