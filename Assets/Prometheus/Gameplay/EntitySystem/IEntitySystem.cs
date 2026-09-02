using System;
using UnityEngine;
using Xuan.Prometheus.Component;
using Xuan.Prometheus.Logic;

namespace Xuan.Prometheus
{
    /// <summary>定义实体注册、查询、回收和属性监听的公共能力，隐藏实体调度与初始化实现。</summary>
    public interface IEntitySystem : ISystemContract
    {
        /// <summary>获取当前仍由系统托管的实体数量。</summary>
        int Count { get; }

        /// <summary>获取实体系统是否已经释放。</summary>
        bool IsDisposed { get; }

        /// <summary>在指定世界坐标创建并注册一只当前配置的敌人。</summary>
        SlimeEntity SpawnEnemy(Vector3 worldPosition);

        /// <summary>注册一个已经构造完成的实体并返回运行时编号。</summary>
        int AddEntity(Entity entity);

        /// <summary>按运行时编号查询仍由系统托管的实体。</summary>
        bool TryGetEntity(int entityId, out Entity entity);

        /// <summary>立即移除指定实体；更新阶段调用时转为安全回收。</summary>
        bool RemoveEntity(int entityId);

        /// <summary>请求在安全边界移除指定实体。</summary>
        bool RequestRemoveEntity(int entityId, float destroyDelay = 0f);

        /// <summary>监听指定实体组件的可修改属性。</summary>
        ListenHandle Listen<TComponent>(int entityId, Func<TComponent, ModifiableProperty> fieldSelector, Action<TComponent> onDirty, bool invokeImmediately = true) where TComponent : IComponent;
    }
}
