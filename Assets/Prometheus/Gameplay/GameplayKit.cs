using System;
using System.Collections.Generic;
using UnityEngine;
using Xuan.Prometheus.Asset;
using Xuan.Prometheus.Effects;
using Xuan.Prometheus.Input;
using Xuan.Prometheus.Logic;

namespace Xuan.Prometheus
{
    /// <summary>
    /// 保存一次玩法启动所需的资源地址和场景出生点。
    /// 该对象是普通运行时参数，不依赖 MonoBehaviour，因此 GameCore 和 GameplayKit 可以独立测试和复用。
    /// </summary>
    public sealed class GameplayStartupOptions
    {
        /// <summary>
        /// 创建一组不可变的玩法启动参数。
        /// </summary>
        /// <param name="packageName">YooAsset 资源包名称。</param>
        /// <param name="runtimeRoot">承载运行时玩法对象的常驻根节点。</param>
        /// <param name="effectLibrary">当前单局使用的持久化 Effect 配置库。</param>
        /// <param name="playerLocation">兼容旧调用的玩家预制体资源地址，三个小队槽位都会使用该地址。</param>
        /// <param name="enemyLocation">敌人预制体的资源地址。</param>
        /// <param name="enemySpawnPoints">敌人出生点集合；空元素会在创建时跳过并输出警告。</param>
        /// <param name="enemySpawnLimit">最多创建的敌人数；零表示使用全部有效出生点。</param>
        public GameplayStartupOptions(string packageName, Transform runtimeRoot, EffectLibrary effectLibrary, string playerLocation, string enemyLocation, IReadOnlyList<Transform> enemySpawnPoints, int enemySpawnLimit) : this(packageName, runtimeRoot, effectLibrary, new[] { playerLocation, playerLocation, playerLocation }, enemyLocation, enemySpawnPoints, enemySpawnLimit)
        {
        }

        /// <summary>创建一组包含固定三人小队配置的不可变玩法启动参数。</summary>
        /// <param name="packageName">YooAsset 资源包名称。</param>
        /// <param name="runtimeRoot">承载运行时玩法对象的常驻根节点。</param>
        /// <param name="effectLibrary">当前单局使用的持久化 Effect 配置库。</param>
        /// <param name="teamMemberLocations">三个固定小队槽位各自使用的玩家预制体资源地址。</param>
        /// <param name="enemyLocation">敌人预制体的资源地址。</param>
        /// <param name="enemySpawnPoints">敌人出生点集合。</param>
        /// <param name="enemySpawnLimit">最多创建的敌人数；零表示使用全部有效出生点。</param>
        public GameplayStartupOptions(string packageName, Transform runtimeRoot, EffectLibrary effectLibrary, IReadOnlyList<string> teamMemberLocations, string enemyLocation, IReadOnlyList<Transform> enemySpawnPoints, int enemySpawnLimit)
        {
            PackageName = ValidateText(packageName, nameof(packageName));
            RuntimeRoot = runtimeRoot != null ? runtimeRoot : throw new ArgumentNullException(nameof(runtimeRoot));
            EffectLibrary = effectLibrary != null ? effectLibrary : throw new ArgumentNullException(nameof(effectLibrary));
            TeamMemberLocations = ValidateTeamMemberLocations(teamMemberLocations);
            EnemyLocation = ValidateText(enemyLocation, nameof(enemyLocation));
            EnemySpawnPoints = enemySpawnPoints ?? Array.Empty<Transform>();
            EnemySpawnLimit = enemySpawnLimit >= 0 ? enemySpawnLimit : throw new ArgumentOutOfRangeException(nameof(enemySpawnLimit), enemySpawnLimit, "Enemy spawn limit cannot be negative.");
        }

        /// <summary>
        /// YooAsset 资源包名称。
        /// </summary>
        public string PackageName { get; }

        /// <summary>
        /// 承载玩家和敌人实例的常驻根节点，确保它们与 Entry 和 GameCore 具有相同生命周期。
        /// </summary>
        public Transform RuntimeRoot { get; }

        /// <summary>
        /// 当前单局 EffectSystem 使用的持久化配置库；场景中的 Entry 必须显式提供。
        /// </summary>
        public EffectLibrary EffectLibrary { get; }

        /// <summary>
        /// 获取三个固定小队槽位的玩家预制体资源地址。
        /// </summary>
        public IReadOnlyList<string> TeamMemberLocations { get; }

        /// <summary>获取第一个小队槽位的资源地址，保留该入口用于兼容只读取默认玩家地址的旧代码。</summary>
        public string PlayerLocation => TeamMemberLocations[0];

        /// <summary>
        /// 敌人预制体的资源地址。
        /// </summary>
        public string EnemyLocation { get; }

