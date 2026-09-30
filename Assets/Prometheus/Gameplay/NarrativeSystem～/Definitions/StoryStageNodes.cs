using System;
using UnityEngine;

namespace Xuan.Prometheus.Narrative
{
    /// <summary>可序列化的摆位目标。</summary>
    [Serializable]
    public struct AnchorData
    {
        [Tooltip("勾选后使用下方的固定世界坐标，否则按标识查找场景锚点。")] public bool useWorldPosition;
        [Tooltip("场景锚点标识。")] public string anchorId;
        [Tooltip("固定世界坐标。")] public Vector3 worldPosition;

        /// <summary>转换为运行时摆位目标。</summary>
        public Anchor ToRuntime()
        {
            return useWorldPosition ? Anchor.World(worldPosition) : Anchor.Named(anchorId);
        }

        /// <summary>获取该目标是否已经填写完整。</summary>
        public bool IsValid => useWorldPosition || !string.IsNullOrWhiteSpace(anchorId);
    }

    /// <summary>可序列化的特效与音效落点。</summary>
    [Serializable]
    public struct VfxPlacementData
    {
        [Tooltip("勾选后跟随角色，否则落在场景锚点上。")] public bool followActor;
        [Tooltip("跟随的角色。")] public ActorRefData actor;
        [Tooltip("落点锚点。")] public AnchorData anchor;
        [Tooltip("相对基准点的偏移。")] public Vector3 offset;

        /// <summary>转换为运行时落点。</summary>
        public VfxPlacement ToRuntime()
        {
            return followActor ? VfxPlacement.On(actor.ToRuntime(), offset) : VfxPlacement.At(anchor.ToRuntime(), offset);
        }
    }

    /// <summary>演出片段节点。</summary>
    [Serializable]
    public sealed class CinematicNode : StoryNode
    {
        [SerializeField] [Tooltip("Timeline 资源地址；必须在 StageSpec.Cinematics 中声明。")] private string location;
        [SerializeField] [Tooltip("起始区间标记名；留空表示从当前时间继续。")] private string fromCue;
        [SerializeField] [Tooltip("结束区间标记名；留空表示播到片段结尾。")] private string toCue;

        /// <summary>获取或设置 Timeline 资源地址。</summary>
        public string Location { get => location; set => location = value; }

        /// <summary>获取或设置起始区间标记名。</summary>
        public string FromCue { get => fromCue; set => fromCue = value; }

        /// <summary>获取或设置结束区间标记名。</summary>
        public string ToCue { get => toCue; set => toCue = value; }

        /// <inheritdoc />
        public override string DisplayName => string.IsNullOrEmpty(toCue) ? $"演出 {location}" : $"演出 {location} [{fromCue}..{toCue}]";

        /// <inheritdoc />
        public override void CollectRequirements(StoryNodeRequirements requirements)
        {
            if (!string.IsNullOrWhiteSpace(location) && !requirements.Cinematics.Contains(location)) requirements.Cinematics.Add(location);
        }

        /// <inheritdoc />
        public override IStoryAction Build(StoryGraphBuildContext context)
        {
            if (string.IsNullOrWhiteSpace(location))
            {
                context.Error(this, "演出节点需要一个资源地址。");
                return null;
            }
            return WithId(new CinematicAction(location, fromCue, toCue));
        }
    }

    /// <summary>镜头切换节点。</summary>
    [Serializable]
    public sealed class CameraBlendNode : StoryNode
    {
        [SerializeField] [Tooltip("场景中登记的具名演出机位。")] private string cameraId;
        [SerializeField] [Tooltip("混合时长；为零表示硬切。")] private float duration = 0.8f;

        /// <summary>获取或设置机位名。</summary>
        public string CameraId { get => cameraId; set => cameraId = value; }

        /// <summary>获取或设置混合时长。</summary>
        public float Duration { get => duration; set => duration = value; }

        /// <inheritdoc />
        public override string DisplayName => duration <= 0f ? $"镜头硬切 {cameraId}" : $"镜头混合 {cameraId} ({duration:0.##}s)";

