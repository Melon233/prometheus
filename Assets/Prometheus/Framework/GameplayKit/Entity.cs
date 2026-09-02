using System;
using System.Collections.Generic;
using UnityEngine;
using Xuan.Prometheus.Component;

namespace Xuan.Prometheus.Logic
{
    /// <summary>定义 Entity 内 Logic 的逐帧执行阶段，枚举顺序就是默认执行顺序。</summary>
    public enum OrderTag
    {
        /// <summary>最先创建或接管 Entity 的 Unity 表现对象，并发布根 Binder。</summary>
        GameObject,
        Input,
        Talent,
        Buff,
        Gameplay,
        Bag,
        Controller,
        Formation,
        Build,
        Item,
        Quest,
        Compound,
        AfterGameplay
    }

    /// <summary>标记可被 Entity 注册的 Logic 类型。</summary>
    public class LogicAttribute : Attribute
    {
    }

    /// <summary>标记可被 Entity 注册的数据类型。</summary>
    public class DataAttribute : Attribute
    {
    }

    /// <summary>描述 Entity 从构造、注册、运行到回收的唯一生命周期状态。</summary>
    public enum EntityLifecycleState
    {
        /// <summary>Entity 正在构造组件和 Logic，尚未注册到 GameplayKit。</summary>
        Created,
        /// <summary>Entity 已获得运行时编号并绑定 GameplayKit，但尚未完成 Logic 初始化。</summary>
        Registered,
        /// <summary>Entity 已完成初始化，可以参与逐帧更新。</summary>
        Active,
        /// <summary>Entity 已请求回收，立即停止更新并等待 GameplayKit 在安全边界移除。</summary>
        DespawnRequested,
        /// <summary>Entity 已完成 Logic、Component 和场景对象清理，不能再次使用。</summary>
        Disposed
    }

    /// <summary>组合一个运行时对象的组件与 Logic，并与所属 GameplayKit 协同管理完整生命周期。</summary>
    public abstract class Entity
    {
        private readonly Dictionary<Type, IComponent> comps = new Dictionary<Type, IComponent>();
        private readonly List<ILogic> logicList = new List<ILogic>();
        private readonly Dictionary<Type, ILogic> logics = new Dictionary<Type, ILogic>();
        private readonly Dictionary<ILogic, int> logicRegistrationOrders = new Dictionary<ILogic, int>();
        private int nextLogicRegistrationOrder;
        private int initializedLogicCount;
        private float destroyDelay;

        /// <summary>获取首次回收请求确定的表现对象延迟销毁时间，供最后释放的 GameObjectLogic 使用。</summary>
        internal float DisposeDelay => destroyDelay;

        /// <summary>获取 GameplayKit 分配的单局运行时编号；未注册实体返回零，已释放实体保留原编号用于诊断。</summary>
        public int EntityId { get; private set; }

        /// <summary>获取当前生命周期状态。</summary>
        public EntityLifecycleState LifecycleState { get; private set; } = EntityLifecycleState.Created;

        /// <summary>获取当前实体是否已经完成初始化并允许参与逐帧玩法更新。</summary>
        public bool IsActive => LifecycleState == EntityLifecycleState.Active;

        /// <summary>获取当前实体是否已经进入回收流程或完成回收。</summary>
        public bool IsDespawningOrDisposed => LifecycleState == EntityLifecycleState.DespawnRequested || LifecycleState == EntityLifecycleState.Disposed;

        /// <summary>当前实体绑定的场景对象；资源定位和实例化由 GameplayKit 负责。</summary>
        public GameObject bindGo;

        /// <summary>由 EntitySystem 在实体初始化前写入当前单局唯一运行时编号。</summary>
        internal void BindEntityId(int entityId)
        {
            if (entityId <= 0) throw new ArgumentOutOfRangeException(nameof(entityId), entityId, "Entity runtime ID must be positive.");
            if (LifecycleState != EntityLifecycleState.Created) throw new InvalidOperationException($"Entity '{GetType().FullName}' cannot be registered from lifecycle state '{LifecycleState}'.");
            EntityId = entityId;
            LifecycleState = EntityLifecycleState.Registered;
        }

        /// <summary>初始化全部 Logic，并在初始化完成后按照 OrderTag 与注册序号建立稳定执行顺序。</summary>
        public void AfterNew()
        {
            if (LifecycleState != EntityLifecycleState.Registered) throw new InvalidOperationException($"Entity '{GetType().FullName}' cannot initialize from lifecycle state '{LifecycleState}'.");
            SortLogics();
            try
            {
                for (int index = 0; index < logicList.Count; index++)
                {
                    initializedLogicCount = index + 1;
                    logicList[index].AfterNew();
                }
                SortLogics();
                LifecycleState = EntityLifecycleState.Active;
            }
            catch
            {
                RequestDispose(0f);
                throw;
            }
        }

