using UnityEngine;
using Xuan.Prometheus.Logic;

namespace Xuan.Prometheus.World
{
    /// <summary>一个兴趣点的运行时实体：绑定场景摆放的 POI 预制体，组合 PoiComponent(数据) 与按类型选择的 PoiLogic(行为)。由 WorldSystem 创建并交给 EntitySystem 托管。</summary>
    public class PoiEntity : Entity
    {
        /// <summary>该 POI 的配置数据（源自场景 PoiMono）。</summary>
        public PoiConfig Config { get; }

        /// <summary>该 POI 的行为逻辑（按类型创建），用于查询消费状态。</summary>
        private readonly PoiLogic logic;

        /// <summary>
        /// 使用 WorldSystem 已收集的场景 POI 对象构建实体（与 PlayerEntity/SlimeEntity 的 bindGameObject 模式一致）。
        /// </summary>
        /// <param name="bindGameObject">场景中摆放的 POI 表现对象。</param>
        /// <param name="config">该 POI 的配置数据。</param>
        public PoiEntity(GameObject bindGameObject, PoiConfig config)
            : this(bindGameObject, config, PoiLogicFactory.Create(config != null ? config.PoiType : throw new System.ArgumentNullException(nameof(config))))
        {
        }

        /// <summary>为特殊 POI 子类提供受控的 Logic 注入入口，保持 EntitySystem 的统一生命周期。</summary>
        protected PoiEntity(GameObject bindGameObject, PoiConfig config, PoiLogic injectedLogic)
        {
            Config = config ?? throw new System.ArgumentNullException(nameof(config));
            bindGo = bindGameObject != null ? bindGameObject : throw new System.ArgumentNullException(nameof(bindGameObject));
            AddComp<PoiComponent>(new PoiComponent { Config = config });
            logic = injectedLogic ?? throw new System.ArgumentNullException(nameof(injectedLogic));
            AddLogic(logic);
        }

        /// <summary>当前是否已被消费（收集物/可刷新物交互后消失）。</summary>
        public bool IsConsumed => logic != null && logic.IsConsumed;

        /// <summary>外部交互入口：调用该 POI 逻辑的自定义交互行为（服务器请求或客户端 UI）。</summary>
        public virtual void OnInteract() => logic?.OnInteract();

        /// <summary>向 NPC 等派生实体暴露其已注入的 POI Logic，只读返回。</summary>
        protected PoiLogic Logic => logic;
    }
}
