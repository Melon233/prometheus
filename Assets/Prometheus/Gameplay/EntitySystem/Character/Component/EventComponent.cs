using System;
using System.Collections.Generic;

namespace Xuan.Prometheus
{
    /// <summary>通知生命值已经发生变化，供界面等只读表现系统刷新。</summary>
    public class HpChangedEvent : IEvent
    {
        /// <summary>获取生命值变化前的数值。</summary>
        public float oldHp;
        /// <summary>获取生命值变化后的数值。</summary>
        public float newHp;
        /// <summary>获取变化发生时的生命值上限。</summary>
        public float maxHp;
    }

    /// <summary>通知实体已经完成唯一一次死亡跃迁。</summary>
    public class DieEvent : IEvent { }

    /// <summary>表示一次非致死实际伤害的打断等级严格高于目标抗打断等级，供受击动画等一次性表现订阅。</summary>
    public sealed class StaggeredEvent : IEvent
    {
        /// <summary>获取触发本次打断的实际扣血量。</summary>
        public float ActualDamage { get; }

        /// <summary>获取本次伤害的打断等级。</summary>
        public int StaggerLevel { get; }

        /// <summary>获取判定时目标的最终抗打断等级，已计入霸体覆盖。</summary>
        public float StaggerResistance { get; }

        /// <summary>创建一条已经通过分级打断判定的受击表现事实。</summary>
        public StaggeredEvent(float actualDamage, int staggerLevel, float staggerResistance)
        {
            ActualDamage = actualDamage;
            StaggerLevel = staggerLevel;
            StaggerResistance = staggerResistance;
        }
    }

    /// <summary>
    /// 表示一次伤害**没能**打断目标：目标正常受伤，但动作不中断。
    ///
    /// 与 <see cref="StaggeredEvent"/> 分成两条事实而不是用一个布尔字段：
    /// 订阅打断的是动画，订阅未打断的是闪白与命中特效（08 第 2.3 节），两者没有共同的消费方。
    /// </summary>
    public sealed class StaggerResistedEvent : IEvent
    {
        /// <summary>获取本次伤害的实际扣血量。</summary>
        public float ActualDamage { get; }

        /// <summary>获取本次伤害的打断等级。</summary>
        public int StaggerLevel { get; }

        /// <summary>获取判定时目标的最终抗打断等级，已计入霸体覆盖。</summary>
        public float StaggerResistance { get; }

        /// <summary>创建一条打断未成立的受击表现事实。</summary>
        public StaggerResistedEvent(float actualDamage, int staggerLevel, float staggerResistance)
        {
            ActualDamage = actualDamage;
            StaggerLevel = staggerLevel;
            StaggerResistance = staggerResistance;
        }
    }

    /// <summary>表示 PropertyComponent 聚合控制状态发生变化后的只读事实，供表现层和调试工具订阅。</summary>
    public sealed class ControlStateChangedEvent : IEvent
    {
        /// <summary>获取变化前的控制状态集合。</summary>
        public Xuan.Prometheus.Component.ControlState PreviousStates { get; }

        /// <summary>获取变化后的控制状态集合。</summary>
        public Xuan.Prometheus.Component.ControlState CurrentStates { get; }

        /// <summary>创建一条包含变化前后完整快照的控制状态事件。</summary>
        public ControlStateChangedEvent(Xuan.Prometheus.Component.ControlState previousStates, Xuan.Prometheus.Component.ControlState currentStates)
        {
            PreviousStates = previousStates;
            CurrentStates = currentStates;
        }
    }

    /// <summary>保存单个实体的类型化事件监听器，仅承载事实通知而不维护可变玩法状态。</summary>
    public class EventComponent : Component.Component, Component.IListenerHost
    {
        /// <summary>按事件具体类型保存当前实体的全部同步监听器。</summary>
        private readonly Dictionary<Type, Delegate> eventDict = new Dictionary<Type, Delegate>();

        /// <summary>为指定事件类型添加监听器。</summary>
        public void AddListener<T>(Action<T> action) where T : IEvent
        {
            Type type = typeof(T);
            if (eventDict.TryGetValue(type, out Delegate callbacks)) eventDict[type] = Delegate.Combine(callbacks, action);
            else eventDict[type] = action;
        }

        /// <summary>移除指定事件类型中的目标监听器，并在列表为空时移除字典项。</summary>
        public void RemoveListener<T>(Action<T> action) where T : IEvent
        {
            Type type = typeof(T);
            if (!eventDict.TryGetValue(type, out Delegate callbacks)) return;
            callbacks = Delegate.Remove(callbacks, action);
            if (callbacks == null) eventDict.Remove(type);
            else eventDict[type] = callbacks;
        }

        /// <summary>同步通知当前实体中指定事件类型的全部监听器。</summary>
        public void Invoke<T>(T evt) where T : IEvent
        {
            if (eventDict.TryGetValue(typeof(T), out Delegate callbacks)) ((Action<T>)callbacks)?.Invoke(evt);
        }

        /// <summary>清除当前实体的全部监听器，由 Entity 最终回收阶段调用以阻断延迟动画回调持有的失效订阅。</summary>
        public void ClearListeners()
        {
            eventDict.Clear();
        }
    }
}
