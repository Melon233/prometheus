using System;
using System.Collections.Generic;
using UnityEngine;
using Xuan.Prometheus.Cutscene;

namespace Xuan.Prometheus.Quest
{
    /// <summary>节点目标的基类。目标只描述「什么算完成」，判定由 QuestSystem 监听对应事实完成。</summary>
    [Serializable]
    public abstract class QuestObjective
    {
        /// <summary>校验目标配置；错误追加到 <paramref name="errors"/>。</summary>
        /// <param name="where">错误定位前缀。</param>
        /// <param name="errors">错误收集列表。</param>
        public abstract void Validate(string where, List<string> errors);
    }

    /// <summary>看完指定演出（正常播完或跳过都算）。只认节点激活之后结束的演出。</summary>
    [Serializable]
    public sealed class CutsceneObjective : QuestObjective
    {
        [SerializeField] [Tooltip("需要看完的演出。")]
        private CutsceneDefinition cutscene;

        /// <summary>需要看完的演出。</summary>
        public CutsceneDefinition Cutscene => cutscene;

        /// <inheritdoc />
        public override void Validate(string where, List<string> errors)
        {
            if (cutscene == null) errors.Add($"{where}：未配置演出。");
        }
    }

    /// <summary>玩家进入区域。节点激活时玩家已在区域内也算完成。</summary>
    [Serializable]
    public sealed class ReachObjective : QuestObjective
    {
        [SerializeField] [Tooltip("目标区域。")]
        private QuestArea area = new QuestArea();

        /// <summary>目标区域。</summary>
        public QuestArea Area => area;

        /// <inheritdoc />
        public override void Validate(string where, List<string> errors) => area.Validate(where, errors);
    }
}
