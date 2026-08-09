namespace PromeArchTrial.Game.Character
{
    /// <summary>
    /// 指定玩家在有方向输入时请求使用的地面移动档位。
    /// </summary>
    public enum CharacterMoveMode
    {
        /// <summary>使用低速行走。</summary>
        Walk = 0,

        /// <summary>使用常规跑步。</summary>
        Run = 1,

        /// <summary>使用高速冲刺。</summary>
        Sprint = 2
    }

    /// <summary>
    /// 描述用于表现同步的角色移动状态，状态变化完全由固定 Tick 模拟产生。
    /// </summary>
    public enum CharacterLocomotionState
    {
        /// <summary>角色在地面保持静止。</summary>
        Idle = 0,

        /// <summary>角色在地面行走。</summary>
        Walk = 1,

        /// <summary>角色在地面跑步。</summary>
        Run = 2,

        /// <summary>角色在地面冲刺。</summary>
        Sprint = 3,

        /// <summary>角色刚刚起跳。</summary>
        Jump = 4,

        /// <summary>角色处于空中上升阶段。</summary>
        Rise = 5,

        /// <summary>角色处于空中下落阶段。</summary>
        Fall = 6,

        /// <summary>角色正在执行落地硬直。</summary>
        Land = 7,

        /// <summary>角色生命值归零且不再接受操作。</summary>
        Dead = 8
    }

    /// <summary>
    /// 标识由配置驱动的离散角色动作，数值可直接映射到网络协议枚举。
    /// </summary>
    public enum CharacterActionKind
    {
        /// <summary>当前没有排他动作。</summary>
        None = 0,

        /// <summary>落地硬直动作。</summary>
        Land = 1,

        /// <summary>朝当前移动或朝向执行前闪避。</summary>
        DodgeForward = 2,

        /// <summary>朝当前朝向反方向执行后闪避。</summary>
        DodgeBackward = 3,

        /// <summary>普攻连段第一击。</summary>
        Attack1 = 4,

        /// <summary>普攻连段第二击。</summary>
        Attack2 = 5,

        /// <summary>普攻连段第三击。</summary>
        Attack3 = 6,

        /// <summary>普攻连段第四击。</summary>
        Attack4 = 7,

        /// <summary>攻击键蓄力达到阈值后触发的重击。</summary>
        HeavyAttack = 8,

        /// <summary>消耗核心能量触发的普通技能。</summary>
        Skill = 9,

        /// <summary>消耗终结能量触发的终结技能。</summary>
        Ultimate = 10
    }

    /// <summary>
    /// 描述当前动作按照配置 Tick 划分得到的阶段。
    /// </summary>
    public enum CharacterActionPhase
    {
        /// <summary>当前没有动作阶段。</summary>
        None = 0,

        /// <summary>动作前摇阶段。</summary>
        Windup = 1,

        /// <summary>动作命中窗口阶段。</summary>
        Active = 2,

        /// <summary>动作后摇阶段。</summary>
        Recovery = 3
    }

    /// <summary>
    /// 标识模拟层输出给命中查询、音画表现和调试系统的确定性角色事件。
    /// </summary>
    public enum CharacterEventType
    {
        /// <summary>移动状态发生变化。</summary>
        LocomotionChanged = 0,

        /// <summary>一个配置动作开始。</summary>
        ActionStarted = 1,

        /// <summary>动作进入命中窗口，外部命中查询应在此事件后执行。</summary>
        HitWindowOpened = 2,

        /// <summary>动作离开命中窗口。</summary>
        HitWindowClosed = 3,

        /// <summary>一个配置动作结束。</summary>
        ActionEnded = 4,

        /// <summary>角色成功起跳。</summary>
        Jumped = 5,

        /// <summary>角色落到模拟平面。</summary>
        Landed = 6,

        /// <summary>角色受到有效伤害，数值字段为实际扣除生命值。</summary>
        DamageTaken = 7,

        /// <summary>伤害因动作无敌帧被忽略，数值字段为被忽略伤害。</summary>
        DamageIgnored = 8,

        /// <summary>当前动作得到外部命中确认，数值字段为确认命中数量。</summary>
        HitConfirmed = 9,

        /// <summary>角色核心能量或终结能量发生变化。</summary>
        EnergyChanged = 10,

        /// <summary>角色生命值首次降到零。</summary>
        Died = 11
    }
}
