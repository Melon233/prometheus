namespace Xuan.Prometheus.Npc
{
    /// <summary>把 NPC 静态定义和可持久化状态挂接到 NpcEntity。</summary>
    public sealed class NpcComponent : Xuan.Prometheus.Component.Component
    {
        /// <summary>当前实体使用的 NPC 静态定义。</summary>
        public NpcDefinition Definition;

        /// <summary>当前实体使用的 NPC 运行时状态。</summary>
        public NpcRuntimeState State;
    }
}