        /// <inheritdoc />
        public override IStoryAction Build(StoryGraphBuildContext context)
        {
            if (string.IsNullOrWhiteSpace(cameraId))
            {
                context.Error(this, "镜头节点需要一个机位名。");
                return null;
            }
            return WithId(new CameraBlendAction(cameraId, duration));
        }
    }

    /// <summary>镜头抖动节点。</summary>
    [Serializable]
    public sealed class CameraShakeNode : StoryNode
    {
        [SerializeField] [Tooltip("抖动幅度。")] private float amplitude = 0.25f;
        [SerializeField] [Tooltip("抖动时长。")] private float duration = 0.35f;

        /// <inheritdoc />
        public override string DisplayName => $"镜头抖动 ({amplitude:0.##}, {duration:0.##}s)";

        /// <inheritdoc />
        public override IStoryAction Build(StoryGraphBuildContext context)
        {
            return WithId(new CameraShakeAction(amplitude, duration));
        }
    }

    /// <summary>特效生成节点。</summary>
    [Serializable]
    public sealed class VfxNode : StoryNode
    {
        [SerializeField] [Tooltip("特效预制体资源地址。")] private string location;
        [SerializeField] [Tooltip("生成位置。")] private VfxPlacementData placement;
        [SerializeField] [Tooltip("存活秒数；小于等于零表示随舞台退出才回收。")] private float lifetime;
        [SerializeField] [Tooltip("勾选后阻塞到存活时间结束。")] private bool waitForCompletion;

        /// <summary>获取或设置特效资源地址。</summary>
        public string Location { get => location; set => location = value; }

        /// <summary>获取或设置生成位置。</summary>
        public VfxPlacementData Placement { get => placement; set => placement = value; }

        /// <summary>获取或设置存活秒数。</summary>
        public float Lifetime { get => lifetime; set => lifetime = value; }

        /// <inheritdoc />
        public override string DisplayName => $"特效 {location}";

        /// <inheritdoc />
        public override void CollectRequirements(StoryNodeRequirements requirements)
        {
            if (placement.followActor) requirements.RequireActor(placement.actor);
        }

        /// <inheritdoc />
        public override IStoryAction Build(StoryGraphBuildContext context)
        {
            if (string.IsNullOrWhiteSpace(location))
            {
                context.Error(this, "特效节点需要一个资源地址。");
                return null;
            }
            return WithId(new VfxSpawnAction(location, placement.ToRuntime(), lifetime, waitForCompletion));
        }
    }

    /// <summary>一次性音效节点。</summary>
    [Serializable]
    public sealed class SfxNode : StoryNode
    {
        [SerializeField] [Tooltip("音频事件键。")] private string eventKey;
        [SerializeField] [Tooltip("勾选后按下方落点播放三维音效，否则不带位置。")] private bool positional;
        [SerializeField] [Tooltip("播放落点。")] private VfxPlacementData placement;

        /// <summary>获取或设置音频事件键。</summary>
        public string EventKey { get => eventKey; set => eventKey = value; }

        /// <summary>获取或设置是否按落点播放三维音效。</summary>
        public bool Positional { get => positional; set => positional = value; }

        /// <summary>获取或设置播放落点。</summary>
        public VfxPlacementData Placement { get => placement; set => placement = value; }

        /// <inheritdoc />
        public override string DisplayName => $"音效 {eventKey}";

        /// <inheritdoc />
        public override void CollectRequirements(StoryNodeRequirements requirements)
        {
            if (positional && placement.followActor) requirements.RequireActor(placement.actor);
        }

        /// <inheritdoc />
        public override IStoryAction Build(StoryGraphBuildContext context)
        {
            if (string.IsNullOrWhiteSpace(eventKey))
            {
                context.Error(this, "音效节点需要一个事件键。");
                return null;
            }
            return WithId(positional ? new SfxAction(eventKey, placement.ToRuntime()) : new SfxAction(eventKey));
        }
    }

