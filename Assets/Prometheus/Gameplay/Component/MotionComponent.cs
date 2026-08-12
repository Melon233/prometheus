using System;
using System.Collections.Generic;
using Spine;
using UnityEngine;

namespace Xuan.Prometheus.Component
{
    public enum MoveMode
    {
        Walk,
        Run,
        Sprint
    }
    /// <summary>集中保存角色移动、重力和接地逻辑共享的纯运行态以及场景运动配置。</summary>
    public class MotionComponent : MonoComponent
    {
        /// <summary>保存 Spine 本帧产生但尚未由 MotionLogic 提交给 CharacterController 的世界空间 Root Motion 位移。</summary>
        private Vector3 pendingRootMotionDelta;

        /// <summary>指示当前实体是否允许接收新的 Root Motion；离场角色会关闭接收以免切回时补交后台动画位移。</summary>
        private bool acceptsRootMotion = true;

        /// <summary>获取最终提交移动并提供接地结果的 CharacterController。</summary>
        public CharacterController cc;

        /// <summary>保存全部水平控制与 GravityLogic 合成后的世界速度。</summary>
        public Vector3 curVelo;

        /// <summary>保存 GravityLogic 上一次检查时的接地状态，用于识别真实的离地到接地边沿。</summary>
        public bool wasGroundedLastFrame;

        /// <summary>指示 GravityLogic 本帧检测到真实落地，LandLogic 消费后会立即清除。</summary>
        public bool landThisFrame;

        /// <summary>保存当前地面移动模式。</summary>
        public MoveMode moveMode = MoveMode.Run;

        /// <summary>保存当前地面移动速度。</summary>
        public float curMoveSpeed;

        /// <summary>保存地面移动模式的速度和切换阈值配置。</summary>
        public PropertyConfig propertyConfig;

        /// <summary>保留旧动画轨道引用以兼容仍在迁移的表现逻辑。</summary>
        public TrackEntry entry;

        /// <summary>由 Root Motion 桥接组件追加一次世界空间位移；离场或停用期间的后台动画位移会被安全丢弃。</summary>
        public void AddRootMotionDelta(Vector3 worldDelta)
        {
            if (!acceptsRootMotion) return;
            pendingRootMotionDelta += worldDelta;
        }

        /// <summary>由 MotionLogic 原子取走当前累计的 Root Motion，确保每段动画位移只提交一次。</summary>
        public Vector3 ConsumeRootMotionDelta()
        {
            Vector3 result = pendingRootMotionDelta;
            pendingRootMotionDelta = Vector3.zero;
            return result;
        }

        /// <summary>丢弃当前尚未提交的 Root Motion，但不改变后续动画位移的接收状态。</summary>
        public void ClearRootMotionDelta()
        {
            pendingRootMotionDelta = Vector3.zero;
        }

        /// <summary>切换 Root Motion 接收状态；关闭时立即清空未提交位移，避免切人后发生位置跳变。</summary>
        public void SetRootMotionEnabled(bool enabled)
        {
            acceptsRootMotion = enabled;
            if (!enabled) ClearRootMotionDelta();
        }
    }
}
