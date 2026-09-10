using Xuan.Prometheus.Effects;
using Xuan.Prometheus.Expression;
using Xuan.Prometheus.Input;
using Xuan.Prometheus.Narrative;
using Xuan.Prometheus.Npc;
using Xuan.Prometheus.Quest;
using Xuan.Prometheus.Service;
using Xuan.Prometheus.World;

namespace Xuan.Prometheus.Bootstrap
{
    /// <summary>
    /// 本游戏的会话组合根：决定一次会话由哪些 System 组成。
    /// 这是整个工程中唯一允许认识全部具体 System 实现的位置；
    /// 其余代码一律只依赖 I*System 契约。
    ///
    /// 它只做一件事——按依赖顺序构造并注册。三件事**不再**属于这里：
    /// 配置资产由各 System 在自己的 AfterNewAsync 里加载；
    /// 初始世界内容由各 System 在世界相位建立；
    /// 玩法场景由 <see cref="WorldSession"/> 加载。
    ///
    /// 注册顺序不需要靠注释维护：后一个系统要用前一个，就必须先拿到 AddSystem 的返回值，
    /// 因此**顺序由编译器强制**，依赖成环会直接编译不过。
    /// </summary>
    public sealed class PrometheusSystemInstaller : IGameplaySystemInstaller
    {
        /// <summary>
        /// 按依赖顺序构造并注册本会话的全部 System。
        ///
        /// 读法就是依赖图：每个 new 的参数列表写明了这个系统需要谁，
        /// 而它需要的东西必然出现在它上面。
        /// </summary>
        /// <param name="registry">GameplayKit 提供的注册端口。</param>
        public void Install(IGameplaySystemRegistry registry)
        {
            // 实体容器与会话通道位于依赖图最底层：所有人都可以依赖它们，它们不依赖任何人。
            IEntitySystem entitySystem = registry.AddSystem<IEntitySystem>(new EntitySystem());
            IServiceSystem serviceSystem = registry.AddSystem<IServiceSystem>(new ServiceSystem());

            // 领域网关必须晚于会话通道初始化、早于它释放，这条由构造依赖直接保证。
            IPoiGateway poiGateway = registry.AddSystem<IPoiGateway>(new PoiGateway(serviceSystem));
            IBagGateway bagGateway = registry.AddSystem<IBagGateway>(new BagGateway(serviceSystem));

            IInputSystem inputSystem = registry.AddSystem<IInputSystem>(new InputSystem(new UnityInputActionSource(), entitySystem));
            IEffectSystem effectSystem = registry.AddSystem<IEffectSystem>(new EffectSystem(traceEnabled: true));
            registry.AddSystem<ICombatAudioPresentationSystem>(new CombatAudioPresentationSystem(effectSystem));
            registry.AddSystem<ICameraSystem>(new CameraSystem(entitySystem));

            // 小队必须晚于输入系统：它在 BeforeEntityUpdate 里处理的切换要发生在输入采样之后。
            ITeamSystem teamSystem = registry.AddSystem<ITeamSystem>(new TeamSystem(inputSystem, entitySystem));

            // 任务与剧情共享同一份变量存储，各自只拥有自己的根段（quest / flag、var）。
            // 存储不是 System 而是会话级共享状态，由组合根创建、构造注入——这与注入一个 System 契约形式一致。
            // 两边因此谁都读得到谁，却没有人需要认识谁：所有权只由路径根段决定，
            // 铁律 Q6（任务不解析其他 System）与 R6（剧情不认识任务）都不受影响，而且不再需要任何交叉接线。
            VariableStore variables = new VariableStore();
            INarrativeSystem narrative = registry.AddSystem<INarrativeSystem>(new NarrativeSystem(variables));
            IQuestSystem quest = registry.AddSystem<IQuestSystem>(new QuestSystem(variables));

            // 对话界面宿主由 UI 层实现、组合根注入：玩法层定义端口，UI 层实现端口，两侧都不认识对方的具体类型。
            registry.AddSystem<INpcSystem>(new NpcSystem(quest, narrative, new DialoguePanelHost()));
            registry.AddSystem<IWorldMapSystem>(new WorldMapSystem());
            registry.AddSystem<IPoiSystem>(new PoiSystem(entitySystem, teamSystem, poiGateway, serviceSystem));
            registry.AddSystem<IBagSystem>(new BagSystem(bagGateway));
        }
    }
}
