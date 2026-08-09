using System;
using System.Collections.Generic;
using Spine;
using Spine.Unity;
using UnityEngine;

namespace PromeArchTrial.Presentation.Character
{
    /// <summary>
    /// 把纯只读角色表现快照投影到 Yefa 的 Transform、Spine track 0 与世界空间血条，不读取输入、不计算战斗结果，也不通过 Spine 事件反写模拟层。
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class YefaCharacterPresenter : MonoBehaviour
    {
        /// <summary>Built-in 渲染管线常用颜色属性的缓存编号。</summary>
        private static readonly int ColorPropertyId = Shader.PropertyToID("_Color");

        /// <summary>URP/HDRP 常用基础颜色属性的缓存编号。</summary>
        private static readonly int BaseColorPropertyId = Shader.PropertyToID("_BaseColor");

        /// <summary>承载 Yefa SkeletonData 和 track 0 动画状态的 Spine 组件。</summary>
        [SerializeField, Tooltip("Yefa 根节点上的 SkeletonAnimation。")] private SkeletonAnimation skeletonAnimation;

        /// <summary>接收模拟坐标和朝向的纯表现根节点。</summary>
        [SerializeField, Tooltip("只由表现快照驱动位置的根节点。")] private Transform movementRoot;

        /// <summary>通过 X 轴缩放表示当前生命比例的血条填充节点。</summary>
        [SerializeField, Tooltip("世界空间血条的填充节点。")] private Transform healthFill;

        /// <summary>生成伤害数字时使用的世界坐标锚点。</summary>
        [SerializeField, Tooltip("伤害飘字生成锚点。")] private Transform damageNumberAnchor;

        /// <summary>血条背景渲染器。</summary>
        [SerializeField, Tooltip("血条背景 Renderer。")] private Renderer healthBackgroundRenderer;

        /// <summary>血条填充渲染器。</summary>
        [SerializeField, Tooltip("血条填充 Renderer。")] private Renderer healthFillRenderer;

        /// <summary>血条背景颜色。</summary>
        [SerializeField, Tooltip("血条背景颜色。")] private Color healthBackgroundColor = new Color(0.08f, 0.08f, 0.08f, 0.9f);

        /// <summary>血条填充颜色。</summary>
        [SerializeField, Tooltip("血条填充颜色。")] private Color healthFillColor = new Color(0.12f, 0.85f, 0.25f, 1f);

        // 下列运行时缓存只属于表现层，用于动画去重、回滚时间同步、血条布局恢复和伤害事件幂等。
        private readonly HashSet<string> reportedMissingAnimations = new HashSet<string>();
        private CharacterAnimationPresentationBindings animationBindings;
        private CharacterPresentationSnapshot currentSnapshot;
        private Vector3 healthFillFullScale;
        private Vector3 healthFillFullPosition;
        private string activeAnimationName;
        private uint activeActionSequence;
        private uint lastDamageEventSequence;
        private bool hasSnapshot;
        private bool hasHealthBarLayout;
        private bool hasAnimationBindings;

        /// <summary>当生命值或最大生命值发生变化时触发，供 HUD 等外部表现订阅。</summary>
        public event Action<CharacterHealthPresentationChange> HealthChanged;

        /// <summary>当快照携带新的伤害事件序号时触发，供飘字对象池或其他数字表现订阅。</summary>
        public event Action<CharacterDamageNumberPresentationRequest> DamageNumberRequested;

        /// <summary>获取是否已经应用过至少一个有效快照。</summary>
        public bool HasSnapshot => hasSnapshot;

        /// <summary>获取最近一次成功应用的角色表现快照。</summary>
        public CharacterPresentationSnapshot CurrentSnapshot => currentSnapshot;

        /// <summary>获取当前实际播放在 Spine track 0 上的动画名。</summary>
        public string ActiveAnimationName => activeAnimationName;

        /// <summary>
        /// 注入从 Luban 客户端表现表解析出的动画绑定；该纯值对象让 Presenter 保持与生成代码程序集解耦。
        /// </summary>
        public void ConfigureAnimationBindings(CharacterAnimationPresentationBindings bindings)
        {
            animationBindings = bindings;
            hasAnimationBindings = true;
            activeAnimationName = null;
            activeActionSequence = 0;
            reportedMissingAnimations.Clear();
        }

        /// <summary>
        /// 注入净化 prefab 上的表现引用；该方法仅建立表现对象之间的组合关系，不接受或持有 gameplay 对象。
        /// </summary>
        public void Configure(SkeletonAnimation targetSkeletonAnimation, Transform targetMovementRoot, Transform targetHealthFill, Transform targetDamageNumberAnchor, Renderer targetHealthBackgroundRenderer, Renderer targetHealthFillRenderer)
        {
            skeletonAnimation = targetSkeletonAnimation != null ? targetSkeletonAnimation : throw new ArgumentNullException(nameof(targetSkeletonAnimation));
            movementRoot = targetMovementRoot != null ? targetMovementRoot : throw new ArgumentNullException(nameof(targetMovementRoot));
            healthFill = targetHealthFill;
            damageNumberAnchor = targetDamageNumberAnchor;
            healthBackgroundRenderer = targetHealthBackgroundRenderer;
            healthFillRenderer = targetHealthFillRenderer;
            CacheHealthBarLayout();
            ApplyHealthBarColors();
        }

        /// <summary>
        /// 应用一个模拟层快照；位置、朝向、动画时间和生命值只单向流向表现对象，动作完成仍由后续快照明确决定。
        /// </summary>
        public void ApplySnapshot(CharacterPresentationSnapshot snapshot)
        {
            EnsureConfigured();
            movementRoot.position = snapshot.Position;
            ApplyFacing(snapshot.Facing);
            ApplyAnimation(snapshot);
            ApplyHealth(snapshot);
            PublishDamageNumber(snapshot);
            currentSnapshot = snapshot;
            hasSnapshot = true;
        }

        /// <summary>在运行时首次启用时初始化 Spine 与血条材质属性。</summary>
        private void Awake()
        {
            ResolveDefaultReferences();
            EnsureAnimationBindings();
            CacheHealthBarLayout();
            ApplyHealthBarColors();
            if (skeletonAnimation != null) skeletonAnimation.Initialize(false);
        }

        /// <summary>在 Inspector 修改引用时补全同物体上的默认组件，避免生成 prefab 后出现空引用。</summary>
        private void OnValidate()
        {
            ResolveDefaultReferences();
        }

        /// <summary>补全可从当前物体确定获得的表现引用。</summary>
        private void ResolveDefaultReferences()
        {
            if (skeletonAnimation == null) skeletonAnimation = GetComponent<SkeletonAnimation>();
            if (movementRoot == null) movementRoot = transform;
            if (damageNumberAnchor == null) damageNumberAnchor = transform;
        }

        /// <summary>在应用快照前检查不可缺少的 Spine 与 Transform 引用。</summary>
        private void EnsureConfigured()
        {
            ResolveDefaultReferences();
            EnsureAnimationBindings();
            if (skeletonAnimation == null) throw new InvalidOperationException($"{nameof(YefaCharacterPresenter)} requires a {nameof(SkeletonAnimation)} reference.");
            if (movementRoot == null) throw new InvalidOperationException($"{nameof(YefaCharacterPresenter)} requires a movement root.");
            skeletonAnimation.Initialize(false);
        }

        /// <summary>只修改 Spine Skeleton 的水平缩放符号，避免翻转血条、飘字锚点或世界坐标。</summary>
        private void ApplyFacing(CharacterFacingDirection facing)
        {
            if (skeletonAnimation.Skeleton == null) return;
            float magnitude = Mathf.Max(0.0001f, Mathf.Abs(skeletonAnimation.Skeleton.ScaleX));
            skeletonAnimation.Skeleton.ScaleX = facing == CharacterFacingDirection.Left ? -magnitude : magnitude;
        }

        /// <summary>依据独占动作优先、持续状态兜底的规则选择全身动画，并始终写入 Spine track 0。</summary>
        private void ApplyAnimation(CharacterPresentationSnapshot snapshot)
        {
            AnimationSelection selection = SelectAnimation(snapshot);
            bool actionRestarted = selection.IsAction && activeActionSequence != snapshot.ActionSequence;
            bool animationChanged = !string.Equals(activeAnimationName, selection.AnimationName, StringComparison.Ordinal);
            if (animationChanged || actionRestarted) PlayTrackZero(selection.AnimationName, selection.Loop, snapshot.ActionSequence);
            if (selection.IsAction) SynchronizeActionTime(selection.TrackNormalizedTime);
        }

        /// <summary>清空所有旧轨道后在 track 0 播放完整角色动画，确保净化 prefab 不残留旧架构的叠加轨道。</summary>
        private void PlayTrackZero(string animationName, bool loop, uint actionSequence)
        {
            string resolvedAnimationName = ResolveExistingAnimation(animationName);
            if (string.IsNullOrEmpty(resolvedAnimationName)) return;
            skeletonAnimation.AnimationState.ClearTracks();
            skeletonAnimation.AnimationState.SetAnimation(0, resolvedAnimationName, loop);
            activeAnimationName = resolvedAnimationName;
            activeActionSequence = actionSequence;
        }

        /// <summary>把权威归一化动作时间写回当前 track 0，使预测回滚后的动画姿态与模拟 tick 对齐。</summary>
        private void SynchronizeActionTime(float normalizedTime)
        {
            TrackEntry trackEntry = skeletonAnimation.AnimationState.GetCurrent(0);
            if (trackEntry == null) return;
            trackEntry.TrackTime = Mathf.Clamp01(normalizedTime) * trackEntry.AnimationEnd;
        }

        /// <summary>验证动画是否存在；缺失时只记录一次告警并回退到待机动画，避免每帧污染 Console。</summary>
        private string ResolveExistingAnimation(string requestedAnimationName)
        {
            SkeletonData skeletonData = ResolveSkeletonData();
            if (skeletonData == null) return null;
            if (skeletonData.FindAnimation(requestedAnimationName) != null) return requestedAnimationName;
            if (reportedMissingAnimations.Add(requestedAnimationName)) Debug.LogWarning($"[PromeArchTrial] Yefa animation '{requestedAnimationName}' is missing; presenter will fall back to '{animationBindings.Idle}'.", this);
            return skeletonData.FindAnimation(animationBindings.Idle) != null ? animationBindings.Idle : null;
        }

        /// <summary>取得当前 SkeletonData；所有动画存在性与时长读取都复用这一入口，且不会推进 AnimationState。</summary>
        private SkeletonData ResolveSkeletonData()
        {
            return skeletonAnimation.SkeletonDataAsset == null ? null : skeletonAnimation.SkeletonDataAsset.GetSkeletonData(false);
        }

        /// <summary>读取指定 Spine 动画的真实秒数；缺失或零时长动画返回零并由技能阶段选择逻辑安全降级。</summary>
        private float ResolveAnimationDuration(string animationName)
        {
            SkeletonData skeletonData = ResolveSkeletonData();
            Spine.Animation animation = skeletonData?.FindAnimation(animationName);
            return animation == null ? 0f : Mathf.Max(0f, animation.Duration);
        }

        /// <summary>在 Luban 表尚未注入时使用当前 Yefa 资源默认值，确保 prefab 可以独立预览与验收。</summary>
        private void EnsureAnimationBindings()
        {
            if (hasAnimationBindings) return;
            animationBindings = CharacterAnimationPresentationBindings.CreateYefaDefaults();
            hasAnimationBindings = true;
        }

        /// <summary>更新内置世界空间血条并发布与具体 UI 技术无关的生命值事件。</summary>
        private void ApplyHealth(CharacterPresentationSnapshot snapshot)
        {
            float normalizedHealth = Mathf.Clamp01((float)snapshot.Health / snapshot.MaxHealth);
            if (healthFill != null)
            {
                if (!hasHealthBarLayout) CacheHealthBarLayout();
                Vector3 scale = healthFillFullScale;
                scale.x = healthFillFullScale.x * normalizedHealth;
                Vector3 position = healthFillFullPosition;
                position.x = healthFillFullPosition.x - healthFillFullScale.x * (1f - normalizedHealth) * 0.5f;
                healthFill.localScale = scale;
                healthFill.localPosition = position;
            }
            if (!hasSnapshot || snapshot.Health != currentSnapshot.Health || snapshot.MaxHealth != currentSnapshot.MaxHealth) HealthChanged?.Invoke(new CharacterHealthPresentationChange(snapshot.SimulationTick, snapshot.Health, snapshot.MaxHealth));
        }

        /// <summary>按伤害事件序号去重并发布飘字入口，不通过生命值差分猜测或重复结算伤害。</summary>
        private void PublishDamageNumber(CharacterPresentationSnapshot snapshot)
        {
            if (snapshot.DamageEventSequence == 0 || snapshot.DamageEventSequence <= lastDamageEventSequence) return;
            lastDamageEventSequence = snapshot.DamageEventSequence;
            if (snapshot.LatestDamageAmount <= 0) return;
            Vector3 anchorPosition = damageNumberAnchor != null ? damageNumberAnchor.position : movementRoot.position;
            DamageNumberRequested?.Invoke(new CharacterDamageNumberPresentationRequest(snapshot.DamageEventSequence, snapshot.LatestDamageAmount, snapshot.LatestDamageWasCritical, anchorPosition));
        }

        /// <summary>缓存满血时的缩放与中心位置，使血条缩短时左边缘保持不动。</summary>
        private void CacheHealthBarLayout()
        {
            if (healthFill == null) return;
            healthFillFullScale = healthFill.localScale;
            healthFillFullPosition = healthFill.localPosition;
            hasHealthBarLayout = true;
        }

        /// <summary>通过 MaterialPropertyBlock 设置血条颜色，不克隆或污染共享材质资源。</summary>
        private void ApplyHealthBarColors()
        {
            ApplyRendererColor(healthBackgroundRenderer, healthBackgroundColor);
            ApplyRendererColor(healthFillRenderer, healthFillColor);
        }

        /// <summary>同时写入 Built-in 与 URP 常用颜色属性，使生成的血条适配当前渲染管线。</summary>
        private static void ApplyRendererColor(Renderer targetRenderer, Color color)
        {
            if (targetRenderer == null) return;
            MaterialPropertyBlock propertyBlock = new MaterialPropertyBlock();
            targetRenderer.GetPropertyBlock(propertyBlock);
            propertyBlock.SetColor(ColorPropertyId, color);
            propertyBlock.SetColor(BaseColorPropertyId, color);
            targetRenderer.SetPropertyBlock(propertyBlock);
        }

        /// <summary>根据快照选择动画名与循环策略；所有动作均覆盖持续运动动画。</summary>
        private AnimationSelection SelectAnimation(CharacterPresentationSnapshot snapshot)
        {
            if (snapshot.Action != CharacterActionPresentationState.None) return SelectActionAnimation(snapshot.Action, snapshot.ActionNormalizedTime);
            return SelectLocomotionAnimation(snapshot.Locomotion);
        }

        /// <summary>把持续运动表现状态映射到 Yefa 动画资源。</summary>
        private AnimationSelection SelectLocomotionAnimation(CharacterLocomotionPresentationState locomotion)
        {
            switch (locomotion)
            {
                case CharacterLocomotionPresentationState.Walk: return new AnimationSelection(animationBindings.Walk, true, false);
                case CharacterLocomotionPresentationState.Run: return new AnimationSelection(animationBindings.Run, true, false);
                case CharacterLocomotionPresentationState.Sprint: return new AnimationSelection(animationBindings.Sprint, true, false);
                case CharacterLocomotionPresentationState.Rising: return new AnimationSelection(animationBindings.Rising, true, false);
                case CharacterLocomotionPresentationState.Falling: return new AnimationSelection(animationBindings.Falling, true, false);
                case CharacterLocomotionPresentationState.Landing: return new AnimationSelection(animationBindings.Landing, false, false);
                case CharacterLocomotionPresentationState.Dead: return new AnimationSelection(animationBindings.Death, false, false);
                default: return new AnimationSelection(animationBindings.Idle, true, false);
            }
        }

        /// <summary>把独占动作表现状态映射到 Yefa 全身动画资源。</summary>
        private AnimationSelection SelectActionAnimation(CharacterActionPresentationState action, float actionNormalizedTime)
        {
            switch (action)
            {
                case CharacterActionPresentationState.JumpStart: return new AnimationSelection(animationBindings.JumpStart, false, true, actionNormalizedTime);
                case CharacterActionPresentationState.DodgeForward: return new AnimationSelection(animationBindings.DodgeForward, false, true, actionNormalizedTime);
                case CharacterActionPresentationState.DodgeBackward: return new AnimationSelection(animationBindings.DodgeBackward, false, true, actionNormalizedTime);
                case CharacterActionPresentationState.Attack1: return new AnimationSelection(animationBindings.Attack1, false, true, actionNormalizedTime);
                case CharacterActionPresentationState.Attack2: return new AnimationSelection(animationBindings.Attack2, false, true, actionNormalizedTime);
                case CharacterActionPresentationState.Attack3: return new AnimationSelection(animationBindings.Attack3, false, true, actionNormalizedTime);
                case CharacterActionPresentationState.Attack4: return new AnimationSelection(animationBindings.Attack4, false, true, actionNormalizedTime);
                case CharacterActionPresentationState.HeavyAttack: return new AnimationSelection(animationBindings.HeavyAttack, false, true, actionNormalizedTime);
                case CharacterActionPresentationState.BranchAttack: return new AnimationSelection(animationBindings.SkillBody, false, true, actionNormalizedTime);
                case CharacterActionPresentationState.Skill: return SelectSkillAnimation(actionNormalizedTime);
                case CharacterActionPresentationState.Ultimate: return new AnimationSelection(animationBindings.Ultimate, false, true, actionNormalizedTime);
                case CharacterActionPresentationState.HitReaction: return new AnimationSelection(animationBindings.HitReaction, false, true, actionNormalizedTime);
                case CharacterActionPresentationState.Death: return new AnimationSelection(animationBindings.Death, false, true, actionNormalizedTime);
                default: return new AnimationSelection(animationBindings.Idle, true, false);
            }
        }

        /// <summary>
        /// 把单个 gameplay 技能的权威归一化时间投影到“起手后接主体”的 Spine 时间线；阶段边界只由两段资源的真实时长决定，因此前进预测与向后回滚会得到同一个阶段和局部姿态。
        /// </summary>
        private AnimationSelection SelectSkillAnimation(float actionNormalizedTime)
        {
            float startupDuration = ResolveAnimationDuration(animationBindings.SkillStartup);
            float bodyDuration = ResolveAnimationDuration(animationBindings.SkillBody);
            if (startupDuration <= 0f && bodyDuration <= 0f) return new AnimationSelection(animationBindings.SkillStartup, false, true, actionNormalizedTime);
            if (startupDuration <= 0f) return new AnimationSelection(animationBindings.SkillBody, false, true, actionNormalizedTime);
            if (bodyDuration <= 0f) return new AnimationSelection(animationBindings.SkillStartup, false, true, actionNormalizedTime);
            float sequenceTime = Mathf.Clamp01(actionNormalizedTime) * (startupDuration + bodyDuration);
            if (sequenceTime < startupDuration) return new AnimationSelection(animationBindings.SkillStartup, false, true, sequenceTime / startupDuration);
            return new AnimationSelection(animationBindings.SkillBody, false, true, (sequenceTime - startupDuration) / bodyDuration);
        }

        /// <summary>封装一次动画选择结果，避免动作与移动映射把播放细节泄漏给调用方。</summary>
        private readonly struct AnimationSelection
        {
            /// <summary>获取 Spine 动画名。</summary>
            public string AnimationName { get; }

            /// <summary>获取动画是否循环。</summary>
            public bool Loop { get; }

            /// <summary>获取该动画是否来自独占动作。</summary>
            public bool IsAction { get; }

            /// <summary>获取选中动画阶段内部的零到一时间；多段技能不会错误复用整个 gameplay 动作的归一化时间。</summary>
            public float TrackNormalizedTime { get; }

            /// <summary>创建不需要动作时间同步的持续动画选择。</summary>
            public AnimationSelection(string animationName, bool loop, bool isAction)
                : this(animationName, loop, isAction, 0f)
            {
            }

            /// <summary>创建一次包含阶段内时间的不可变动画选择。</summary>
            public AnimationSelection(string animationName, bool loop, bool isAction, float trackNormalizedTime)
            {
                AnimationName = animationName;
                Loop = loop;
                IsAction = isAction;
                TrackNormalizedTime = Mathf.Clamp01(trackNormalizedTime);
            }
        }
    }
}
