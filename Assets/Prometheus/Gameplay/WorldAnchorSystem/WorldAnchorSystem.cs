using System;
using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using UnityEngine;

namespace Xuan.Prometheus
{
    /// <summary>
    /// 世界锚点索引的实现。游戏是单世界设计，场景在会话建立前已加载，因此在 AfterNew 中扫描一次即可；
    /// id 为空或重复属于关卡配置错误，直接抛出。
    /// </summary>
    internal sealed class WorldAnchorSystem : IWorldAnchorSystem
    {
        /// <summary>按 id 索引的锚点。</summary>
        private readonly Dictionary<string, Transform> anchors = new Dictionary<string, Transform>(StringComparer.Ordinal);

        /// <inheritdoc />
        public UniTask AfterNewAsync() => UniTask.CompletedTask;

        /// <summary>扫描场景中的全部锚点（包括未激活的对象）。</summary>
        public void AfterNew()
        {
            foreach (WorldAnchor anchor in UnityEngine.Object.FindObjectsByType<WorldAnchor>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                if (string.IsNullOrWhiteSpace(anchor.AnchorId)) throw new InvalidOperationException($"场景中的 WorldAnchor '{anchor.name}' 未配置 id。");
                if (!anchors.TryAdd(anchor.AnchorId, anchor.transform)) throw new InvalidOperationException($"场景中存在重复的 WorldAnchor id '{anchor.AnchorId}'。");
            }
        }

        /// <summary>本系统不需要逐帧驱动。</summary>
        /// <param name="dt">帧时间（秒）。</param>
        public void OnUpdate(float dt)
        {
        }

        /// <inheritdoc />
        public bool TryGet(string anchorId, out Transform anchor) => anchors.TryGetValue(anchorId, out anchor);

        /// <inheritdoc />
        public Transform Require(string anchorId, string owner)
        {
            if (anchors.TryGetValue(anchorId, out Transform anchor)) return anchor;
            throw new InvalidOperationException($"{owner} 引用的世界锚点 '{anchorId}' 在场景中不存在。");
        }

        /// <summary>只清空索引；锚点是场景对象，不归本系统所有。</summary>
        public void Dispose() => anchors.Clear();
    }
}
