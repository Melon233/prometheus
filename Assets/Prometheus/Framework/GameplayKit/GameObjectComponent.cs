using System;
using UnityEngine;

namespace Xuan.Prometheus.Component
{
    /// <summary>保存 Entity 表现对象的创建规格、实例、根 Binder 和所有权，不承载玩法业务状态。</summary>
    public sealed class GameObjectComponent : Component
    {
        /// <summary>使用 Entity 构造阶段提供的不可变表现规格创建组件。</summary>
        public GameObjectComponent(Logic.GameObjectSpawnSpec spawnSpec)
        {
            SpawnSpec = spawnSpec ?? throw new ArgumentNullException(nameof(spawnSpec));
        }

        /// <summary>获取 Entity 构造阶段声明的表现创建规格。</summary>
        public Logic.GameObjectSpawnSpec SpawnSpec { get; }

        /// <summary>获取 GameObjectLogic 已创建或接管的表现对象。</summary>
        public GameObject Instance { get; private set; }

        /// <summary>获取表现对象根节点上经过校验的唯一 Binder。</summary>
        public Logic.EntityBinder Binder { get; private set; }

        /// <summary>由 GameObjectLogic 原子发布已经完成校验的表现对象与 Binder。</summary>
        internal void Bind(GameObject instance, Logic.EntityBinder binder)
        {
            if (Instance != null || Binder != null) throw new InvalidOperationException("GameObjectComponent has already bound a representation.");
            Instance = instance != null ? instance : throw new ArgumentNullException(nameof(instance));
            Binder = binder != null ? binder : throw new ArgumentNullException(nameof(binder));
        }

        /// <summary>在表现对象释放前清除运行时引用，避免已回收 Entity 继续访问 Unity 对象。</summary>
        internal void Clear()
        {
            Instance = null;
            Binder = null;
        }
    }
}
