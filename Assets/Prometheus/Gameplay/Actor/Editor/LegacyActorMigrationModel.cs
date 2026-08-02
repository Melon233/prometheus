using System;
using System.Collections.Generic;
using Spine;
using Spine.Unity;
using Spine.Unity.AnimationTools;
using UnityEditor;
using UnityEngine;
using Xuan.Prometheus.Effects;

namespace Xuan.Prometheus.Actor.Editor
{
    /// <summary>
    /// 集中保存旧角色迁移所使用的稳定资产路径，避免生成器和校验器各自维护易漂移的字符串。
    /// </summary>
    internal static class LegacyActorMigrationPaths
    {
        internal const string OutputFolder = "Assets/BundleResources/Config/Actor";
        internal const string DefaultMotionPath = OutputFolder + "/DefaultCharacterControllerMotion.asset";
        internal const string DefaultCameraPath = OutputFolder + "/DefaultCameraFollowProfile.asset";
        internal const string YefaDefinitionPath = OutputFolder + "/YefaActorDefinition.asset";
        internal const string SlimeDefinitionPath = OutputFolder + "/SlimeActorDefinition.asset";
        internal const string YefaSkillBehaviorPath = OutputFolder + "/YefaSkill.asset";
        internal const string YefaUltimateBehaviorPath = OutputFolder + "/YefaUltimate.asset";
        internal const string YefaSpecialAttackBehaviorPath = OutputFolder + "/YefaSpecialAttack.asset";
        internal const string YefaDodgeBehaviorPath = OutputFolder + "/YefaDodge.asset";
        internal const string YefaAnimationLibraryPath = "Assets/BundleResources/Config/Animation/YefaAnimationLibrary.asset";
        internal const string SlimeAnimationLibraryPath = "Assets/BundleResources/Config/Animation/SlimeAnimationLibrary.asset";
        internal const string YefaPrefabPath = "Assets/BundleResources/Character/Yefa.prefab";
        internal const string SlimePrefabPath = "Assets/BundleResources/Enemy/Slime.prefab";
        internal const string HitboxBindingId = "Attack";
        internal const int TickRate = 60;
        internal const int YefaImportedAttackCount = 4;
        internal const int SlimeImportedAttackCount = 1;

        /// <summary>
        /// 返回指定连段序号对应的夜法普通攻击资产路径。
        /// </summary>
        internal static string GetYefaBehaviorPath(int zeroBasedIndex)
        {
            return OutputFolder + "/YefaBasicAttack" + (zeroBasedIndex + 1) + ".asset";
        }

        /// <summary>
        /// 返回指定连段序号对应的史莱姆普通攻击资产路径。
        /// </summary>
        internal static string GetSlimeBehaviorPath(int zeroBasedIndex)
        {
            return OutputFolder + "/SlimeBasicAttack" + (zeroBasedIndex + 1) + ".asset";
        }

        /// <summary>
        /// 返回夜法普通攻击对应的稳定 VFX 绑定编号。
        /// </summary>
        internal static string GetYefaVfxBindingId(int zeroBasedIndex)
        {
            return "Yefa.BasicAttack." + (zeroBasedIndex + 1) + ".Vfx";
        }

        /// <summary>
        /// 返回夜法移动普通攻击使用的稳定 Motion 绑定编号。
        /// </summary>
        internal static string GetYefaMotionBindingId(int zeroBasedIndex)
        {
            return "Yefa.BasicAttack." + (zeroBasedIndex + 1) + ".Motion";
        }
    }

