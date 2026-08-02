using System;
using System.Collections.Generic;
using Spine.Unity;
using UnityEngine;
using Xuan.Prometheus.Effects;

namespace Xuan.Prometheus.Actor
{
    /// <summary>标识哪个高层控制命令可以启动一个行为资产，避免运行时依赖行为名称前缀。</summary>
    public enum ActorBehaviorCommand
    {
        None,
        BasicAttack,
        Skill,
        Ultimate,
        Dodge,
        SpecialAttack
    }

    /// <summary>
    /// 指定一个资产化模拟片段如何编译为不依赖 Unity 或 Spine 的 BehaviorProgram 数据。
    /// </summary>
    [Serializable]
    public sealed class ActorSimulationClipDefinition
    {
        [SerializeField] private string clipId = "Clip";
        [SerializeField] private SimulationClipKind kind;
        [SerializeField, Min(0)] private int startTick;
        [SerializeField, Min(1)] private int endTick = 1;
        [SerializeField] private string bindingId;
        [SerializeField] private ActorCapability blockedCapabilities;

        /// <summary>获取片段稳定编号。</summary>
        public string ClipId => clipId;

        /// <summary>获取片段模拟语义。</summary>
        public SimulationClipKind Kind => kind;

        /// <summary>获取包含式开始 Tick。</summary>
        public int StartTick => startTick;

        /// <summary>获取排除式结束 Tick。</summary>
        public int EndTick => endTick;

        /// <summary>获取由运行时适配器解释的 Hitbox、事件或运动绑定编号。</summary>
        public string BindingId => bindingId;

        /// <summary>获取 CapabilityBlock 片段需要阻塞的角色能力。</summary>
        public ActorCapability BlockedCapabilities => blockedCapabilities;

        /// <summary>将只读资产配置编译为纯模拟片段。</summary>
        public SimulationClip BuildRuntimeClip()
        {
            switch (kind)
            {
                case SimulationClipKind.HitWindow: return new HitWindowClip(clipId, startTick, endTick, RequireBindingId());
                case SimulationClipKind.CapabilityBlock: return new CapabilityBlockClip(clipId, startTick, endTick, blockedCapabilities);
                case SimulationClipKind.GameplayEvent: return new GameplayEventClip(clipId, startTick, RequireBindingId());
                case SimulationClipKind.Motion: return new MotionClip(clipId, startTick, endTick, RequireBindingId());
                default: throw new ArgumentOutOfRangeException(nameof(kind), kind, "Unsupported actor simulation clip kind.");
            }
        }

        /// <summary>确保需要语义绑定的片段不会以空字符串进入运行时。</summary>
        private string RequireBindingId()
        {
            if (string.IsNullOrWhiteSpace(bindingId)) throw new InvalidOperationException($"Simulation clip '{clipId}' requires a binding ID for kind '{kind}'.");
            return bindingId;
        }

    }

    /// <summary>
    /// 指定客户端表现时间轴支持的可扩展 Cue 类型；模拟规则不会依赖这些表现枚举。
    /// </summary>
    public enum ActorPresentationCueKind
    {
        SpineAnimation,
        Audio,
        Vfx,
        Camera,
        PresentationEvent
    }

    /// <summary>指定 HitConfirmed 信号的请求伤害如何从行为资产与攻击者属性中取得。</summary>
    public enum ActorDamageSource
    {
        CalculatedAttack,
        RawAttack,
        Constant
    }

    /// <summary>
    /// 保存一个客户端表现 Cue；一个行为 Variant 可以在同一时间轴上组合任意数量动画、音效、特效、镜头和事件。
    /// </summary>
    [Serializable]
    public sealed class ActorPresentationCueDefinition
    {
        [SerializeField] private string cueId = "Cue";
        [SerializeField] private ActorPresentationCueKind kind;
        [SerializeField, Min(0)] private int startTick;
        [SerializeField, Min(0)] private int endTick;
        [SerializeField] private string bindingId;
        [SerializeField] private AnimationReferenceAsset animation;
        [SerializeField] private AudioClip audioClip;
        [SerializeField, Min(0)] private int spineTrack;
        [SerializeField] private bool loop;
        [SerializeField, Min(0f)] private float mixIn = 0.1f;
        [SerializeField, Min(0f)] private float mixOut = 0.1f;
        [SerializeField] private CameraFollowProfile cameraProfile;
        [SerializeField] private int cameraPriority = 100;

