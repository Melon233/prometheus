using UnityEngine;
using Xuan.Prometheus.Component;
using Xuan.Prometheus.World;

namespace Xuan.Prometheus.Logic
{
    /// <summary>
    /// 玩家交互感应逻辑：通过交互 ColliderProxy 的触发进入/离开，维护 InteractComponent 中交互半径内的可交互 POI 配置列表。
    /// 感应属于基础设施，不受控制状态暂停；交互动作由 UI 层按 Id 解析实体后提交。
    /// </summary>
    public sealed class InteractLogic : Logic, ITriggerHandler
    {
        /// <summary>玩家交互感应半径（米）。</summary>
        private const float InteractionRadius = 3f;

        private InteractComponent interactComponent;
        private ColliderProxy sensor;

        /// <inheritdoc />
        public override void AfterNew()
        {
            ControlRequirement = LogicControlRequirement.None;
            Entity.TryGetComp(out interactComponent);
            EnsureSensor();
        }

        /// <inheritdoc />
        public override bool CanEnable() => true;

        /// <inheritdoc />
        public override bool CanDisable() => false;

        /// <inheritdoc />
        public override void OnEnable() { }

        /// <inheritdoc />
        public override void OnDisable() { }

        /// <inheritdoc />
        public override void OnUpdate(float dt) { }

        /// <inheritdoc />
        public override void OnDispose()
        {
            if (sensor != null) sensor.handler = null;
            sensor = null;
            interactComponent = null;
        }

        /// <summary>交互触发进入：识别碰撞体所属 POI 配置并加入附近交互物列表。</summary>
        public void OnTriggerEnter(ColliderProxy source, Collider other)
        {
            if (interactComponent == null || other == null) return;
            PoiMono mono = other.GetComponentInParent<PoiMono>();
            if (mono == null || mono.Config == null) return;
            Debug.Log($"[交互] 感应进入 {mono.Config.Id} ({mono.Config.PoiType})");
            interactComponent.AddNearby(mono.Config);
        }

        /// <summary>交互触发离开：识别碰撞体所属 POI 配置并从附近交互物列表移除。</summary>
        public void OnTriggerExit(ColliderProxy source, Collider other)
        {
            if (interactComponent == null || other == null) return;
            PoiMono mono = other.GetComponentInParent<PoiMono>();
            if (mono == null || mono.Config == null) return;
            Debug.Log($"[交互] 感应离开 {mono.Config.Id}");
            interactComponent.RemoveNearby(mono.Config);
        }

        /// <summary>在玩家身上创建（或复用）交互感应 ColliderProxy：球形 trigger + 运动学刚体，保证触发事件可靠触发。</summary>
        private void EnsureSensor()
        {
            GameObject root = Entity.bindGo;
            if (root == null) return;
            Transform existing = root.transform.Find("InteractSensor");
            if (existing != null) sensor = existing.GetComponent<ColliderProxy>();
            if (sensor == null)
            {
                GameObject go = new GameObject("InteractSensor");
                go.transform.SetParent(root.transform, false);
                SphereCollider trigger = go.AddComponent<SphereCollider>();
                trigger.isTrigger = true;
                trigger.radius = InteractionRadius;
                Rigidbody body = go.AddComponent<Rigidbody>();
                body.isKinematic = true;
                body.useGravity = false;
                sensor = go.AddComponent<ColliderProxy>();
            }
            sensor.handler = this;
        }
    }
}