    /// <summary>
    /// 保存从一个旧 AnimationLibrary 读取出的迁移源引用；该对象只在编辑器内存中存在，不会写入运行时资产。
    /// </summary>
    internal sealed class LegacyActorSource
    {
        internal LegacyActorSource(string actorName, global::Xuan.Prometheus.AnimationLibrary library, AnimationReferenceAsset[] defaultAttacks, AnimationReferenceAsset[] movingAttacks, AudioClip[] attackAudioClips, bool hasAttackVfx, int[] attackVfxSlotIndices, AnimationReferenceAsset idleAnimation, AnimationReferenceAsset moveAnimation, AnimationReferenceAsset sprintAnimation, AnimationReferenceAsset jumpAnimation, AnimationReferenceAsset fallAnimation, AnimationReferenceAsset landAnimation)
        {
            ActorName = actorName;
            Library = library;
            DefaultAttacks = defaultAttacks;
            MovingAttacks = movingAttacks;
            AttackAudioClips = attackAudioClips;
            HasAttackVfx = hasAttackVfx;
            AttackVfxSlotIndices = attackVfxSlotIndices;
            IdleAnimation = idleAnimation;
            MoveAnimation = moveAnimation;
            SprintAnimation = sprintAnimation;
            JumpAnimation = jumpAnimation;
            FallAnimation = fallAnimation;
            LandAnimation = landAnimation;
        }

        internal string ActorName { get; }

        internal global::Xuan.Prometheus.AnimationLibrary Library { get; }

        internal AnimationReferenceAsset[] DefaultAttacks { get; }

        internal AnimationReferenceAsset[] MovingAttacks { get; }

        internal AudioClip[] AttackAudioClips { get; }

        internal bool HasAttackVfx { get; }

        internal int[] AttackVfxSlotIndices { get; }

        internal int AttackVfxCount => AttackVfxSlotIndices == null ? 0 : AttackVfxSlotIndices.Length;

        internal AnimationReferenceAsset IdleAnimation { get; }

        internal AnimationReferenceAsset MoveAnimation { get; }

        internal AnimationReferenceAsset SprintAnimation { get; }

        internal AnimationReferenceAsset JumpAnimation { get; }

        internal AnimationReferenceAsset FallAnimation { get; }

        internal AnimationReferenceAsset LandAnimation { get; }
    }

    /// <summary>
    /// 从旧 AnimationLibrary 的公开引用和序列化私有字段构造只读迁移源。
    /// </summary>
    internal static class LegacyActorSourceReader
    {
        /// <summary>
        /// 读取夜法旧动画库及其普通攻击、音效、VFX 元数据和基础移动动画。
        /// </summary>
        internal static LegacyActorSource LoadYefa()
        {
            return Load("Yefa", LegacyActorMigrationPaths.YefaAnimationLibraryPath);
        }

        /// <summary>
        /// 读取史莱姆旧动画库及其普通攻击、音效和基础移动动画。
        /// </summary>
        internal static LegacyActorSource LoadSlime()
        {
            return Load("Slime", LegacyActorMigrationPaths.SlimeAnimationLibraryPath);
        }