        /// <summary>
        /// 场景提供的敌人出生点。
        /// </summary>
        public IReadOnlyList<Transform> EnemySpawnPoints { get; }

        /// <summary>
        /// 最多创建的敌人数；零表示不限制数量。
        /// </summary>
        public int EnemySpawnLimit { get; }

        /// <summary>
        /// 校验入口传入的关键字符串，尽早暴露缺少场景配置的问题。
        /// </summary>
        private static string ValidateText(string value, string parameterName)
        {
            if (string.IsNullOrWhiteSpace(value))
                throw new ArgumentException("Gameplay startup value cannot be empty.", parameterName);

            return value;
        }

        /// <summary>复制并校验三个固定小队槽位，避免外部集合在初始化过程中被修改。</summary>
        private static IReadOnlyList<string> ValidateTeamMemberLocations(IReadOnlyList<string> locations)
        {
            if (locations == null) throw new ArgumentNullException(nameof(locations));
            if (locations.Count != TeamSystem.Capacity) throw new ArgumentException($"Gameplay requires exactly {TeamSystem.Capacity} team member locations.", nameof(locations));
            string[] validatedLocations = new string[TeamSystem.Capacity];
            for (int slotIndex = 0; slotIndex < validatedLocations.Length; slotIndex++) validatedLocations[slotIndex] = ValidateText(locations[slotIndex], $"{nameof(locations)}[{slotIndex}]");
            return validatedLocations;
        }
    }

    /// <summary>
    /// 对外暴露玩法世界的只读状态和公共 System 查询能力。
    /// </summary>
    public interface IGameplayKit
    {
        /// <summary>
        /// GameplayKit 是否已经完成初始实体创建。
        /// </summary>
        bool IsReady { get; }

        /// <summary>
        /// 当前玩家实体；初始化完成前为空。
        /// </summary>
        PlayerEntity Player { get; }

        /// <summary>
        /// 获取当前单局中指定类型的唯一公共 System。
        /// </summary>
        TSystem GetSystem<TSystem>() where TSystem : XSystem;

        /// <summary>
        /// 尝试获取当前单局中指定类型的唯一公共 System。
        /// </summary>
        bool TryGetSystem<TSystem>(out TSystem system) where TSystem : XSystem;
    }

    /// <summary>
    /// 负责玩法对象创建与公共 System 生命周期编排；Entity 的注册、更新、监听和销毁统一交给 EntitySystem。
    /// 资源能力通过构造函数注入，避免 Entity、入口组件和 YooAsset 形成隐式全局依赖。
    /// </summary>
    public sealed class GameplayKit : Kit, IGameplayKit
    {
        private readonly IAssetKit assetKit;
        /// <summary>保存当前玩法世界内建且唯一的实体系统。</summary>
        private readonly EntitySystem entitySystem;
        private readonly XMap<Type, XSystem> systems = new XMap<Type, XSystem>();
        private readonly List<XSystem> systemInitializationOrder = new List<XSystem>();
        private GameplayStartupOptions startupOptions;
        /// <summary>保存当前单局唯一的小队系统，使 Player 属性始终解析为当前上场成员。</summary>
        private TeamSystem teamSystem;
        private bool isDisposing;
        private bool isDisposed;

        /// <summary>
        /// 创建 GameplayKit，并显式声明其依赖的资源 Kit。
        /// </summary>
        /// <param name="assetKit">由同一个 GameCore 持有的资源 Kit。</param>
        public GameplayKit(IAssetKit assetKit)
        {
            this.assetKit = assetKit ?? throw new ArgumentNullException(nameof(assetKit));
            entitySystem = new EntitySystem(this);
            AddSystem(entitySystem);
            Core.Gameplay = this;
        }

        /// <inheritdoc />
        public bool IsReady { get; private set; }

        /// <inheritdoc />
        public PlayerEntity Player => teamSystem == null ? null : teamSystem.ActiveMember as PlayerEntity;

        /// <summary>
        /// 在初始化前写入场景启动参数，每个 GameplayKit 实例只接受一次配置。
        /// </summary>
        /// <param name="options">由 Entry 从场景序列化字段构建的启动参数。</param>
        public void Configure(GameplayStartupOptions options)
        {
            ThrowIfDisposed();

            if (IsReady)
                throw new InvalidOperationException("GameplayKit cannot be configured after it is ready.");

            if (options == null)
                throw new ArgumentNullException(nameof(options));

            if (startupOptions != null)
            {
                if (ReferenceEquals(startupOptions, options)) return;
                throw new InvalidOperationException("GameplayKit can only be configured once.");
            }

            AddSystem(new InputSystem(new UnityInputActionSource()));
            AddSystem(new HudCommandSystem());
            AddSystem(new EffectSystem(library: options.EffectLibrary, traceEnabled: true));
            AddSystem(new CombatAudioPresentationSystem());
            AddSystem(new CameraSystem(options.RuntimeRoot));
            AddSystem(new MinimapSystem());
            teamSystem = new TeamSystem();
            AddSystem(teamSystem);
            startupOptions = options;
        }

