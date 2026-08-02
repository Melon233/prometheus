using System;
using System.Collections.Generic;
using UnityEngine;
using Xuan.Prometheus.Asset;
using Xuan.Prometheus.Actor;
using Xuan.Prometheus.Effects;
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
        /// <param name="playerLocation">玩家预制体的资源地址。</param>
        /// <param name="enemyLocation">敌人预制体的资源地址。</param>
        /// <param name="enemySpawnPoints">敌人出生点集合；空元素会在创建时跳过并输出警告。</param>
        /// <param name="enemySpawnLimit">最多创建的敌人数；零表示使用全部有效出生点。</param>
        public GameplayStartupOptions(string packageName, Transform runtimeRoot, EffectLibrary effectLibrary, string playerLocation, string enemyLocation, IReadOnlyList<Transform> enemySpawnPoints, int enemySpawnLimit)
        {
            PackageName = ValidateText(packageName, nameof(packageName));
            RuntimeRoot = runtimeRoot != null ? runtimeRoot : throw new ArgumentNullException(nameof(runtimeRoot));
            EffectLibrary = effectLibrary != null ? effectLibrary : throw new ArgumentNullException(nameof(effectLibrary));
            PlayerLocation = ValidateText(playerLocation, nameof(playerLocation));
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
        /// 玩家预制体的资源地址。
        /// </summary>
        public string PlayerLocation { get; }

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
    }

    /// <summary>
    /// 对外暴露玩法世界的只读状态和实体管理能力。
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
        /// 注册一个已经完成构造的实体、建立所属 GameplayKit，并返回运行时编号；调用方随后才能执行 Entity.AfterNew。
        /// </summary>
        int AddEntity(Entity entity);

        /// <summary>
        /// 按运行时编号查询实体。
        /// </summary>
        bool TryGetEntity(int entityId, out Entity entity);

        /// <summary>
        /// 移除并立即释放指定实体。
        /// </summary>
        bool RemoveEntity(int entityId);

        /// <summary>
        /// 请求在当前帧安全边界移除指定实体，重复请求不会重复注销 Logic 或销毁场景对象。
        /// </summary>
        bool RequestRemoveEntity(int entityId, float destroyDelay = 0f);

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
    /// 管理玩法对象的创建、Entity 注册、逐帧更新和销毁。
    /// 资源能力通过构造函数注入，避免 Entity、入口组件和 YooAsset 形成隐式全局依赖。
    /// </summary>
    public sealed class GameplayKit : Kit, IGameplayKit
    {
        /// <summary>当前客户端启动流程为本地玩家保留的稳定控制器编号。</summary>
        private const int LocalPlayerControllerId = 1;

        private readonly IAssetKit assetKit;
        private readonly XMap<int, Entity> entities = new XMap<int, Entity>();
        private readonly XMap<Type, XSystem> systems = new XMap<Type, XSystem>();
        private readonly List<XSystem> systemInitializationOrder = new List<XSystem>();
        private readonly Dictionary<int, float> pendingEntityRemovals = new Dictionary<int, float>();
        private readonly List<int> pendingEntityRemovalBuffer = new List<int>();
        private GameplayStartupOptions startupOptions;
        private int nextEntityId = 1;
        private bool isDisposing;
        private bool isDisposed;
        private ControlLeaseHandle localPlayerControlLease;
        private bool localPlayerControllerRegistered;
        /// <summary>标记当前是否正在枚举实体，直接移除请求会在该阶段自动转为安全边界回收。</summary>
        private bool isUpdatingEntities;

        /// <summary>
        /// 创建 GameplayKit，并显式声明其依赖的资源 Kit。
        /// </summary>
        /// <param name="assetKit">由同一个 GameCore 持有的资源 Kit。</param>
        public GameplayKit(IAssetKit assetKit)
        {
            this.assetKit = assetKit ?? throw new ArgumentNullException(nameof(assetKit));
        }

        /// <inheritdoc />
        public bool IsReady { get; private set; }

        /// <inheritdoc />
        public PlayerEntity Player { get; private set; }

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

            AddSystem(new PossessionSystem());
            AddSystem(new ActorSimulationSystem());
            AddSystem(new CameraDirectorSystem());
            AddSystem(new EffectSystem(library: options.EffectLibrary));
            startupOptions = options;
        }

        /// <inheritdoc />
        public int AddEntity(Entity entity)
        {
            ThrowIfDisposed();

            if (isDisposing)
                throw new InvalidOperationException("GameplayKit cannot register an entity while it is disposing.");

            if (isUpdatingEntities)
                throw new InvalidOperationException("GameplayKit cannot register an entity while the entity collection is updating.");

            if (entity == null)
                throw new ArgumentNullException(nameof(entity));

            int entityId = nextEntityId++;
            entity.BindGameplayKit(this, entityId);
            entities.Add(entityId, entity);
            return entityId;
        }

        /// <inheritdoc />
        public bool TryGetEntity(int entityId, out Entity entity)
        {
            return entities.TryGet(entityId, out entity);
        }

        /// <inheritdoc />
        public bool RemoveEntity(int entityId)
        {
            if (isUpdatingEntities) return RequestRemoveEntity(entityId, 0f);
            if (!entities.TryGet(entityId, out Entity entity))
                return false;

            pendingEntityRemovals.Remove(entityId);
            entities.Remove(entityId);
            ClearLocalPlayerBinding(entity);
            entity.MarkDespawnRequested(0f);
            entity.DisposeImmediately();
            return true;
        }

        /// <inheritdoc />
        public bool RequestRemoveEntity(int entityId, float destroyDelay = 0f)
        {
            if (isDisposed || isDisposing) return false;
            if (!entities.TryGet(entityId, out Entity entity)) return false;
            if (pendingEntityRemovals.ContainsKey(entityId)) return false;
            if (!entity.MarkDespawnRequested(Mathf.Max(0f, destroyDelay))) return false;
            pendingEntityRemovals.Add(entityId, Mathf.Max(0f, destroyDelay));
            return true;
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

            CreatePlayer();
            CreateEnemies();
            IsReady = true;
        }

        /// <summary>
        /// 按稳定注册顺序驱动当前玩法世界中的所有实体。
        /// </summary>
        /// <param name="dt">当前帧增量时间。</param>
        public override void OnUpdate(float dt)
        {
            if (isDisposed) return;
            DrainPendingEntityRemovals();
            if (!IsReady) return;

            foreach (XSystem system in systemInitializationOrder)
                system.OnBeforeEntityUpdate(dt);

            isUpdatingEntities = true;
            try
            {
                foreach (Entity entity in entities) entity.OnUpdate(dt);
            }
            finally
            {
                isUpdatingEntities = false;
            }

            DrainPendingEntityRemovals();

            foreach (XSystem system in systemInitializationOrder)
                system.OnUpdate(dt);

            DrainPendingEntityRemovals();
        }

        /// <summary>
        /// 在全部普通玩法更新完成后按稳定注册顺序驱动客户端迟更新系统。
        /// </summary>
        /// <param name="dt">当前帧增量时间。</param>
        public override void OnLateUpdate(float dt)
        {
            if (isDisposed || !IsReady) return;

            foreach (XSystem system in systemInitializationOrder)
                system.OnLateUpdate(dt);
        }

        /// <summary>
        /// 立即释放全部 Entity；GameCore 随后才会释放 AssetKit 句柄，保证销毁顺序安全。
        /// </summary>
        public override void Dispose()
        {
            if (isDisposed || isDisposing)
                return;

            isDisposing = true;
            IsReady = false;
            try
            {
                ReleaseLocalPlayerControl();
                foreach (Entity entity in entities)
                {
                    entity.MarkDespawnRequested(0f);
                    entity.DisposeImmediately();
                }
            }
            finally
            {
                pendingEntityRemovals.Clear();
                pendingEntityRemovalBuffer.Clear();
                isUpdatingEntities = false;
                entities.Dispose();
                try
                {
                    for (int index = systemInitializationOrder.Count - 1; index >= 0; index--)
                        systemInitializationOrder[index].Dispose();
                }
                finally
                {
                    systemInitializationOrder.Clear();
                    systems.Dispose();
                    Player = null;
                    startupOptions = null;
                    isDisposed = true;
                    isDisposing = false;
                }
            }
        }

        /// <summary>
        /// 从配置的玩家资源创建场景对象和 PlayerEntity。
        /// </summary>
        private void CreatePlayer()
        {
            GameObject playerObject = assetKit.InstantiateSync(startupOptions.PlayerLocation, startupOptions.RuntimeRoot);
            int entityId = 0;

            try
            {
                Player = new PlayerEntity(playerObject);
                entityId = AddEntity(Player);
                Player.AfterNew();
                BindLocalPlayerSystems(playerObject, Player);
            }
            catch
            {
                if (entityId > 0) RemoveEntity(entityId);
                else UnityEngine.Object.Destroy(playerObject);
                Player = null;
                throw;
            }
        }

        /// <summary>为已经注册完成的玩家 Pawn 建立默认本地控制租约和基础镜头目标。</summary>
        private void BindLocalPlayerSystems(GameObject playerObject, PlayerEntity playerEntity)
        {
            PossessionSystem possessionSystem = GetSystem<PossessionSystem>();
            ReleaseLocalPlayerControl();
            possessionSystem.RegisterController(new LegacyPlayerControllerRuntime(LocalPlayerControllerId));
            localPlayerControllerRegistered = true;
            localPlayerControlLease = possessionSystem.AcquireLease(new ControlLeaseRequest(LocalPlayerControllerId, playerEntity.EntityId, ControlScope.All, 0));
            ActorAuthoringComponent authoring = playerObject.GetComponent<ActorAuthoringComponent>();
            if (authoring == null || authoring.Definition == null) throw new InvalidOperationException($"Player prefab '{playerObject.name}' requires ActorAuthoringComponent and ActorDefinition.");
            Camera playerCamera = playerObject.GetComponentInChildren<Camera>(true);
            if (playerCamera == null) throw new InvalidOperationException($"Player prefab '{playerObject.name}' does not contain a camera for the current client bootstrap.");
            if (authoring.CameraSubject == null || authoring.Definition.CameraProfile == null) throw new InvalidOperationException($"Player actor '{authoring.Definition.ActorId}' requires CameraSubject and CameraFollowProfile.");
            CameraDirectorSystem cameraDirector = GetSystem<CameraDirectorSystem>();
            cameraDirector.AdoptCameraRig(playerCamera, startupOptions.RuntimeRoot, playerObject.transform);
            cameraDirector.SetBaseTarget(authoring.CameraSubject, authoring.Definition.CameraProfile, true);
        }

        /// <summary>
        /// 遍历场景出生点创建敌人，默认兼容旧入口只创建第一个有效敌人的行为。
        /// </summary>
        private void CreateEnemies()
        {
            int createdCount = 0;

            foreach (Transform spawnPoint in startupOptions.EnemySpawnPoints)
            {
                if (spawnPoint == null)
                {
                    Debug.LogWarning("GameplayKit skipped an empty enemy spawn point.");
                    continue;
                }

                GameObject enemyObject = assetKit.InstantiateSync(startupOptions.EnemyLocation, spawnPoint.position, spawnPoint.rotation, startupOptions.RuntimeRoot);
                int entityId = 0;

                try
                {
                    SlimeEntity enemy = new SlimeEntity(enemyObject);
                    entityId = AddEntity(enemy);
                    enemy.AfterNew();
                    createdCount++;
                }
                catch
                {
                    if (entityId > 0) RemoveEntity(entityId);
                    else UnityEngine.Object.Destroy(enemyObject);
                    throw;
                }

                if (startupOptions.EnemySpawnLimit > 0 && createdCount >= startupOptions.EnemySpawnLimit)
                    break;
            }
        }

        /// <summary>在 XMap 遍历之外处理本帧全部回收请求，避免死亡回调直接修改实体容器。</summary>
        private void DrainPendingEntityRemovals()
        {
            if (pendingEntityRemovals.Count == 0) return;
            pendingEntityRemovalBuffer.Clear();
            foreach (int entityId in pendingEntityRemovals.Keys) pendingEntityRemovalBuffer.Add(entityId);
            for (int index = 0; index < pendingEntityRemovalBuffer.Count; index++)
            {
                int entityId = pendingEntityRemovalBuffer[index];
                pendingEntityRemovals.Remove(entityId);
                if (!entities.TryGet(entityId, out Entity entity)) continue;
                entities.Remove(entityId);
                ClearLocalPlayerBinding(entity);
                entity.DisposeImmediately();
            }
            pendingEntityRemovalBuffer.Clear();
        }

        /// <summary>在本地玩家 Pawn 回收前清除基础镜头目标，同时保留独立 CameraRig 供后续切换目标或重建 Pawn 使用。</summary>
        private void ClearLocalPlayerBinding(Entity entity)
        {
            if (!ReferenceEquals(Player, entity)) return;
            if (TryGetSystem(out CameraDirectorSystem cameraDirector)) cameraDirector.ClearBaseTarget();
            ReleaseLocalPlayerControl();
            Player = null;
        }

        /// <summary>成对释放本地玩家租约与控制器，使玩家回收、重建和后续小队切换不会遗留重复控制器编号或幽灵租约。</summary>
        private void ReleaseLocalPlayerControl()
        {
            if (!TryGetSystem(out PossessionSystem possessionSystem))
            {
                localPlayerControlLease = default;
                localPlayerControllerRegistered = false;
                return;
            }
            if (localPlayerControlLease.IsValid) possessionSystem.ReleaseLease(localPlayerControlLease);
            localPlayerControlLease = default;
            if (localPlayerControllerRegistered) possessionSystem.UnregisterController(LocalPlayerControllerId);
            localPlayerControllerRegistered = false;
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
