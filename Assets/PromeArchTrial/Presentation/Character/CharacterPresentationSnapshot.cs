using System;
using UnityEngine;

namespace PromeArchTrial.Presentation.Character
{
    /// <summary>
    /// 定义角色模型的水平朝向；数值固定为 -1 与 1，便于客户端适配任何共享模拟层的整数朝向。
    /// </summary>
    public enum CharacterFacingDirection : sbyte
    {
        /// <summary>角色朝向屏幕左侧。</summary>
        Left = -1,

        /// <summary>角色朝向屏幕右侧。</summary>
        Right = 1
    }

    /// <summary>
    /// 定义没有独占动作时的持续运动表现状态；稳定整数值用于隔离表现程序集与 GameNative 枚举。
    /// </summary>
    public enum CharacterLocomotionPresentationState
    {
        /// <summary>站立待机。</summary>
        Idle = 0,

        /// <summary>低速行走。</summary>
        Walk = 1,

        /// <summary>常规跑步。</summary>
        Run = 2,

        /// <summary>高速冲刺。</summary>
        Sprint = 3,

        /// <summary>起跳后的上升阶段。</summary>
        Rising = 4,

        /// <summary>越过最高点后的下落阶段。</summary>
        Falling = 5,

        /// <summary>触地后的短暂落地阶段。</summary>
        Landing = 6,

        /// <summary>角色死亡后的持续状态。</summary>
        Dead = 7
    }

    /// <summary>
    /// 定义会临时覆盖移动动画的独占动作；数值按动作类别预留区间，方便网络协议长期保持兼容。
    /// </summary>
    public enum CharacterActionPresentationState
    {
        /// <summary>当前没有独占动作，表现回退到 Locomotion。</summary>
        None = 0,

        /// <summary>起跳动作。</summary>
        JumpStart = 1,

        /// <summary>向前闪避。</summary>
        DodgeForward = 10,

        /// <summary>向后闪避。</summary>
        DodgeBackward = 11,

        /// <summary>普通攻击连段第一段。</summary>
        Attack1 = 20,

        /// <summary>普通攻击连段第二段。</summary>
        Attack2 = 21,

        /// <summary>普通攻击连段第三段。</summary>
        Attack3 = 22,

        /// <summary>普通攻击连段第四段。</summary>
        Attack4 = 23,

        /// <summary>重攻击。</summary>
        HeavyAttack = 30,

        /// <summary>派生攻击。</summary>
        BranchAttack = 31,

        /// <summary>角色技能。</summary>
        Skill = 40,

        /// <summary>终结动作；当前 Yefa 资源复用 xskill 全身动画，后续可通过配置替换。</summary>
        Ultimate = 41,

        /// <summary>受击硬直动作。</summary>
        HitReaction = 80,

        /// <summary>死亡动作。</summary>
        Death = 90
    }