    /// <summary>持续音频节点。</summary>
    [Serializable]
    public sealed class AmbienceNode : StoryNode
    {
        [SerializeField] [Tooltip("音频事件键。")] private string eventKey;
        [SerializeField] [Tooltip("淡入秒数。")] private float fadeInSeconds;
        [SerializeField] [Tooltip("淡出秒数。")] private float fadeOutSeconds = 1f;

        /// <summary>获取或设置音频事件键。</summary>
        public string EventKey { get => eventKey; set => eventKey = value; }

        /// <summary>获取或设置淡入秒数。</summary>
        public float FadeInSeconds { get => fadeInSeconds; set => fadeInSeconds = value; }

        /// <summary>获取或设置淡出秒数。</summary>
        public float FadeOutSeconds { get => fadeOutSeconds; set => fadeOutSeconds = value; }

        /// <inheritdoc />
        public override string DisplayName => $"持续音频 {eventKey}";

        /// <inheritdoc />
        public override IStoryAction Build(StoryGraphBuildContext context)
        {
            if (string.IsNullOrWhiteSpace(eventKey))
            {
                context.Error(this, "持续音频节点需要一个事件键。");
                return null;
            }
            return WithId(new AmbienceAction(eventKey, fadeInSeconds, fadeOutSeconds));
        }
    }

    /// <summary>角色或场景物体的位移节点。</summary>
    [Serializable]
    public sealed class MoveToNode : StoryNode
    {
        [SerializeField] [Tooltip("被移动的角色或场景物体。")] private ActorRefData actor;
        [SerializeField] [Tooltip("终点。")] private AnchorData destination;
        [SerializeField] [Tooltip("移动时长；为零表示瞬移。")] private float duration = 1f;
        [SerializeField] [Tooltip("勾选后移动过程中朝向前进方向。")] private bool faceMoveDirection;

        /// <summary>获取或设置被移动的角色。</summary>
        public ActorRefData Actor { get => actor; set => actor = value; }

        /// <summary>获取或设置终点。</summary>
        public AnchorData Destination { get => destination; set => destination = value; }

        /// <summary>获取或设置移动时长。</summary>
        public float Duration { get => duration; set => duration = value; }

        /// <inheritdoc />
        public override string DisplayName => $"走位 {actor.id} → {(destination.useWorldPosition ? destination.worldPosition.ToString() : destination.anchorId)}";

        /// <inheritdoc />
        public override void CollectRequirements(StoryNodeRequirements requirements)
        {
            requirements.RequireActor(actor);
        }

        /// <inheritdoc />
        public override IStoryAction Build(StoryGraphBuildContext context)
        {
            if (actor.kind == ActorKind.None || string.IsNullOrWhiteSpace(actor.id))
            {
                context.Error(this, "走位节点需要一个角色。");
                return null;
            }
            if (!destination.IsValid)
            {
                context.Error(this, "走位节点需要一个终点。");
                return null;
            }
            return WithId(new ActorMoveAction(actor.ToRuntime(), destination.ToRuntime(), duration, faceMoveDirection));
        }
    }

    /// <summary>角色朝向节点。</summary>
    [Serializable]
    public sealed class FaceNode : StoryNode
    {
        [SerializeField] [Tooltip("要转向的角色。")] private ActorRefData actor;
        [SerializeField] [Tooltip("勾选后朝向另一个角色，否则朝向下方欧拉角。")] private bool lookAtActor = true;
        [SerializeField] [Tooltip("被朝向的角色。")] private ActorRefData target;
        [SerializeField] [Tooltip("朝向的欧拉角。")] private Vector3 eulerAngles;

        /// <summary>获取或设置要转向的角色。</summary>
        public ActorRefData Actor { get => actor; set => actor = value; }

        /// <summary>获取或设置被朝向的角色。</summary>
        public ActorRefData Target { get => target; set => target = value; }

        /// <inheritdoc />
        public override string DisplayName => lookAtActor ? $"朝向 {actor.id} → {target.id}" : $"朝向 {actor.id} → {eulerAngles.y:0}°";