        /// <summary>获取 Cue 稳定编号。</summary>
        public string CueId => cueId;

        /// <summary>获取 Cue 表现种类。</summary>
        public ActorPresentationCueKind Kind => kind;

        /// <summary>获取 Cue 触发 Tick。</summary>
        public int StartTick => startTick;

        /// <summary>获取持续 Cue 的排除式结束 Tick；零表示随行为结束清理或仅触发一次。</summary>
        public int EndTick => endTick;

        /// <summary>获取 VFX、PresentationEvent 或其他客户端适配器使用的稳定绑定编号。</summary>
        public string BindingId => bindingId;

        /// <summary>获取 SpineAnimation Cue 使用的动画资产。</summary>
        public AnimationReferenceAsset Animation => animation;

        /// <summary>获取 Audio Cue 使用的音频资产。</summary>
        public AudioClip AudioClip => audioClip;

        /// <summary>获取 SpineAnimation Cue 独占的 Spine Track。</summary>
        public int SpineTrack => spineTrack;

        /// <summary>获取 SpineAnimation Cue 是否循环。</summary>
        public bool Loop => loop;

        /// <summary>获取 SpineAnimation Cue 混入时间。</summary>
        public float MixIn => mixIn;

        /// <summary>获取 SpineAnimation Cue 混出时间。</summary>
        public float MixOut => mixOut;

        /// <summary>获取 Camera Cue 使用的镜头配置。</summary>
        public CameraFollowProfile CameraProfile => cameraProfile;

        /// <summary>获取 Camera Cue 的请求优先级。</summary>
        public int CameraPriority => cameraPriority;
    }

    /// <summary>
    /// 保存同一个权威行为的一个客户端表现变体，例如站立攻击与移动攻击共享相同命中窗口。
    /// </summary>
    [Serializable]
    public sealed class ActorPresentationVariantDefinition
    {
        [SerializeField] private string variantId = "Default";
        [SerializeField] private List<ActorPresentationCueDefinition> cues = new List<ActorPresentationCueDefinition>();

        /// <summary>获取表现变体稳定编号。</summary>
        public string VariantId => variantId;

        /// <summary>获取按资产顺序保存的表现 Cue。</summary>
        public IReadOnlyList<ActorPresentationCueDefinition> Cues => cues;
    }

    /// <summary>
    /// 指定 HitWindow 命中后发布 EffectSignal 所需的独立战斗语义，避免所有技能被错误标记为普通攻击。
    /// </summary>
    [Serializable]
    public sealed class ActorHitSignalDefinition
    {
        [SerializeField] private string signalId = "Actor.NormalAttack";
        [SerializeField] private EffectTag tags = EffectTag.Attack | EffectTag.NormalAttack;
        [SerializeField] private ActorFactionMask targetFactions = ActorFactionMask.All;
        [SerializeField] private LayerMask targetLayerMask = ~0;
        [SerializeField] private string requiredTargetTag;
        [SerializeField] private ActorDamageSource damageSource = ActorDamageSource.CalculatedAttack;
        [SerializeField, Min(0f)] private float constantDamage = 1f;

        /// <summary>获取 EffectSignal 稳定编号。</summary>
        public string SignalId => signalId;

        /// <summary>获取 EffectSignal 语义标签。</summary>
        public EffectTag Tags => tags;

        /// <summary>获取该命中信号允许命中的稳定目标阵营集合。</summary>
        public ActorFactionMask TargetFactions => targetFactions;

        /// <summary>获取物理命中查询使用的目标层。</summary>
        public int TargetLayerMask => targetLayerMask.value;

        /// <summary>获取目标必须具有的可选 Unity Tag。</summary>
        public string RequiredTargetTag => requiredTargetTag;

        /// <summary>获取请求伤害的数据来源。</summary>
        public ActorDamageSource DamageSource => damageSource;

        /// <summary>获取未使用角色攻击伤害计算时采用的常量伤害。</summary>
        public float ConstantDamage => constantDamage;
    }

