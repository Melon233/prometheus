using System;
using System.Collections.Generic;
using Spine.Unity;
using UnityEngine;
using Xuan.Prometheus.Component;
using Xuan.Prometheus.Logic;
using Xuan.Prometheus.Logic.Talent;

namespace Xuan.Prometheus
{
    /// <summary>集中保存玩家与敌人角色 Prefab 共用的 Unity 引用和只读配置。</summary>
    public abstract class CharacterBinder : EntityBinder
    {
        [SerializeField] private CharacterController characterController;
        [SerializeField] private SkeletonAnimation spineAnimator;
        [SerializeField] private AnimationLibrary animationLibrary;
        [SerializeField] private Transform rotateRoot;
        [SerializeField] private CharacterRootMotionComponent rootMotionBridge;
        [SerializeField] private PropertyConfig propertyConfig;
        [SerializeField] private TalentConfig attackTalentConfig;
        [SerializeField] private List<NormalAttackHitBinding> attackHits = new List<NormalAttackHitBinding>();
        [SerializeField] private ColliderProxy legacyAttackCollider;
        [SerializeField] private TalentGrowthState attackTalentGrowth = new TalentGrowthState();
        [SerializeField] private List<GameObject> vfxSlots = new List<GameObject>();

        /// <summary>获取角色统一运动出口。</summary>
        public CharacterController CharacterController => characterController;

        /// <summary>获取角色 Spine 动画组件。</summary>
        public SkeletonAnimation SpineAnimator => spineAnimator;

        /// <summary>获取角色共享动画语义库。</summary>
        public AnimationLibrary AnimationLibrary => animationLibrary;

        /// <summary>获取与角色朝向同步的表现旋转根节点。</summary>
        public Transform RotateRoot => rotateRoot;

        /// <summary>获取只转发 Spine Root Motion 的 Unity 桥接组件。</summary>
        public CharacterRootMotionComponent RootMotionBridge => rootMotionBridge;

        /// <summary>获取角色基础属性与移动模式的只读配置。</summary>
        public PropertyConfig PropertyConfig => propertyConfig;

        /// <summary>获取普通攻击使用的共享天赋配置。</summary>
        public TalentConfig AttackTalentConfig => attackTalentConfig;

        /// <summary>获取普通攻击各段的碰撞引用与能力编号绑定。</summary>
        public IReadOnlyList<NormalAttackHitBinding> AttackHits => attackHits;

        /// <summary>获取仍使用单段旧攻击资源的敌人碰撞代理；新玩家连段使用 AttackHits。</summary>
        public ColliderProxy LegacyAttackCollider => legacyAttackCollider;

        /// <summary>获取普通攻击 Debug 等级模板；运行时 Component 必须复制而不能回写它。</summary>
        public TalentGrowthState AttackTalentGrowth => attackTalentGrowth;

        /// <summary>获取动作特效槽位的集中引用。</summary>
        public IReadOnlyList<GameObject> VfxSlots => vfxSlots;

        /// <summary>校验所有角色共同需要的引用，缺失配置直接阻止 Entity 进入 Active。</summary>
        public override void Validate()
        {
            if (CharacterController == null) throw new InvalidOperationException($"CharacterBinder '{name}' requires CharacterController.");
            if (SpineAnimator == null) throw new InvalidOperationException($"CharacterBinder '{name}' requires SkeletonAnimation.");
            if (AnimationLibrary == null) throw new InvalidOperationException($"CharacterBinder '{name}' requires AnimationLibrary.");
            if (PropertyConfig == null) throw new InvalidOperationException($"CharacterBinder '{name}' requires PropertyConfig.");
        }
    }
}
