using System;
using System.Collections.Generic;
using Spine.Unity;
using UnityEngine;

namespace Xuan.Prometheus.Actor
{
    /// <summary>标识 GameplayObject 在内容层面的基础分类；分类只用于资产筛选，不限制对象可以安装的模块。</summary>
    public enum GameplayObjectCategory
    {
        Character,
        Monster,
        Mechanism,
        Vehicle,
        Prop
    }

    /// <summary>
    /// 保存行为通道空闲时使用的 Spine 基础状态动画；飞行、载具或机关可以只填写自身需要的条目。
    /// </summary>
    [Serializable]
    public sealed class ActorLocomotionPresentationDefinition
    {
        [SerializeField] private AnimationReferenceAsset idle;
        [SerializeField] private AnimationReferenceAsset move;
        [SerializeField] private AnimationReferenceAsset sprint;
        [SerializeField] private AnimationReferenceAsset jump;
        [SerializeField] private AnimationReferenceAsset fall;
        [SerializeField] private AnimationReferenceAsset land;
        [SerializeField, Min(0f)] private float mixDuration = 0.15f;

        /// <summary>获取待机动画。</summary>
        public AnimationReferenceAsset Idle => idle;

        /// <summary>获取普通移动动画。</summary>
        public AnimationReferenceAsset Move => move;

        /// <summary>获取冲刺动画。</summary>
        public AnimationReferenceAsset Sprint => sprint;

        /// <summary>获取起跳动画。</summary>
        public AnimationReferenceAsset Jump => jump;

        /// <summary>获取下落动画。</summary>
        public AnimationReferenceAsset Fall => fall;

        /// <summary>获取落地动画。</summary>
        public AnimationReferenceAsset Land => land;

        /// <summary>获取基础状态动画混合时间。</summary>
        public float MixDuration => mixDuration;
    }

    /// <summary>把 Behavior Core 中稳定 MotionId 映射为客户端每个行为 Tick 应用的局部位移。</summary>
    [Serializable]
    public sealed class ActorMotionBindingDefinition
    {
        [SerializeField] private string motionId = "Motion";
        [SerializeField] private string requiredVariantId;
        [SerializeField] private Vector3 localDisplacementPerBehaviorTick;
        [SerializeField] private List<Vector3> localDisplacementsByBehaviorTick = new List<Vector3>();

        /// <summary>获取 MotionClip 使用的稳定绑定编号。</summary>
        public string MotionId => motionId;

        /// <summary>获取允许该位移绑定生效的确定性行为变体；空值表示所有变体共享。</summary>
        public string RequiredVariantId => requiredVariantId;

        /// <summary>获取每个权威行为 Tick 产生的对象局部位移。</summary>
        public Vector3 LocalDisplacementPerBehaviorTick => localDisplacementPerBehaviorTick;

        /// <summary>获取从动画根骨骼离线烘焙的逐行为 Tick 位移数量；零表示使用常量位移。</summary>
        public int BakedDisplacementCount => localDisplacementsByBehaviorTick == null ? 0 : localDisplacementsByBehaviorTick.Count;

        /// <summary>读取指定行为 Tick 的权威局部位移；烘焙数据之外返回零，未烘焙时回退到常量位移。</summary>
        public Vector3 GetLocalDisplacement(int behaviorTick)
        {
            if (behaviorTick < 0) throw new ArgumentOutOfRangeException(nameof(behaviorTick), behaviorTick, "Behavior tick cannot be negative.");
            if (localDisplacementsByBehaviorTick == null || localDisplacementsByBehaviorTick.Count == 0) return localDisplacementPerBehaviorTick;
            return behaviorTick < localDisplacementsByBehaviorTick.Count ? localDisplacementsByBehaviorTick[behaviorTick] : Vector3.zero;
        }

