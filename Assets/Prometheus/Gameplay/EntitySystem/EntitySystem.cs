using System;
using System.Collections.Generic;
using UnityEngine;
using Xuan.Prometheus.Component;
using Xuan.Prometheus.Logic;

namespace Xuan.Prometheus
{
    /// <summary>表示一条可由调用方精确释放的数值脏监听；重复释放不会产生副作用。</summary>
    public sealed class ListenHandle : IDisposable
    {
        /// <summary>保存唯一一次注销监听需要执行的委托。</summary>
        private Action disposeAction;

        /// <summary>创建一条由属性或系统持有注销逻辑的监听句柄。</summary>
        internal ListenHandle(Action disposeAction)
        {
            this.disposeAction = disposeAction ?? throw new ArgumentNullException(nameof(disposeAction));
        }

        /// <summary>获取当前监听是否已经完成注销。</summary>
        public bool IsDisposed => disposeAction == null;

        /// <summary>执行唯一一次注销并断开对回调目标的引用。</summary>
        public void Dispose()
        {
            Action action = disposeAction;
            if (action == null) return;
            disposeAction = null;
            action.Invoke();
        }
    }

    /// <summary>集中管理单局 Entity 的注册、查询、逐帧调度、安全回收和字段监听。</summary>
    public sealed class EntitySystem : XSystem
    {
        /// <summary>保存当前单局统一使用的敌人预制体地址。</summary>
        private string enemyLocation;

        /// <summary>保存敌人实例所属的跨场景运行时根节点。</summary>
        private Transform enemyRuntimeRoot;

        /// <summary>保存全部已注册 Entity，并维持稳定的逐帧更新顺序。</summary>
        private readonly XMap<int, Entity> entities = new XMap<int, Entity>();

        /// <summary>保存等待安全边界处理的 EntityId 与场景对象延迟销毁时间。</summary>
        private readonly Dictionary<int, float> pendingEntityRemovals = new Dictionary<int, float>();

        /// <summary>复用回收编号快照，避免遍历待回收字典时直接修改集合。</summary>
        private readonly List<int> pendingEntityRemovalBuffer = new List<int>();

        /// <summary>保存当前单局仍处于活动状态的字段监听句柄。</summary>
        private readonly HashSet<ListenHandle> activeHandles = new HashSet<ListenHandle>();

        /// <summary>按 EntityId 归组字段监听，使实体回收时能立即释放对应回调。</summary>
        private readonly Dictionary<int, HashSet<ListenHandle>> entityHandles = new Dictionary<int, HashSet<ListenHandle>>();

        /// <summary>保存下一个可分配的单局运行时编号；零始终表示无效 Entity。</summary>
        private int nextEntityId = 1;

        /// <summary>标记当前是否正在遍历 Entity；帧内直接移除会自动转为安全回收请求。</summary>
        private bool isUpdatingEntities;

        /// <summary>标记当前系统是否正在释放全部 Entity 与监听。</summary>
        private bool isDisposing;

        /// <summary>标记当前系统已经完成最终释放。</summary>
        private bool isDisposed;

        /// <summary>获取当前仍由系统托管的 Entity 数量。</summary>
        public int Count { get; private set; }

        /// <summary>获取系统是否已经完成释放。</summary>
        public bool IsDisposed => isDisposed;

        /// <summary>配置当前单局的敌人实例化上下文，使初始出生点和世界 POI 可以复用同一条创建链路。</summary>
        internal void ConfigureEnemySpawner(string location, Transform runtimeRoot)
        {
            enemyLocation = !string.IsNullOrWhiteSpace(location) ? location : throw new ArgumentException("Enemy asset location cannot be empty.", nameof(location));
            enemyRuntimeRoot = runtimeRoot != null ? runtimeRoot : throw new ArgumentNullException(nameof(runtimeRoot));
        }

        /// <summary>在指定世界坐标创建、注册并初始化一只当前单局配置的史莱姆。</summary>
        public SlimeEntity SpawnEnemy(Vector3 worldPosition)
        {
            ThrowIfDisposed();
            int entityId = 0;
            try
            {
                SlimeEntity enemy = new SlimeEntity(enemyLocation, worldPosition, Quaternion.identity, enemyRuntimeRoot);
                entityId = AddEntity(enemy);
                enemy.AfterNew();
                return enemy;
            }
            catch
            {
                if (entityId > 0) RemoveEntity(entityId);
                throw;
            }
        }

        /// <summary>注册一个已经完成构造的 Entity、写入单局运行时编号并纳入统一生命周期。</summary>
        public int AddEntity(Entity entity)
        {
            ThrowIfDisposed();
            if (isDisposing) throw new InvalidOperationException("EntitySystem cannot register an Entity while it is disposing.");
            if (isUpdatingEntities) throw new InvalidOperationException("EntitySystem cannot register an Entity while the Entity collection is updating.");
            if (entity == null) throw new ArgumentNullException(nameof(entity));
            int entityId = nextEntityId++;
            entity.BindEntityId(entityId);
            entities.Add(entityId, entity);
            Count++;
            return entityId;
        }

