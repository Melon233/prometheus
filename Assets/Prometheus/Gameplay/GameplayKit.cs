using System;
using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using UnityEngine;
using Xuan.Prometheus.Asset;
using Xuan.Prometheus.Effects;
using Xuan.Prometheus.Input;
using Xuan.Prometheus.Logic;

namespace Xuan.Prometheus
{
    /// <summary>
    /// 保存一次玩法启动所需的资源地址和外部出生坐标。
    /// 该对象是普通运行时参数，不依赖 MonoBehaviour，因此 Core 和 GameplayKit 可以独立测试和复用。
    /// </summary>
    public sealed class GameplayStartupOptions
    {
        /// <summary>创建正式入口使用的不可变玩法启动参数，全部内容在加载玩法场景前由外部提供。</summary>
        /// <param name="packageName">YooAsset 资源包名称。</param>
        /// <param name="runtimeRoot">承载跨场景运行时对象的常驻根节点。</param>
        /// <param name="sceneLocation">GameplayKit 在 AfterNew 中加载的玩法场景地址。</param>
        /// <param name="effectLibraryLocation">GameplayKit 初始化效果系统时加载的 EffectLibrary 地址。</param>
        /// <param name="teamMemberLocations">三个固定小队槽位各自使用的玩家预制体地址。</param>
        /// <param name="enemyLocation">敌人预制体地址。</param>
        /// <param name="enemySpawnPositions">不依赖玩法场景对象的敌人出生世界坐标。</param>
        /// <param name="enemySpawnLimit">最多创建的敌人数；零表示使用全部出生坐标。</param>
        public GameplayStartupOptions(string packageName, Transform runtimeRoot, string sceneLocation, string effectLibraryLocation, IReadOnlyList<string> teamMemberLocations, string enemyLocation, IReadOnlyList<Vector3> enemySpawnPositions, int enemySpawnLimit)
        {
            PackageName = ValidateText(packageName, nameof(packageName));
            RuntimeRoot = runtimeRoot != null ? runtimeRoot : throw new ArgumentNullException(nameof(runtimeRoot));
            SceneLocation = ValidateText(sceneLocation, nameof(sceneLocation));
            EffectLibraryLocation = ValidateText(effectLibraryLocation, nameof(effectLibraryLocation));
            TeamMemberLocations = ValidateTeamMemberLocations(teamMemberLocations);
            EnemyLocation = ValidateText(enemyLocation, nameof(enemyLocation));
            EnemySpawnPositions = CopyEnemySpawnPositions(enemySpawnPositions);
            EnemySpawnLimit = enemySpawnLimit >= 0 ? enemySpawnLimit : throw new ArgumentOutOfRangeException(nameof(enemySpawnLimit), enemySpawnLimit, "Enemy spawn limit cannot be negative.");
        }

        /// <summary>保留测试与独立工具使用的显式 EffectLibrary 构造入口，不参与正式 Entry 启动链路。</summary>
        /// <param name="packageName">测试资源包名称。</param>
        /// <param name="runtimeRoot">测试运行时根节点。</param>
        /// <param name="effectLibrary">测试显式提供的 EffectLibrary。</param>
        /// <param name="playerLocation">三个固定小队槽位共用的玩家地址。</param>
        /// <param name="enemyLocation">测试敌人地址。</param>
        /// <param name="enemySpawnPoints">需要立即复制为世界坐标的测试 Transform。</param>
        /// <param name="enemySpawnLimit">测试敌人最大生成数量。</param>
        public GameplayStartupOptions(string packageName, Transform runtimeRoot, EffectLibrary effectLibrary, string playerLocation, string enemyLocation, IReadOnlyList<Transform> enemySpawnPoints, int enemySpawnLimit) : this(packageName, runtimeRoot, effectLibrary, new[] { playerLocation, playerLocation, playerLocation }, enemyLocation, enemySpawnPoints, enemySpawnLimit)
        {
        }

        /// <summary>保留测试与独立工具使用的显式 EffectLibrary 构造入口，并将 Transform 出生点立即复制为世界坐标。</summary>
        /// <param name="packageName">测试资源包名称。</param>
        /// <param name="runtimeRoot">测试运行时根节点。</param>
        /// <param name="effectLibrary">测试显式提供的 EffectLibrary。</param>
        /// <param name="teamMemberLocations">三个固定测试小队槽位的玩家地址。</param>
        /// <param name="enemyLocation">测试敌人地址。</param>
        /// <param name="enemySpawnPoints">需要立即复制为世界坐标的测试 Transform。</param>
        /// <param name="enemySpawnLimit">测试敌人最大生成数量。</param>
        public GameplayStartupOptions(string packageName, Transform runtimeRoot, EffectLibrary effectLibrary, IReadOnlyList<string> teamMemberLocations, string enemyLocation, IReadOnlyList<Transform> enemySpawnPoints, int enemySpawnLimit)
        {
            PackageName = ValidateText(packageName, nameof(packageName));
            RuntimeRoot = runtimeRoot != null ? runtimeRoot : throw new ArgumentNullException(nameof(runtimeRoot));
            SceneLocation = "MainWorld";
            EffectLibrary = effectLibrary != null ? effectLibrary : throw new ArgumentNullException(nameof(effectLibrary));
            TeamMemberLocations = ValidateTeamMemberLocations(teamMemberLocations);
            EnemyLocation = ValidateText(enemyLocation, nameof(enemyLocation));
            EnemySpawnPositions = CopyEnemySpawnPositions(enemySpawnPoints);
            EnemySpawnLimit = enemySpawnLimit >= 0 ? enemySpawnLimit : throw new ArgumentOutOfRangeException(nameof(enemySpawnLimit), enemySpawnLimit, "Enemy spawn limit cannot be negative.");
        }

