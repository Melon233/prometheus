namespace Xuan.Prometheus.Input
{
    /// <summary>定义输入源注册、控制权租约和实体输入注入的公共能力。</summary>
    public interface IInputSystem : ISystemContract
    {
        /// <summary>获取默认输入源的稳定编号。</summary>
        string DefaultSourceId { get; }

        /// <summary>获取最近完成采样的输入帧编号。</summary>
        long CurrentFrameId { get; }

        /// <summary>获取当前有效输入绑定数量。</summary>
        int BindingCount { get; }

        /// <summary>注册一个可被控制租约引用的输入源。</summary>
        void RegisterSource(IInputSource source);

        /// <summary>为接收者申请指定输入动作的控制权。</summary>
        ControlLease AcquireControl(string sourceId, IInputReceiver receiver, InputActionMask actions, InputContext context, int bindingPriority = 0, InputDeliveryMode deliveryMode = InputDeliveryMode.Exclusive);

        /// <summary>使用默认输入源为实体申请控制权。</summary>
        ControlLease AcquireEntityControl(int entityId, InputActionMask actions, InputContext context, int bindingPriority = 0, InputDeliveryMode deliveryMode = InputDeliveryMode.Exclusive);

        /// <summary>使用指定输入源为实体申请控制权。</summary>
        ControlLease AcquireEntityControl(string sourceId, int entityId, InputActionMask actions, InputContext context, int bindingPriority = 0, InputDeliveryMode deliveryMode = InputDeliveryMode.Exclusive);

        /// <summary>向指定实体注入一次性按钮动作。</summary>
        void QueueEntityButtonActions(int entityId, InputActionMask actions);
    }
}
