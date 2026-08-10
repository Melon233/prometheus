using System;
using UnityEngine;

namespace Xuan.Prometheus
{
    /// <summary>指定世界锚点 UI 最终在三维世界 Canvas 还是屏幕空间 Overlay Canvas 中渲染。</summary>
    internal enum WorldUIRenderSpace
    {
        /// <summary>在 World Space Canvas 中渲染并参与场景深度测试。</summary>
        WorldSpace,

        /// <summary>把世界坐标投影到 Screen Space Overlay Canvas，并始终显示在场景模型之上。</summary>
        ScreenSpaceOverlay
    }

    /// <summary>
    /// 表示一次世界锚点 UI 租约，为业务层提供根对象、Binder、跟随目标、位置和主动回收能力。
    /// 句柄回收后会立即失效，即使底层实例随后被对象池复用，旧句柄也无法操作新的显示内容。
    /// </summary>
    public sealed class WorldUIHandle
    {
        private UIKit owner;
        private WorldUIRecord record;
        private readonly uint version;

        /// <summary>
        /// 创建仅由 UIKit 持有和校验的世界 UI 租约。
        /// </summary>
        internal WorldUIHandle(UIKit owner, WorldUIRecord record, uint version)
        {
            this.owner = owner ?? throw new ArgumentNullException(nameof(owner));
            this.record = record ?? throw new ArgumentNullException(nameof(record));
            this.version = version;
        }

        /// <summary>
        /// 获取该租约是否仍对应一个正在显示的世界 UI 实例。
        /// </summary>
        public bool IsValid => owner != null && owner.IsWorldUIHandleValid(this);

        /// <summary>
        /// 获取世界 UI Prefab 根对象；句柄失效后返回空。
        /// </summary>
        public GameObject Root => IsValid ? record.Instance : null;

        /// <summary>
        /// 获取世界 UI 根节点 Binder；Prefab 未使用 Binder 或句柄失效时返回空。
        /// </summary>
        public UIComponentBinder Binder => IsValid ? record.Binder : null;

        /// <summary>
        /// 获取创建该世界 UI 使用的 AssetKit 资源地址。
        /// </summary>
        public string AssetAddress => record != null ? record.AssetAddress : string.Empty;

        /// <summary>
        /// 获取当前跟随目标；固定坐标模式或句柄失效时返回空。
        /// </summary>
        public Transform FollowTarget => IsValid && record.IsFollowing ? record.FollowTarget : null;

        /// <summary>获取当前实例是否通过屏幕空间 Overlay Canvas 显示。</summary>
        public bool IsScreenSpaceOverlay => IsValid && record.RenderSpace == WorldUIRenderSpace.ScreenSpaceOverlay;

        /// <summary>
        /// 从有效世界 UI 根节点获取指定组件，适合不使用 Binder、直接由 MonoBehaviour 驱动的动态 UI Prefab。
        /// </summary>
        /// <typeparam name="TComponent">需要读取的 Unity 组件类型。</typeparam>
        /// <returns>根节点上的目标组件；不存在时返回空。</returns>
        public TComponent GetComponent<TComponent>() where TComponent : UnityEngine.Component
        {
            return IsValid ? record.Instance.GetComponent<TComponent>() : null;
        }

        /// <summary>
        /// 从有效世界 UI 层级中获取指定组件，支持在未激活子节点中查找。
        /// </summary>
        /// <typeparam name="TComponent">需要读取的 Unity 组件类型。</typeparam>
        /// <returns>根节点或子节点中的首个目标组件；不存在时返回空。</returns>
        public TComponent GetComponentInChildren<TComponent>() where TComponent : UnityEngine.Component
        {
            return IsValid ? record.Instance.GetComponentInChildren<TComponent>(true) : null;
        }

        /// <summary>
        /// 将当前世界 UI 切换为跟随目标模式，并更新相对目标的世界坐标偏移。
        /// </summary>
        /// <param name="followTarget">需要跟随的场景 Transform。</param>
        /// <param name="worldOffset">叠加在目标世界坐标上的偏移。</param>
        public void SetFollowTarget(Transform followTarget, Vector3 worldOffset)
        {
            GetOwnerOrThrow().ConfigureWorldUIFollow(this, followTarget, worldOffset);
        }