        /// <summary>
        /// 加载一个旧动画库并对迁移必需的结构进行早期校验。
        /// </summary>
        private static LegacyActorSource Load(string actorName, string assetPath)
        {
            global::Xuan.Prometheus.AnimationLibrary library = AssetDatabase.LoadAssetAtPath<global::Xuan.Prometheus.AnimationLibrary>(assetPath);
            if (library == null) throw new InvalidOperationException($"无法在 '{assetPath}' 加载 {actorName} 的旧 AnimationLibrary。");
            if (library.atkExecutor == null) throw new InvalidOperationException($"旧 AnimationLibrary '{assetPath}' 缺少 AttackExecutor。");
            if (string.IsNullOrWhiteSpace(library.hitStart)) throw new InvalidOperationException($"旧 AnimationLibrary '{assetPath}' 缺少 hit_start 事件名。");
            if (string.IsNullOrWhiteSpace(library.hitEnd)) throw new InvalidOperationException($"旧 AnimationLibrary '{assetPath}' 缺少 hit_end 事件名。");
            SerializedObject serializedLibrary = new SerializedObject(library);
            SerializedProperty attackExecutor = RequireProperty(serializedLibrary, "atkExecutor", assetPath);
            SerializedProperty audioClips = RequireRelativeProperty(attackExecutor, "atkSfx", assetPath);
            SerializedProperty hasVfx = RequireRelativeProperty(attackExecutor, "hasVfx", assetPath);
            SerializedProperty vfxEntries = RequireRelativeProperty(attackExecutor, "atkVfx", assetPath);
            AnimationReferenceAsset[] defaultAttacks = CopyAnimations(library.atkExecutor.atkAnis);
            AnimationReferenceAsset[] movingAttacks = CopyAnimations(library.atkExecutor.atkMoveAnis);
            AudioClip[] attackAudioClips = ReadObjectReferenceArray<AudioClip>(audioClips, assetPath);
            int[] attackVfxSlotIndices = ReadEnumValueArray(vfxEntries, assetPath);
            AnimationReferenceAsset idleAnimation = ReadNestedObjectReference<AnimationReferenceAsset>(serializedLibrary, "idleExecutor", "idleAnimation", assetPath);
            AnimationReferenceAsset moveAnimation = ReadNestedObjectReference<AnimationReferenceAsset>(serializedLibrary, "groundMoveExecutor", "runAnimaiton", assetPath);
            AnimationReferenceAsset sprintAnimation = ReadNestedObjectReference<AnimationReferenceAsset>(serializedLibrary, "groundMoveExecutor", "sprintAnimation", assetPath);
            AnimationReferenceAsset jumpAnimation = ReadNestedObjectReference<AnimationReferenceAsset>(serializedLibrary, "airMoveExecutor", "jumpAni", assetPath);
            AnimationReferenceAsset fallAnimation = ReadNestedObjectReference<AnimationReferenceAsset>(serializedLibrary, "airMoveExecutor", "fallAni", assetPath);
            AnimationReferenceAsset landAnimation = ReadNestedObjectReference<AnimationReferenceAsset>(serializedLibrary, "airMoveExecutor", "landAni", assetPath);
            return new LegacyActorSource(actorName, library, defaultAttacks, movingAttacks, attackAudioClips, hasVfx.boolValue, attackVfxSlotIndices, idleAnimation, moveAnimation, sprintAnimation, jumpAnimation, fallAnimation, landAnimation);
        }

        /// <summary>
        /// 复制旧攻击动画列表，避免迁移过程中持有可变列表本身。
        /// </summary>
        private static AnimationReferenceAsset[] CopyAnimations(IReadOnlyList<AnimationReferenceAsset> source)
        {
            if (source == null) return Array.Empty<AnimationReferenceAsset>();
            AnimationReferenceAsset[] result = new AnimationReferenceAsset[source.Count];
            for (int index = 0; index < source.Count; index++) result[index] = source[index];
            return result;
        }

        /// <summary>
        /// 从序列化数组读取指定 UnityEngine.Object 类型的引用。
        /// </summary>
        private static TObject[] ReadObjectReferenceArray<TObject>(SerializedProperty property, string assetPath) where TObject : UnityEngine.Object
        {
            if (!property.isArray) throw new InvalidOperationException($"资产 '{assetPath}' 的字段 '{property.propertyPath}' 不是数组。");
            TObject[] result = new TObject[property.arraySize];
            for (int index = 0; index < property.arraySize; index++) result[index] = property.GetArrayElementAtIndex(index).objectReferenceValue as TObject;
            return result;
        }

        /// <summary>
        /// 从序列化枚举数组读取真实 VFX 槽位值，避免假设旧数组顺序必然等于枚举值。
        /// </summary>
        private static int[] ReadEnumValueArray(SerializedProperty property, string assetPath)
        {
            if (!property.isArray) throw new InvalidOperationException($"资产 '{assetPath}' 的字段 '{property.propertyPath}' 不是数组。");
            int[] result = new int[property.arraySize];
            for (int index = 0; index < property.arraySize; index++) result[index] = property.GetArrayElementAtIndex(index).enumValueIndex;
            return result;
        }

        /// <summary>
        /// 从序列化内联对象读取一个 UnityEngine.Object 引用。
        /// </summary>
        private static TObject ReadNestedObjectReference<TObject>(SerializedObject owner, string parentName, string childName, string assetPath) where TObject : UnityEngine.Object
        {
            SerializedProperty parent = RequireProperty(owner, parentName, assetPath);
            SerializedProperty child = RequireRelativeProperty(parent, childName, assetPath);
            return child.objectReferenceValue as TObject;
        }