        /// <summary>验证位移绑定只包含有限数值，并拒绝看似非空但无法匹配 Variant 的空白编号。</summary>
        internal void ValidateOrThrow(string actorId)
        {
            if (!string.IsNullOrEmpty(requiredVariantId) && string.IsNullOrWhiteSpace(requiredVariantId)) throw new InvalidOperationException($"Actor '{actorId}' motion '{motionId}' contains a whitespace-only required variant ID.");
            if (!IsFinite(localDisplacementPerBehaviorTick)) throw new InvalidOperationException($"Actor '{actorId}' motion '{motionId}' contains a non-finite constant displacement.");
            if (localDisplacementsByBehaviorTick == null) throw new InvalidOperationException($"Actor '{actorId}' motion '{motionId}' requires a baked-displacement collection.");
            for (int index = 0; index < localDisplacementsByBehaviorTick.Count; index++)
            {
                if (!IsFinite(localDisplacementsByBehaviorTick[index])) throw new InvalidOperationException($"Actor '{actorId}' motion '{motionId}' contains a non-finite baked displacement at tick '{index}'.");
            }
        }

        /// <summary>判断三维向量的每个分量都可以安全进入物理模拟和跨端数据导出。</summary>
        private static bool IsFinite(Vector3 value)
        {
            return !float.IsNaN(value.x) && !float.IsInfinity(value.x) && !float.IsNaN(value.y) && !float.IsInfinity(value.y) && !float.IsNaN(value.z) && !float.IsInfinity(value.z);
        }
    }

    /// <summary>
    /// 定义一个可复用 GameplayObject 的能力、运动、基础表现和行为资产集合；资产本身永远不保存实例可变状态。
    /// </summary>
    [CreateAssetMenu(fileName = "ActorDefinition", menuName = "Prometheus/Actor/Definition")]
    public sealed class ActorDefinition : ScriptableObject
    {
        [SerializeField] private string actorId = "Actor";
        [SerializeField] private GameplayObjectCategory category = GameplayObjectCategory.Character;
        [SerializeField] private ActorFaction faction = ActorFaction.Neutral;
        [SerializeField] private ActorCapability defaultCapabilities = ActorCapability.All;
        [SerializeField] private ActorMotionModelDefinition motionModel;
        [SerializeField] private CameraFollowProfile cameraProfile;
        [SerializeField] private ActorLocomotionPresentationDefinition locomotionPresentation = new ActorLocomotionPresentationDefinition();
        [SerializeField, Min(0f)] private float moveSpeed = 5f;
        [SerializeField, Min(0f)] private float sprintSpeed = 8f;
        [SerializeField, Min(1)] private int heldAttackSpecialTriggerTicks = 30;
        [SerializeField] private List<ActorMotionBindingDefinition> motionBindings = new List<ActorMotionBindingDefinition>();
        [SerializeField] private List<ActorBehaviorDefinition> behaviors = new List<ActorBehaviorDefinition>();

        /// <summary>获取跨资产和网络协议稳定的对象编号。</summary>
        public string ActorId => actorId;

        /// <summary>获取对象内容分类。</summary>
        public GameplayObjectCategory Category => category;

        /// <summary>获取服务器可复用的稳定战斗阵营；命中规则不依赖 Unity Tag 或 Layer。</summary>
        public ActorFaction Faction => faction;

        /// <summary>获取对象默认支持的能力。</summary>
        public ActorCapability DefaultCapabilities => defaultCapabilities;

        /// <summary>获取客户端运动模型工厂资产。</summary>
        public ActorMotionModelDefinition MotionModel => motionModel;

        /// <summary>获取对象成为本地镜头目标时使用的基础跟随配置；非镜头对象可以留空。</summary>
        public CameraFollowProfile CameraProfile => cameraProfile;

        /// <summary>获取 Spine 基础状态表现配置。</summary>
        public ActorLocomotionPresentationDefinition LocomotionPresentation => locomotionPresentation;

        /// <summary>获取普通移动速度。</summary>
        public float MoveSpeed => moveSpeed;