        /// <summary>按单局运行时编号查询仍由当前系统托管的 Entity。</summary>
        public bool TryGetEntity(int entityId, out Entity entity)
        {
            if (isDisposed)
            {
                entity = null;
                return false;
            }
            return entities.TryGet(entityId, out entity);
        }

        /// <summary>立即移除并释放指定 Entity；更新遍历期间调用会自动转为安全边界回收。</summary>
        public bool RemoveEntity(int entityId)
        {
            if (isDisposed || isDisposing) return false;
            if (isUpdatingEntities) return RequestRemoveEntity(entityId, 0f);
            if (!entities.TryGet(entityId, out Entity entity)) return false;
            pendingEntityRemovals.Remove(entityId);
            RemoveRegisteredEntity(entityId, entity);
            return true;
        }

        /// <summary>请求在当前帧安全边界移除指定 Entity；首次请求决定场景对象的延迟销毁时间。</summary>
        public bool RequestRemoveEntity(int entityId, float destroyDelay = 0f)
        {
            if (isDisposed || isDisposing) return false;
            if (!entities.TryGet(entityId, out Entity entity)) return false;
            if (pendingEntityRemovals.ContainsKey(entityId)) return false;
            float safeDelay = Mathf.Max(0f, destroyDelay);
            if (!entity.MarkDespawnRequested(safeDelay)) return false;
            pendingEntityRemovals.Add(entityId, safeDelay);
            return true;
        }

        /// <summary>监听指定 Entity 的指定组件字段；注册成功后默认立即回调一次，后续仅在字段最终值实际变化时回调。</summary>
        public ListenHandle Listen<TComponent>(int entityId, Func<TComponent, ModifiableProperty> fieldSelector, Action<TComponent> onDirty, bool invokeImmediately = true) where TComponent : IComponent
        {
            ThrowIfDisposed();
            if (entityId <= 0) throw new ArgumentOutOfRangeException(nameof(entityId), entityId, "Listened EntityId must be positive.");
            if (fieldSelector == null) throw new ArgumentNullException(nameof(fieldSelector));
            if (onDirty == null) throw new ArgumentNullException(nameof(onDirty));
            if (!TryGetEntity(entityId, out Entity entity)) throw new InvalidOperationException($"EntitySystem cannot find Entity {entityId}.");
            if (!entity.TryGetComp(out TComponent component)) throw new InvalidOperationException($"Entity {entityId} does not contain component '{typeof(TComponent).FullName}'.");
            ModifiableProperty property = fieldSelector.Invoke(component) ?? throw new InvalidOperationException($"The selected field on component '{typeof(TComponent).FullName}' is null.");
            ListenHandle propertyHandle = null;
            ListenHandle systemHandle = null;
            systemHandle = new ListenHandle(() =>
            {
                propertyHandle?.Dispose();
                propertyHandle = null;
                activeHandles.Remove(systemHandle);
                if (entityHandles.TryGetValue(entityId, out HashSet<ListenHandle> handles))
                {
                    handles.Remove(systemHandle);
                    if (handles.Count == 0) entityHandles.Remove(entityId);
                }
            });
            activeHandles.Add(systemHandle);
            if (!entityHandles.TryGetValue(entityId, out HashSet<ListenHandle> entityHandleSet))
            {
                entityHandleSet = new HashSet<ListenHandle>();
                entityHandles.Add(entityId, entityHandleSet);
            }
            entityHandleSet.Add(systemHandle);
            try
            {
                propertyHandle = property.Listen(() =>
                {
                    if (component.Entity == null || component.Entity.EntityId != entityId) return;
                    onDirty.Invoke(component);
                }, invokeImmediately);
                return systemHandle;
            }
            catch
            {
                systemHandle.Dispose();
                throw;
            }
        }

        /// <summary>由 GameplayKit 在系统前置更新前调用，处理上一阶段积累的安全回收请求。</summary>
        internal void DrainPendingEntityRemovals()
        {
            if (isDisposed || isDisposing || pendingEntityRemovals.Count == 0) return;
            pendingEntityRemovalBuffer.Clear();
            foreach (int entityId in pendingEntityRemovals.Keys) pendingEntityRemovalBuffer.Add(entityId);
            for (int index = 0; index < pendingEntityRemovalBuffer.Count; index++)
            {
                int entityId = pendingEntityRemovalBuffer[index];
                pendingEntityRemovals.Remove(entityId);
                if (!entities.TryGet(entityId, out Entity entity)) continue;
                RemoveRegisteredEntity(entityId, entity);
            }
            pendingEntityRemovalBuffer.Clear();
        }

