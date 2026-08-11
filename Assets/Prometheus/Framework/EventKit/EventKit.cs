using System;
using System.Collections.Generic;

namespace Xuan.Prometheus
{
    /// <summary>定义由全局 EventKit 路由的事件类型。</summary>
    public enum Event
    {
        /// <summary>表示一个实体的生命值发生变化。</summary>
        EntityHpChanged,
        /// <summary>表示一个实体的核心能量发生变化。</summary>
        EntityCoreEnergyChanged,
        /// <summary>表示一个实体的大招能量或大招冷却状态发生变化。</summary>
        EntityUltimateStateChanged,
        /// <summary>表示一个 UI 面板已经完成绑定并进入打开生命周期，事件数据携带具体面板类型。</summary>
        UIPanelOpened,
        /// <summary>表示当前上场小队成员已经发生变化，HUD 和跟随系统据此切换观察目标。</summary>
        ActiveTeamMemberChanged,
        /// <summary>表示指定实体需要重新发布一份完整 HUD 状态快照。</summary>
        EntityHudRefreshRequested,
    }

    /// <summary>携带本次进入打开生命周期的具体 UI 面板类型，供玩法侧按面板类型重放当前状态。</summary>
    public sealed class UIPanelOpenedEvent : IEvent
    {
        /// <summary>获取本次打开的具体 UIPanel 类型。</summary>
        public Type PanelType { get; }

        /// <summary>创建一条不可变的面板打开事实，并拒绝空类型或非 UIPanel 类型。</summary>
        public UIPanelOpenedEvent(Type panelType)
        {
            if (panelType == null) throw new ArgumentNullException(nameof(panelType));
            if (!typeof(UIPanel).IsAssignableFrom(panelType)) throw new ArgumentException($"Panel type '{panelType.FullName}' must derive from {nameof(UIPanel)}.", nameof(panelType));
            PanelType = panelType;
        }

        /// <summary>判断事件携带的面板类型是否与指定具体 UIPanel 类型完全一致。</summary>
        public bool Is<TPanel>() where TPanel : UIPanel
        {
            return PanelType == typeof(TPanel);
        }
    }

    /// <summary>为所有携带确定运行时 EntityId 的全局事实提供统一只读标识。</summary>
    public abstract class EntityEvent : IEvent
    {
        /// <summary>创建一条属于已注册实体的全局事实，并拒绝未分配的运行时编号。</summary>
        protected EntityEvent(int entityId)
        {
            if (entityId <= 0) throw new ArgumentOutOfRangeException(nameof(entityId), entityId, "Entity event requires a positive runtime entity ID.");
            EntityId = entityId;
        }

        /// <summary>获取产生当前事实的实体运行时编号。</summary>
        public int EntityId { get; }
    }

    /// <summary>携带任意实体一次生命值变化前后的快照，界面可以按 EntityId 动态过滤。</summary>
    public sealed class EntityHpChangedEvent : EntityEvent
    {
        /// <summary>获取受到本次伤害前的生命值。</summary>
        public float OldHp { get; }

        /// <summary>获取结算本次伤害后的当前生命值。</summary>
        public float CurrentHp { get; }

        /// <summary>获取结算本次伤害时的生命值上限。</summary>
        public float MaxHp { get; }

        /// <summary>创建一条携带实体编号的不可变生命值变化事实。</summary>
        public EntityHpChangedEvent(int entityId, float oldHp, float currentHp, float maxHp) : base(entityId)
        {
            OldHp = oldHp;
            CurrentHp = currentHp;
            MaxHp = maxHp;
        }
    }

    /// <summary>携带任意实体一次核心能量变化前后的快照，界面可以按 EntityId 动态过滤。</summary>
    public sealed class EntityCoreEnergyChangedEvent : EntityEvent
    {
        /// <summary>获取本次变化前的核心能量。</summary>
        public float Old { get; }

        /// <summary>获取本次变化后的核心能量。</summary>
        public float Current { get; }

