using System;
using UnityEngine;

namespace Xuan.Prometheus.Quest
{
    /// <summary>
    /// 任务交互点的运行时对象：由 QuestSystem 在节点激活时生成并挂到锚点下（随锚点移动），节点结束时销毁。
    /// 它的触发球就是交互范围：玩家的交互感应碰到它时，它通过 <see cref="IInteractable"/> 出现在交互栏。
    /// 不在场景中手工摆放。
    /// </summary>
    public sealed class QuestInteractable : MonoBehaviour, IInteractable
    {
        /// <summary>点击回调。</summary>
        private Action handler;

        /// <inheritdoc />
        public string InteractLabel { get; private set; }

        /// <summary>
        /// 在锚点下生成一个交互点。
        /// </summary>
        /// <param name="anchor">挂载锚点。</param>
        /// <param name="offset">相对锚点的偏移（世界轴向）。</param>
        /// <param name="radius">交互范围半径（米）。</param>
        /// <param name="label">交互栏文案。</param>
        /// <param name="onInteract">点击回调。</param>
        /// <param name="objectName">对象名，便于在层级中辨认归属。</param>
        public static QuestInteractable Spawn(Transform anchor, Vector3 offset, float radius, string label, Action onInteract, string objectName)
        {
            GameObject go = new GameObject(objectName);
            go.transform.SetParent(anchor, false);
            go.transform.position = anchor.position + offset;
            SphereCollider range = go.AddComponent<SphereCollider>();
            // 交互范围只用于感应，必须是触发器，否则会挡住玩家移动。
            range.isTrigger = true;
            range.radius = radius;
            QuestInteractable interactable = go.AddComponent<QuestInteractable>();
            interactable.InteractLabel = label;
            interactable.handler = onInteract;
            return interactable;
        }

        /// <inheritdoc />
        public void Interact() => handler();
    }
}