        /// <summary>由 GameplayKit 在全部 System 前置阶段结束后调用，稳定驱动当前所有 Active Entity。</summary>
        internal void UpdateEntities(float dt)
        {
            if (isDisposed || isDisposing) return;
            DrainPendingEntityRemovals();
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
        }

        /// <summary>根据玩法启动参数创建三人小队与场景敌人，并将所有实例纳入当前系统托管。</summary>
        internal void CreateInitialEntities(GameplayStartupOptions startupOptions, TeamSystem teamSystem)
        {
            ThrowIfDisposed();
            if (startupOptions == null) throw new ArgumentNullException(nameof(startupOptions));
            if (teamSystem == null) throw new ArgumentNullException(nameof(teamSystem));
            CreateTeam(startupOptions, teamSystem);
            CreateEnemies(startupOptions);
        }

        /// <summary>单局结束时先释放字段监听，再按稳定顺序释放全部 Entity。</summary>
        public override void Dispose()
        {
            if (isDisposed || isDisposing) return;
            isDisposing = true;
            ListenHandle[] handles = new ListenHandle[activeHandles.Count];
            activeHandles.CopyTo(handles);
            foreach (ListenHandle handle in handles) handle.Dispose();
            activeHandles.Clear();
            entityHandles.Clear();
            foreach (Entity entity in entities)
            {
                entity.MarkDespawnRequested(0f);
                entity.DisposeImmediately();
            }
            pendingEntityRemovals.Clear();
            pendingEntityRemovalBuffer.Clear();
            entities.Dispose();
            Count = 0;
            isUpdatingEntities = false;
            isDisposed = true;
            isDisposing = false;
        }

        /// <summary>解除 Entity 的字段监听和小队关系，再从容器移除并执行最终清理。</summary>
        private void RemoveRegisteredEntity(int entityId, Entity entity)
        {
            DisposeEntityListeners(entityId);
            if (Core.Gameplay.TryGetSystem(out TeamSystem teamSystem)) teamSystem.UnregisterMember(entity);
            entities.Remove(entityId);
            Count--;
            entity.MarkDespawnRequested(0f);
            entity.DisposeImmediately();
        }

        /// <summary>释放指定 Entity 持有的全部字段监听，避免已回收组件继续持有界面回调。</summary>
        private void DisposeEntityListeners(int entityId)
        {
            if (!entityHandles.TryGetValue(entityId, out HashSet<ListenHandle> handles)) return;
            ListenHandle[] handleBuffer = new ListenHandle[handles.Count];
            handles.CopyTo(handleBuffer);
            foreach (ListenHandle handle in handleBuffer) handle.Dispose();
            entityHandles.Remove(entityId);
        }

        /// <summary>从三个固定槽位配置创建独立 PlayerEntity，并在全部成员就绪后交给 TeamSystem 原子初始化。</summary>
        private void CreateTeam(GameplayStartupOptions startupOptions, TeamSystem teamSystem)
        {
            List<Entity> createdMembers = new List<Entity>(TeamSystem.Capacity);
            try
            {
                for (int slotIndex = 0; slotIndex < TeamSystem.Capacity; slotIndex++)
                {
                    //UnityEditor.TransformWorldPlacementJSON:{"position":{"x":277.2999572753906,"y":1.0,"z":1068.099853515625},"rotation":{"x":0.0,"y":0.0,"z":0.0,"w":1.0000001192092896},"scale":{"x":1.0,"y":1.0,"z":1.0}}
                    int entityId = 0;
                    try
                    {
                        PlayerEntity member = new PlayerEntity(startupOptions.TeamMemberLocations[slotIndex], new Vector3(277f, 0.95f, 1068f), Quaternion.identity, startupOptions.RuntimeRoot);
                        entityId = AddEntity(member);
                        member.AfterNew();
                        createdMembers.Add(member);
                    }
                    catch
                    {
                        if (entityId > 0) RemoveEntity(entityId);
                        throw;
                    }
                }
                teamSystem.InitializeMembers(createdMembers);
            }
            catch
            {
                for (int index = createdMembers.Count - 1; index >= 0; index--)
                {
                    Entity member = createdMembers[index];
                    if (member != null && !member.IsDespawningOrDisposed) RemoveEntity(member.EntityId);
                }
                throw;
            }
        }

        /// <summary>遍历入口配置的世界坐标创建敌人，并按照启动参数限制有效实例数量。</summary>
        private void CreateEnemies(GameplayStartupOptions startupOptions)
        {
            int createdCount = 0;
            foreach (Vector3 spawnPosition in startupOptions.EnemySpawnPositions)
            {
                SpawnEnemy(spawnPosition);
                createdCount++;
                if (startupOptions.EnemySpawnLimit > 0 && createdCount >= startupOptions.EnemySpawnLimit) break;
            }
        }

        /// <summary>防止已经释放的实体系统被重新注册 Entity 或监听。</summary>
        private void ThrowIfDisposed()
        {
            if (isDisposed) throw new ObjectDisposedException(nameof(EntitySystem));
        }
    }
}