        /// <summary>获取本次变化时的核心能量上限。</summary>
        public float Max { get; }

        /// <summary>创建一条携带实体编号的不可变核心能量变化事实。</summary>
        public EntityCoreEnergyChangedEvent(int entityId, float old, float current, float max) : base(entityId)
        {
            Old = old;
            Current = current;
            Max = max;
        }
    }

    /// <summary>携带任意实体大招能量与冷却的完整只读快照，界面可以按 EntityId 动态过滤。</summary>
    public sealed class EntityUltimateStateChangedEvent : EntityEvent
    {
        /// <summary>获取状态变化前的大招能量。</summary>
        public float OldEnergy { get; }

        /// <summary>获取状态变化后的当前大招能量。</summary>
        public float CurrentEnergy { get; }

        /// <summary>获取大招能量上限。</summary>
        public float MaxEnergy { get; }

        /// <summary>获取大招剩余冷却秒数。</summary>
        public float CooldownRemaining { get; }

        /// <summary>获取大招完整冷却秒数。</summary>
        public float CooldownDuration { get; }

        /// <summary>创建一份携带实体编号的不可变大招能量与冷却状态快照。</summary>
        public EntityUltimateStateChangedEvent(int entityId, float oldEnergy, float currentEnergy, float maxEnergy, float cooldownRemaining, float cooldownDuration) : base(entityId)
        {
            OldEnergy = oldEnergy;
            CurrentEnergy = currentEnergy;
            MaxEnergy = maxEnergy;
            CooldownRemaining = cooldownRemaining;
            CooldownDuration = cooldownDuration;
        }
    }

    /// <summary>描述本地玩家当前上场成员从一个槽位切换到另一个槽位的完整事实。</summary>
    public sealed class ActiveTeamMemberChangedEvent : IEvent
    {
        /// <summary>创建一条不可变的小队上场成员变化事实，实体编号为零表示对应方向没有成员。</summary>
        public ActiveTeamMemberChangedEvent(int previousEntityId, int currentEntityId, int previousSlotIndex, int currentSlotIndex)
        {
            if (previousEntityId < 0) throw new ArgumentOutOfRangeException(nameof(previousEntityId), previousEntityId, "Previous entity ID cannot be negative.");
            if (currentEntityId < 0) throw new ArgumentOutOfRangeException(nameof(currentEntityId), currentEntityId, "Current entity ID cannot be negative.");
            PreviousEntityId = previousEntityId;
            CurrentEntityId = currentEntityId;
            PreviousSlotIndex = previousSlotIndex;
            CurrentSlotIndex = currentSlotIndex;
        }

        /// <summary>获取切换前的实体编号；没有旧成员时为零。</summary>
        public int PreviousEntityId { get; }

        /// <summary>获取切换后的实体编号；当前没有可用成员时为零。</summary>
        public int CurrentEntityId { get; }

        /// <summary>获取切换前的零基槽位；没有旧成员时为负一。</summary>
        public int PreviousSlotIndex { get; }

        /// <summary>获取切换后的零基槽位；当前没有可用成员时为负一。</summary>
        public int CurrentSlotIndex { get; }
    }

    /// <summary>请求指定实体立即重放所有 HUD 动态状态，不要求请求方持有具体玩家 Logic。</summary>
    public sealed class EntityHudRefreshRequestedEvent : EntityEvent
    {
        /// <summary>创建一条指向确定实体的 HUD 状态重放请求。</summary>
        public EntityHudRefreshRequestedEvent(int entityId) : base(entityId)
        {
        }
    }

    /// <summary>定义全局事件总线的监听、退订和同步发布能力。</summary>
    public interface IEventKit
    {
        /// <summary>添加一条无参数全局事件监听。</summary>
        void AddListener(Event evt, Action callback);

        /// <summary>添加一条携带类型化事件数据的全局事件监听。</summary>
        void AddListener<TEvent>(Event evt, Action<TEvent> callback) where TEvent : IEvent;