        /// <inheritdoc />
        public override void CollectRequirements(StoryNodeRequirements requirements)
        {
            requirements.RequireActor(actor);
            if (lookAtActor) requirements.RequireActor(target);
        }

        /// <inheritdoc />
        public override IStoryAction Build(StoryGraphBuildContext context)
        {
            if (actor.kind == ActorKind.None || string.IsNullOrWhiteSpace(actor.id))
            {
                context.Error(this, "朝向节点需要一个角色。");
                return null;
            }
            if (!lookAtActor) return WithId(new ActorFaceAction(actor.ToRuntime(), eulerAngles));
            if (target.kind == ActorKind.None || string.IsNullOrWhiteSpace(target.id))
            {
                context.Error(this, "朝向节点需要一个被朝向的角色。");
                return null;
            }
            return WithId(new ActorFaceAction(actor.ToRuntime(), target.ToRuntime()));
        }
    }

    /// <summary>角色或场景物体的显隐节点。</summary>
    [Serializable]
    public sealed class ShowNode : StoryNode
    {
        [SerializeField] [Tooltip("目标角色或场景物体。")] private ActorRefData actor;
        [SerializeField] [Tooltip("目标显隐状态。")] private bool visible = true;

        /// <summary>获取或设置目标角色或场景物体。</summary>
        public ActorRefData Actor { get => actor; set => actor = value; }

        /// <summary>获取或设置目标显隐状态。</summary>
        public bool Visible { get => visible; set => visible = value; }

        /// <inheritdoc />
        public override string DisplayName => $"{(visible ? "显示" : "隐藏")} {actor.id}";

        /// <inheritdoc />
        public override void CollectRequirements(StoryNodeRequirements requirements)
        {
            requirements.RequireActor(actor);
        }

        /// <inheritdoc />
        public override IStoryAction Build(StoryGraphBuildContext context)
        {
            if (actor.kind == ActorKind.None || string.IsNullOrWhiteSpace(actor.id))
            {
                context.Error(this, "显隐节点需要一个角色或场景物体。");
                return null;
            }
            return WithId(new ActorVisibilityAction(actor.ToRuntime(), visible));
        }
    }

    /// <summary>Spine 角色动画节点。</summary>
    [Serializable]
    public sealed class SpineAnimNode : StoryNode
    {
        [SerializeField] [Tooltip("目标角色。")] private ActorRefData actor;
        [SerializeField] [Tooltip("Spine 骨骼数据中的动画名。")] private string animationName;
        [SerializeField] [Tooltip("勾选后循环播放；循环动画不会自然结束，应放在分离并发节点下。")] private bool loop;
        [SerializeField] [Tooltip("采样速度倍率。")] private float speed = 1f;

        /// <summary>获取或设置动画名。</summary>
        public string AnimationName { get => animationName; set => animationName = value; }

        /// <summary>获取或设置目标角色。</summary>
        public ActorRefData Actor { get => actor; set => actor = value; }

        /// <summary>获取或设置是否循环播放。</summary>
        public bool Loop { get => loop; set => loop = value; }

        /// <inheritdoc />
        public override string DisplayName => $"骨骼动画 {actor.id}:{animationName}{(loop ? " (循环)" : string.Empty)}";

        /// <inheritdoc />
        public override void CollectRequirements(StoryNodeRequirements requirements)
        {
            requirements.RequireActor(actor);
        }

        /// <inheritdoc />
        public override IStoryAction Build(StoryGraphBuildContext context)
        {
            if (actor.kind == ActorKind.None || string.IsNullOrWhiteSpace(actor.id))
            {
                context.Error(this, "骨骼动画节点需要一个角色。");
                return null;
            }
            if (string.IsNullOrWhiteSpace(animationName))
            {
                context.Error(this, "骨骼动画节点需要一个动画名。");
                return null;
            }
            return WithId(new SpineAnimAction(actor.ToRuntime(), animationName, loop, speed));
        }
    }