        /// <summary>驱动 Active Entity 的全部 Logic；帧内请求回收后立即终止剩余 Logic，避免死亡后的额外行动。</summary>
        public void OnUpdate(float dt)
        {
            if (LifecycleState != EntityLifecycleState.Active) return;
            for (int index = 0; index < logicList.Count; index++)
            {
                ILogic logic = logicList[index];
                CheckLogic(logic);
                if (LifecycleState != EntityLifecycleState.Active) break;
                if (logic.Enable && logic.BlockCnt == 0) logic.OnUpdate(dt);
                if (LifecycleState != EntityLifecycleState.Active) break;
            }
        }

        /// <summary>请求所属 EntitySystem 在本帧安全边界移除当前实体，首次请求决定场景对象的延迟销毁时间。</summary>
        public bool RequestDispose(float delay = 2f)
        {
            if (IsDespawningOrDisposed) return false;
            float safeDelay = Mathf.Max(0f, delay);
            if (LifecycleState == EntityLifecycleState.Registered || LifecycleState == EntityLifecycleState.Active) return Core.Gameplay.GetSystem<IEntitySystem>().RequestRemoveEntity(EntityId, safeDelay);
            if (!MarkDespawnRequested(safeDelay)) return false;
            DisposeImmediately();
            return true;
        }

        /// <summary>保留旧调用入口并转交给幂等的 RequestDispose，调用方无需自行驱动 Entity 更新执行释放。</summary>
        public void OnDispose(float delay = 2f)
        {
            RequestDispose(delay);
        }

        /// <summary>由 GameplayKit 标记首次回收请求，使 Entity 立即停止参与逐帧更新。</summary>
        internal bool MarkDespawnRequested(float delay)
        {
            if (IsDespawningOrDisposed) return false;
            destroyDelay = Mathf.Max(0f, delay);
            LifecycleState = EntityLifecycleState.DespawnRequested;
            return true;
        }

        /// <summary>由 GameplayKit 在安全边界执行一次最终清理，并保证禁用、注销和解绑阶段都只执行一次。</summary>
        internal bool DisposeImmediately()
        {
            if (LifecycleState == EntityLifecycleState.Disposed) return false;
            if (LifecycleState != EntityLifecycleState.DespawnRequested) MarkDespawnRequested(0f);
            int cleanupCount = Mathf.Min(initializedLogicCount, logicList.Count);
            for (int index = cleanupCount - 1; index >= 0; index--)
            {
                ILogic logic = logicList[index];
                if (!logic.Enable) continue;
                logic.Enable = false;
                try
                {
                    logic.OnDisable();
                }
                catch (Exception exception)
                {
                    Debug.LogException(exception);
                }
            }
            for (int index = cleanupCount - 1; index >= 0; index--)
            {
                try
                {
                    logicList[index].OnDispose();
                }
                catch (Exception exception)
                {
                    Debug.LogException(exception);
                }
            }
            if (comps.TryGetValue(typeof(EventComponent), out IComponent eventComponent) && eventComponent is EventComponent typedEventComponent) typedEventComponent.ClearListeners();
            foreach (IComponent component in comps.Values) component.Entity = null;
            foreach (ILogic logic in logicList) logic.Entity = null;
            comps.Clear();
            logicList.Clear();
            logics.Clear();
            logicRegistrationOrders.Clear();
            LifecycleState = EntityLifecycleState.Disposed;
            return true;
        }

        /// <summary>根据阻塞计数、控制状态和 Logic 自身条件执行唯一一次启用或禁用跃迁。</summary>
        private void CheckLogic(ILogic logic)
        {
            bool controlAllowed = IsLogicAllowedByControlState(logic);
            if (!logic.Enable && logic.BlockCnt == 0 && controlAllowed && logic.CanEnable())
            {
                logic.Enable = true;
                try
                {
                    logic.OnEnable();
                }
                catch
                {
                    logic.Enable = false;
                    throw;
                }
            }
            else if (logic.Enable && (logic.BlockCnt != 0 || !controlAllowed || logic.CanDisable()))
            {
                logic.Enable = false;
                logic.OnDisable();
            }
        }

        /// <summary>根据 Logic 声明的能力需求查询 PropertyComponent；没有属性组件的基础设施实体维持原有调度行为。</summary>
        private bool IsLogicAllowedByControlState(ILogic logic)
        {
            LogicControlRequirement requirement = logic is Logic gameplayLogic ? gameplayLogic.ControlRequirement : LogicControlRequirement.Act;
            if (requirement == LogicControlRequirement.None) return true;
            if (!comps.TryGetValue(typeof(PropertyComponent), out IComponent component) || !(component is PropertyComponent property)) return true;
            if ((requirement & LogicControlRequirement.Act) != 0 && !property.CanAct) return false;
            if ((requirement & LogicControlRequirement.Move) != 0 && !property.CanMove) return false;
            if ((requirement & LogicControlRequirement.ActiveSkill) != 0 && !property.CanUseActiveSkill) return false;
            return true;
        }

