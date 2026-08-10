using System;

namespace Xuan.Prometheus.Logic
{
    /// <summary>
    /// 使用位标记声明 Logic 每帧执行所依赖的实体能力，由 Entity 根据 PropertyComponent 的聚合控制状态统一判定。
    /// </summary>
    [Flags]
    public enum LogicControlRequirement
    {
        /// <summary>该 Logic 属于输入采样、物理、受击、死亡或 Effect 生命周期等基础设施，不受控制状态暂停。</summary>
        None = 0,
        /// <summary>该 Logic 需要普通行动能力；Stun 或受击动画状态存在时不可执行。</summary>
        Act = 1 << 0,
        /// <summary>该 Logic 需要移动能力；Stun、受击动画状态或 Root 存在时不可执行。</summary>
        Move = 1 << 1,
        /// <summary>该 Logic 需要主动技能能力；Stun、受击动画状态或 Silence 存在时不可执行。</summary>
        ActiveSkill = 1 << 2
    }

    /// <summary>
    /// 定义 Entity 可统一调度的玩法逻辑生命周期；控制能力需求由兼容既有接口的 Logic 基类提供。
    /// </summary>
    public interface ILogic
    {
        /// <summary>获取或设置其他 Logic 对当前 Logic 施加的阻塞计数。</summary>
        int BlockCnt { get; set; }
        /// <summary>获取或设置当前 Logic 是否处于启用状态。</summary>
        bool Enable { get; set; }
        /// <summary>获取或设置当前 Logic 的帧内执行顺序。</summary>
        OrderTag OrderTag { get; set; }
        /// <summary>获取或设置当前 Logic 所属的实体。</summary>
        Entity Entity { get; set; }
        void AfterNew();
        bool CanEnable();
        bool CanDisable();
        void OnEnable();
        void OnDisable();
        void OnUpdate(float dt);
        void OnDispose();
    }

    /// <summary>
    /// 为具体玩法逻辑提供统一状态字段；默认要求 Act，使新增主动逻辑自动受到 Stun 约束。
    /// </summary>
    public abstract class Logic : ILogic
    {
        /// <inheritdoc />
        public int BlockCnt { get; set; }
        /// <inheritdoc />
        public bool Enable { get; set; }
        /// <inheritdoc />
        public OrderTag OrderTag { get; set; } = OrderTag.Gameplay;
        /// <inheritdoc />
        public Entity Entity { get; set; }
        /// <inheritdoc />
        public LogicControlRequirement ControlRequirement { get; set; } = LogicControlRequirement.Act;
        public abstract void AfterNew();
        public abstract bool CanEnable();
        public abstract bool CanDisable();
        public abstract void OnEnable();
        public abstract void OnDisable();
        public abstract void OnUpdate(float dt);
        public abstract void OnDispose();
    }
}