        /// <summary>
        /// 获取必需的根序列化字段，并在旧结构漂移时给出可定位错误。
        /// </summary>
        private static SerializedProperty RequireProperty(SerializedObject owner, string propertyName, string assetPath)
        {
            SerializedProperty property = owner.FindProperty(propertyName);
            if (property == null) throw new InvalidOperationException($"资产 '{assetPath}' 缺少序列化字段 '{propertyName}'，旧结构可能已经发生变化。");
            return property;
        }

        /// <summary>
        /// 获取必需的子序列化字段，并在旧结构漂移时给出可定位错误。
        /// </summary>
        private static SerializedProperty RequireRelativeProperty(SerializedProperty owner, string propertyName, string assetPath)
        {
            SerializedProperty property = owner.FindPropertyRelative(propertyName);
            if (property == null) throw new InvalidOperationException($"资产 '{assetPath}' 缺少序列化字段 '{owner.propertyPath}.{propertyName}'，旧结构可能已经发生变化。");
            return property;
        }
    }

    /// <summary>
    /// 保存一次 Spine 攻击动画在 60 Hz 固定 Tick 中的总时长和半开命中区间。
    /// </summary>
    internal readonly struct LegacyAttackTiming
    {
        internal LegacyAttackTiming(int durationTicks, int hitStartTick, int hitEndTick, bool hasExplicitHitEnd)
        {
            DurationTicks = durationTicks;
            HitStartTick = hitStartTick;
            HitEndTick = hitEndTick;
            HasExplicitHitEnd = hasExplicitHitEnd;
        }

        internal int DurationTicks { get; }

        internal int HitStartTick { get; }

        internal int HitEndTick { get; }

        internal bool HasExplicitHitEnd { get; }

        /// <summary>
        /// 将 Spine 动画实际时长量化为至少一个 60 Hz Tick，并确保引用已经初始化。
        /// </summary>
        internal static int ReadDurationTicks(AnimationReferenceAsset animationReference)
        {
            Spine.Animation animation = ResolveAnimation(animationReference);
            return QuantizeEnd(animation.Duration);
        }

        /// <summary>
        /// 从 AnimationReferenceAsset 的 Spine EventTimeline 读取事件并量化为 60 Hz 半开区间。
        /// </summary>
        internal static LegacyAttackTiming Read(AnimationReferenceAsset animationReference, global::Xuan.Prometheus.AnimationLibrary library, Action<string> reportWarning)
        {
            if (animationReference == null) throw new InvalidOperationException($"AnimationLibrary '{library.name}' 包含空攻击动画引用。");
            Spine.Animation animation = ResolveAnimation(animationReference);
            float hitStartTime = -1f;
            float hitEndTime = -1f;
            for (int timelineIndex = 0; timelineIndex < animation.Timelines.Count; timelineIndex++)
            {
                EventTimeline eventTimeline = animation.Timelines.Items[timelineIndex] as EventTimeline;
                if (eventTimeline == null) continue;
                for (int eventIndex = 0; eventIndex < eventTimeline.FrameCount; eventIndex++)
                {
                    Spine.Event timelineEvent = eventTimeline.Events[eventIndex];
                    if (timelineEvent == null || timelineEvent.Data == null) continue;
                    if (string.Equals(timelineEvent.Data.Name, library.hitStart, StringComparison.Ordinal))
                    {
                        if (hitStartTime >= 0f) throw new InvalidOperationException($"Spine 动画 '{animationReference.name}' 包含多个 '{library.hitStart}' 事件，无法确定唯一命中开始时间。");
                        hitStartTime = eventTimeline.Frames[eventIndex];
                    }
                    else if (string.Equals(timelineEvent.Data.Name, library.hitEnd, StringComparison.Ordinal))
                    {
                        if (hitEndTime >= 0f) throw new InvalidOperationException($"Spine 动画 '{animationReference.name}' 包含多个 '{library.hitEnd}' 事件，无法确定唯一命中结束时间。");
                        hitEndTime = eventTimeline.Frames[eventIndex];
                    }
                }
            }
            if (hitStartTime < 0f) throw new InvalidOperationException($"Spine 动画 '{animationReference.name}' 缺少必需事件 '{library.hitStart}'。");
            bool hasExplicitHitEnd = hitEndTime >= 0f;
            if (!hasExplicitHitEnd)
            {
                hitEndTime = animation.Duration;
                reportWarning?.Invoke($"Spine 动画 '{animationReference.name}' 缺少 '{library.hitEnd}'；迁移将使用动画结束时间作为半开命中区间终点，以保持旧 OnStop 关闭碰撞体的语义。");
            }
            if (hitEndTime <= hitStartTime) throw new InvalidOperationException($"Spine 动画 '{animationReference.name}' 的命中事件顺序无效：start={hitStartTime}，end={hitEndTime}。");
            int hitStartTick = QuantizeStart(hitStartTime);
            int hitEndTick = Mathf.Max(hitStartTick + 1, QuantizeEnd(hitEndTime));
            int durationTicks = Mathf.Max(hitEndTick, QuantizeEnd(animation.Duration));
            return new LegacyAttackTiming(durationTicks, hitStartTick, hitEndTick, hasExplicitHitEnd);
        }

        /// <summary>
        /// 将包含式开始时刻向下量化，同时抵消 Spine 单精度帧时间产生的微小负误差。
        /// </summary>
        private static int QuantizeStart(float seconds)
        {
            return Mathf.Max(0, Mathf.FloorToInt(seconds * LegacyActorMigrationPaths.TickRate + 0.0001f));
        }

        /// <summary>
        /// 将排除式结束时刻向上量化，同时抵消 Spine 单精度帧时间产生的微小正误差。
        /// </summary>
        private static int QuantizeEnd(float seconds)
        {
            return Mathf.Max(1, Mathf.CeilToInt(seconds * LegacyActorMigrationPaths.TickRate - 0.0001f));
        }

        /// <summary>
        /// 初始化并解析 AnimationReferenceAsset，避免编辑器域重载后缓存尚未建立时把有效引用误判为空。
        /// </summary>
        private static Spine.Animation ResolveAnimation(AnimationReferenceAsset animationReference)
        {
            if (animationReference == null) throw new InvalidOperationException("无法解析空 AnimationReferenceAsset。");
            if (animationReference.Animation == null) animationReference.Initialize();
            Spine.Animation animation = animationReference.Animation;
            if (animation == null) throw new InvalidOperationException($"动画引用 '{AssetDatabase.GetAssetPath(animationReference)}' 无法解析 Spine 动画。");
            return animation;
        }
    }

