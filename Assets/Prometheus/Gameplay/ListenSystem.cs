using System;
using System.Collections.Generic;
using Xuan.Prometheus.Component;

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

    /// <summary>以 EntityId、组件类型和属性字段为寻址条件向 UI 提供强类型脏监听，并统一管理单局监听生命周期。</summary>
    public sealed class ListenSystem : XSystem
    {
        /// <summary>保存当前单局仍处于活动状态的系统级监听句柄。</summary>
        private readonly HashSet<ListenHandle> activeHandles = new HashSet<ListenHandle>();

        /// <summary>保存所属玩法世界，用于按 EntityId 解析独立实体。</summary>
        private IGameplayKit gameplayKit;

        /// <summary>绑定所属玩法世界；所有 Entity 注册完成后即可通过其运行时编号建立监听。</summary>
        public override void AfterNew(IGameplayKit ownerGameplayKit)
        {
            gameplayKit = ownerGameplayKit ?? throw new ArgumentNullException(nameof(ownerGameplayKit));
        }

        /// <summary>监听指定 Entity 的指定组件字段；注册成功后默认立即回调一次，后续仅在字段最终值实际变化时回调。</summary>
        public ListenHandle Listen<TComponent>(int entityId, Func<TComponent, ModifiableProperty> fieldSelector, Action<TComponent> onDirty, bool invokeImmediately = true) where TComponent : IComponent
        {
            if (gameplayKit == null) throw new InvalidOperationException("ListenSystem must complete AfterNew before registering listeners.");
            if (entityId <= 0) throw new ArgumentOutOfRangeException(nameof(entityId), entityId, "Listened EntityId must be positive.");
            if (fieldSelector == null) throw new ArgumentNullException(nameof(fieldSelector));
            if (onDirty == null) throw new ArgumentNullException(nameof(onDirty));
            if (!gameplayKit.TryGetEntity(entityId, out Logic.Entity entity)) throw new InvalidOperationException($"ListenSystem cannot find Entity {entityId}.");
            if (!entity.TryGetComp(out TComponent component)) throw new InvalidOperationException($"Entity {entityId} does not contain component '{typeof(TComponent).FullName}'.");
            ModifiableProperty property = fieldSelector.Invoke(component) ?? throw new InvalidOperationException($"The selected field on component '{typeof(TComponent).FullName}' is null.");
            ListenHandle propertyHandle = null;
            ListenHandle systemHandle = null;
            systemHandle = new ListenHandle(() =>
            {
                propertyHandle?.Dispose();
                propertyHandle = null;
                activeHandles.Remove(systemHandle);
            });
            activeHandles.Add(systemHandle);
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

        /// <summary>单局结束时释放所有尚未由 UI 主动释放的监听，避免属性继续持有失效界面。</summary>
        public override void Dispose()
        {
            ListenHandle[] handles = new ListenHandle[activeHandles.Count];
            activeHandles.CopyTo(handles);
            foreach (ListenHandle handle in handles) handle.Dispose();
            activeHandles.Clear();
            gameplayKit = null;
        }
    }
}
