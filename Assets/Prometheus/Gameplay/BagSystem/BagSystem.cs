using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using Xuan.Prometheus.Component;
using Xuan.Prometheus.Protocol;
using Xuan.Prometheus.Service;

namespace Xuan.Prometheus
{
    /// <summary>封装背包数据请求与缓存，提供可监听的修订号供 UI 响应式更新。</summary>
    internal sealed class BagSystem : XSystem, IBagSystem
    {
        /// <summary>记录背包缓存版本，供界面订阅后按需重绘。</summary>
        private readonly ModifiableProperty revision = new ModifiableProperty();
        /// <summary>保存服务器最近一次下发的当前玩家物品快照。</summary>
        private readonly List<Item> items = new List<Item>();

        /// <summary>在系统释放时取消尚未完成的背包请求，阻止响应继续修改已清空的缓存。</summary>
        private readonly CancellationTokenSource lifetimeCancellation = new CancellationTokenSource();

        /// <summary>通过统一 Core 入口按需取得当前单局 ServiceSystem 接口，不保存或注入公共 System 实例。</summary>
        private static IServiceSystem ServiceSystem => Core.Gameplay.GetSystem<IServiceSystem>();

        /// <summary>背包物品列表的变化版本；UI 通过 Listen 监听它并在变化时重新读取列表。</summary>
        public ModifiableProperty RevisionProperty => revision;

        /// <summary>当前缓存的全部物品（按服务器下发顺序）。</summary>
        public IReadOnlyList<Item> Items => items;

        /// <summary>请求服务器全部物品并刷新缓存与修订号。</summary>
        public async UniTask RequestItemsAsync(CancellationToken cancellationToken = default)
        {
            using (CancellationTokenSource operationCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, lifetimeCancellation.Token))
            {
                try
                {
                    GetItemsResponse response = await ServiceSystem.GetItemsAsync(operationCancellation.Token);
                    operationCancellation.Token.ThrowIfCancellationRequested();
                    if (response == null) return;
                    items.Clear();
                    items.AddRange(response.Items);
                    revision.SetBaseValue(revision.Value + 1f);
                }
                catch (OperationCanceledException) when (lifetimeCancellation.IsCancellationRequested) { }
            }
        }

        /// <inheritdoc />
        public override void Dispose()
        {
            lifetimeCancellation.Cancel();
            items.Clear();
        }
    }
}