        /// <summary>
        /// 在 GameplayKit 初始化前注册一个单局唯一 System。
        /// 同一具体类型重复注册会立即抛出异常，避免 Entity 获取到不确定实例。
        /// </summary>
        /// <typeparam name="TSystem">需要注册的具体 System 类型。</typeparam>
        /// <param name="system">由当前 GameplayKit 独占并负责释放的 System 实例。</param>
        public void AddSystem<TSystem>(TSystem system) where TSystem : XSystem
        {
            ThrowIfDisposed();

            if (isDisposing)
                throw new InvalidOperationException("GameplayKit cannot register a system while it is disposing.");

            if (IsReady)
                throw new InvalidOperationException("GameplayKit cannot register a system after it is ready.");

            if (system == null)
                throw new ArgumentNullException(nameof(system));

            Type systemType = system.GetType();
            if (systems.HasKey(systemType))
                throw new InvalidOperationException($"GameplayKit already contains a system of type '{systemType.FullName}'.");

            systems.Add(systemType, system);
            systemInitializationOrder.Add(system);
        }

        /// <inheritdoc />
        public TSystem GetSystem<TSystem>() where TSystem : XSystem
        {
            ThrowIfDisposed();

            if (!systems.TryGet(typeof(TSystem), out XSystem system))
                throw new InvalidOperationException($"GameplayKit does not contain a system of type '{typeof(TSystem).FullName}'.");

            return system as TSystem ?? throw new InvalidCastException($"Registered system '{system.GetType().FullName}' cannot be cast to '{typeof(TSystem).FullName}'.");
        }

        /// <inheritdoc />
        public bool TryGetSystem<TSystem>(out TSystem system) where TSystem : XSystem
        {
            ThrowIfDisposed();

            if (systems.TryGet(typeof(TSystem), out XSystem registeredSystem) && registeredSystem is TSystem typedSystem)
            {
                system = typedSystem;
                return true;
            }

            system = null;
            return false;
        }

        /// <summary>
        /// 在 AssetKit 完成异步初始化后创建玩家和敌人，并将它们纳入统一更新。
        /// </summary>
        public override void AfterNew()
        {
            ThrowIfDisposed();

            if (IsReady)
                return;

            if (startupOptions == null)
                throw new InvalidOperationException("GameplayKit must be configured before initialization.");

            if (!assetKit.IsReady)
                throw new InvalidOperationException("AssetKit must be ready before GameplayKit creates entities.");

            foreach (XSystem system in systemInitializationOrder)
                system.AfterNew(this);

            entitySystem.CreateInitialEntities(assetKit, startupOptions, teamSystem);
            IsReady = true;
        }

        /// <summary>
        /// 按系统阶段驱动当前玩法世界，并在前置与后置 System 之间调用 EntitySystem 更新实体。
        /// </summary>
        /// <param name="dt">当前帧增量时间。</param>
        public override void OnUpdate(float dt)
        {
            if (isDisposed) return;
            entitySystem.DrainPendingEntityRemovals();
            if (!IsReady) return;

            foreach (XSystem system in systemInitializationOrder)
                system.BeforeEntityUpdate(dt);

            entitySystem.UpdateEntities(dt);

            foreach (XSystem system in systemInitializationOrder)
                system.OnUpdate(dt);

            entitySystem.DrainPendingEntityRemovals();
        }

        /// <summary>
        /// 先通过 EntitySystem 释放全部 Entity，再逆序释放其他 System；GameCore 随后才会释放 AssetKit 句柄。
        /// </summary>
        public override void Dispose()
        {
            if (isDisposed || isDisposing)
                return;

            isDisposing = true;
            IsReady = false;
            try
            {
                entitySystem.Dispose();
            }
            finally
            {
                try
                {
                    for (int index = systemInitializationOrder.Count - 1; index >= 0; index--)
                    {
                        XSystem system = systemInitializationOrder[index];
                        if (!ReferenceEquals(system, entitySystem)) system.Dispose();
                    }
                }
                finally
                {
                    systemInitializationOrder.Clear();
                    systems.Dispose();
                    if (ReferenceEquals(Core.Gameplay, this)) Core.Gameplay = null;
                    teamSystem = null;
                    startupOptions = null;
                    isDisposed = true;
                    isDisposing = false;
                }
            }
        }

        /// <summary>
        /// 阻止已释放 Kit 被再次使用，避免静默写入已经清空的实体容器。
        /// </summary>
        private void ThrowIfDisposed()
        {
            if (isDisposed)
                throw new ObjectDisposedException(nameof(GameplayKit));
        }
    }
}
