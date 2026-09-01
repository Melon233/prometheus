using System;
using UnityEngine;

namespace Xuan.Prometheus.Logic
{
    /// <summary>定义 Entity 表现对象的创建来源和最终释放责任。</summary>
    public enum GameObjectOwnership
    {
        /// <summary>对象由 Entity 通过 AssetKit 创建，Entity 回收时负责销毁。</summary>
        Spawned,
        /// <summary>对象由场景拥有，Entity 只接管和解绑而不销毁。</summary>
        SceneBound
    }

    /// <summary>保存创建或接管 Entity 表现对象需要的全部不可变参数。</summary>
    public sealed class GameObjectSpawnSpec
    {
        private GameObjectSpawnSpec(string location, Vector3 position, Quaternion rotation, Transform parent, GameObject instance, Type binderType, GameObjectOwnership ownership)
        {
            Location = location;
            Position = position;
            Rotation = rotation;
            Parent = parent;
            Instance = instance;
            BinderType = binderType ?? throw new ArgumentNullException(nameof(binderType));
            Ownership = ownership;
        }

        /// <summary>获取 AssetKit 使用的资源地址；场景接管模式为空。</summary>
        public string Location { get; }

        /// <summary>获取实例化时使用的世界坐标。</summary>
        public Vector3 Position { get; }

        /// <summary>获取实例化时使用的世界旋转。</summary>
        public Quaternion Rotation { get; }

        /// <summary>获取表现对象的运行时父节点。</summary>
        public Transform Parent { get; }

        /// <summary>获取场景接管模式提供的现有对象。</summary>
        public GameObject Instance { get; }

        /// <summary>获取该 Entity 要求的根 Binder 具体类型。</summary>
        public Type BinderType { get; }

        /// <summary>获取表现对象的所有权模式。</summary>
        public GameObjectOwnership Ownership { get; }

        /// <summary>创建一份由 Entity 自己通过 AssetKit 实例化并负责销毁的表现规格。</summary>
        public static GameObjectSpawnSpec Spawned<TBinder>(string location, Vector3 position, Quaternion rotation, Transform parent) where TBinder : EntityBinder
        {
            if (string.IsNullOrWhiteSpace(location)) throw new ArgumentException("Spawned GameObject location cannot be empty.", nameof(location));
            return new GameObjectSpawnSpec(location.Trim(), position, rotation, parent, null, typeof(TBinder), GameObjectOwnership.Spawned);
        }

        /// <summary>创建一份只接管现有场景对象且不负责销毁的表现规格。</summary>
        public static GameObjectSpawnSpec SceneBound<TBinder>(GameObject instance) where TBinder : EntityBinder
        {
            if (instance == null) throw new ArgumentNullException(nameof(instance));
            return new GameObjectSpawnSpec(string.Empty, instance.transform.position, instance.transform.rotation, instance.transform.parent, instance, typeof(TBinder), GameObjectOwnership.SceneBound);
        }
    }
}
