using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Serialization;

namespace Xuan.Prometheus.Cutscene
{
    /// <summary>
    /// 一段演出的完整配置：锚点、演员表与线性步骤。
    /// 所有坐标（镜头、站位、生成点）都相对锚点表达，同一段演出换地方只需要换锚点。
    /// 本类型独占一个文件：ScriptableObject 子类与文件名不一致时，资产会在域重载后反序列化成 null。
    /// </summary>
    [CreateAssetMenu(fileName = "Cutscene", menuName = "Prometheus/Narrative/Cutscene")]
    public sealed class CutsceneDefinition : ScriptableObject
    {
        [SerializeField] [FormerlySerializedAs("anchorMarkerId")] [Tooltip("作为坐标原点的 WorldAnchor id。")]
        private string anchorId;

        [SerializeField] [Tooltip("是否允许玩家按取消键跳过。")]
        private bool skippable = true;

        [SerializeField] [Tooltip("演员表。")]
        private List<CutsceneActorDef> actors = new List<CutsceneActorDef>();

        [SerializeField] [Tooltip("按顺序执行的步骤。")]
        private List<CutsceneStep> steps = new List<CutsceneStep>();

        /// <summary>坐标原点的世界锚点 id。</summary>
        public string AnchorId => anchorId;

        /// <summary>是否允许跳过。</summary>
        public bool Skippable => skippable;

        /// <summary>演员表。</summary>
        public IReadOnlyList<CutsceneActorDef> Actors => actors;

        /// <summary>步骤列表。</summary>
        public IReadOnlyList<CutsceneStep> Steps => steps;

        /// <summary>按键查找演员定义；不存在时返回空。</summary>
        /// <param name="key">演员键。</param>
        public CutsceneActorDef FindActor(string key)
        {
            for (int index = 0; index < actors.Count; index++)
            {
                if (actors[index].Key == key) return actors[index];
            }
            return null;
        }

        /// <summary>收集本演出引用的全部世界锚点 id（坐标原点与 Scene 来源演员），供编辑期与场景核对。</summary>
        public List<string> CollectAnchorIds()
        {
            List<string> ids = new List<string>();
            if (!string.IsNullOrEmpty(anchorId)) ids.Add(anchorId);
            foreach (CutsceneActorDef actor in actors)
            {
                if (actor.Source == CutsceneActorSource.Scene && !string.IsNullOrEmpty(actor.AnchorId)) ids.Add(actor.AnchorId);
            }
            return ids;
        }

        /// <summary>
        /// 校验资产自身的结构（不涉及场景）：演员键唯一、来源参数齐全、每步至多一句对话、
        /// 非对话步骤时长为正、各 Action 引用的演员存在且能力匹配。
        /// 播放前由 CutsceneSystem 调用，编辑器中由 Inspector 的校验按钮调用。
        /// </summary>
        /// <param name="errors">收集到的错误描述；为空表示通过。</param>
        public void Validate(List<string> errors)
        {
            if (string.IsNullOrWhiteSpace(anchorId)) errors.Add("未配置锚点 id。");
            if (steps.Count == 0) errors.Add("至少需要一个步骤。");
            HashSet<string> keys = new HashSet<string>();
            for (int index = 0; index < actors.Count; index++)
            {
                CutsceneActorDef actor = actors[index];
                if (string.IsNullOrWhiteSpace(actor.Key)) errors.Add($"演员 {index} 未配置键。");
                else if (!keys.Add(actor.Key)) errors.Add($"演员键 '{actor.Key}' 重复。");
                if (actor.Source == CutsceneActorSource.Scene && string.IsNullOrWhiteSpace(actor.AnchorId)) errors.Add($"演员 '{actor.Key}' 来源为 Scene 但未配置锚点 id。");
                if (actor.Source == CutsceneActorSource.Spawn && actor.SpawnPrefab == null) errors.Add($"演员 '{actor.Key}' 来源为 Spawn 但未配置 prefab。");
            }
            for (int stepIndex = 0; stepIndex < steps.Count; stepIndex++)
            {
                CutsceneStep step = steps[stepIndex];
                int dialogCount = 0;
                for (int actionIndex = 0; actionIndex < step.Actions.Count; actionIndex++)
                {
                    CutsceneAction action = step.Actions[actionIndex];
                    if (action == null)
                    {
                        errors.Add($"步骤 {stepIndex} 的 Action {actionIndex} 为空。");
                        continue;
                    }
                    if (action is DialogAction) dialogCount++;
                    action.Validate(this, $"步骤 {stepIndex} / {action.GetType().Name}", errors);
                }
                if (dialogCount > 1) errors.Add($"步骤 {stepIndex} 含 {dialogCount} 句对话，每步至多一句。");
                if (dialogCount == 0 && step.Duration <= 0f) errors.Add($"步骤 {stepIndex} 不含对话，时长必须大于 0。");
            }
        }
    }
}
