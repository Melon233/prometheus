using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using Xuan.Prometheus.Component;
using Xuan.Prometheus.Protocol;
using Xuan.Prometheus.World;

namespace Xuan.Prometheus
{
    /// <summary>封装背包数据请求与缓存，提供可监听的修订号供 UI 响应式更新。</summary>
    public sealed class BagSystem : XSystem
    {
        private readonly ModifiableProperty revision = new ModifiableProperty();
        private readonly List<Item> items = new List<Item>();
        private IGameplayKit gameplayKit;

        /// <summary>背包物品列表的变化版本；UI 通过 Listen 监听它并在变化时重新读取列表。</summary>
        public ModifiableProperty RevisionProperty => revision;

        /// <summary>当前缓存的全部物品（按服务器下发顺序）。</summary>
        public IReadOnlyList<Item> Items => items;

        /// <inheritdoc />
        public override void AfterNew(IGameplayKit gameplayKit)
        {
            this.gameplayKit = gameplayKit;
        }

        /// <summary>请求服务器全部物品并刷新缓存与修订号。</summary>
        public async UniTask RequestItemsAsync()
        {
            if (gameplayKit == null || !gameplayKit.TryGetSystem(out WorldSystem world) || world.Client == null) return;
            GetItemsResponse response = await world.Client.GetItemsAsync();
            items.Clear();
            items.AddRange(response.Items);
            revision.SetBaseValue(revision.Value + 1f);
        }

        /// <inheritdoc />
        public override void Dispose()
        {
            items.Clear();
            gameplayKit = null;
        }
    }
}