    /// <summary>
    /// 保存一个待生成 ActorBehaviorDefinition 的确定性迁移计划，使生成和校验共享同一套量化结果。
    /// </summary>
    internal sealed class LegacyAttackPlan
    {
        internal LegacyAttackPlan(string behaviorId, int commandIndex, int durationTicks, int chainFromTick, int hitStartTick, int hitEndTick, AnimationReferenceAsset defaultAnimation, int defaultAnimationEndTick, AnimationReferenceAsset movingAnimation, int movingAnimationEndTick, AudioClip audioClip, string vfxBindingId, int vfxSlotIndex, ActorFactionMask targetFactions, string motionBindingId, Vector3[] bakedMotionSamples)
        {
            BehaviorId = behaviorId;
            CommandIndex = commandIndex;
            DurationTicks = durationTicks;
            ChainFromTick = chainFromTick;
            HitStartTick = hitStartTick;
            HitEndTick = hitEndTick;
            DefaultAnimation = defaultAnimation;
            DefaultAnimationEndTick = defaultAnimationEndTick;
            MovingAnimation = movingAnimation;
            MovingAnimationEndTick = movingAnimationEndTick;
            AudioClip = audioClip;
            VfxBindingId = vfxBindingId;
            VfxSlotIndex = vfxSlotIndex;
            TargetFactions = targetFactions;
            MotionBindingId = motionBindingId;
            BakedMotionSamples = bakedMotionSamples;
        }

