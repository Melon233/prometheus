using System;
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

    /// <summary>
    /// 标记在实体最终回收时需要主动清空自身订阅的 Component。
    /// Entity 只认识这个能力，不需要认识任何具体的事件组件类型。
    /// </summary>
    public interface IListenerHost
    {
        /// <summary>清空该组件持有的全部订阅，阻断延迟回调继续访问已回收实体。</summary>
        void ClearListeners();
    }

    /// <summary>
    /// 由 Component 向 Entity 声明当前允许的行为能力，使 Logic 调度不必认识任何具体属性组件。
    /// 一个 Entity 最多只应存在一个实现该端口的 Component。
    /// </summary>
    public interface IControlStateProvider
    {
        /// <summary>当前是否允许执行一般行为。</summary>
        bool CanAct { get; }

        /// <summary>当前是否允许移动。</summary>
        bool CanMove { get; }

        /// <summary>当前是否允许使用主动技能。</summary>
        bool CanUseActiveSkill { get; }
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