        /// <summary>在 Entity 构造阶段创建并注册一个具体 Logic。</summary>
        public void AddLogic<T>() where T : ILogic, new()
        {
            AddLogic(new T());
        }

        /// <summary>在 Entity 构造阶段注册一个 Logic，并保存稳定注册序号作为同优先级排序依据。</summary>
        public void AddLogic<T>(T logic) where T : ILogic
        {
            EnsureCanCompose();
            if (ReferenceEquals(logic, null)) throw new ArgumentNullException(nameof(logic));
            Type logicType = logic.GetType();
            logics.Add(logicType, logic);
            logicList.Add(logic);
            logicRegistrationOrders.Add(logic, nextLogicRegistrationOrder++);
            logic.Entity = this;
        }

        /// <summary>在 Entity 构造阶段注册一个已经存在的具体组件。</summary>
        public void AddComp<T>(T comp) where T : IComponent
        {
            EnsureCanCompose();
            if (ReferenceEquals(comp, null)) throw new ArgumentNullException(nameof(comp), $"Entity '{GetType().FullName}' cannot register a null component of type '{typeof(T).FullName}'.");
            comps.Add(comp.GetType(), comp);
            comp.Entity = this;
        }

        /// <summary>在 Entity 构造阶段创建并注册一个普通 C# 组件。</summary>
        public void AddComp<T>() where T : IComponent, new()
        {
            AddComp<T>(new T());
        }

        /// <summary>按具体类型尝试获取当前 Entity 仍然持有的组件。</summary>
        public bool TryGetComp<T>(out T comp) where T : IComponent
        {
            if (comps.TryGetValue(typeof(T), out IComponent data) && data is T typedComponent)
            {
                comp = typedComponent;
                return true;
            }
            comp = default;
            return false;
        }

        /// <summary>按具体类型尝试获取当前 Entity 注册的 Logic，回收完成后始终返回 false。</summary>
        public bool TryGetLogic<T>(out T logic) where T : ILogic
        {
            if (logics.TryGetValue(typeof(T), out ILogic registeredLogic) && registeredLogic is T typedLogic)
            {
                logic = typedLogic;
                return true;
            }
            logic = default;
            return false;
        }

        /// <summary>由首位 GameObjectLogic 初始化构造阶段已经注册的全部 Binder 感知 Component，不改变 Entity 组成。</summary>
        internal void BindComponents(EntityBinder binder)
        {
            foreach (IComponent component in comps.Values)
            {
                if (component is IEntityBinderComponent binderComponent) binderComponent.Bind(binder);
            }
        }

        /// <summary>由最后释放的 GameObjectLogic 逆向解除全部 Binder 感知 Component 持有的 Unity 引用与回调。</summary>
        internal void UnbindComponents()
        {
            foreach (IComponent component in comps.Values)
            {
                if (component is IEntityBinderComponent binderComponent) binderComponent.Unbind();
            }
        }

        /// <summary>为指定 Logic 增加一层阻塞，并使用饱和保护避免计数溢出后意外启用。</summary>
        public void BlockLogic<T>() where T : ILogic
        {
            if (!TryGetLogic(out T logic)) throw new InvalidOperationException($"Entity '{GetType().FullName}' does not contain Logic '{typeof(T).FullName}'.");
            if (logic.BlockCnt == int.MaxValue) throw new OverflowException($"Logic '{typeof(T).FullName}' block count reached Int32.MaxValue.");
            logic.BlockCnt++;
        }

        /// <summary>为指定 Logic 释放一层阻塞，重复释放会稳定停留在零。</summary>
        public void UnBlockLogic<T>() where T : ILogic
        {
            if (!TryGetLogic(out T logic)) throw new InvalidOperationException($"Entity '{GetType().FullName}' does not contain Logic '{typeof(T).FullName}'.");
            if (logic.BlockCnt > 0) logic.BlockCnt--;
        }

        /// <summary>按照 OrderTag 和注册序号稳定排序，避免相同 OrderTag 产生不确定执行顺序。</summary>
        private void SortLogics()
        {
            logicList.Sort((left, right) =>
            {
                int orderComparison = left.OrderTag.CompareTo(right.OrderTag);
                return orderComparison != 0 ? orderComparison : logicRegistrationOrders[left].CompareTo(logicRegistrationOrders[right]);
            });
        }

        /// <summary>禁止在 Entity 注册后改变组件和 Logic 组成，避免更新期间修改集合或漏掉初始化与清理。</summary>
        private void EnsureCanCompose()
        {
            if (LifecycleState != EntityLifecycleState.Created) throw new InvalidOperationException($"Entity '{GetType().FullName}' cannot change components or Logic from lifecycle state '{LifecycleState}'.");
        }
    }
}