        internal string BehaviorId { get; }

        internal int CommandIndex { get; }

        internal int DurationTicks { get; }

        internal int ChainFromTick { get; }

        internal int HitStartTick { get; }

        internal int HitEndTick { get; }

        internal AnimationReferenceAsset DefaultAnimation { get; }

        internal int DefaultAnimationEndTick { get; }

        internal AnimationReferenceAsset MovingAnimation { get; }

        internal int MovingAnimationEndTick { get; }

        internal AudioClip AudioClip { get; }

        internal string VfxBindingId { get; }

        internal int VfxSlotIndex { get; }

        internal ActorFactionMask TargetFactions { get; }

        internal string MotionBindingId { get; }

        internal Vector3[] BakedMotionSamples { get; }

        internal bool HasMovingVariant => MovingAnimation != null;

        internal bool HasVfxCue => !string.IsNullOrWhiteSpace(VfxBindingId);

        internal bool HasMotion => !string.IsNullOrWhiteSpace(MotionBindingId);
    }

    /// <summary>
    /// 把旧动画库转换为有限且可验证的行为计划，并明确阻止孤儿动画进入新资产集合。
    /// </summary>
    internal static class LegacyAttackPlanBuilder
    {
        /// <summary>
        /// 创建四个夜法普通攻击计划，并报告但不导入索引超出 Default 连段的移动攻击。
        /// </summary>
        internal static LegacyAttackPlan[] BuildYefa(LegacyActorSource source, Action<string> reportWarning)
        {
            RequireExactCount(source.DefaultAttacks, LegacyActorMigrationPaths.YefaImportedAttackCount, source.ActorName, "Default 攻击动画");
            RequireMinimumCount(source.MovingAttacks, LegacyActorMigrationPaths.YefaImportedAttackCount, source.ActorName, "Moving 攻击动画");
            RequireMinimumCount(source.AttackAudioClips, LegacyActorMigrationPaths.YefaImportedAttackCount, source.ActorName, "攻击音效");
            if (!source.HasAttackVfx || source.AttackVfxCount < LegacyActorMigrationPaths.YefaImportedAttackCount) throw new InvalidOperationException("Yefa 旧 AttackExecutor 缺少四个普通攻击 VFX 元数据。");
            for (int orphanIndex = LegacyActorMigrationPaths.YefaImportedAttackCount; orphanIndex < source.MovingAttacks.Length; orphanIndex++) reportWarning?.Invoke($"检测到 Yefa 第 {orphanIndex + 1} 个移动攻击动画 '{GetAssetLabel(source.MovingAttacks[orphanIndex])}' 没有对应 Default 攻击；该项已报告为孤儿且不会导入。");
            LegacyAttackPlan[] plans = new LegacyAttackPlan[LegacyActorMigrationPaths.YefaImportedAttackCount];
            for (int index = 0; index < plans.Length; index++)
            {
                AnimationReferenceAsset defaultAnimation = RequireReference(source.DefaultAttacks[index], source.ActorName, "Default 攻击动画", index);
                AnimationReferenceAsset movingAnimation = RequireReference(source.MovingAttacks[index], source.ActorName, "Moving 攻击动画", index);
                AudioClip audioClip = RequireReference(source.AttackAudioClips[index], source.ActorName, "攻击音效", index);
                LegacyAttackTiming defaultTiming = LegacyAttackTiming.Read(defaultAnimation, source.Library, reportWarning);
                LegacyAttackTiming movingTiming = LegacyAttackTiming.Read(movingAnimation, source.Library, reportWarning);
                if (defaultTiming.HitStartTick != movingTiming.HitStartTick || defaultTiming.HitEndTick != movingTiming.HitEndTick) throw new InvalidOperationException($"Yefa 第 {index + 1} 段 Default 与 Moving 动画的命中区间不一致，无法共享同一权威 Behavior：Default=[{defaultTiming.HitStartTick},{defaultTiming.HitEndTick})，Moving=[{movingTiming.HitStartTick},{movingTiming.HitEndTick})。");
                int durationTicks = Mathf.Max(defaultTiming.DurationTicks, movingTiming.DurationTicks);
                string behaviorId = "Yefa.BasicAttack." + (index + 1);
                string motionBindingId = LegacyActorMigrationPaths.GetYefaMotionBindingId(index);
                Vector3[] bakedMotionSamples = LegacySpineRootMotionBaker.Bake(movingAnimation, durationTicks, "hips", reportWarning);
                plans[index] = new LegacyAttackPlan(behaviorId, index, durationTicks, defaultTiming.HitEndTick, defaultTiming.HitStartTick, defaultTiming.HitEndTick, defaultAnimation, defaultTiming.DurationTicks, movingAnimation, movingTiming.DurationTicks, audioClip, LegacyActorMigrationPaths.GetYefaVfxBindingId(index), source.AttackVfxSlotIndices[index], ActorFactionMask.Enemy, motionBindingId, bakedMotionSamples);
            }
            return plans;
        }

