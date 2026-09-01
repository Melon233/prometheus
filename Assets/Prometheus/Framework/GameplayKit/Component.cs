using System;
using Xuan.Prometheus.Logic;
using Xuan.Prometheus.Logic;

namespace Xuan.Prometheus.Component
{
    public interface IComponent
    {
        public Entity Entity { get; set; }
    }

    // [AttributeUsage(AttributeTargets.Field)]
    // public class InjectDataAttribute : Attribute
    // {
    // }

    public abstract class Component : IComponent
    {
        public Entity Entity { get; set; }
    }

    /// <summary>定义需要从根 EntityBinder 复制 Prefab 配置或订阅 Unity 桥接器的纯 C# Component。</summary>
    public interface IEntityBinderComponent
    {
        /// <summary>在 GameObjectLogic 取得并校验根 Binder 后初始化当前 Component。</summary>
        void Bind(EntityBinder binder);

        /// <summary>在 GameObjectLogic 最后释放表现对象前解除 Unity 引用和回调。</summary>
        void Unbind();
    }
}
