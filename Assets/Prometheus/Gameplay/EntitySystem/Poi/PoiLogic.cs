using Cysharp.Threading.Tasks;
using Xuan.Prometheus.Logic;

namespace Xuan.Prometheus.World
{
    /// <summary>
    /// POI 行为基类：提供读取 PoiComponent / Config 的便捷入口与默认空实现，
    /// 各类型 Logic 按需覆盖 AfterNew / OnUpdate / OnEnable 等回调（业务在 P4 填充）。
    /// </summary>
    public abstract class PoiLogic : Xuan.Prometheus.Logic.Logic
    {
        /// <inheritdoc />
        public override void AfterNew() { }

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
        public override void OnDispose() { }

        /// <summary>当前实体上的 POI 数据组件；组件缺失时返回空。</summary>
        protected PoiComponent Poi => Entity.TryGetComp(out PoiComponent comp) ? comp : null;

        /// <summary>当前 POI 的配置数据；组件缺失时返回空。</summary>
        protected PoiConfig Config => Poi != null ? Poi.Config : null;

        /// <summary>当前是否已被消费（收集物/可刷新物交互后应消失）。默认 false，收集类与可刷新类覆盖。</summary>
        public virtual bool IsConsumed => false;

        /// <summary>立即设置 POI 表现对象的显隐；收集、冷却和重生均由具体 POI 状态逻辑调用。</summary>
        protected void SetPoiVisible(bool visible)
        {
            if (Entity != null && Entity.bindGo != null && Entity.bindGo.activeSelf != visible)
                Entity.bindGo.SetActive(visible);
        }

        /// <summary>外部交互入口：各 POI 类型在此实现自定义交互行为（请求服务器或打开客户端 UI）。默认无操作。</summary>
        public virtual void OnInteract() { }

        /// <summary>请求服务器执行指定交互操作（收集/采集/击败/解锁等），供服务器型 POI 的 OnInteract 调用。</summary>
        protected void RequestServerInteract(PoiOp op)
        {
            if (Entity == null || !Entity.IsActive) return;
            if (Core.Gameplay.TryGetSystem(out IWorldSystem world)) world.TryInteractAsync((PoiEntity)Entity, op).Forget();
        }
    }
}