        /// <summary>移除一条无参数全局事件监听。</summary>
        void RemoveListener(Event evt, Action callback);

        /// <summary>移除一条携带类型化事件数据的全局事件监听。</summary>
        void RemoveListener<TEvent>(Event evt, Action<TEvent> callback) where TEvent : IEvent;

        /// <summary>同步发布一条无参数全局事件。</summary>
        void Invoke(Event evt);

        /// <summary>同步发布一条携带类型化事件数据的全局事件。</summary>
        void Invoke<TEvent>(Event evt, TEvent eventData) where TEvent : IEvent;
    }

    /// <summary>由 GameCore 托管的全局同步事件总线，负责保存事件监听并在释放时统一清理。</summary>
    public sealed class EventKit : Kit, IEventKit
    {
        /// <summary>按全局事件类型保存当前注册的委托链。</summary>
        private readonly Dictionary<Event, Delegate> eventDict = new Dictionary<Event, Delegate>();

        /// <summary>记录事件总线是否已经释放，阻止失效实例继续收发事件。</summary>
        private bool isDisposed;

        /// <summary>创建事件总线并公开当前 GameCore 使用的全局事件入口。</summary>
        public EventKit()
        {
            Core.Event = this;
        }

        /// <inheritdoc />
        public void AddListener(Event evt, Action callback)
        {
            AddListenerInternal(evt, callback);
        }

        /// <inheritdoc />
        public void AddListener<TEvent>(Event evt, Action<TEvent> callback) where TEvent : IEvent
        {
            AddListenerInternal(evt, callback);
        }

        /// <inheritdoc />
        public void RemoveListener(Event evt, Action callback)
        {
            RemoveListenerInternal(evt, callback);
        }

        /// <inheritdoc />
        public void RemoveListener<TEvent>(Event evt, Action<TEvent> callback) where TEvent : IEvent
        {
            RemoveListenerInternal(evt, callback);
        }

        /// <inheritdoc />
        public void Invoke(Event evt)
        {
            ThrowIfDisposed();
            if (eventDict.TryGetValue(evt, out Delegate callbacks) && callbacks is Action action) action.Invoke();
        }

        /// <inheritdoc />
        public void Invoke<TEvent>(Event evt, TEvent eventData) where TEvent : IEvent
        {
            ThrowIfDisposed();
            if (eventDict.TryGetValue(evt, out Delegate callbacks) && callbacks is Action<TEvent> action) action.Invoke(eventData);
        }

        /// <summary>清空全部全局监听，并仅在静态入口仍指向当前实例时解除引用。</summary>
        public override void Dispose()
        {
            if (isDisposed) return;
            eventDict.Clear();
            if (ReferenceEquals(Core.Event, this)) Core.Event = null;
            isDisposed = true;
        }

        /// <summary>校验监听器并将其追加到指定全局事件的委托链。</summary>
        private void AddListenerInternal(Event evt, Delegate callback)
        {
            ThrowIfDisposed();
            if (callback == null) throw new ArgumentNullException(nameof(callback));
            if (eventDict.TryGetValue(evt, out Delegate callbacks)) eventDict[evt] = Delegate.Combine(callbacks, callback);
            else eventDict.Add(evt, callback);
        }

        /// <summary>从指定全局事件移除监听器，并在委托链为空时清理字典项。</summary>
        private void RemoveListenerInternal(Event evt, Delegate callback)
        {
            ThrowIfDisposed();
            if (callback == null) throw new ArgumentNullException(nameof(callback));
            if (!eventDict.TryGetValue(evt, out Delegate callbacks)) return;
            Delegate remainingCallbacks = Delegate.Remove(callbacks, callback);
            if (remainingCallbacks == null) eventDict.Remove(evt);
            else eventDict[evt] = remainingCallbacks;
        }

        /// <summary>防止已经释放的事件总线继续收发全局事件。</summary>
        private void ThrowIfDisposed()
        {
            if (isDisposed) throw new ObjectDisposedException(nameof(EventKit));
        }
    }
}