    /// <summary>
    /// 表示模拟层在一个确定 tick 上交付给表现层的只读角色快照；该类型不引用 GameNative、网络协议或旧版角色组件。
    /// </summary>
    public readonly struct CharacterPresentationSnapshot
    {
        /// <summary>获取产生该快照的模拟 tick。</summary>
        public uint SimulationTick { get; }

        /// <summary>获取角色在 Unity 世界空间中的位置。</summary>
        public Vector3 Position { get; }

        /// <summary>获取角色水平朝向。</summary>
        public CharacterFacingDirection Facing { get; }

        /// <summary>获取角色持续运动表现状态。</summary>
        public CharacterLocomotionPresentationState Locomotion { get; }

        /// <summary>获取当前独占动作；None 表示使用 Locomotion 选择动画。</summary>
        public CharacterActionPresentationState Action { get; }

        /// <summary>获取动作实例序号；同一种动作再次开始时必须递增，以便表现层可靠重播同名动画。</summary>
        public uint ActionSequence { get; }

        /// <summary>获取动作已经推进的模拟 tick 数。</summary>
        public int ActionTick { get; }

        /// <summary>获取动作总 tick 数；没有独占动作时可以为零。</summary>
        public int ActionDurationTicks { get; }

        /// <summary>获取动作在零到一之间的权威归一化时间；预测回滚后表现层用它重新定位 Spine track 0。</summary>
        public float ActionNormalizedTime { get; }

        /// <summary>获取当前生命值。</summary>
        public int Health { get; }

        /// <summary>获取最大生命值。</summary>
        public int MaxHealth { get; }

        /// <summary>获取最近一次伤害表现事件的单调递增序号；零表示尚无伤害事件。</summary>
        public uint DamageEventSequence { get; }

        /// <summary>获取最近一次伤害事件的非负伤害值。</summary>
        public int LatestDamageAmount { get; }

        /// <summary>获取最近一次伤害是否为暴击。</summary>
        public bool LatestDamageWasCritical { get; }

        /// <summary>
        /// 创建一个经过边界校验的角色表现快照；调用方负责把共享模拟状态映射成此独立表现契约。
        /// </summary>
        public CharacterPresentationSnapshot(uint simulationTick, Vector3 position, CharacterFacingDirection facing, CharacterLocomotionPresentationState locomotion, CharacterActionPresentationState action, uint actionSequence, int actionTick, int actionDurationTicks, float actionNormalizedTime, int health, int maxHealth, uint damageEventSequence, int latestDamageAmount, bool latestDamageWasCritical)
        {
            if (!IsFinite(position.x) || !IsFinite(position.y) || !IsFinite(position.z)) throw new ArgumentOutOfRangeException(nameof(position), "角色表现位置必须是有限数值。");
            if (facing != CharacterFacingDirection.Left && facing != CharacterFacingDirection.Right) throw new ArgumentOutOfRangeException(nameof(facing), "角色表现朝向只能是 Left 或 Right。");
            if (actionTick < 0) throw new ArgumentOutOfRangeException(nameof(actionTick), "动作 tick 不能为负数。");
            if (actionDurationTicks < 0) throw new ArgumentOutOfRangeException(nameof(actionDurationTicks), "动作总 tick 数不能为负数。");
            if (!IsFinite(actionNormalizedTime)) throw new ArgumentOutOfRangeException(nameof(actionNormalizedTime), "动作归一化时间必须是有限数值。");
            if (maxHealth <= 0) throw new ArgumentOutOfRangeException(nameof(maxHealth), "最大生命值必须大于零。");
            if (health < 0 || health > maxHealth) throw new ArgumentOutOfRangeException(nameof(health), "当前生命值必须位于零到最大生命值之间。");
            if (latestDamageAmount < 0) throw new ArgumentOutOfRangeException(nameof(latestDamageAmount), "伤害表现值不能为负数。");
            SimulationTick = simulationTick;
            Position = position;
            Facing = facing;
            Locomotion = locomotion;
            Action = action;
            ActionSequence = actionSequence;
            ActionTick = actionTick;
            ActionDurationTicks = actionDurationTicks;
            ActionNormalizedTime = Mathf.Clamp01(actionNormalizedTime);
            Health = health;
            MaxHealth = maxHealth;
            DamageEventSequence = damageEventSequence;
            LatestDamageAmount = latestDamageAmount;
            LatestDamageWasCritical = latestDamageWasCritical;
        }

        /// <summary>
        /// 根据动作 tick 计算零到一之间的归一化时间；总 tick 为零时安全返回零。
        /// </summary>
        public static float CalculateNormalizedActionTime(int actionTick, int actionDurationTicks)
        {
            if (actionTick < 0) throw new ArgumentOutOfRangeException(nameof(actionTick), "动作 tick 不能为负数。");
            if (actionDurationTicks < 0) throw new ArgumentOutOfRangeException(nameof(actionDurationTicks), "动作总 tick 数不能为负数。");
            return actionDurationTicks == 0 ? 0f : Mathf.Clamp01((float)actionTick / actionDurationTicks);
        }

        /// <summary>判断浮点数是否可安全用于 Unity Transform 与 Spine 时间轴。</summary>
        private static bool IsFinite(float value)
        {
            return !float.IsNaN(value) && !float.IsInfinity(value);
        }
    }

    /// <summary>
    /// 表示一次生命值表现更新，供外部 HUD 或世界空间血条订阅而不读取 gameplay 对象。
    /// </summary>
    public readonly struct CharacterHealthPresentationChange
    {
        /// <summary>获取快照 tick。</summary>
        public uint SimulationTick { get; }

        /// <summary>获取当前生命值。</summary>
        public int Health { get; }

        /// <summary>获取最大生命值。</summary>
        public int MaxHealth { get; }

        /// <summary>获取零到一之间的生命比例。</summary>
        public float NormalizedHealth { get; }

        /// <summary>创建一次不可变的生命值表现更新。</summary>
        public CharacterHealthPresentationChange(uint simulationTick, int health, int maxHealth)
        {
            SimulationTick = simulationTick;
            Health = health;
            MaxHealth = maxHealth;
            NormalizedHealth = maxHealth <= 0 ? 0f : Mathf.Clamp01((float)health / maxHealth);
        }
    }

    /// <summary>
    /// 表示一次伤害数字生成请求；实际文本、对象池和屏幕投影由独立表现组件决定。
    /// </summary>
    public readonly struct CharacterDamageNumberPresentationRequest
    {
        /// <summary>获取伤害事件的去重序号。</summary>
        public uint Sequence { get; }

        /// <summary>获取应显示的非负伤害值。</summary>
        public int Amount { get; }

        /// <summary>获取是否使用暴击样式。</summary>
        public bool WasCritical { get; }

        /// <summary>获取伤害数字生成时的世界空间锚点。</summary>
        public Vector3 WorldPosition { get; }

        /// <summary>创建一次不可变的伤害数字生成请求。</summary>
        public CharacterDamageNumberPresentationRequest(uint sequence, int amount, bool wasCritical, Vector3 worldPosition)
        {
            Sequence = sequence;
            Amount = Mathf.Max(0, amount);
            WasCritical = wasCritical;
            WorldPosition = worldPosition;
        }
    }
}
