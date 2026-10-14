using System;
using System.Collections.Generic;
using UnityEngine;

namespace Xuan.Prometheus.Cutscene
{
    /// <summary>
    /// 镜头运动：从起始位姿按曲线过渡到目标位姿，坐标相对锚点。
    /// 起始位姿默认取镜头当前位置，便于连续运镜；配置了注视演员时，旋转改为每帧对准该演员。
    /// 时长为 0 表示直接切镜。
    /// </summary>
    [Serializable]
    public sealed class CameraAction : CutsceneAction
    {
        [SerializeField] [Tooltip("勾选时从镜头当前位置出发，忽略下面的起始位姿。")]
        private bool startFromCurrent = true;

        [SerializeField] [Tooltip("起始位置（相对锚点）。")]
        private Vector3 fromPosition;

        [SerializeField] [Tooltip("起始旋转（相对锚点的欧拉角）。")]
        private Vector3 fromEuler;

        [SerializeField] [Tooltip("目标位置（相对锚点）。")]
        private Vector3 toPosition = new Vector3(0f, 2f, -5f);

        [SerializeField] [Tooltip("目标旋转（相对锚点的欧拉角）；配置了注视演员时忽略。")]
        private Vector3 toEuler = new Vector3(10f, 0f, 0f);

        [SerializeField] [Tooltip("过渡时长（秒）；0 表示直接切镜。")]
        private float duration = 1f;

        [SerializeField] [Tooltip("过渡曲线，横轴与纵轴都归一化到 0..1。")]
        private AnimationCurve curve = AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);

        [SerializeField] [Tooltip("可选：镜头始终注视的演员键。")]
        private string lookAtActor;

        [SerializeField] [Tooltip("注视点相对演员根节点的偏移。")]
        private Vector3 lookAtOffset = new Vector3(0f, 1f, 0f);

        /// <inheritdoc />
        public override string Channel => "camera";

        /// <inheritdoc />
        public override CutsceneActionRun Begin(CutsceneContext context)
        {
            Transform camera = context.CameraTransform;
            Vector3 startPosition = startFromCurrent ? camera.position : context.ToWorld(fromPosition);
            Quaternion startRotation = startFromCurrent ? camera.rotation : context.ToWorldRotation(fromEuler);
            Run run = new Run(this, context, startPosition, startRotation);
            // 时长为 0 时直接落位，不需要逐帧驱动。
            if (duration <= 0f)
            {
                run.Finish();
                return null;
            }
            return run;
        }

        /// <inheritdoc />
        public override void ApplyFinalState(CutsceneContext context)
        {
            Vector3 end = context.ToWorld(toPosition);
            context.CameraTransform.SetPositionAndRotation(end, ResolveRotation(context, end, 1f, Quaternion.identity));
        }

        /// <inheritdoc />
        public override void Validate(CutsceneDefinition definition, string where, List<string> errors)
        {
            if (!string.IsNullOrEmpty(lookAtActor)) ValidateActor(definition, lookAtActor, true, where, errors);
        }

        /// <summary>
        /// 计算某一进度下的镜头旋转：有注视演员时对准演员，否则在起止旋转之间插值。
        /// </summary>
        private Quaternion ResolveRotation(CutsceneContext context, Vector3 cameraPosition, float t, Quaternion startRotation)
        {
            if (!string.IsNullOrEmpty(lookAtActor)) return Quaternion.LookRotation(context.GetActor(lookAtActor).Position + lookAtOffset - cameraPosition);
            return Quaternion.Slerp(startRotation, context.ToWorldRotation(toEuler), t);
        }

        /// <summary>一次镜头过渡的运行体。</summary>
        private sealed class Run : CutsceneActionRun
        {
            private readonly CameraAction action;
            private readonly CutsceneContext context;
            private readonly Vector3 startPosition;
            private readonly Quaternion startRotation;
            private float elapsed;

            public Run(CameraAction action, CutsceneContext context, Vector3 startPosition, Quaternion startRotation)
            {
                this.action = action;
                this.context = context;
                this.startPosition = startPosition;
                this.startRotation = startRotation;
            }

            /// <inheritdoc />
            public override bool Tick(float dt)
            {
                elapsed += dt;
                float t = Mathf.Clamp01(elapsed / action.duration);
                Apply(action.curve.Evaluate(t));
                return t >= 1f;
            }

            /// <inheritdoc />
            public override void Finish() => Apply(1f);

            /// <summary>按归一化进度写入镜头位姿。</summary>
            private void Apply(float t)
            {
                Vector3 position = Vector3.LerpUnclamped(startPosition, context.ToWorld(action.toPosition), t);
                context.CameraTransform.SetPositionAndRotation(position, action.ResolveRotation(context, position, t, startRotation));
            }
        }
    }
}