    /// <summary>场景物体原生动画节点。</summary>
    [Serializable]
    public sealed class PropAnimNode : StoryNode
    {
        [SerializeField] [Tooltip("目标场景物体。")] private ActorRefData prop;
        [SerializeField] [Tooltip("AnimationClip 资源地址；必须在 StageSpec.Preload 中声明。")] private string clipLocation;
        [SerializeField] [Tooltip("采样速度倍率。")] private float speed = 1f;

        /// <summary>获取或设置动画片段资源地址。</summary>
        public string ClipLocation { get => clipLocation; set => clipLocation = value; }

        /// <summary>获取或设置目标场景物体。</summary>
        public ActorRefData Prop { get => prop; set => prop = value; }

        /// <inheritdoc />
        public override string DisplayName => $"物体动画 {prop.id}:{clipLocation}";

        /// <inheritdoc />
        public override void CollectRequirements(StoryNodeRequirements requirements)
        {
            requirements.RequireActor(prop);
            // 落终态是同步过程，无法等待加载，因此片段必须预加载。
            if (!string.IsNullOrWhiteSpace(clipLocation) && !requirements.Preload.Contains(clipLocation)) requirements.Preload.Add(clipLocation);
        }

        /// <inheritdoc />
        public override IStoryAction Build(StoryGraphBuildContext context)
        {
            if (prop.kind == ActorKind.None || string.IsNullOrWhiteSpace(prop.id))
            {
                context.Error(this, "物体动画节点需要一个目标物体。");
                return null;
            }
            if (string.IsNullOrWhiteSpace(clipLocation))
            {
                context.Error(this, "物体动画节点需要一个片段资源地址。");
                return null;
            }
            return WithId(new PropAnimAction(prop.ToRuntime(), clipLocation, speed));
        }
    }

    /// <summary>黑幕过渡节点。</summary>
    [Serializable]
    public sealed class ScreenFadeNode : StoryNode
    {
        [SerializeField] [Range(0f, 1f)] [Tooltip("目标不透明度；1 为全黑。")] private float target = 1f;
        [SerializeField] [Tooltip("过渡时长。")] private float duration = 0.35f;

        /// <summary>获取或设置目标不透明度。</summary>
        public float Target { get => target; set => target = value; }

        /// <summary>获取或设置过渡时长。</summary>
        public float Duration { get => duration; set => duration = value; }

        /// <inheritdoc />
        public override string DisplayName => target >= 1f ? $"淡出到黑 ({duration:0.##}s)" : $"淡入 ({duration:0.##}s)";

        /// <inheritdoc />
        public override IStoryAction Build(StoryGraphBuildContext context)
        {
            return WithId(new ScreenFadeAction(target, duration));
        }
    }

    /// <summary>黑边过渡节点。</summary>
    [Serializable]
    public sealed class LetterboxNode : StoryNode
    {
        [SerializeField] [Range(0f, 0.5f)] [Tooltip("上下黑边占屏幕高度的比例。")] private float ratio = 0.12f;
        [SerializeField] [Tooltip("过渡时长。")] private float duration = 0.3f;

        /// <summary>获取或设置黑边比例。</summary>
        public float Ratio { get => ratio; set => ratio = value; }

        /// <summary>获取或设置过渡时长。</summary>
        public float Duration { get => duration; set => duration = value; }

        /// <inheritdoc />
        public override string DisplayName => $"黑边 {ratio:0.##}";

        /// <inheritdoc />
        public override IStoryAction Build(StoryGraphBuildContext context)
        {
            return WithId(new ScreenLetterboxAction(ratio, duration));
        }
    }

    /// <summary>HUD 显隐节点。</summary>
    [Serializable]
    public sealed class HudNode : StoryNode
    {
        [SerializeField] [Tooltip("目标显隐状态。")] private bool visible;

        /// <summary>获取或设置目标显隐状态。</summary>
        public bool Visible { get => visible; set => visible = value; }

        /// <inheritdoc />
        public override string DisplayName => visible ? "显示 HUD" : "隐藏 HUD";

        /// <inheritdoc />
        public override IStoryAction Build(StoryGraphBuildContext context)
        {
            return WithId(new ScreenHudAction(visible));
        }
    }
}
