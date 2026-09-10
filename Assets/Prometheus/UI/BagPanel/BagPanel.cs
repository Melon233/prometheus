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
        private IBagSystem bagSystem;
        private ListenHandle listenHandle;

        /// <summary>背包网格每行显示的物品格数，必须与 Prefab 中 LoopGridView 的固定列数一致。</summary>
        private const int BagColumnCount = 8;

        /// <summary>网格条目 Prefab 名称，由 BagItemProto 生成。</summary>
        private const string BagItemPrefabName = "BagItem";

        /// <summary>首次创建时初始化物品网格（只执行一次）。</summary>
        protected override void OnInitialize()
        {
            // Prefab 已写入固定列数，这里再显式传入一次，避免网格以 0 列计算行数触发除零异常。
            LoopGridViewSettingParam setting = new LoopGridViewSettingParam { mGridFixedType = GridFixedType.ColumnCountFixed, mFixedRowOrColumnCount = BagColumnCount };
            BagGrid.InitGridView(0, OnGetBagItemByRowColumn, setting);
        }

        /// <summary>每次进入显示状态时：监听背包修订号并请求服务器刷新背包数据。</summary>
        protected override void OnOpen()
        {
            if (!Core.Gameplay.TryGetSystem(out bagSystem)) throw new System.InvalidOperationException($"{nameof(BagPanel)} requires {nameof(IBagSystem)}.");
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

        /// <summary>背包数据变化时刷新网格项数量与容量文本。</summary>
        private void OnItemsChanged()
        {
            BagGrid.SetListItemCount(bagSystem.Items.Count, false);
            BagGrid.RefreshAllShownItem();
            CapacityLabel.text = $"{CategoryLabel.text} {bagSystem.Items.Count}/2000";
        }

        /// <summary>按行列索引返回背包格子项，并把物品信息写入 BagItemMono。</summary>
        private LoopGridViewItem OnGetBagItemByRowColumn(LoopGridView gridView, int itemIndex, int row, int column)
        {
            if (itemIndex < 0 || itemIndex >= bagSystem.Items.Count) return null;
            LoopGridViewItem item = gridView.NewListViewItem(BagItemPrefabName);
            if (item == null) throw new System.InvalidOperationException($"BagPanel BagGrid requires an item prefab named '{BagItemPrefabName}'.");
            BagItemMono mono = item.GetComponent<BagItemMono>();
            if (mono == null) throw new System.InvalidOperationException($"BagPanel BagGrid item prefab requires {nameof(BagItemMono)}.");
            mono.Apply(bagSystem.Items[itemIndex]);
            return item;
        }

        /// <summary>响应 CloseBtn 点击：关闭本面板。</summary>
        protected override void OnCloseBtnClick()
        {
            Close();
        }

        /// <summary>响应 PrevCategoryBtn 点击：切换到上一个分类。</summary>
        protected override void OnPrevCategoryBtnClick()
        {
            Debug.Log("[Bag] 分类切换尚未实现。");
        }

        /// <summary>响应 NextCategoryBtn 点击：切换到下一个分类。</summary>
        protected override void OnNextCategoryBtnClick()
        {
            Debug.Log("[Bag] 分类切换尚未实现。");
        }

        /// <summary>响应 TabWeapon 点击：切换到武器分类。</summary>
        protected override void OnTabWeaponClick()
        {
            SelectCategory("武器");
        }

        /// <summary>响应 TabArtifact 点击：切换到圣遗物分类。</summary>
        protected override void OnTabArtifactClick()
        {
            SelectCategory("圣遗物");
        }

        /// <summary>响应 TabMaterial 点击：切换到材料分类。</summary>
        protected override void OnTabMaterialClick()
        {
            SelectCategory("材料");
        }

        /// <summary>响应 TabFood 点击：切换到食物分类。</summary>
        protected override void OnTabFoodClick()
        {
            SelectCategory("食物");
        }

        /// <summary>响应 TabGadget 点击：切换到小道具分类。</summary>
        protected override void OnTabGadgetClick()
        {
            SelectCategory("小道具");
        }

        /// <summary>响应 TabQuest 点击：切换到任务道具分类。</summary>
        protected override void OnTabQuestClick()
        {
            SelectCategory("任务");
        }

        /// <summary>响应 TabPrecious 点击：切换到贵重物品分类。</summary>
        protected override void OnTabPreciousClick()
        {
            SelectCategory("贵重");
        }

        /// <summary>响应 TabFurnishing 点击：切换到洞天摆设分类。</summary>
        protected override void OnTabFurnishingClick()
        {
            SelectCategory("洞天");
        }

        /// <summary>切换分类：目前只更新标题文本，分类过滤待背包系统提供分类字段后接入。</summary>
        /// <param name="categoryName">分类显示名称。</param>
        private void SelectCategory(string categoryName)
        {
            CategoryLabel.text = categoryName;
            if (bagSystem != null) OnItemsChanged();
        }

        /// <summary>响应 LockBtn 点击：切换选中物品的锁定状态。</summary>
        protected override void OnLockBtnClick()
        {
            Debug.Log("[Bag] 物品锁定尚未实现。");
        }

        /// <summary>响应 DetailHintBtn 点击：与详情按钮共用同一业务入口。</summary>
        protected override void OnDetailHintBtnClick()
        {
            OnDetailBtnClick();
        }

        /// <summary>响应 DetailBtn 点击：打开选中物品的完整详情。</summary>
        protected override void OnDetailBtnClick()
        {
            Debug.Log("[Bag] 物品详情尚未实现。");
        }

        /// <summary>响应 DiscardBtn 点击：进入批量整理模式。</summary>
        protected override void OnDiscardBtnClick()
        {
            Debug.Log("[Bag] 批量整理尚未实现。");
        }

        /// <summary>响应 SortModeBtn 点击：切换排序方式。</summary>
        protected override void OnSortModeBtnClick()
        {
            Debug.Log("[Bag] 排序方式切换尚未实现。");
        }

        /// <summary>响应 SortOrderBtn 点击：在升序与降序之间切换。</summary>
        protected override void OnSortOrderBtnClick()
        {
            Debug.Log("[Bag] 排序方向切换尚未实现。");
        }

        /// <summary>响应 LockFilterBtn 点击：切换只显示已锁定物品的筛选。</summary>
        protected override void OnLockFilterBtnClick()
        {
            Debug.Log("[Bag] 物品筛选尚未实现。");
        }
    }
}
