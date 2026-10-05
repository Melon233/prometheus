using System;
using Xuan.Prometheus.Effects;
using Xuan.Prometheus.Elements;
using Xuan.Prometheus.Input;
using Xuan.Prometheus.LocalAuthority;
using Xuan.Prometheus.World;
using Xuan.Prometheus.Reactions;
using Xuan.Prometheus.Shields;
using Xuan.Prometheus.Stamina;

namespace Xuan.Prometheus.Bootstrap
{
    /// <summary>
    /// 本游戏的会话组合根：决定一次会话由哪些 System 组成。
    /// 这是整个工程中唯一允许认识全部具体 System 实现的位置；
    /// 其余代码一律只依赖 I*System 契约。
    ///
    /// 它只做一件事——按依赖顺序构造并注册。三件事**不再**属于这里：
    /// 配置资产由各 System 在自己的 AfterNewAsync 里加载；
    /// 场景内容由各 System 在 AfterNew 里建立；
    /// 玩法场景由 <see cref="Entry"/> 在会话建立前加载。
    ///
    /// 注册顺序不需要靠注释维护：后一个系统要用前一个，就必须先拿到 AddSystem 的返回值，
    /// 因此**顺序由编译器强制**，依赖成环会直接编译不过。
    /// </summary>
    public sealed class PrometheusSystemInstaller : IGameplaySystemInstaller
    {
        /// <summary>本局所在的主世界定义，提供小队出生位姿。</summary>
        private readonly WorldDefinition world;

        /// <summary>创建会话组合根。</summary>
        /// <param name="world">本局所在的主世界定义；游戏只有一个世界，会话建立前已由入口加载其场景。</param>
        public PrometheusSystemInstaller(WorldDefinition world)
        {
            this.world = world ?? throw new ArgumentNullException(nameof(world));
        }

        /// <summary>
        /// 按依赖顺序构造并注册本会话的全部 System。
        ///
        /// 读法就是依赖图：每个 new 的参数列表写明了这个系统需要谁，
        /// 而它需要的东西必然出现在它上面。
        /// </summary>
        /// <param name="registry">GameplayKit 提供的注册端口。</param>
        public void Install(IGameplaySystemRegistry registry)
        {
            // 实体容器位于依赖图最底层：所有人都可以依赖它，它不依赖任何人。
            IEntitySystem entitySystem = registry.AddSystem<IEntitySystem>(new EntitySystem());

            // 单机版：领域网关由本地实现承担原服务器的权威规则，两者共享同一个背包，
            // 因此 POI 交互发放的物品与背包读到的物品天然一致。背包是会话级共享状态，由组合根创建、构造注入。
            // 全部数据只存在于内存中（不做存档），每次进入游戏都重新开始。
            // 恢复联网时只需把下面两个网关的注册换成 ServiceSystem + NetworkPoiGateway / NetworkBagGateway（见 Prometheus.Gameplay.Online），
            // 依赖网关契约的 PoiSystem 与 BagSystem 无需改动。
            LocalInventory inventory = new LocalInventory();
            IPoiGateway poiGateway = registry.AddSystem<IPoiGateway>(new LocalPoiGateway(inventory));
            IBagGateway bagGateway = registry.AddSystem<IBagGateway>(new LocalBagGateway(inventory));

            // 元素系统不依赖任何其它 System：它只读配表、只改附着与 ICD，反应的数值求值在纯函数里。
            IElementSystem elementSystem = registry.AddSystem<IElementSystem>(new ElementSystem());

            IInputSystem inputSystem = registry.AddSystem<IInputSystem>(new InputSystem(new UnityInputActionSource(), entitySystem));
            IShieldSystem shieldSystem = registry.AddSystem<IShieldSystem>(new ShieldSystem());
            // 体力是重击、冲刺、攀爬、游泳与滑翔的公共约束，全队共享因此由系统持有。
            registry.AddSystem<IStaminaSystem>(new StaminaSystem(entitySystem));
            IEffectSystem effectSystem = registry.AddSystem<IEffectSystem>(new EffectSystem(elementSystem, shieldSystem, traceEnabled: true));
            registry.AddSystem<IReactionProductSystem>(new ReactionProductSystem(elementSystem, entitySystem, effectSystem));
            registry.AddSystem<ICombatAudioPresentationSystem>(new CombatAudioPresentationSystem(effectSystem));
            registry.AddSystem<ICameraSystem>(new CameraSystem(entitySystem));

            // 小队必须晚于输入系统：它在 OnUpdate 里处理的切换要发生在同帧输入采样之后。
            ITeamSystem teamSystem = registry.AddSystem<ITeamSystem>(new TeamSystem(inputSystem, entitySystem, world.SpawnPosition, world.SpawnRotation));

            registry.AddSystem<IWorldMapSystem>(new WorldMapSystem());
            registry.AddSystem<IPoiSystem>(new PoiSystem(entitySystem, teamSystem, poiGateway));
            registry.AddSystem<IBagSystem>(new BagSystem(bagGateway));
        }
    }
}
