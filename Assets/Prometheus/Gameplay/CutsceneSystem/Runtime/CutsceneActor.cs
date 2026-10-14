using System;
using Spine.Unity;
using UnityEngine;
using Xuan.Prometheus.Component;
using Xuan.Prometheus.Logic;

namespace Xuan.Prometheus.Cutscene
{
    /// <summary>
    /// 一次播放中演员的运行时句柄：把三种来源统一成「站位 / 朝向 / 显隐 / 动画」四种操作。
    /// 玩家演员经由实体组件操作（CharacterController 与 SpineComponent），其余演员直接操作 Transform 与 Spine 骨骼。
    /// </summary>
    public sealed class CutsceneActor
    {
        /// <summary>玩家演员的实体；非玩家演员为空。</summary>
        private readonly Entity playerEntity;

        /// <summary>演员的 Spine 骨骼；没有骨骼的道具类演员为空。</summary>
        private readonly SkeletonAnimation skeleton;

        /// <summary>创建演员句柄。</summary>
        /// <param name="key">演出内的演员键。</param>
        /// <param name="root">演员根节点，站位与显隐作用于它。</param>
        /// <param name="skeleton">演员的 Spine 骨骼，可为空。</param>
        /// <param name="playerEntity">玩家演员对应的实体；非玩家传空。</param>
        /// <param name="spawned">是否为本次演出生成、结束时需要销毁的对象。</param>
        public CutsceneActor(string key, Transform root, SkeletonAnimation skeleton, Entity playerEntity, bool spawned)
        {
            Key = key;
            Root = root;
            this.skeleton = skeleton;
            this.playerEntity = playerEntity;
            Spawned = spawned;
        }

        /// <summary>演出内的演员键。</summary>
        public string Key { get; }

        /// <summary>演员根节点。</summary>
        public Transform Root { get; }

        /// <summary>是否为本次演出生成的临时对象。</summary>
        public bool Spawned { get; }

        /// <summary>是否为玩家演员。</summary>
        public bool IsPlayer => playerEntity != null;

        /// <summary>玩家演员的 Spine 组件；玩家实体必然带有它，缺失属于实体装配错误。</summary>
        private SpineComponent PlayerSpine => playerEntity.TryGetComp(out SpineComponent spine) ? spine : throw new InvalidOperationException($"玩家演员 '{Key}' 缺少 SpineComponent。");

        /// <summary>演员当前世界坐标。</summary>
        public Vector3 Position => Root.position;

        /// <summary>
        /// 把演员放到指定世界坐标。
        /// 玩家由 CharacterController 驱动，直接改 Transform 会被下一次 Move 覆盖，因此先停用控制器再写入。
        /// </summary>
        /// <param name="position">目标世界坐标。</param>
        public void SetPosition(Vector3 position)
        {
            if (playerEntity != null && playerEntity.TryGetComp(out MotionComponent motion))
            {
                motion.cc.enabled = false;
                Root.position = position;
                motion.cc.enabled = true;
                return;
            }
            Root.position = position;
        }

        /// <summary>设置左右朝向；玩家走 SpineComponent 以同步特效旋转根节点。</summary>
        /// <param name="faceRight">是否面朝右。</param>
        public void SetFaceRight(bool faceRight)
        {
            if (playerEntity != null)
            {
                PlayerSpine.CurFaceDir = faceRight ? FaceDir.Right : FaceDir.Left;
                return;
            }
            if (skeleton != null) skeleton.skeleton.ScaleX = faceRight ? 1f : -1f;
        }

        /// <summary>
        /// 设置显隐。玩家只关闭骨骼渲染：停用玩家 GameObject 会让实体、相机跟随与交互感应一起失效。
        /// </summary>
        /// <param name="visible">是否可见。</param>
        public void SetVisible(bool visible)
        {
            if (playerEntity != null)
            {
                PlayerSpine.spineAnimator.GetComponent<MeshRenderer>().enabled = visible;
                return;
            }
            Root.gameObject.SetActive(visible);
        }

        /// <summary>
        /// 播放一段 Spine 动画；非循环动画可指定播完后接续的循环动画（通常回到 idle）。
        /// 玩家演员不支持，校验阶段已拦截。
        /// </summary>
        /// <param name="animationName">动画名。</param>
        /// <param name="loop">是否循环。</param>
        /// <param name="thenLoopAnimation">非循环动画播完后接续的循环动画；为空表示停在最后一帧。</param>
        public void PlayAnimation(string animationName, bool loop, string thenLoopAnimation)
        {
            if (skeleton == null) throw new InvalidOperationException($"演员 '{Key}' 没有 Spine 骨骼，无法播放动画 '{animationName}'。");
            skeleton.AnimationState.SetAnimation(0, animationName, loop);
            if (!loop && !string.IsNullOrEmpty(thenLoopAnimation)) skeleton.AnimationState.AddAnimation(0, thenLoopAnimation, true, 0f);
        }
    }
}