    /// <summary>
    /// 将一段权威模拟程序、连携窗口、战斗信号和多个客户端表现 Variant 组合成可复用行为资产。
    /// </summary>
    [CreateAssetMenu(fileName = "ActorBehavior", menuName = "Prometheus/Actor/Behavior")]
    public sealed class ActorBehaviorDefinition : ScriptableObject
    {
        [SerializeField] private string behaviorId = "Actor.Behavior";
        [SerializeField] private ActorBehaviorCommand command;
        [SerializeField, Min(0)] private int commandIndex;
        [SerializeField, Min(1)] private int durationTicks = 30;
        [SerializeField, Min(0)] private int chainFromTick;
        [SerializeField] private List<ActorSimulationClipDefinition> simulationClips = new List<ActorSimulationClipDefinition>();
        [SerializeField] private ActorHitSignalDefinition hitSignal = new ActorHitSignalDefinition();
        [SerializeField] private List<ActorPresentationVariantDefinition> presentationVariants = new List<ActorPresentationVariantDefinition>();

        /// <summary>获取行为稳定编号。</summary>
        public string BehaviorId => behaviorId;

        /// <summary>获取可以启动当前行为的高层控制命令。</summary>
        public ActorBehaviorCommand Command => command;

        /// <summary>获取同一命令中的稳定序号，例如普通攻击连段编号。</summary>
        public int CommandIndex => commandIndex;

        /// <summary>获取权威行为总 Tick 数。</summary>
        public int DurationTicks => durationTicks;

        /// <summary>获取允许同通道新行为替换当前行为的最早 Tick。</summary>
        public int ChainFromTick => chainFromTick;

        /// <summary>获取资产化模拟片段。</summary>
        public IReadOnlyList<ActorSimulationClipDefinition> SimulationClips => simulationClips;

        /// <summary>获取命中后发布 EffectSignal 的战斗语义。</summary>
        public ActorHitSignalDefinition HitSignal => hitSignal;

        /// <summary>获取客户端表现变体。</summary>
        public IReadOnlyList<ActorPresentationVariantDefinition> PresentationVariants => presentationVariants;

        /// <summary>把当前资产复制为可被任意实例独立执行的纯 BehaviorProgram。</summary>
        public BehaviorProgram BuildProgram()
        {
            ValidateOrThrow();
            var runtimeClips = new List<SimulationClip>(simulationClips.Count);
            for (int index = 0; index < simulationClips.Count; index++) runtimeClips.Add(simulationClips[index].BuildRuntimeClip());
            return new BehaviorProgram(behaviorId, durationTicks, runtimeClips);
        }

        /// <summary>按稳定编号查找表现变体。</summary>
        public bool TryGetPresentationVariant(string variantId, out ActorPresentationVariantDefinition variant)
        {
            for (int index = 0; index < presentationVariants.Count; index++)
            {
                ActorPresentationVariantDefinition candidate = presentationVariants[index];
                if (candidate != null && string.Equals(candidate.VariantId, variantId, StringComparison.Ordinal))
                {
                    variant = candidate;
                    return true;
                }
            }
            variant = null;
            return false;
        }

