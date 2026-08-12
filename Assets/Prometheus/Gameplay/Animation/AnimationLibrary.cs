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
        [NonSerialized] private Dictionary<Spine.Animation, AnimationSemantic> runtimeAnimationIndex;
        [NonSerialized] private HashSet<Spine.Animation> runtimeAnimationConflicts;

        [SerializeField] public SkeletonDataAsset skeletonDataAsset;
        [SerializeField, Tooltip("配置任意两个动画语义之间的混合时长；未覆盖单元格使用矩阵默认值。")]
        private AnimationMixDurationMatrix mixDurationMatrix = new AnimationMixDurationMatrix();
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

        /// <summary>获取当前动画库的 MixDuration 矩阵；旧资源缺少字段时自动创建默认值为 0.2 秒的配置。</summary>
        public AnimationMixDurationMatrix MixDurationMatrix
        {
            get
            {
                if (mixDurationMatrix == null) mixDurationMatrix = new AnimationMixDurationMatrix();
                return mixDurationMatrix;
            }
        }

        /// <summary>获取该动画库所有未覆盖过渡共同使用的默认 MixDuration。</summary>
        public float DefaultMixDuration => mixDurationMatrix == null ? AnimationMixDurationMatrix.FallbackDuration : mixDurationMatrix.DefaultDuration;

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

        /// <summary>读取两个动画语义之间的有向 MixDuration；旧资源没有矩阵时安全回退为 0.2 秒。</summary>
        public float GetMixDuration(AnimationSemantic from, AnimationSemantic to)
        {
            return mixDurationMatrix == null ? AnimationMixDurationMatrix.FallbackDuration : mixDurationMatrix.GetMixDuration(from, to);
        }

        /// <summary>根据当前 Spine 运行时动画对象反查稳定语义，使序列播放中途被打断时仍能命中正确矩阵行。</summary>
        public bool TryGetSemantic(Spine.Animation animation, out AnimationSemantic semantic)
        {
            if (animation == null)
            {
                semantic = AnimationSemantic.None;
                return false;
            }
            if (runtimeAnimationIndex == null) RebuildSemanticIndex();
            if (runtimeAnimationConflicts.Contains(animation))
            {
                semantic = AnimationSemantic.None;
                return false;
            }
            return runtimeAnimationIndex.TryGetValue(animation, out semantic);
        }

        /// <summary>把完整语义矩阵写入 Spine AnimationStateData，确保组件外的标准 Spine 切换也使用同一配置。</summary>
        public void ApplyMixDurationMatrix(Spine.AnimationStateData stateData)
        {
            if (stateData == null) return;
            if (semanticIndex == null) RebuildSemanticIndex();
            stateData.DefaultMix = DefaultMixDuration;
            foreach (KeyValuePair<AnimationSemantic, AnimationLine> fromPair in semanticIndex)
            {
                Spine.Animation fromAnimation = fromPair.Value == null ? null : fromPair.Value.GetRuntimeAnimation();
                if (fromAnimation == null) continue;
                foreach (KeyValuePair<AnimationSemantic, AnimationLine> toPair in semanticIndex)
                {
                    Spine.Animation toAnimation = toPair.Value == null ? null : toPair.Value.GetRuntimeAnimation();
                    if (toAnimation == null) continue;
                    stateData.SetMix(fromAnimation, toAnimation, GetMixDuration(fromPair.Key, toPair.Key));
                }
            }
        }

        /// <summary>使运行时语义索引失效；编辑器迁移 AnimationLine 后调用该入口即可在下次播放时重建。</summary>
        public void InvalidateSemanticIndex()
        {
            semanticIndex = null;
            semanticConflicts = null;
            runtimeAnimationIndex = null;
            runtimeAnimationConflicts = null;
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
            RebuildRuntimeAnimationIndex();
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

        /// <summary>从无冲突语义索引建立 Spine 动画对象反查表，并拒绝一个运行时动画对应多个语义的歧义配置。</summary>
        private void RebuildRuntimeAnimationIndex()
        {
            runtimeAnimationIndex = new Dictionary<Spine.Animation, AnimationSemantic>();
            runtimeAnimationConflicts = new HashSet<Spine.Animation>();
            foreach (KeyValuePair<AnimationSemantic, AnimationLine> pair in semanticIndex)
            {
                Spine.Animation runtimeAnimation = pair.Value == null ? null : pair.Value.GetRuntimeAnimation();
                if (runtimeAnimation == null || runtimeAnimationConflicts.Contains(runtimeAnimation)) continue;
                if (!runtimeAnimationIndex.TryGetValue(runtimeAnimation, out AnimationSemantic existingSemantic))
                {
                    runtimeAnimationIndex.Add(runtimeAnimation, pair.Key);
                    continue;
                }
                if (existingSemantic == pair.Key) continue;
                runtimeAnimationIndex.Remove(runtimeAnimation);
                runtimeAnimationConflicts.Add(runtimeAnimation);
                Debug.LogError($"AnimationLibrary '{name}' 的 Spine 动画 '{runtimeAnimation.Name}' 同时对应语义 '{existingSemantic}' 与 '{pair.Key}'，该动画无法用于 MixDuration 矩阵源语义反查。", this);
            }
        }

        /// <summary>资源载入后丢弃非序列化索引，确保进入运行时读取最新的 AnimationLine 语义。</summary>
        private void OnEnable()
        {
            InvalidateSemanticIndex();
        }

        /// <summary>规范化混合矩阵并使运行时语义索引失效，保证 Inspector 修改后读取最新配置。</summary>
        private void OnValidate()
        {
            MixDurationMatrix.Normalize();
            InvalidateSemanticIndex();
        }

#if UNITY_EDITOR
        /// <summary>从 Odin Inspector 打开完整的 MixDuration 行列矩阵编辑窗口。</summary>
        [Button("打开 MixDuration 矩阵配置"), PropertyOrder(-1000)]
        private void OpenMixDurationMatrixWindow()
        {
            Xuan.Prometheus.Editor.AnimationMixDurationMatrixWindow.Open(this);
        }
#endif
    }
}
