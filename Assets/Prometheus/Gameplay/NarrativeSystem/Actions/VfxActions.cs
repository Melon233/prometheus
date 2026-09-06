using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;

namespace Xuan.Prometheus.Narrative
{
    /// <summary>描述一个特效的生成位置：跟随某个角色，或落在一个场景锚点上。</summary>
    public readonly struct VfxPlacement
    {
        /// <summary>创建一个跟随角色的位置。</summary>
        private VfxPlacement(ActorRef actor, Anchor anchor, bool followsActor, Vector3 offset)
        {
            Actor = actor;
            Anchor = anchor;
            FollowsActor = followsActor;
            Offset = offset;
        }

        /// <summary>获取跟随的角色引用。</summary>
        public ActorRef Actor { get; }

        /// <summary>获取落点锚点。</summary>
        public Anchor Anchor { get; }

        /// <summary>获取该位置是否跟随角色。</summary>
        public bool FollowsActor { get; }

        /// <summary>获取相对基准点的偏移。</summary>
        public Vector3 Offset { get; }

        /// <summary>创建一个挂在角色身上的位置。</summary>
        public static VfxPlacement On(ActorRef actor, Vector3 offset = default)
        {
            return new VfxPlacement(actor, default, true, offset);
        }

        /// <summary>创建一个落在场景锚点上的位置。</summary>
        public static VfxPlacement At(Anchor anchor, Vector3 offset = default)
        {
            return new VfxPlacement(default, anchor, false, offset);
        }
    }

    /// <summary>
    /// 特效生成动作。
    /// 特效是纯表现：生成的实例登记在舞台作用域上并随舞台退出统一回收，落终态为空实现。
    /// </summary>
    public sealed class VfxSpawnAction : StoryAction
    {
        private readonly string location;
        private readonly VfxPlacement placement;
        private readonly bool waitForCompletion;
        private readonly float lifetime;

        /// <summary>创建一个特效生成动作。</summary>
        /// <param name="location">特效预制体的资源地址。</param>
        /// <param name="placement">生成位置。</param>
        /// <param name="lifetime">存活秒数；小于等于零表示随舞台退出才回收。</param>
        /// <param name="waitForCompletion">是否阻塞到存活时间结束。</param>
        public VfxSpawnAction(string location, VfxPlacement placement, float lifetime = 0f, bool waitForCompletion = false)
        {
            if (string.IsNullOrWhiteSpace(location)) throw new ArgumentException("Vfx location cannot be empty.", nameof(location));
            this.location = location;
            this.placement = placement;
            this.lifetime = Mathf.Max(0f, lifetime);
            this.waitForCompletion = waitForCompletion;
        }

        /// <inheritdoc />
        public override async UniTask PlayAsync(StoryContext context, CancellationToken cancellationToken)
        {
            StageScope stage = context.RequireStage();
            INarrativeVfxPort vfx = stage.Services.RequireVfx();
            ResolvePlacement(context, out Vector3 position, out Transform parent);
            INarrativeVfxHandle handle = await vfx.SpawnAsync(location, position, Quaternion.identity, parent, cancellationToken);
            if (handle == null) return;
            stage.Track($"vfx:{location}", () =>
            {
                vfx.Stop(handle);
                return UniTask.CompletedTask;
            });
            if (lifetime <= 0f) return;
            if (waitForCompletion)
            {
                await UniTask.Delay(TimeSpan.FromSeconds(lifetime), DelayType.DeltaTime, PlayerLoopTiming.Update, cancellationToken);
                vfx.Stop(handle);
                return;
            }
            StopAfterAsync(vfx, handle, lifetime, cancellationToken).Forget();
        }

        /// <inheritdoc />
        public override void Settle(StoryContext context)
        {
            // 纯表现动作：跳过时不生成任何特效。
        }

        /// <summary>把位置声明解析为世界坐标与父节点。</summary>
        private void ResolvePlacement(StoryContext context, out Vector3 position, out Transform parent)
        {
            StageScope stage = context.RequireStage();
            if (placement.FollowsActor)
            {
                ActorHandle actor = stage.RequireActor(placement.Actor);
                parent = actor.Transform;
                position = actor.Transform.TransformPoint(placement.Offset);
                return;
            }
            parent = stage.RuntimeRoot;
            position = placement.Anchor.Resolve(stage.Services.Actors) + placement.Offset;
        }

        /// <summary>在后台等待存活时间结束后回收特效，并吞掉取消异常。</summary>
        private static async UniTaskVoid StopAfterAsync(INarrativeVfxPort vfx, INarrativeVfxHandle handle, float seconds, CancellationToken cancellationToken)
        {
            try
            {
                await UniTask.Delay(TimeSpan.FromSeconds(seconds), DelayType.DeltaTime, PlayerLoopTiming.Update, cancellationToken);
                vfx.Stop(handle);
            }
            catch (OperationCanceledException)
            {
            }
        }
    }
}
