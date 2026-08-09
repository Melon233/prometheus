using System.Collections.Generic;

namespace PromeArchTrial.Presentation.Character
{
    /// <summary>
    /// 集中定义 Yefa Spine 3.8 骨骼中由新架构使用的稳定动画名称，避免表现逻辑散落字符串常量。
    /// </summary>
    public static class YefaCharacterAnimationNames
    {
        /// <summary>待机动画。</summary>
        public const string Idle = "idle1_1";

        /// <summary>行走动画。</summary>
        public const string Walk = "base/walk";

        /// <summary>跑步动画。</summary>
        public const string Run = "run";

        /// <summary>冲刺动画。</summary>
        public const string Sprint = "run2";

        /// <summary>起跳动作动画。</summary>
        public const string JumpStart = "jump_atk_start";

        /// <summary>上升循环动画。</summary>
        public const string Rising = "city/jump_loop";

        /// <summary>下落循环动画。</summary>
        public const string Falling = "jump_atk_loop";

        /// <summary>落地动作动画。</summary>
        public const string Landing = "jump_atk_end";

        /// <summary>向前闪避动画。</summary>
        public const string DodgeForward = "dodge_front_move";

        /// <summary>向后闪避动画。</summary>
        public const string DodgeBackward = "dodge_back_move";

        /// <summary>普通攻击第一段动画。</summary>
        public const string Attack1 = "atk1";

        /// <summary>普通攻击第二段动画。</summary>
        public const string Attack2 = "atk2";

        /// <summary>普通攻击第三段动画。</summary>
        public const string Attack3 = "atk3";

        /// <summary>普通攻击第四段动画。</summary>
        public const string Attack4 = "atk4";

        /// <summary>重攻击动画。</summary>
        public const string HeavyAttack = "heavy";

        /// <summary>技能起手动画；旧版 SkillExecutor 会先在全身轨道播放该段。</summary>
        public const string SkillStartup = "atk_branch_start";

        /// <summary>技能主体动画；旧版 SkillExecutor 会在起手结束后继续播放该段。</summary>
        public const string SkillBody = "atk_branch";

        /// <summary>终结技能动画。</summary>
        public const string Ultimate = "xskill";

        /// <summary>受击动画。</summary>
        public const string HitReaction = "leg_hitted";

        /// <summary>地面死亡动画。</summary>
        public const string Death = "ground_death";

        /// <summary>获取生成工具必须在 Yefa SkeletonData 中验证的完整动画名集合。</summary>
        public static IReadOnlyList<string> RequiredAnimationNames { get; } = new[] { Idle, Walk, Run, Sprint, JumpStart, Rising, Falling, Landing, DodgeForward, DodgeBackward, Attack1, Attack2, Attack3, Attack4, HeavyAttack, SkillStartup, SkillBody, Ultimate, HitReaction, Death };
    }
}
