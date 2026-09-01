using System;
using UnityEngine;
using Xuan.Prometheus.Component;

namespace Xuan.Prometheus.Logic
{
    /// <summary>作为第一个普通 Logic 创建或接管 Entity 表现，并在全部 Gameplay Logic 释放后最后清理表现对象。</summary>
    public sealed class GameObjectLogic : Logic
    {
        private GameObjectComponent gameObjectComponent;

        /// <summary>把表现绑定放在现有排序协议的最早阶段，并声明其不受角色控制状态影响。</summary>
        public GameObjectLogic()
        {
            OrderTag = OrderTag.GameObject;
            ControlRequirement = LogicControlRequirement.None;
        }

        /// <summary>按 SpawnSpec 创建或接管对象，只读取一次根 Binder，完成类型与引用校验后写入 GameObjectComponent。</summary>
        public override void AfterNew()
        {
            if (!Entity.TryGetComp(out gameObjectComponent)) throw new InvalidOperationException($"Entity '{Entity.GetType().FullName}' requires GameObjectComponent before GameObjectLogic.");
            GameObjectSpawnSpec spec = gameObjectComponent.SpawnSpec;
            GameObject instance = spec.Ownership == GameObjectOwnership.Spawned ? Core.Asset.InstantiateSync(spec.Location, spec.Position, spec.Rotation, spec.Parent) : spec.Instance;
            if (instance == null) throw new InvalidOperationException($"Entity '{Entity.GetType().FullName}' could not obtain its GameObject representation.");
            bool published = false;
            try
            {
                EntityBinder[] binders = instance.GetComponents<EntityBinder>();
                if (binders.Length != 1) throw new InvalidOperationException($"Entity GameObject '{instance.name}' requires exactly one root EntityBinder but found {binders.Length}.");
                EntityBinder binder = binders[0];
                if (binder.GetType() != spec.BinderType) throw new InvalidOperationException($"Entity GameObject '{instance.name}' requires Binder '{spec.BinderType.FullName}' but found '{binder.GetType().FullName}'.");
                binder.Validate();
                binder.BindHost(Entity);
                gameObjectComponent.Bind(instance, binder);
                Entity.bindGo = instance;
                published = true;
                Entity.BindComponents(binder);
            }
            catch
            {
                if (!published && spec.Ownership == GameObjectOwnership.Spawned)
                {
                    if (Application.isPlaying) UnityEngine.Object.Destroy(instance);
                    else UnityEngine.Object.DestroyImmediate(instance);
                }
                throw;
            }
        }

        /// <inheritdoc />
        public override bool CanEnable() => false;

        /// <inheritdoc />
        public override bool CanDisable() => false;

        /// <inheritdoc />
        public override void OnEnable() { }

        /// <inheritdoc />
        public override void OnDisable() { }

        /// <inheritdoc />
        public override void OnUpdate(float dt) { }

        /// <summary>最后清除 Binder 引用，并仅销毁由 Entity 自己创建和拥有的表现对象。</summary>
        public override void OnDispose()
        {
            if (gameObjectComponent == null) return;
            GameObject instance = gameObjectComponent.Instance;
            EntityBinder binder = gameObjectComponent.Binder;
            GameObjectOwnership ownership = gameObjectComponent.SpawnSpec.Ownership;
            Entity.UnbindComponents();
            if (binder != null) binder.UnbindHost(Entity);
            gameObjectComponent.Clear();
            Entity.bindGo = null;
            if (ownership == GameObjectOwnership.Spawned && instance != null)
            {
                if (Application.isPlaying) UnityEngine.Object.Destroy(instance, Entity.DisposeDelay);
                else UnityEngine.Object.DestroyImmediate(instance);
            }
            gameObjectComponent = null;
        }
    }
}
