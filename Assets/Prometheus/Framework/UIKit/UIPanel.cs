using System;
using UnityEngine;

namespace Xuan.Prometheus
{
    /// <summary>
    /// 所有 UI 面板的纯 C# 控制器基类，负责组件绑定和稳定的生命周期顺序，不参与 Unity 的 MonoBehaviour 生命周期。
    /// </summary>
    public abstract class UIPanel
    {
        private UIKit owner;
        private bool isBound;
        private bool isInitialized;
        private bool isDisposed;

        /// <summary>
        /// 获取当前面板对应的 Prefab 实例根节点；控制器释放后为空。
        /// </summary>
        public GameObject Root { get; private set; }

        /// <summary>
        /// 获取当前面板根节点上的组件绑定器；控制器释放后为空。
        /// </summary>
        public UIComponentBinder Binder { get; private set; }

        /// <summary>
        /// 获取当前面板是否已经完成 OnOpen 且尚未执行 OnClose。
        /// </summary>
        public bool IsOpen { get; private set; }

        /// <summary>
        /// 请求拥有当前控制器的 UIKit 关闭本面板。
        /// </summary>
        public void Close()
        {
            ThrowIfDisposed();
            owner.ClosePanel(GetType());
        }

        /// <summary>
        /// 由生成的 PanelBase 覆写，将 Binder 表转换成强类型组件字段。
        /// </summary>
        /// <param name="binder">Prefab 根节点上的组件绑定器。</param>
        protected abstract void BindComponents(UIComponentBinder binder);

        /// <summary>
        /// 由生成的 PanelBase 覆写，清空全部强类型组件字段。
        /// </summary>
        protected abstract void UnbindComponents();

        /// <summary>
        /// 在强类型组件字段全部赋值后调用，适合注册按钮事件等只需执行一次的逻辑。
        /// </summary>
        protected virtual void OnBind()
        {
        }

        /// <summary>
        /// 在首次绑定完成后调用一次，适合建立只需初始化一次的非组件状态。
        /// </summary>
        protected virtual void OnInitialize()
        {
        }

        /// <summary>
        /// 每次面板从关闭或缓存状态进入显示状态时调用。
        /// </summary>
        protected virtual void OnOpen()
        {
        }

        /// <summary>
        /// 每次打开的面板进入关闭状态时调用，此时组件引用仍然有效。
        /// </summary>
        protected virtual void OnClose()
        {
        }

        /// <summary>面板处于打开状态时每帧调用一次；具体面板可在此读取实体状态并刷新 UI。</summary>
        protected virtual void OnUpdate(float dt)
        {
        }

        /// <summary>
        /// 在面板实例最终释放前调用，适合移除 OnBind 注册的事件监听。
        /// </summary>
        protected virtual void OnUnbind()
        {
        }

        /// <summary>
        /// 在控制器最终释放时调用，组件字段此时已经被清空。
        /// </summary>
        protected virtual void OnDispose()
        {
        }

        /// <summary>
        /// 建立控制器与 UIKit、Prefab 实例以及 Binder 的唯一关联。
        /// </summary>
        internal void InternalAttach(UIKit panelOwner, GameObject root, UIComponentBinder binder)
        {
            if (owner != null)
                throw new InvalidOperationException($"Panel controller '{GetType().FullName}' is already attached to a UIKit instance.");

            owner = panelOwner ?? throw new ArgumentNullException(nameof(panelOwner));
            Root = root != null ? root : throw new ArgumentNullException(nameof(root));
            Binder = binder != null ? binder : throw new ArgumentNullException(nameof(binder));
        }

        /// <summary>
        /// 执行生成字段绑定和业务事件绑定，并保证失败时可以进入统一释放流程。
        /// </summary>
        internal void InternalBind()
        {
            ThrowIfDisposed();

            if (isBound)
                return;

            BindComponents(Binder);
            isBound = true;
            OnBind();
        }

        /// <summary>
        /// 首次绑定后执行一次业务初始化。
        /// </summary>
        internal void InternalInitialize()
        {
            ThrowIfDisposed();

            if (isInitialized)
                return;

            OnInitialize();
            isInitialized = true;
        }

        /// <summary>
        /// 进入打开状态并触发每次显示生命周期。
        /// </summary>
        internal void InternalOpen()
        {
            ThrowIfDisposed();

            if (IsOpen)
                return;

            OnOpen();
            IsOpen = true;
        }

        /// <summary>
        /// 退出打开状态；即使 OnClose 抛出异常也会准确更新内部状态。
        /// </summary>
        internal void InternalClose()
        {
            if (!IsOpen || isDisposed)
                return;

            try
            {
                OnClose();
            }
            finally
            {
                IsOpen = false;
            }
        }

        /// <summary>由 UIKit 统一驱动当前打开面板的逐帧生命周期。</summary>
        internal void InternalUpdate(float dt)
        {
            if (!IsOpen || isDisposed) return;
            OnUpdate(dt);
        }

        /// <summary>
        /// 按取消事件、清空字段、释放业务状态的顺序最终释放控制器。
        /// </summary>
        internal void InternalRelease()
        {
            if (isDisposed)
                return;

            if (IsOpen)
                InternalClose();

            if (isBound)
            {
                try
                {
                    OnUnbind();
                }
                finally
                {
                    UnbindComponents();
                    isBound = false;
                }
            }

            OnDispose();
            owner = null;
            Root = null;
            Binder = null;
            isDisposed = true;
        }

        /// <summary>
        /// 阻止已经释放的控制器继续执行公开或内部生命周期。
        /// </summary>
        private void ThrowIfDisposed()
        {
            if (isDisposed)
                throw new ObjectDisposedException(GetType().FullName);
        }
    }
}