        /// <summary>
        /// 将当前世界 UI 切换为固定世界坐标模式，适合伤害飘字等生成后独立运动的内容。
        /// </summary>
        /// <param name="worldPosition">新的固定世界坐标。</param>
        public void SetWorldPosition(Vector3 worldPosition)
        {
            GetOwnerOrThrow().ConfigureWorldUIPosition(this, worldPosition);
        }

        /// <summary>设置屏幕空间世界锚点 UI 在投影位置上叠加的像素偏移。</summary>
        /// <param name="screenOffset">以 Overlay Canvas 参考分辨率为基准的二维偏移。</param>
        public void SetScreenOffset(Vector2 screenOffset)
        {
            GetOwnerOrThrow().ConfigureWorldUIScreenOffset(this, screenOffset);
        }

        /// <summary>
        /// 设置剩余自动回收时间；传入零表示取消自动回收并持续显示。
        /// </summary>
        /// <param name="lifetime">从当前时刻开始计算的秒数，必须大于或等于零。</param>
        public void SetLifetime(float lifetime)
        {
            GetOwnerOrThrow().ConfigureWorldUILifetime(this, lifetime);
        }

        /// <summary>
        /// 主动回收当前世界 UI；重复调用是安全的空操作。
        /// </summary>
        public void Release()
        {
            owner?.ReleaseWorldUI(this);
        }

        /// <summary>
        /// 获取创建当前租约时记录的池实例版本，供 UIKit 防止旧句柄操作复用实例。
        /// </summary>
        internal uint Version => version;

        /// <summary>
        /// 获取创建当前租约的 UIKit，供 UIKit 拒绝处理其他运行上下文的句柄。
        /// </summary>
        internal UIKit Owner => owner;

        /// <summary>
        /// 获取租约内部记录，只有 UIKit 可以结合版本验证后使用。
        /// </summary>
        internal WorldUIRecord Record => record;

        /// <summary>
        /// 在实例回收时断开 UIKit 和记录引用，使业务侧能够立即观察到句柄失效。
        /// </summary>
        internal void Invalidate()
        {
            owner = null;
            record = null;
        }

        /// <summary>
        /// 获取仍然有效的所属 UIKit，无效句柄尝试修改状态时抛出明确异常。
        /// </summary>
        private UIKit GetOwnerOrThrow()
        {
            if (!IsValid)
                throw new InvalidOperationException("World UI handle is no longer valid because its instance has already been released or reused.");

            return owner;
        }
    }

    /// <summary>
    /// 保存一个可在活动列表与对象池之间移动的世界 UI 实例及其当前租约状态。
    /// </summary>
    internal sealed class WorldUIRecord
    {
        /// <summary>
        /// 创建一条不会改变资源地址、实例和可选 Binder 引用的池记录。
        /// </summary>
        internal WorldUIRecord(string assetAddress, GameObject instance, UIComponentBinder binder)
        {
            AssetAddress = assetAddress;
            Instance = instance;
            Binder = binder;
        }

        internal string AssetAddress { get; }
        internal GameObject Instance { get; }
        internal UIComponentBinder Binder { get; }
        internal Transform FollowTarget { get; set; }
        internal Vector3 FixedWorldPosition { get; set; }
        internal Vector3 WorldOffset { get; set; }
        /// <summary>获取或设置投影到 Overlay Canvas 后附加的二维动画偏移。</summary>
        internal Vector2 ScreenOffset { get; set; }
        internal float RemainingLifetime { get; set; }
        internal bool IsFollowing { get; set; }
        internal bool IsActive { get; set; }
        /// <summary>获取或设置当前租约使用的最终渲染空间。</summary>
        internal WorldUIRenderSpace RenderSpace { get; set; }
        internal uint Version { get; set; }
        internal WorldUIHandle Handle { get; set; }
    }
}