        /// <summary>
        /// 创建一个史莱姆普通攻击计划；缺少 hit_end 时由时间读取器采用动画结束边界。
        /// </summary>
        internal static LegacyAttackPlan[] BuildSlime(LegacyActorSource source, Action<string> reportWarning)
        {
            RequireExactCount(source.DefaultAttacks, LegacyActorMigrationPaths.SlimeImportedAttackCount, source.ActorName, "Default 攻击动画");
            RequireMinimumCount(source.AttackAudioClips, LegacyActorMigrationPaths.SlimeImportedAttackCount, source.ActorName, "攻击音效");
            AnimationReferenceAsset defaultAnimation = RequireReference(source.DefaultAttacks[0], source.ActorName, "Default 攻击动画", 0);
            AudioClip audioClip = RequireReference(source.AttackAudioClips[0], source.ActorName, "攻击音效", 0);
            LegacyAttackTiming timing = LegacyAttackTiming.Read(defaultAnimation, source.Library, reportWarning);
            return new[] { new LegacyAttackPlan("Slime.BasicAttack.1", 0, timing.DurationTicks, timing.HitEndTick, timing.HitStartTick, timing.HitEndTick, defaultAnimation, timing.DurationTicks, null, 0, audioClip, null, -1, ActorFactionMask.Player, null, Array.Empty<Vector3>()) };
        }

        /// <summary>
        /// 要求源数组数量与预期完全一致，防止未定义的 Default 连段被静默遗漏。
        /// </summary>
        private static void RequireExactCount<TValue>(IReadOnlyCollection<TValue> values, int expectedCount, string actorName, string sourceName)
        {
            int actualCount = values == null ? 0 : values.Count;
            if (actualCount != expectedCount) throw new InvalidOperationException($"{actorName} 的{sourceName}数量应为 {expectedCount}，实际为 {actualCount}。");
        }

        /// <summary>
        /// 要求源数组至少包含指定数量的逐段配套数据。
        /// </summary>
        private static void RequireMinimumCount<TValue>(IReadOnlyCollection<TValue> values, int expectedCount, string actorName, string sourceName)
        {
            int actualCount = values == null ? 0 : values.Count;
            if (actualCount < expectedCount) throw new InvalidOperationException($"{actorName} 的{sourceName}至少需要 {expectedCount} 项，实际为 {actualCount}。");
        }

        /// <summary>
        /// 获取必需引用并在缺失时指出具体连段位置。
        /// </summary>
        private static TReference RequireReference<TReference>(TReference value, string actorName, string sourceName, int zeroBasedIndex) where TReference : UnityEngine.Object
        {
            if (value == null) throw new InvalidOperationException($"{actorName} 第 {zeroBasedIndex + 1} 段的{sourceName}为空。");
            return value;
        }

        /// <summary>
        /// 返回用于诊断的资产路径或空引用标记。
        /// </summary>
        private static string GetAssetLabel(UnityEngine.Object asset)
        {
            return asset == null ? "<null>" : AssetDatabase.GetAssetPath(asset);
        }
    }

