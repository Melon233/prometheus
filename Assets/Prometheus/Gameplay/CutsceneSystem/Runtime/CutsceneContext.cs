using System;
using System.Collections.Generic;
using UnityEngine;

namespace Xuan.Prometheus.Cutscene
{
    /// <summary>
    /// 一次播放提供给 Action 的全部能力：锚点坐标换算、演员查询、演出镜头、临时对象父节点与台词显示。
    /// Action 只通过它与外界交互，因此 Action 不需要认识任何 System。
    /// </summary>
    public sealed class CutsceneContext
    {
        /// <summary>按键索引的本次演员。</summary>
        private readonly Dictionary<string, CutsceneActor> actors;

        /// <summary>台词变化时的回调，由 CutsceneSystem 转成状态与事件。</summary>
        private readonly Action<CutsceneLine> showLine;

        /// <summary>创建播放上下文。</summary>
        /// <param name="anchor">坐标原点。</param>
        /// <param name="cameraTransform">演出镜头的 Transform，镜头 Action 直接驱动它。</param>
        /// <param name="propRoot">本次演出临时对象（生成的演员、特效）的父节点，演出结束时整体销毁。</param>
        /// <param name="actors">按键索引的演员。</param>
        /// <param name="showLine">显示或清除台词（传空表示清除）的回调。</param>
        public CutsceneContext(Transform anchor, Transform cameraTransform, Transform propRoot, Dictionary<string, CutsceneActor> actors, Action<CutsceneLine> showLine)
        {
            Anchor = anchor;
            CameraTransform = cameraTransform;
            PropRoot = propRoot;
            this.actors = actors;
            this.showLine = showLine;
        }

        /// <summary>坐标原点。</summary>
        public Transform Anchor { get; }

        /// <summary>演出镜头的 Transform。</summary>
        public Transform CameraTransform { get; }

        /// <summary>本次演出临时对象的父节点。</summary>
        public Transform PropRoot { get; }

        /// <summary>按键取演员；键已在播放前校验，不存在属于逻辑错误。</summary>
        /// <param name="key">演员键。</param>
        public CutsceneActor GetActor(string key) => actors[key];

        /// <summary>把锚点局部坐标换算为世界坐标。</summary>
        /// <param name="local">相对锚点的坐标。</param>
        public Vector3 ToWorld(Vector3 local) => Anchor.TransformPoint(local);

        /// <summary>把相对锚点的欧拉角换算为世界旋转。</summary>
        /// <param name="localEuler">相对锚点的欧拉角。</param>
        public Quaternion ToWorldRotation(Vector3 localEuler) => Anchor.rotation * Quaternion.Euler(localEuler);

        /// <summary>显示一句台词；传空表示清除当前台词。</summary>
        /// <param name="line">台词快照。</param>
        public void ShowLine(CutsceneLine line) => showLine(line);
    }
}
