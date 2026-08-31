using System;
using UnityEngine;
using UnityEngine.Timeline;
using Xuan.Prometheus.Input;

namespace Xuan.Prometheus.Film
{
    /// <summary>区分 Timeline Marker 触发的对话和 QTE 交互类型。</summary>
    public enum FilmInteractionType
    {
        Dialogue,
        Qte
    }

    /// <summary>Timeline 上的交互标记；到达该时间点后 FilmInstance 会暂停时间轴并调用交互服务。</summary>
    public sealed class FilmInteractionMarker : Marker
    {
        [SerializeField] private FilmInteractionType interactionType;
        [SerializeField] private string interactionId;
        [SerializeField] private InputActionMask qteSuccessActions = InputActionMask.Submit;
        [SerializeField] private float qteTimeoutSeconds;

        /// <summary>获取或设置交互类型，供 Timeline Inspector 和编辑器测试使用。</summary>
        public FilmInteractionType InteractionType { get => interactionType; set => interactionType = value; }

        /// <summary>获取或设置交互标识，业务服务据此加载对话或 QTE 配置。</summary>
        public string InteractionId { get => interactionId; set => interactionId = value; }

        /// <summary>获取或设置 QTE 成功所需的输入动作集合。</summary>
        public InputActionMask QteSuccessActions { get => qteSuccessActions; set => qteSuccessActions = value; }

        /// <summary>获取或设置 QTE 超时秒数；小于等于零表示不由 FilmSystem 计时。</summary>
        public float QteTimeoutSeconds { get => qteTimeoutSeconds; set => qteTimeoutSeconds = value; }

        /// <summary>校验 Marker 的交互 ID 和 QTE 成功动作，避免播放到中途才暴露配置错误。</summary>
        public void Validate()
        {
            if (string.IsNullOrWhiteSpace(interactionId)) throw new InvalidOperationException("Film interaction marker requires a non-empty interaction ID.");
            if (interactionType == FilmInteractionType.Qte && qteSuccessActions == InputActionMask.None) throw new InvalidOperationException($"Film QTE marker '{interactionId}' requires at least one success input action.");
        }
    }
}
