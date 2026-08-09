using System;
using System.Collections.Generic;
using Sirenix.OdinInspector;
using Spine.Unity;
using UnityEngine;

namespace Xuan.Prometheus
{
    /// <summary>保存一个角色可共享的纯动画配置；所有实体运行态、组件引用和播放会话均由 SpineComponent 与对应 Logic 管理。</summary>
    [CreateAssetMenu(menuName = "Prometheus/Animation/Animation Library", fileName = "AnimationLibrary")]
    public sealed class AnimationLibrary : ScriptableObject
    {
        [NonSerialized] private Dictionary<AnimationSemantic, AnimationLine> semanticIndex;
        [NonSerialized] private HashSet<AnimationSemantic> semanticConflicts;

        [SerializeField] public SkeletonDataAsset skeletonDataAsset;
        [SerializeField, SpineEvent(dataField = "skeletonDataAsset")] public string hitStart = "hit_start";
        [SerializeField, SpineEvent(dataField = "skeletonDataAsset")] public string hitEnd = "hit_end";
        [SerializeField] public AttackExecutor atkExecutor = new AttackExecutor();
        [SerializeField] public IdleExecutor idleExecutor = new IdleExecutor();
        [SerializeField] public GroundMoveExecutor groundMoveExecutor = new GroundMoveExecutor();
        [SerializeField] public DodgeExecutor dodgeExecutor = new DodgeExecutor();
        [SerializeField] public AirMoveExecutor airMoveExecutor = new AirMoveExecutor();
        [SerializeField] public bool hasTalent;
        [SerializeField, ShowIf(nameof(hasTalent))] public UltimateExecutor ultimateExecutor = new UltimateExecutor();
        [SerializeField, ShowIf(nameof(hasTalent))] public SkillExecutor skillExecutor = new SkillExecutor();
        [SerializeField, ShowIf(nameof(hasTalent))] public SpecialAttackExecutor specialAttackExecutor = new SpecialAttackExecutor();
        [SerializeField] public AttackedExecutor attackedExecutor = new AttackedExecutor();
        [SerializeField] public DieExecutor dieExecutor = new DieExecutor();

        /// <summary>按稳定动画语义获取当前角色专属 AnimationLine；未配置、语义为空或存在冲突时返回失败。</summary>
        public bool TryGetLine(AnimationSemantic semantic, out AnimationLine line)
        {
            if (semantic == AnimationSemantic.None)
            {
                line = null;
                return false;
            }
            if (semanticIndex == null) RebuildSemanticIndex();
            if (semanticConflicts.Contains(semantic))
            {
                line = null;
                return false;
            }
            return semanticIndex.TryGetValue(semantic, out line) && line != null;
        }

        /// <summary>使运行时语义索引失效；编辑器迁移 AnimationLine 后调用该入口即可在下次播放时重建。</summary>
        public void InvalidateSemanticIndex()
        {
            semanticIndex = null;
            semanticConflicts = null;
        }

        /// <summary>从全部正式配置收集 AnimationLine，并保证当前角色库内每个语义只对应一个资产。</summary>
        private void RebuildSemanticIndex()
        {
            semanticIndex = new Dictionary<AnimationSemantic, AnimationLine>();
            semanticConflicts = new HashSet<AnimationSemantic>();
            List<AnimationLine> configuredLines = new List<AnimationLine>();
            atkExecutor?.CollectLines(configuredLines);
            idleExecutor?.CollectLines(configuredLines);
            groundMoveExecutor?.CollectLines(configuredLines);
            dodgeExecutor?.CollectLines(configuredLines);
            airMoveExecutor?.CollectLines(configuredLines);
            if (hasTalent)
            {
                ultimateExecutor?.CollectLines(configuredLines);
                skillExecutor?.CollectLines(configuredLines);
                specialAttackExecutor?.CollectLines(configuredLines);
            }
            attackedExecutor?.CollectLines(configuredLines);
            dieExecutor?.CollectLines(configuredLines);
            for (int index = 0; index < configuredLines.Count; index++) RegisterLine(configuredLines[index]);
        }

        /// <summary>注册一个 AnimationLine，并对缺失语义和同库重复语义输出可定位的配置错误。</summary>
        private void RegisterLine(AnimationLine line)
        {
            if (line == null) return;
            if (line.Semantic == AnimationSemantic.None)
            {
                Debug.LogError($"AnimationLibrary '{name}' 中的 AnimationLine '{line.name}' 尚未配置动画语义。", this);
                return;
            }
            if (semanticConflicts.Contains(line.Semantic))
            {
                Debug.LogError($"AnimationLibrary '{name}' 的动画语义 '{line.Semantic}' 已存在配置冲突，AnimationLine '{line.name}' 不会进入运行时索引。", this);
                return;
            }
            if (!semanticIndex.TryGetValue(line.Semantic, out AnimationLine existingLine))
            {
                semanticIndex.Add(line.Semantic, line);
                return;
            }
            if (existingLine == line) return;
            semanticIndex.Remove(line.Semantic);
            semanticConflicts.Add(line.Semantic);
            Debug.LogError($"AnimationLibrary '{name}' 的动画语义 '{line.Semantic}' 同时映射到 '{existingLine.name}' 与 '{line.name}'，该语义的播放请求将被拒绝。", this);
        }

        /// <summary>资源载入后丢弃非序列化索引，确保进入运行时读取最新的 AnimationLine 语义。</summary>
        private void OnEnable()
        {
            InvalidateSemanticIndex();
        }

        /// <summary>规范化公共事件名，避免空白配置让所有命中窗口静默失效。</summary>
        private void OnValidate()
        {
            hitStart = string.IsNullOrWhiteSpace(hitStart) ? "hit_start" : hitStart.Trim();
            hitEnd = string.IsNullOrWhiteSpace(hitEnd) ? "hit_end" : hitEnd.Trim();
            InvalidateSemanticIndex();
        }
    }
}