    /// <summary>
    /// 将 Spine 指定根骨骼的 TranslateTimeline 按 60 Hz 离线采样为逐 Tick 局部位移；采样使用导入后的 SkeletonData 数值，因此自然继承 SkeletonDataAsset 的 0.001 缩放。
    /// </summary>
    internal static class LegacySpineRootMotionBaker
    {
        /// <summary>
        /// 烘焙覆盖完整行为时长的逐 Tick 位移；动画结束后的样本保持为零，缺少平移轨道代表该动画没有根运动而不是使用近似常量。
        /// </summary>
        internal static Vector3[] Bake(AnimationReferenceAsset animationReference, int behaviorDurationTicks, string rootMotionBoneName, Action<string> reportWarning)
        {
            if (TryBake(animationReference, behaviorDurationTicks, rootMotionBoneName, out Vector3[] samples)) return samples;
            reportWarning?.Invoke($"Spine 动画 '{animationReference.name}' 没有骨骼 '{rootMotionBoneName}' 的 TranslateTimeline；迁移将保存 {behaviorDurationTicks} 个零位移样本以准确表达该动画无根运动。");
            return samples;
        }

        /// <summary>
        /// 尝试烘焙一段动画的根运动并返回轨道是否存在；解析失败会抛出异常，只有明确缺少 TranslateTimeline 才返回 false。
        /// </summary>
        internal static bool TryBake(AnimationReferenceAsset animationReference, int behaviorDurationTicks, string rootMotionBoneName, out Vector3[] samples)
        {
            if (animationReference == null) throw new InvalidOperationException("根运动烘焙需要有效 AnimationReferenceAsset。");
            if (behaviorDurationTicks <= 0) throw new ArgumentOutOfRangeException(nameof(behaviorDurationTicks), behaviorDurationTicks, "根运动烘焙时长必须大于零。");
            if (string.IsNullOrWhiteSpace(rootMotionBoneName)) throw new ArgumentException("根运动骨骼名称不能为空。", nameof(rootMotionBoneName));
            if (animationReference.Animation == null) animationReference.Initialize();
            Spine.Animation animation = animationReference.Animation;
            if (animation == null) throw new InvalidOperationException($"动画引用 '{AssetDatabase.GetAssetPath(animationReference)}' 无法解析，不能可靠烘焙根运动。");
            SkeletonDataAsset skeletonDataAsset = animationReference.SkeletonDataAsset;
            SkeletonData skeletonData = skeletonDataAsset == null ? null : skeletonDataAsset.GetSkeletonData(true);
            if (skeletonData == null) throw new InvalidOperationException($"动画引用 '{AssetDatabase.GetAssetPath(animationReference)}' 缺少可解析 SkeletonData，不能可靠烘焙根运动。");
            int boneIndex = skeletonData.FindBoneIndex(rootMotionBoneName);
            if (boneIndex < 0) throw new InvalidOperationException($"动画引用 '{AssetDatabase.GetAssetPath(animationReference)}' 的 SkeletonData 不包含根运动骨骼 '{rootMotionBoneName}'。");
            TranslateTimeline timeline = animation.FindTranslateTimelineForBone(boneIndex);
            samples = new Vector3[behaviorDurationTicks];
            if (timeline == null) return false;
            for (int tick = 0; tick < behaviorDurationTicks; tick++)
            {
                float startTime = Mathf.Min(animation.Duration, tick / (float)LegacyActorMigrationPaths.TickRate);
                float endTime = Mathf.Min(animation.Duration, (tick + 1) / (float)LegacyActorMigrationPaths.TickRate);
                Vector2 delta = timeline.Evaluate(endTime) - timeline.Evaluate(startTime);
                if (float.IsNaN(delta.x) || float.IsInfinity(delta.x) || float.IsNaN(delta.y) || float.IsInfinity(delta.y)) throw new InvalidOperationException($"动画 '{animationReference.name}' 在 Tick {tick} 产生无效根运动位移，迁移已停止以避免写入猜测数据。");
                samples[tick] = new Vector3(delta.x, delta.y, 0f);
            }
            return true;
        }
    }
}
