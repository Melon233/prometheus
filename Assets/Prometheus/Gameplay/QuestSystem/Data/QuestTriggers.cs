using System;
using System.Collections.Generic;
using UnityEngine;
using Xuan.Prometheus.Cutscene;

namespace Xuan.Prometheus.Quest
{
    /// <summary>
    /// 节点上的演出触发器：描述「用什么方式播放哪段演出」。
    /// 触发器只负责开始演出；演出是否算完成由 <see cref="CutsceneObjective"/> 判定，两者解耦，
    /// 因此一个节点可以有「可反复观看的闲聊」而不影响推进。
    /// 触发所需的区域与交互点都写在这里、运行时生成，场景中只需要被引用的 <see cref="WorldAnchor"/>。
    /// </summary>
    [Serializable]
    public abstract class QuestCutsceneTrigger
    {
        [SerializeField] [Tooltip("要播放的演出。")]
        private CutsceneDefinition cutscene;

        /// <summary>要播放的演出。</summary>
        public CutsceneDefinition Cutscene => cutscene;

        /// <summary>校验触发器配置；错误追加到 <paramref name="errors"/>。</summary>
        /// <param name="where">错误定位前缀。</param>
        /// <param name="errors">错误收集列表。</param>
        public virtual void Validate(string where, List<string> errors)
        {
            if (cutscene == null) errors.Add($"{where}：未配置演出。");
        }
    }

    /// <summary>节点激活时立即播放（若此刻正在播放别的演出，则排队到其结束后）。</summary>
    [Serializable]
    public sealed class OnEnterTrigger : QuestCutsceneTrigger
    {
    }

    /// <summary>玩家首次进入区域时播放；每次节点激活只触发一次。</summary>
    [Serializable]
    public sealed class AreaTrigger : QuestCutsceneTrigger
    {
        [SerializeField] [Tooltip("触发区域。")]
        private QuestArea area = new QuestArea();

        /// <summary>触发区域。</summary>
        public QuestArea Area => area;

        /// <inheritdoc />
        public override void Validate(string where, List<string> errors)
        {
            base.Validate(where, errors);
            area.Validate(where, errors);
        }
    }

    /// <summary>
    /// 节点激活期间，在锚点旁生成一个交互点（跟随锚点移动），玩家靠近后出现在交互栏，点击即播放；
    /// 可反复交互，直到节点结束时交互点被销毁。同一个锚点可以同时挂多个任务的交互，交互栏各占一条。
    /// </summary>
    [Serializable]
    public sealed class InteractTrigger : QuestCutsceneTrigger
    {
        [SerializeField] [Tooltip("交互点挂载的 WorldAnchor id（通常是 NPC）。")]
        private string anchorId;

        [SerializeField] [Tooltip("交互点相对锚点的偏移（米，世界轴向）。")]
        private Vector3 offset = new Vector3(0f, 1f, 0f);

        [SerializeField] [Tooltip("交互范围半径（米）；玩家交互感应进入这个范围后出现在交互栏。")]
        private float radius = 1.5f;

        [SerializeField] [Tooltip("交互栏显示的文本 id。")]
        private int promptTextId;

        /// <summary>挂载锚点 id。</summary>
        public string AnchorId => anchorId;

        /// <summary>相对锚点的偏移。</summary>
        public Vector3 Offset => offset;

        /// <summary>交互范围半径。</summary>
        public float Radius => radius;

        /// <summary>交互栏文本 id。</summary>
        public int PromptTextId => promptTextId;

        /// <inheritdoc />
        public override void Validate(string where, List<string> errors)
        {
            base.Validate(where, errors);
            if (string.IsNullOrWhiteSpace(anchorId)) errors.Add($"{where}：未配置挂载锚点 id。");
            if (radius <= 0f) errors.Add($"{where}：交互半径必须为正。");
            if (promptTextId == NarrativeText.None) errors.Add($"{where}：未配置交互文本 id。");
        }
    }
}
