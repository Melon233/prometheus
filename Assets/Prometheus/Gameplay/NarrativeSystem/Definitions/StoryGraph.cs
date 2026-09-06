using System;
using System.Collections.Generic;
using UnityEngine;

namespace Xuan.Prometheus.Narrative
{
    /// <summary>图资产中一个参演角色的声明。</summary>
    [Serializable]
    public sealed class StoryStageActorData
    {
        [SerializeField] [Tooltip("参演角色。")] private ActorRefData actor;
        [SerializeField] [Tooltip("勾选后使用下方的初始摆位。")] private bool hasPlacement;
        [SerializeField] [Tooltip("初始摆位。")] private AnchorData placement;

        /// <summary>创建一条参演角色声明。</summary>
        public StoryStageActorData(ActorRefData actor, AnchorData? placement = null)
        {
            this.actor = actor;
            hasPlacement = placement.HasValue;
            this.placement = placement ?? default;
        }

        /// <summary>提供 Unity 序列化器所需的无参构造入口。</summary>
        public StoryStageActorData()
        {
        }

        /// <summary>获取参演角色。</summary>
        public ActorRefData Actor => actor;

        /// <summary>获取是否声明了初始摆位。</summary>
        public bool HasPlacement => hasPlacement;

        /// <summary>获取初始摆位。</summary>
        public AnchorData Placement => placement;
    }

    /// <summary>图资产中的舞台声明；与运行时 <see cref="StageSpec"/> 一一对应。</summary>
    [Serializable]
    public sealed class StoryStageData
    {
        [SerializeField] [Tooltip("进入与退出时的淡入淡出时长。")] private float fadeSeconds = 0.35f;
        [SerializeField] [Tooltip("是否隐藏玩法 HUD。")] private bool hideHud = true;
        [SerializeField] [Range(0f, 0.5f)] [Tooltip("上下黑边比例；为零表示不加黑边。")] private float letterboxRatio = 0.12f;
        [SerializeField] [Tooltip("是否接管玩法输入。")] private bool lockInput = true;
        [SerializeField] [Tooltip("AI 冻结半径；小于等于零表示不冻结。")] private float freezeAiRadius;
        [SerializeField] [Tooltip("AI 冻结中心。")] private Vector3 freezeAiCenter;
        [SerializeField] [Tooltip("是否接管演出镜头控制权。")] private bool takeCameraControl = true;
        [SerializeField] [Tooltip("演出镜头优先级。")] private int cameraPriority = 200;
        [SerializeField] [Tooltip("参演角色；这些角色会在进入舞台阶段一次性解析完毕。")] private List<StoryStageActorData> actors = new List<StoryStageActorData>();
        [SerializeField] [Tooltip("需要在淡黑期间预加载的资源地址。")] private List<string> preload = new List<string>();
        [SerializeField] [Tooltip("本段剧情会用到的演出片段地址。")] private List<string> cinematics = new List<string>();

        /// <summary>获取参演角色声明。</summary>
        public IReadOnlyList<StoryStageActorData> Actors => actors;

        /// <summary>获取预加载资源地址。</summary>
        public IReadOnlyList<string> Preload => preload;

        /// <summary>获取演出片段地址。</summary>
        public IReadOnlyList<string> Cinematics => cinematics;

        /// <summary>声明一个参演角色，并可选地指定初始摆位。</summary>
        public void AddActor(ActorRefData actor, AnchorData? placement = null)
        {
            actors.Add(new StoryStageActorData(actor, placement));
        }

        /// <summary>声明一个需要预加载的资源地址。</summary>
        public void AddPreload(string location)
        {
            if (!string.IsNullOrWhiteSpace(location) && !preload.Contains(location)) preload.Add(location);
        }

        /// <summary>声明一个本段剧情会用到的演出片段地址。</summary>
        public void AddCinematic(string location)
        {
            if (!string.IsNullOrWhiteSpace(location) && !cinematics.Contains(location)) cinematics.Add(location);
        }

