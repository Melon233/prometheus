using System;
using System.Collections.Generic;
using UnityEngine;

namespace Xuan.Prometheus.Narrative
{
    /// <summary>
    /// 舞台运行所需的全部外部能力端口。
    /// 端口可以缺省；缺省端口只有在剧情真正用到对应能力时才报错，并明确指出缺少哪一个端口。
    /// </summary>
    public sealed class StageServices
    {
        /// <summary>创建一组舞台能力端口。</summary>
        /// <param name="screen">屏幕表现端口；淡入淡出与黑边是舞台自身的必需能力。</param>
        /// <param name="actors">角色解析端口。</param>
        public StageServices(INarrativeScreen screen, IActorResolver actors)
        {
            Screen = screen ?? throw new ArgumentNullException(nameof(screen));
            Actors = actors ?? throw new ArgumentNullException(nameof(actors));
        }

        /// <summary>获取屏幕表现端口。</summary>
        public INarrativeScreen Screen { get; }

        /// <summary>获取角色解析端口。</summary>
        public IActorResolver Actors { get; }

        /// <summary>获取或设置演出镜头端口。</summary>
        public INarrativeCameraPort Camera { get; set; }

        /// <summary>获取或设置特效端口。</summary>
        public INarrativeVfxPort Vfx { get; set; }

        /// <summary>获取或设置音频端口。</summary>
        public INarrativeAudioPort Audio { get; set; }

        /// <summary>获取或设置世界接管端口。</summary>
        public INarrativeWorldPort World { get; set; }

        /// <summary>获取或设置资源端口。</summary>
        public INarrativeAssetPort Assets { get; set; }

        /// <summary>取用演出镜头端口；缺省时抛出指明端口名的错误。</summary>
        public INarrativeCameraPort RequireCamera()
        {
            return Camera ?? throw new InvalidOperationException($"Narrative stage requires an {nameof(INarrativeCameraPort)}.");
        }

        /// <summary>取用特效端口；缺省时抛出指明端口名的错误。</summary>
        public INarrativeVfxPort RequireVfx()
        {
            return Vfx ?? throw new InvalidOperationException($"Narrative stage requires an {nameof(INarrativeVfxPort)}.");
        }

        /// <summary>取用音频端口；缺省时抛出指明端口名的错误。</summary>
        public INarrativeAudioPort RequireAudio()
        {
            return Audio ?? throw new InvalidOperationException($"Narrative stage requires an {nameof(INarrativeAudioPort)}.");
        }

        /// <summary>取用资源端口；缺省时抛出指明端口名的错误。</summary>
        public INarrativeAssetPort RequireAssets()
        {
            return Assets ?? throw new InvalidOperationException($"Narrative stage requires an {nameof(INarrativeAssetPort)}.");
        }
    }

    /// <summary>描述一次舞台接管所需的全部环境改动。</summary>
    public sealed class StageSpec
    {
        /// <summary>获取或设置进入与退出时的淡入淡出时长；小于等于零表示不做淡入淡出。</summary>
        public float FadeSeconds { get; set; } = 0.35f;

        /// <summary>获取或设置是否隐藏玩法 HUD。</summary>
        public bool HideHud { get; set; } = true;

        /// <summary>获取或设置上下黑边比例；小于等于零表示不加黑边。</summary>
        public float LetterboxRatio { get; set; } = 0.12f;

        /// <summary>获取或设置是否接管玩法输入。</summary>
        public bool LockInput { get; set; } = true;

        /// <summary>获取或设置 AI 冻结半径；小于等于零表示不冻结。</summary>
        public float FreezeAiRadius { get; set; }

        /// <summary>获取或设置 AI 冻结的中心点。</summary>
        public Vector3 FreezeAiCenter { get; set; }

        /// <summary>获取或设置演出镜头优先级。</summary>
        public int CameraPriority { get; set; } = 200;

        /// <summary>获取或设置是否在进入舞台时接管演出镜头控制权。</summary>
        public bool TakeCameraControl { get; set; } = true;

        /// <summary>获取参演角色列表；这些角色会在进入舞台阶段一次性解析完毕。</summary>
        public List<ActorRef> Actors { get; } = new List<ActorRef>();

        /// <summary>获取参演角色的初始摆位；键必须出现在 Actors 中。</summary>
        public Dictionary<ActorRef, Anchor> Placements { get; } = new Dictionary<ActorRef, Anchor>();

        /// <summary>获取需要在淡黑期间预加载的资源地址。</summary>
        public List<string> Preload { get; } = new List<string>();

        /// <summary>
        /// 获取本次舞台会用到的演出片段资源地址。
        /// 这些片段会在进入舞台时就创建好 PlayableDirector 并完成轨道绑定，
        /// 使同步的落终态可以直接寻址到同一个播放器，也让分段演出在段与段之间保持连续。
        /// </summary>
        public List<string> Cinematics { get; } = new List<string>();

        /// <summary>声明一个参演角色，并可选地指定其初始摆位。</summary>
        public StageSpec WithActor(ActorRef actor, Anchor? placement = null)
        {
            if (!Actors.Contains(actor)) Actors.Add(actor);
            if (placement.HasValue) Placements[actor] = placement.Value;
            return this;
        }

        /// <summary>声明一批本次舞台会用到的演出片段。</summary>
        public StageSpec WithCinematics(params string[] locations)
        {
            if (locations == null) return this;
            for (int index = 0; index < locations.Length; index++)
            {
                if (!string.IsNullOrWhiteSpace(locations[index]) && !Cinematics.Contains(locations[index])) Cinematics.Add(locations[index]);
            }
            return this;
        }

        /// <summary>声明一批需要预加载的资源地址。</summary>
        public StageSpec WithPreload(params string[] locations)
        {
            if (locations == null) return this;
            for (int index = 0; index < locations.Length; index++)
            {
                if (!string.IsNullOrWhiteSpace(locations[index]) && !Preload.Contains(locations[index])) Preload.Add(locations[index]);
            }
            return this;
        }

        /// <summary>校验摆位声明只引用已登记的参演角色。</summary>
        public void Validate()
        {
            foreach (KeyValuePair<ActorRef, Anchor> placement in Placements)
            {
                if (!Actors.Contains(placement.Key)) throw new InvalidOperationException($"Stage placement references actor '{placement.Key}' that is not declared in Actors.");
            }
        }
    }
}
