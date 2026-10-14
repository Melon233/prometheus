using System;
using System.Collections.Generic;
using UnityEngine;

namespace Xuan.Prometheus.Quest
{
    /// <summary>
    /// 任务区域：写在任务资产里的球体，球心 = 锚点位置 + offset。
    /// 锚点为空时 offset 就是世界坐标；挂在会移动的锚点（例如 NPC）上时，区域每帧跟随锚点。
    /// 区域不在场景中摆放任何对象，判定是对上场角色位置的纯几何比较（到球心的距离不超过半径）。
    /// </summary>
    [Serializable]
    public sealed class QuestArea
    {
        [SerializeField] [Tooltip("可选：区域跟随的 WorldAnchor id；为空时 offset 即世界坐标。")]
        private string anchorId;

        [SerializeField] [Tooltip("球心相对锚点的偏移（米，世界轴向）。")]
        private Vector3 offset;

        [SerializeField] [Tooltip("区域半径（米）。")]
        private float radius = 2f;

        /// <summary>跟随的锚点 id；为空表示使用世界坐标。</summary>
        public string AnchorId => anchorId;

        /// <summary>球心相对锚点的偏移。</summary>
        public Vector3 Offset => offset;

        /// <summary>半径。</summary>
        public float Radius => radius;

        /// <summary>是否跟随锚点。</summary>
        public bool HasAnchor => !string.IsNullOrEmpty(anchorId);

        /// <summary>判断一点是否在区域内（含边界）。</summary>
        /// <param name="anchorPosition">锚点当前世界坐标；无锚点时传 Vector3.zero。</param>
        /// <param name="point">待判定的世界坐标。</param>
        public bool Contains(Vector3 anchorPosition, Vector3 point)
        {
            return (point - (anchorPosition + offset)).sqrMagnitude <= radius * radius;
        }

        /// <summary>校验半径为正。</summary>
        /// <param name="where">错误定位前缀。</param>
        /// <param name="errors">错误收集列表。</param>
        public void Validate(string where, List<string> errors)
        {
            if (radius <= 0f) errors.Add($"{where}：区域半径必须为正。");
        }
    }
}