        /// <summary>转换为运行时舞台声明。</summary>
        public StageSpec ToRuntime()
        {
            StageSpec spec = new StageSpec
            {
                FadeSeconds = fadeSeconds,
                HideHud = hideHud,
                LetterboxRatio = letterboxRatio,
                LockInput = lockInput,
                FreezeAiRadius = freezeAiRadius,
                FreezeAiCenter = freezeAiCenter,
                TakeCameraControl = takeCameraControl,
                CameraPriority = cameraPriority
            };
            for (int index = 0; index < actors.Count; index++)
            {
                StoryStageActorData entry = actors[index];
                if (entry == null) continue;
                ActorRef actor = entry.Actor.ToRuntime();
                if (actor.IsNone) continue;
                spec.WithActor(actor, entry.HasPlacement ? entry.Placement.ToRuntime() : (Anchor?)null);
            }
            for (int index = 0; index < preload.Count; index++) spec.WithPreload(preload[index]);
            for (int index = 0; index < cinematics.Count; index++) spec.WithCinematics(cinematics[index]);
            return spec;
        }
    }

    /// <summary>
    /// 一段剧情的图资产。
    /// <para>
    /// 节点树用 <c>[SerializeReference]</c> 做多态序列化，结构与运行时的 <see cref="IStoryAction"/> 树一一对应；
    /// <see cref="Build"/> 是资产与运行时之间唯一的转换点，因此「C# DSL 写的剧情」与「图资产写的剧情」
    /// 产出的是同一种运行时表示，跳过、续演、预览等能力对两者一视同仁。
    /// </para>
    /// </summary>
    [CreateAssetMenu(fileName = "StoryGraph", menuName = "Prometheus/Narrative/Story Graph")]
    public sealed class StoryGraph : ScriptableObject
    {
        [SerializeField] [Tooltip("剧情稳定标识；节点路径与存档都以此为根。")] private string storyId;
        [SerializeField] [Tooltip("本段剧情的舞台声明。")] private StoryStageData stage = new StoryStageData();
        [SerializeReference] [Tooltip("剧情树根节点。")] private StoryNode root;

        /// <summary>获取或设置剧情稳定标识。</summary>
        public string StoryId { get => storyId; set => storyId = value; }

        /// <summary>获取舞台声明。</summary>
        public StoryStageData Stage => stage;

        /// <summary>获取或设置剧情树根节点。</summary>
        public StoryNode Root { get => root; set => root = value; }

        /// <summary>构建运行时舞台声明。</summary>
        public StageSpec BuildStage()
        {
            return stage.ToRuntime();
        }

        /// <summary>
        /// 构建运行时剧情树。
        /// 构建失败时返回 null，并把逐条错误写入 <paramref name="errors"/>。
        /// </summary>
        public IStoryAction Build(out IReadOnlyList<string> errors)
        {
            StoryGraphBuildContext context = new StoryGraphBuildContext();
            errors = context.Errors;
            if (string.IsNullOrWhiteSpace(storyId))
            {
                context.Errors.Add("剧情图缺少 StoryId。");
                return null;
            }
            if (root == null)
            {
                context.Errors.Add("剧情图缺少根节点。");
                return null;
            }
            IStoryAction action = root.Build(context);
            if (action == null || context.HasErrors) return null;
            StoryTree.Bind(action, storyId);
            try
            {
                StoryTree.ValidateUniquePaths(action);
            }
            catch (InvalidOperationException exception)
            {
                context.Errors.Add(exception.Message);
                return null;
            }
            return action;
        }

        /// <summary>构建运行时剧情树；构建失败时抛出包含全部错误的异常。</summary>
        public IStoryAction BuildOrThrow()
        {
            IStoryAction action = Build(out IReadOnlyList<string> errors);
            if (action != null) return action;
            throw new InvalidOperationException($"剧情图 '{name}' 构建失败：\n- {string.Join("\n- ", errors)}");
        }

        /// <summary>按深度优先顺序枚举图中的全部节点。</summary>
        public IEnumerable<StoryNode> EnumerateNodes()
        {
            return Enumerate(root);
        }

        /// <summary>按深度优先顺序枚举一棵节点子树。</summary>
        public static IEnumerable<StoryNode> Enumerate(StoryNode node)
        {
            if (node == null) yield break;
            yield return node;
            IReadOnlyList<StoryNode> children = node.Children;
            for (int index = 0; index < children.Count; index++)
            {
                foreach (StoryNode descendant in Enumerate(children[index])) yield return descendant;
            }
        }

        /// <summary>查找一个节点的父节点；根节点或不属于本图时返回 null。</summary>
        public StoryNode FindParent(StoryNode child)
        {
            if (child == null || ReferenceEquals(child, root)) return null;
            foreach (StoryNode node in EnumerateNodes())
            {
                IReadOnlyList<StoryNode> children = node.Children;
                for (int index = 0; index < children.Count; index++)
                {
                    if (ReferenceEquals(children[index], child)) return node;
                }
            }
            return null;
        }
    }
}