        /// <summary>
        /// YooAsset 资源包名称。
        /// </summary>
        public string PackageName { get; }

        /// <summary>GameplayKit 在 AfterNew 中通过 AssetKit 加载的玩法场景地址。</summary>
        public string SceneLocation { get; }

        /// <summary>
        /// 承载玩家和敌人实例的常驻根节点，确保它们与 Entry 和 Core 具有相同生命周期。
        /// </summary>
        public Transform RuntimeRoot { get; }

        /// <summary>
        /// 正式入口用于加载当前单局 EffectLibrary 的 YooAsset 地址。
        /// </summary>
        public string EffectLibraryLocation { get; }

        /// <summary>测试或独立工具显式提供的 EffectLibrary；正式入口始终通过 EffectLibraryLocation 加载。</summary>
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
        /// 入口在玩法场景加载前提供的敌人出生世界坐标。
        /// </summary>
        public IReadOnlyList<Vector3> EnemySpawnPositions { get; }

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

        /// <summary>复制外部配置的敌人出生坐标，避免入口列表在 GameplayKit 初始化期间变化。</summary>
        private static IReadOnlyList<Vector3> CopyEnemySpawnPositions(IReadOnlyList<Vector3> positions)
        {
            if (positions == null) throw new ArgumentNullException(nameof(positions));
            Vector3[] copiedPositions = new Vector3[positions.Count];
            for (int index = 0; index < copiedPositions.Length; index++) copiedPositions[index] = positions[index];
            return copiedPositions;
        }

        /// <summary>把测试或工具提供的 Transform 出生点立即转换为稳定的世界坐标。</summary>
        private static IReadOnlyList<Vector3> CopyEnemySpawnPositions(IReadOnlyList<Transform> spawnPoints)
        {
            if (spawnPoints == null) throw new ArgumentNullException(nameof(spawnPoints));
            Vector3[] copiedPositions = new Vector3[spawnPoints.Count];
            for (int index = 0; index < copiedPositions.Length; index++) copiedPositions[index] = spawnPoints[index] != null ? spawnPoints[index].position : throw new ArgumentException($"Enemy spawn point at index {index} is null.", nameof(spawnPoints));
            return copiedPositions;
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
        /// <param name="assetKit">由同一个 Core 持有的资源 Kit。</param>
        public GameplayKit(IAssetKit assetKit)
        {
            this.assetKit = assetKit ?? throw new ArgumentNullException(nameof(assetKit));
            entitySystem = new EntitySystem(this);
            AddSystem(entitySystem);
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

            startupOptions = options;
            if (options.EffectLibrary != null) RegisterGameplaySystems(options.EffectLibrary);
        }

        /// <summary>等待 AssetKit 初始化完成，异步加载正式 EffectLibrary 和 SampleScene，并为同步 AfterNew 准备完整玩法依赖。</summary>
        public override async UniTask AfterNewAsync()
        {
            ThrowIfDisposed();
            if (startupOptions == null) throw new InvalidOperationException("GameplayKit must be configured before AfterNewAsync.");
            await assetKit.WaitUntilReadyAsync();
            if (startupOptions.EffectLibrary == null)
            {
                EffectLibrary loadedEffectLibrary = null;
                await assetKit.LoadAssetAsync<EffectLibrary>(startupOptions.EffectLibraryLocation, library => loadedEffectLibrary = library, error => throw new InvalidOperationException(error)).ToUniTask();
                RegisterGameplaySystems(loadedEffectLibrary);
            }
            await assetKit.LoadSceneAsync(startupOptions.SceneLocation);
        }

        /// <summary>以已经取得的 EffectLibrary 注册当前单局全部公共 System，并保持 EntitySystem 为首个系统。</summary>
        /// <param name="effectLibrary">当前单局效果系统使用的持久化配置库。</param>
        private void RegisterGameplaySystems(EffectLibrary effectLibrary)
        {
            AddSystem(new InputSystem(new UnityInputActionSource()));
            AddSystem(new HudCommandSystem());
            AddSystem(new EffectSystem(library: effectLibrary, traceEnabled: true));
            AddSystem(new CombatAudioPresentationSystem());
            AddSystem(new CameraSystem(startupOptions.RuntimeRoot));
            AddSystem(new MinimapSystem());
            AddSystem(new Xuan.Prometheus.World.WorldSystem());
            AddSystem(new BagSystem());
            teamSystem = new TeamSystem();
            AddSystem(teamSystem);
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
        /// 在 Entry 等待全部 Kit 异步任务完成后，初始化全部玩法系统并创建初始实体。
        /// </summary>
        public override void AfterNew()
        {
            ThrowIfDisposed();

            if (IsReady)
                return;

            if (startupOptions == null)
                throw new InvalidOperationException("GameplayKit must be configured before initialization.");

            if (!assetKit.IsReady) throw new InvalidOperationException("AssetKit must be ready before GameplayKit creates entities.");
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
        /// 先通过 EntitySystem 释放全部 Entity，再逆序释放其他 System；Core 随后才会释放 AssetKit 句柄。
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
