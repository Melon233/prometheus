using System;

namespace PromeArchTrial.Presentation.Character
{
    /// <summary>
    /// 保存一个角色表现资源行解析后的动画名称；调用方可从 Luban 客户端专用表构造该对象，而 Presenter 不需要引用生成代码程序集。
    /// </summary>
    public readonly struct CharacterAnimationPresentationBindings
    {
        /// <summary>获取待机动画名。</summary>
        public string Idle { get; }

        /// <summary>获取行走动画名。</summary>
        public string Walk { get; }

        /// <summary>获取跑步动画名。</summary>
        public string Run { get; }

        /// <summary>获取冲刺动画名。</summary>
        public string Sprint { get; }

        /// <summary>获取起跳动画名。</summary>
        public string JumpStart { get; }

        /// <summary>获取上升循环动画名。</summary>
        public string Rising { get; }

        /// <summary>获取下落循环动画名。</summary>
        public string Falling { get; }

        /// <summary>获取落地动画名。</summary>
        public string Landing { get; }

        /// <summary>获取向前闪避动画名。</summary>
        public string DodgeForward { get; }

        /// <summary>获取向后闪避动画名。</summary>
        public string DodgeBackward { get; }

        /// <summary>获取普通攻击第一段动画名。</summary>
        public string Attack1 { get; }

        /// <summary>获取普通攻击第二段动画名。</summary>
        public string Attack2 { get; }

        /// <summary>获取普通攻击第三段动画名。</summary>
        public string Attack3 { get; }

        /// <summary>获取普通攻击第四段动画名。</summary>
        public string Attack4 { get; }

        /// <summary>获取重攻击动画名。</summary>
        public string HeavyAttack { get; }

        /// <summary>获取技能起手动画名；该动画必须对应 Luban 技能动画 ref 列表的第一项。</summary>
        public string SkillStartup { get; }

        /// <summary>获取技能主体动画名；该动画必须对应 Luban 技能动画 ref 列表的最后一项。</summary>
        public string SkillBody { get; }

        /// <summary>获取终结技能动画名；当前默认 Yefa 资源可与普通技能复用同一名称。</summary>
        public string Ultimate { get; }

        /// <summary>获取受击动画名。</summary>
        public string HitReaction { get; }

        /// <summary>获取死亡动画名。</summary>
        public string Death { get; }

        /// <summary>创建一组完整且不允许空字符串的角色动画表现绑定。</summary>
        public CharacterAnimationPresentationBindings(string idle, string walk, string run, string sprint, string jumpStart, string rising, string falling, string landing, string dodgeForward, string dodgeBackward, string attack1, string attack2, string attack3, string attack4, string heavyAttack, string skillStartup, string skillBody, string ultimate, string hitReaction, string death)
        {
            Idle = RequireAnimationName(idle, nameof(idle));
            Walk = RequireAnimationName(walk, nameof(walk));
            Run = RequireAnimationName(run, nameof(run));
            Sprint = RequireAnimationName(sprint, nameof(sprint));
            JumpStart = RequireAnimationName(jumpStart, nameof(jumpStart));
            Rising = RequireAnimationName(rising, nameof(rising));
            Falling = RequireAnimationName(falling, nameof(falling));
            Landing = RequireAnimationName(landing, nameof(landing));
            DodgeForward = RequireAnimationName(dodgeForward, nameof(dodgeForward));
            DodgeBackward = RequireAnimationName(dodgeBackward, nameof(dodgeBackward));
            Attack1 = RequireAnimationName(attack1, nameof(attack1));
            Attack2 = RequireAnimationName(attack2, nameof(attack2));
            Attack3 = RequireAnimationName(attack3, nameof(attack3));
            Attack4 = RequireAnimationName(attack4, nameof(attack4));
            HeavyAttack = RequireAnimationName(heavyAttack, nameof(heavyAttack));
            SkillStartup = RequireAnimationName(skillStartup, nameof(skillStartup));
            SkillBody = RequireAnimationName(skillBody, nameof(skillBody));
            Ultimate = RequireAnimationName(ultimate, nameof(ultimate));
            HitReaction = RequireAnimationName(hitReaction, nameof(hitReaction));
            Death = RequireAnimationName(death, nameof(death));
        }

        /// <summary>创建与当前 Yefa Spine 3.8 资源完全一致的默认绑定，供生成 prefab 与配置加载前安全预览。</summary>
        public static CharacterAnimationPresentationBindings CreateYefaDefaults()
        {
            return new CharacterAnimationPresentationBindings(YefaCharacterAnimationNames.Idle, YefaCharacterAnimationNames.Walk, YefaCharacterAnimationNames.Run, YefaCharacterAnimationNames.Sprint, YefaCharacterAnimationNames.JumpStart, YefaCharacterAnimationNames.Rising, YefaCharacterAnimationNames.Falling, YefaCharacterAnimationNames.Landing, YefaCharacterAnimationNames.DodgeForward, YefaCharacterAnimationNames.DodgeBackward, YefaCharacterAnimationNames.Attack1, YefaCharacterAnimationNames.Attack2, YefaCharacterAnimationNames.Attack3, YefaCharacterAnimationNames.Attack4, YefaCharacterAnimationNames.HeavyAttack, YefaCharacterAnimationNames.SkillStartup, YefaCharacterAnimationNames.SkillBody, YefaCharacterAnimationNames.Ultimate, YefaCharacterAnimationNames.HitReaction, YefaCharacterAnimationNames.Death);
        }

        /// <summary>校验动画名，避免把空配置延迟到 Spine 播放阶段才暴露。</summary>
        private static string RequireAnimationName(string value, string parameterName)
        {
            if (string.IsNullOrWhiteSpace(value)) throw new ArgumentException("角色动画表现绑定不能为空。", parameterName);
            return value;
        }
    }
}
