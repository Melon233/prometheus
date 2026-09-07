using System;
using Cysharp.Threading.Tasks;
using Xuan.Prometheus.Effects;
using Xuan.Prometheus.Input;
using Xuan.Prometheus.Narrative;
using Xuan.Prometheus.Npc;
using Xuan.Prometheus.Quest;
using Xuan.Prometheus.Service;
using Xuan.Prometheus.World;

namespace Xuan.Prometheus.Bootstrap
{
    /// <summary>
    /// 本游戏的单局组合根：决定一局由哪些 System 组成、它们的注册顺序，以及初始世界里有什么。
    /// 这是整个工程中唯一允许认识全部具体 System 实现的位置；
    /// 其余代码一律只依赖 I*System 契约，由 GameplayKit 按契约分发。
    /// 组合根不接受任何启动参数：资源包名由 AssetKit 固定，各资源地址由使用它的系统各自持有。
    /// </summary>
    public sealed class PrometheusSystemInstaller : IGameplaySystemInstaller
    {
        /// <summary>玩法场景的 YooAsset 地址；场景加载是启动流程的职责，因此地址由组合根持有。</summary>
        private const string GameplaySceneAddress = "MainWorld";

        /// <summary>测试或工具预置的效果配置库；为空时在安装阶段按地址异步加载。</summary>
        private EffectLibrary effectLibrary;

        /// <summary>保存本局实体容器，供初始内容创建阶段构造固定小队。</summary>
        private EntitySystem entitySystem;

        /// <summary>保存本局小队系统，供初始内容创建阶段写入固定槽位。</summary>
        private TeamSystem teamSystem;

        /// <summary>创建正式组合根；效果配置库在安装阶段按地址加载。</summary>
        public PrometheusSystemInstaller() : this(null)
        {
        }

        /// <summary>
        /// 使用预置效果配置库创建组合根。
        /// 该入口只服务于无法访问异步资源管线的测试与独立工具，正式入口始终走地址加载。
        /// </summary>
        /// <param name="preloadedEffectLibrary">已经就绪的效果配置库；为空表示按地址加载。</param>
        public PrometheusSystemInstaller(EffectLibrary preloadedEffectLibrary)
        {
            effectLibrary = preloadedEffectLibrary;
        }

        /// <summary>
        /// 加载本局配置资产、按依赖顺序注册全部公共 System，最后加载玩法场景。
        /// 注册顺序即初始化顺序：被依赖方必须先于依赖方注册，释放时按其逆序执行。
        /// </summary>
        /// <param name="registry">GameplayKit 提供的注册端口。</param>
        public async UniTask InstallAsync(IGameplaySystemRegistry registry)
        {
            if (registry == null) throw new ArgumentNullException(nameof(registry));
            if (effectLibrary == null)
            {
                EffectLibrary loadedEffectLibrary = null;
                await Core.Asset.LoadAssetAsync<EffectLibrary>(EffectSystem.DefaultLibraryAddress, library => loadedEffectLibrary = library, error => throw new InvalidOperationException(error)).ToUniTask();
                effectLibrary = loadedEffectLibrary;
            }

            RegisterSystems(registry);
            await Core.Asset.LoadSceneAsync(GameplaySceneAddress);
        }

        /// <summary>在全部 System 完成初始化后创建本局初始实体：固定小队；场景敌人由 PoiSystem 按营地实例生成。</summary>
        public void CreateInitialContent()
        {
            entitySystem.CreateInitialTeam(teamSystem);
        }

        /// <summary>
        /// 按确定顺序注册本局全部公共 System。
        /// EntitySystem 最先注册，因为它是所有实体的宿主；
        /// ServiceSystem 紧随其后，两个领域网关再次之，保证任何网络消费者都晚于网关初始化、早于网关释放，
        /// 而网关又晚于唯一会话通道初始化、早于它释放。
        /// 这是组合过程中唯一不接触资源管线的步骤，因此可以被测试和编辑器工具单独调用。
        /// 调用前必须已经具备效果配置库：正式链路由 InstallAsync 加载，其余宿主通过构造函数预置。
        /// </summary>
        /// <param name="registry">GameplayKit 提供的注册端口。</param>
        public void RegisterSystems(IGameplaySystemRegistry registry)
        {
            if (registry == null) throw new ArgumentNullException(nameof(registry));
            if (effectLibrary == null) throw new InvalidOperationException("Effect library must be loaded or preloaded before registering gameplay systems.");
            entitySystem = new EntitySystem();
            registry.AddSystem<IEntitySystem>(entitySystem);
            registry.AddSystem<IServiceSystem>(new ServiceSystem());
            registry.AddSystem<IPoiGateway>(new PoiGateway());
            registry.AddSystem<IBagGateway>(new BagGateway());
            registry.AddSystem<IInputSystem>(new InputSystem(new UnityInputActionSource()));
            registry.AddSystem<IEffectSystem>(new EffectSystem(library: effectLibrary, traceEnabled: true));
            registry.AddSystem<ICombatAudioPresentationSystem>(new CombatAudioPresentationSystem());
            registry.AddSystem<ICameraSystem>(new CameraSystem());
            registry.AddSystem<INarrativeSystem>(new NarrativeSystem());
            registry.AddSystem<INpcSystem>(new NpcSystem());
            registry.AddSystem<IQuestSystem>(new QuestSystem());
            registry.AddSystem<IWorldMapSystem>(new WorldMapSystem());
            registry.AddSystem<IPoiSystem>(new PoiSystem());
            registry.AddSystem<IBagSystem>(new BagSystem());
            teamSystem = new TeamSystem();
            registry.AddSystem<ITeamSystem>(teamSystem);
        }
    }
}
