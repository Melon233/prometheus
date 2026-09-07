namespace Xuan.Prometheus
{
    /// <summary>定义跨角色共享的动画语义；同一语义由各自 AnimationLibrary 映射到该角色专属的 AnimationLine。</summary>
    public enum AnimationSemantic
    {
        /// <summary>表示尚未配置动画语义，运行时不会接受该值作为播放请求。</summary>
        None = 0,
        /// <summary>待机循环。</summary>
        Idle = 10,
        /// <summary>步行循环。</summary>
        Walk = 20,
        /// <summary>跑步循环。</summary>
        Run = 21,
        /// <summary>冲刺循环。</summary>
        Sprint = 22,
        /// <summary>起跳动画。</summary>
        JumpStart = 30,
        /// <summary>空中上升循环。</summary>
        Rise = 31,
        /// <summary>空中下落循环。</summary>
        Fall = 32,
        /// <summary>落地动画。</summary>
        Land = 33,
        /// <summary>向前闪避。</summary>
        DodgeFront = 40,
        /// <summary>原地或向后闪避。</summary>
        DodgeBack = 41,
        /// <summary>普通攻击第一段。</summary>
        Attack1 = 100,
        /// <summary>移动普通攻击第一段。</summary>
        Attack1Move = 101,
        /// <summary>普通攻击第二段。</summary>
        Attack2 = 102,
        /// <summary>移动普通攻击第二段。</summary>
        Attack2Move = 103,
        /// <summary>普通攻击第三段。</summary>
        Attack3 = 104,
        /// <summary>移动普通攻击第三段。</summary>
        Attack3Move = 105,
        /// <summary>普通攻击第四段。</summary>
        Attack4 = 106,
        /// <summary>移动普通攻击第四段。</summary>
        Attack4Move = 107,
        /// <summary>特殊攻击。</summary>
        SpecialAttack = 120,
        /// <summary>技能起手。</summary>
        SkillStart = 130,
        /// <summary>技能主体。</summary>
        Skill = 131,
        /// <summary>终结技。</summary>
        Ultimate = 140,
        /// <summary>受击主体。</summary>
        Hit = 200,
        /// <summary>受击恢复。</summary>
        HitRecovery = 201,
        /// <summary>死亡动画。</summary>
        Death = 300
    }
}