        /// <summary>验证资产中的稳定编号、区间、连携 Tick 和表现变体唯一性。</summary>
        public void ValidateOrThrow()
        {
            if (string.IsNullOrWhiteSpace(behaviorId)) throw new InvalidOperationException($"Behavior asset '{name}' requires a stable behavior ID.");
            if (durationTicks <= 0) throw new InvalidOperationException($"Behavior '{behaviorId}' requires a positive duration.");
            if (chainFromTick < 0 || chainFromTick > durationTicks) throw new InvalidOperationException($"Behavior '{behaviorId}' has an invalid chain tick '{chainFromTick}'.");
            var clipIds = new HashSet<string>(StringComparer.Ordinal);
            bool hasHitWindow = false;
            for (int index = 0; index < simulationClips.Count; index++)
            {
                ActorSimulationClipDefinition clip = simulationClips[index] ?? throw new InvalidOperationException($"Behavior '{behaviorId}' contains a null simulation clip.");
                if (string.IsNullOrWhiteSpace(clip.ClipId) || !clipIds.Add(clip.ClipId)) throw new InvalidOperationException($"Behavior '{behaviorId}' contains an empty or duplicate simulation clip ID '{clip.ClipId}'.");
                if (clip.StartTick < 0 || clip.StartTick >= durationTicks) throw new InvalidOperationException($"Behavior '{behaviorId}' clip '{clip.ClipId}' starts outside the behavior duration.");
                int effectiveEndTick = clip.Kind == SimulationClipKind.GameplayEvent ? clip.StartTick + 1 : clip.EndTick;
                if (effectiveEndTick <= clip.StartTick || effectiveEndTick > durationTicks) throw new InvalidOperationException($"Behavior '{behaviorId}' clip '{clip.ClipId}' has invalid interval '[{clip.StartTick},{effectiveEndTick})'.");
                if (clip.Kind == SimulationClipKind.HitWindow) hasHitWindow = true;
                clip.BuildRuntimeClip();
            }
            if (hasHitWindow && (hitSignal == null || string.IsNullOrWhiteSpace(hitSignal.SignalId) || hitSignal.Tags == EffectTag.None || hitSignal.TargetFactions == ActorFactionMask.None || (hitSignal.TargetFactions & ~ActorFactionMask.All) != ActorFactionMask.None)) throw new InvalidOperationException($"Behavior '{behaviorId}' has a HitWindow but does not declare a valid hit signal ID, tags and target factions.");
            var variantIds = new HashSet<string>(StringComparer.Ordinal);
            for (int index = 0; index < presentationVariants.Count; index++)
            {
                ActorPresentationVariantDefinition variant = presentationVariants[index] ?? throw new InvalidOperationException($"Behavior '{behaviorId}' contains a null presentation variant.");
                if (string.IsNullOrWhiteSpace(variant.VariantId) || !variantIds.Add(variant.VariantId)) throw new InvalidOperationException($"Behavior '{behaviorId}' contains an empty or duplicate presentation variant ID '{variant.VariantId}'.");
                var cueIds = new HashSet<string>(StringComparer.Ordinal);
                for (int cueIndex = 0; cueIndex < variant.Cues.Count; cueIndex++)
                {
                    ActorPresentationCueDefinition cue = variant.Cues[cueIndex] ?? throw new InvalidOperationException($"Behavior '{behaviorId}' variant '{variant.VariantId}' contains a null Cue.");
                    if (string.IsNullOrWhiteSpace(cue.CueId) || !cueIds.Add(cue.CueId)) throw new InvalidOperationException($"Behavior '{behaviorId}' variant '{variant.VariantId}' contains an empty or duplicate Cue ID '{cue.CueId}'.");
                    if (cue.StartTick < 0 || cue.StartTick >= durationTicks || cue.EndTick > durationTicks || cue.EndTick > 0 && cue.EndTick <= cue.StartTick) throw new InvalidOperationException($"Behavior '{behaviorId}' Cue '{cue.CueId}' has an invalid interval '[{cue.StartTick},{cue.EndTick})'.");
                    if (cue.Kind == ActorPresentationCueKind.SpineAnimation && cue.Animation == null) throw new InvalidOperationException($"Behavior '{behaviorId}' Spine Cue '{cue.CueId}' requires an animation.");
                    if (cue.Kind == ActorPresentationCueKind.Audio && cue.AudioClip == null) throw new InvalidOperationException($"Behavior '{behaviorId}' Audio Cue '{cue.CueId}' requires an audio clip.");
                    if ((cue.Kind == ActorPresentationCueKind.Vfx || cue.Kind == ActorPresentationCueKind.PresentationEvent) && string.IsNullOrWhiteSpace(cue.BindingId)) throw new InvalidOperationException($"Behavior '{behaviorId}' Cue '{cue.CueId}' requires a binding ID.");
                    if (cue.Kind == ActorPresentationCueKind.Camera && cue.CameraProfile == null) throw new InvalidOperationException($"Behavior '{behaviorId}' Camera Cue '{cue.CueId}' requires a camera profile.");
                }
            }
            if (!variantIds.Contains("Default")) throw new InvalidOperationException($"Behavior '{behaviorId}' requires a 'Default' presentation variant.");
        }
    }
}