        /// <summary>获取冲刺移动速度。</summary>
        public float SprintSpeed => sprintSpeed;

        /// <summary>获取连续按住普通攻击多少个固定 Tick 后请求特殊攻击；默认三十 Tick 对应 60 Hz 下的 0.5 秒。</summary>
        public int HeldAttackSpecialTriggerTicks => heldAttackSpecialTriggerTicks;

        /// <summary>获取行为时间轴可使用的资产化运动绑定。</summary>
        public IReadOnlyList<ActorMotionBindingDefinition> MotionBindings => motionBindings;

        /// <summary>获取对象可启动的全部行为资产。</summary>
        public IReadOnlyList<ActorBehaviorDefinition> Behaviors => behaviors;

        /// <summary>按稳定编号查找行为资产。</summary>
        public bool TryGetBehavior(string behaviorId, out ActorBehaviorDefinition behavior)
        {
            for (int index = 0; index < behaviors.Count; index++)
            {
                ActorBehaviorDefinition candidate = behaviors[index];
                if (candidate != null && string.Equals(candidate.BehaviorId, behaviorId, StringComparison.Ordinal))
                {
                    behavior = candidate;
                    return true;
                }
            }
            behavior = null;
            return false;
        }

        /// <summary>按稳定 MotionId 查找行为位移绑定。</summary>
        public bool TryGetMotionBinding(string motionId, out ActorMotionBindingDefinition binding)
        {
            for (int index = 0; index < motionBindings.Count; index++)
            {
                ActorMotionBindingDefinition candidate = motionBindings[index];
                if (candidate != null && string.Equals(candidate.MotionId, motionId, StringComparison.Ordinal))
                {
                    binding = candidate;
                    return true;
                }
            }
            binding = null;
            return false;
        }

        /// <summary>验证对象资产的稳定编号、必需运动模型和行为编号唯一性。</summary>
        public void ValidateOrThrow()
        {
            if (string.IsNullOrWhiteSpace(actorId)) throw new InvalidOperationException($"Actor definition '{name}' requires a stable actor ID.");
            if (!Enum.IsDefined(typeof(ActorFaction), faction)) throw new InvalidOperationException($"Actor '{actorId}' contains an unsupported faction '{faction}'.");
            if ((defaultCapabilities & ~ActorCapability.All) != ActorCapability.None) throw new InvalidOperationException($"Actor '{actorId}' contains undeclared capability bits.");
            if (motionModel == null) throw new InvalidOperationException($"Actor '{actorId}' requires a motion model definition.");
            if (heldAttackSpecialTriggerTicks <= 0) throw new InvalidOperationException($"Actor '{actorId}' requires a positive held-attack special trigger duration.");
            var motionIds = new HashSet<string>(StringComparer.Ordinal);
            for (int index = 0; index < motionBindings.Count; index++)
            {
                ActorMotionBindingDefinition binding = motionBindings[index] != null ? motionBindings[index] : throw new InvalidOperationException($"Actor '{actorId}' contains a null motion binding.");
                if (string.IsNullOrWhiteSpace(binding.MotionId) || !motionIds.Add(binding.MotionId)) throw new InvalidOperationException($"Actor '{actorId}' contains an empty or duplicate motion ID '{binding.MotionId}'.");
                binding.ValidateOrThrow(actorId);
            }
            var behaviorIds = new HashSet<string>(StringComparer.Ordinal);
            for (int index = 0; index < behaviors.Count; index++)
            {
                ActorBehaviorDefinition behavior = behaviors[index] != null ? behaviors[index] : throw new InvalidOperationException($"Actor '{actorId}' contains a null behavior asset.");
                behavior.ValidateOrThrow();
                if (!behaviorIds.Add(behavior.BehaviorId)) throw new InvalidOperationException($"Actor '{actorId}' contains duplicate behavior ID '{behavior.BehaviorId}'.");
            }
        }
    }
}
