using System;
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using Xuan.Prometheus.Asset;
using Xuan.Prometheus.Component;
using Xuan.Prometheus.Effects;
using Xuan.Prometheus.Input;
using Xuan.Prometheus.Logic;

namespace Xuan.Prometheus.Animation.Tests
{
    /// <summary>使用三个正式 Yefa 实例验证小队独立 Entity、数字键切换、动作中断、同帧输入重定向、HUD 目标顺序、后台 Effect 推进与离场受击门禁。</summary>
    public sealed class TeamSystemTests
    {
        /// <summary>正式 Yefa 预制体路径，确保切换测试覆盖实际 Spine、属性和运动组件组合。</summary>
        private const string YefaPrefabPath = "Assets/BundleResources/Character/Yefa.prefab";

        /// <summary>正式持续增益路径，用于证明成员隐藏后 EffectSystem 仍会推进并移除到期实例。</summary>
        private const string BoostEffectPath = "Assets/BundleResources/Config/Effect/EffectDefinitions/Boost.asset";

        /// <summary>允许 EditMode 测试显式重放私有 Unity Awake 生命周期。</summary>
        private const BindingFlags PrivateInstanceMethod = BindingFlags.Instance | BindingFlags.NonPublic;

        /// <summary>保存测试开始前的全局事件入口，结束时必须恢复以隔离其他用例。</summary>
        private IEventKit previousEventKit;

        /// <summary>保存当前测试独占的全局事件总线。</summary>
        private EventKit eventKit;

        /// <summary>保存测试独占的资源 Kit，满足 GameplayKit 的显式依赖。</summary>
        private AssetKit assetKit;

        /// <summary>保存测试独占的玩法世界与三个 Entity。</summary>
        private GameplayKit gameplayKit;

        /// <summary>保存可脚本化输入源，以确定性地产生数字键和移动输入。</summary>
        private ScriptedTeamInputSource inputSource;

        /// <summary>保存当前测试使用的输入系统。</summary>
        private InputSystem inputSystem;

        /// <summary>保存当前测试使用的效果系统。</summary>
        private EffectSystem effectSystem;

        /// <summary>保存当前测试使用的小队系统。</summary>
        private TeamSystem teamSystem;

        /// <summary>保存 EffectSystem 所需的临时默认配置库。</summary>
        private EffectLibrary effectLibrary;

        /// <summary>保存三个拥有不同 EntityId 的最小玩家实体。</summary>
        private TeamTestEntity[] members;

        /// <summary>保存 SetUp 已经实例化的场景对象，使初始化中途失败时 TearDown 仍可完整回收。</summary>
        private List<GameObject> createdMemberObjects;

        /// <summary>为每个测试创建三个正式 Yefa 实例，并按运行时顺序初始化 Input、Effect 与 Team System。</summary>
        [SetUp]
        public void SetUp()
        {
            previousEventKit = Core.Event;
            eventKit = new EventKit();
            assetKit = new AssetKit();
            gameplayKit = new GameplayKit(assetKit);
            inputSource = new ScriptedTeamInputSource();
            inputSystem = new InputSystem(inputSource);
            effectLibrary = ScriptableObject.CreateInstance<EffectLibrary>();
            effectLibrary.name = "TeamSystemTests.EffectLibrary";
            effectSystem = new EffectSystem(effectLibrary);
            teamSystem = new TeamSystem();
            gameplayKit.AddSystem(inputSystem);
            gameplayKit.AddSystem(effectSystem);
            gameplayKit.AddSystem(teamSystem);
            inputSystem.AfterNew(gameplayKit);
            effectSystem.AfterNew(gameplayKit);
            teamSystem.AfterNew(gameplayKit);
            GameObject yefaPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(YefaPrefabPath);
            Assert.That(yefaPrefab, Is.Not.Null, $"无法加载正式角色预制体：{YefaPrefabPath}");
            members = new TeamTestEntity[TeamSystem.Capacity];
            createdMemberObjects = new List<GameObject>(TeamSystem.Capacity);
            for (int slotIndex = 0; slotIndex < TeamSystem.Capacity; slotIndex++)
            {
                GameObject memberObject = UnityEngine.Object.Instantiate(yefaPrefab);
                memberObject.name = $"TeamSystemTests.Yefa.{slotIndex + 1}";
                createdMemberObjects.Add(memberObject);
                SpineComponent spineComponent = memberObject.GetComponent<SpineComponent>();
                Assert.That(spineComponent, Is.Not.Null, "正式 Yefa 预制体必须包含 SpineComponent。");
                spineComponent.spineAnimator = memberObject.GetComponent<Spine.Unity.SkeletonAnimation>();
                Assert.That(spineComponent.spineAnimator, Is.Not.Null, "正式 Yefa SpineComponent 必须绑定 SkeletonAnimation。");
                spineComponent.spineAnimator.Initialize(true);
                spineComponent.spineAnimator.AnimationState.Data.DefaultMix = SpineComponent.TransitionDuration;
                PropertyComponent propertyComponent = memberObject.GetComponent<PropertyComponent>();
                Assert.That(propertyComponent, Is.Not.Null, "正式 Yefa 预制体必须包含 PropertyComponent。");
                InitializePropertyComponent(propertyComponent);
                TeamTestEntity member = new TeamTestEntity(memberObject);
                gameplayKit.AddEntity(member);
                member.AfterNew();
                members[slotIndex] = member;
            }
            teamSystem.InitializeMembers(members);
            MarkGameplayKitReady(gameplayKit);
        }

        /// <summary>按依赖逆序释放三个 Entity、System、资源和事件入口，避免静态状态及场景对象泄漏到后续测试。</summary>
        [TearDown]
        public void TearDown()
        {
            gameplayKit?.Dispose();
            gameplayKit = null;
            inputSystem = null;
            effectSystem = null;
            teamSystem = null;
            inputSource = null;
            members = null;
            if (createdMemberObjects != null)
            {
                for (int index = createdMemberObjects.Count - 1; index >= 0; index--)
                {
                    if (createdMemberObjects[index] != null) UnityEngine.Object.DestroyImmediate(createdMemberObjects[index]);
                }
            }
            createdMemberObjects = null;
            if (effectLibrary != null) UnityEngine.Object.DestroyImmediate(effectLibrary);
            effectLibrary = null;
            assetKit?.Dispose();
            assetKit = null;
            eventKit?.Dispose();
            eventKit = null;
            Core.Event = previousEventKit;
            previousEventKit = null;
        }

        /// <summary>验证三个槽位始终对应不同且仍为 Active 的 Entity，初始化只隐藏后备成员并施加 OffField 行为门禁。</summary>
        [Test]
        public void InitializeMembers_KeepsThreeIndependentEntitiesAndOnlyFirstMemberOnField()
        {
            HashSet<int> entityIds = new HashSet<int>();
            for (int slotIndex = 0; slotIndex < TeamSystem.Capacity; slotIndex++)
            {
                TeamTestEntity member = members[slotIndex];
                Assert.That(entityIds.Add(member.EntityId), Is.True, "每个小队槽位必须拥有不同的运行时 EntityId。");
                Assert.That(member.LifecycleState, Is.EqualTo(EntityLifecycleState.Active), "隐藏只能改变上场状态，不能暂停或销毁 Entity 生命周期。");
                Assert.That(member.TryGetComp(out TeamMemberComponent teamMemberComponent), Is.True);
                Assert.That(teamMemberComponent.SlotIndex, Is.EqualTo(slotIndex));
                Assert.That(teamMemberComponent.IsOnField, Is.EqualTo(slotIndex == 0));
                Assert.That(member.bindGo.activeSelf, Is.EqualTo(slotIndex == 0));
                Assert.That(member.TryGetComp(out PropertyComponent propertyComponent), Is.True);
                Assert.That(propertyComponent.HasAnyControlState(ControlState.OffField), Is.EqualTo(slotIndex != 0));
            }
            Assert.That(teamSystem.ActiveSlotIndex, Is.Zero);
            Assert.That(teamSystem.ActiveMember, Is.SameAs(members[0]));
        }

        /// <summary>验证数字键切换会中断退场动画、迁移世界运动状态、先更新 HUD EntityId，并把同一帧玩法输入直接交给切入成员。</summary>
        [Test]
        public void NumberKeySwitch_InterruptsOutgoingActionAndRedirectsSameFrameGameplayInput()
        {
            TeamTestEntity outgoingMember = members[0];
            TeamTestEntity incomingMember = members[1];
            outgoingMember.TryGetComp(out SpineComponent outgoingSpine);
            outgoingMember.TryGetComp(out MotionComponent outgoingMotion);
            outgoingMember.TryGetComp(out InputComponent outgoingInput);
            outgoingMember.TryGetComp(out VfxComponent outgoingVfx);
            incomingMember.TryGetComp(out MotionComponent incomingMotion);
            incomingMember.TryGetComp(out InputComponent incomingInput);
            Vector3 transferPosition = new Vector3(7f, 2f, -3f);
            Quaternion transferRotation = Quaternion.Euler(0f, 137f, 0f);
            Vector3 transferVelocity = new Vector3(2f, -4f, 5f);
            outgoingMember.bindGo.transform.SetPositionAndRotation(transferPosition, transferRotation);
            outgoingMotion.curVelo = transferVelocity;
            AnimationPlayback outgoingPlayback = outgoingSpine.TryPlay(AnimationSemantic.Attack1, AnimationOwner.NormalAttack, AnimationPriority.Attack, false, 1f, true);
            Assert.That(outgoingPlayback, Is.Not.Null, "正式 Yefa 动画库必须能够启动普通攻击会话。");
            outgoingVfx.Play(YefaVfx.Atk1);
            Assert.That(outgoingVfx.vfxSlots[(int)YefaVfx.Atk1].activeSelf, Is.True, "切换前必须先建立一个正在播放的普通攻击特效运行态。");
            bool outgoingPlaybackFinished = false;
            AnimationEndReason outgoingEndReason = default;
            outgoingPlayback.Finished += (_, reason) =>
            {
                outgoingPlaybackFinished = true;
                outgoingEndReason = reason;
            };
            List<string> notificationOrder = new List<string>();
            int observedEntityId = outgoingMember.EntityId;
            eventKit.AddListener<ActiveTeamMemberChangedEvent>(Event.ActiveTeamMemberChanged, eventData =>
            {
                observedEntityId = eventData.CurrentEntityId;
                notificationOrder.Add($"Member:{eventData.CurrentEntityId}");
            });
            inputSource.Move = new Vector2(0.8f, -0.25f);
            inputSource.RequestedSlotIndex = 1;
            inputSystem.BeforeEntityUpdate(0.016f);
            teamSystem.BeforeEntityUpdate(0.016f);
            Assert.That(teamSystem.ActiveMember, Is.SameAs(incomingMember));
            Assert.That(outgoingMember.bindGo.activeSelf, Is.False);
            Assert.That(incomingMember.bindGo.activeSelf, Is.True);
            Assert.That(outgoingSpine.CurrentPlayback, Is.Null);
            Assert.That(outgoingPlaybackFinished, Is.True, "退场成员当前动作必须在切换边界立即收到结束通知。");
            Assert.That(outgoingEndReason, Is.EqualTo(AnimationEndReason.Interrupted));
            Assert.That(outgoingVfx.vfxSlots[(int)YefaVfx.Atk1].activeSelf, Is.False, "换人退场必须清除普通攻击特效的 activeSelf 状态。");
            Assert.That(outgoingInput.hasInputThisFrame, Is.False);
            Assert.That(outgoingInput.moveDir, Is.EqualTo(Vector2.zero));
            Assert.That(incomingInput.hasInputThisFrame, Is.True, "切入成员必须在按下数字键的同一帧接管玩法输入。");
            Assert.That(incomingInput.moveDir, Is.EqualTo(inputSource.Move));
            Assert.That(Vector3.Distance(incomingMember.bindGo.transform.position, transferPosition), Is.LessThan(0.0001f));
            Assert.That(Quaternion.Angle(incomingMember.bindGo.transform.rotation, transferRotation), Is.LessThan(0.0001f));
            Assert.That(incomingMotion.curVelo, Is.EqualTo(transferVelocity));
            Assert.That(notificationOrder, Is.EqualTo(new[] { $"Member:{incomingMember.EntityId}" }), "换人只发布新 EntityId，HUD 数值由 ListenSystem 立即读取。");
            inputSource.Move = Vector2.up;
            inputSource.RequestedSlotIndex = -1;
            inputSystem.BeforeEntityUpdate(0.016f);
            teamSystem.BeforeEntityUpdate(0.016f);
            Assert.That(incomingInput.moveDir, Is.EqualTo(Vector2.up), "切换后的后续输入只能持续写入当前上场成员。");
            Assert.That(outgoingInput.moveDir, Is.EqualTo(Vector2.zero));
            Assert.That(teamSystem.SwitchToSlot(0), Is.True);
            Assert.That(outgoingVfx.vfxSlots[(int)YefaVfx.Atk1].activeSelf, Is.False, "切回角色后不得恢复退场前的普通攻击特效。");
        }

        /// <summary>验证成员退场后仍保持 Active Entity，且 EffectSystem 会继续推进该隐藏成员的持续增益直到正常到期。</summary>
        [Test]
        public void OffFieldMember_ContinuesEffectDurationAndExpiresNormally()
        {
            EffectDefinition boostEffect = AssetDatabase.LoadAssetAtPath<EffectDefinition>(BoostEffectPath);
            Assert.That(boostEffect, Is.Not.Null, $"无法加载正式持续增益：{BoostEffectPath}");
            TeamTestEntity boostedMember = members[0];
            effectSystem.Runtime.ApplyEffect(boostEffect, boostedMember, boostedMember, boostedMember);
            Assert.That(effectSystem.Runtime.GetStackCount(boostedMember, boostEffect.EffectId), Is.EqualTo(1));
            Assert.That(teamSystem.SwitchToSlot(2), Is.True);
            Assert.That(boostedMember.bindGo.activeSelf, Is.False);
            Assert.That(boostedMember.LifecycleState, Is.EqualTo(EntityLifecycleState.Active));
            effectSystem.OnUpdate(boostEffect.Duration + 0.01f);
            Assert.That(effectSystem.Runtime.GetStackCount(boostedMember, boostEffect.EffectId), Is.Zero, "隐藏成员的持续效果计时不能因 GameObject 停用而暂停。");
        }

        /// <summary>验证隐藏成员仍可接收伤害事实，但不会启动无法在停用 GameObject 上推进的受击动画或遗留 Attacked 状态。</summary>
        [Test]
        public void OffFieldMember_IgnoresVisualHitReactionWithoutBlockingEffectLifecycle()
        {
            TeamTestEntity offFieldMember = members[0];
            Assert.That(teamSystem.SwitchToSlot(1), Is.True);
            Assert.That(offFieldMember.bindGo.activeSelf, Is.False);
            Assert.That(offFieldMember.TryGetComp(out EventComponent eventComponent), Is.True);
            Assert.That(offFieldMember.TryGetComp(out SpineComponent spineComponent), Is.True);
            Assert.That(offFieldMember.TryGetComp(out PropertyComponent propertyComponent), Is.True);
            eventComponent.Invoke(new StaggeredEvent(10f, 2f, 1f));
            Assert.That(spineComponent.CurrentPlayback, Is.Null, "离场成员只保留数值与 Effect 生命周期，不应启动后台受击表现。");
            Assert.That(propertyComponent.IsAttacked, Is.False, "离场受击不得遗留依赖动画完成回调才能清除的 Attacked 状态。");
        }

        /// <summary>在 EditMode 中显式执行 PropertyComponent.Awake，使测试与运行时实例化后的满生命初始化顺序一致。</summary>
        private static void InitializePropertyComponent(PropertyComponent propertyComponent)
        {
            MethodInfo awakeMethod = typeof(PropertyComponent).GetMethod("Awake", PrivateInstanceMethod);
            if (awakeMethod == null) throw new MissingMethodException(typeof(PropertyComponent).FullName, "Awake");
            awakeMethod.Invoke(propertyComponent, null);
        }

        /// <summary>在手动组装 System 的测试环境中进入与 GameplayKit.AfterNew 结束后相同的 Ready 状态，使 EntityInputReceiver 可以按正式规则解析目标。</summary>
        private static void MarkGameplayKitReady(GameplayKit targetGameplayKit)
        {
            PropertyInfo readyProperty = typeof(GameplayKit).GetProperty(nameof(GameplayKit.IsReady), BindingFlags.Instance | BindingFlags.Public);
            MethodInfo readySetter = readyProperty?.GetSetMethod(true);
            if (readySetter == null) throw new MissingMethodException(typeof(GameplayKit).FullName, $"set_{nameof(GameplayKit.IsReady)}");
            readySetter.Invoke(targetGameplayKit, new object[] { true });
        }

        /// <summary>提供只包含 TeamSystem 必需组件的最小玩家 Entity，避免无关战斗 Logic 干扰系统边界测试。</summary>
        private sealed class TeamTestEntity : Entity
        {
            /// <summary>从正式 Yefa 对象注册输入、小队、属性、动画、运动和动作特效组件。</summary>
            public TeamTestEntity(GameObject bindGameObject)
            {
                bindGo = bindGameObject != null ? bindGameObject : throw new ArgumentNullException(nameof(bindGameObject));
                AddComp<InputComponent>();
                AddComp<TeamMemberComponent>();
                AddComp<EventComponent>();
                AddComp(bindGo.GetComponent<PropertyComponent>());
                AddComp(bindGo.GetComponent<SpineComponent>());
                AddComp(bindGo.GetComponent<MotionComponent>());
                AddComp(bindGo.GetComponent<VfxComponent>());
                AddLogic<AttackedLogic>();
            }
        }

        /// <summary>提供可由测试逐帧设置的本地输入源，避免依赖编辑器键盘焦点或 UnityEngine.Input。</summary>
        private sealed class ScriptedTeamInputSource : IInputSource
        {
            /// <inheritdoc />
            public string SourceId => "TeamSystemTests.Local";

            /// <summary>获取或设置下一次采样使用的移动输入。</summary>
            public Vector2 Move { get; set; }

            /// <summary>获取或设置下一次采样按下的零基小队槽位；负一表示不切换。</summary>
            public int RequestedSlotIndex { get; set; } = -1;

            /// <summary>获取输入源是否已随 InputSystem 完成释放。</summary>
            public bool IsDisposed { get; private set; }

            /// <inheritdoc />
            public InputFrame Sample(long frameId)
            {
                InputButtonState selectFirst = CreateSelectionState(0);
                InputButtonState selectSecond = CreateSelectionState(1);
                InputButtonState selectThird = CreateSelectionState(2);
                return new InputFrame(frameId, Move, Move, default, default, default, default, default, default, default, default, default, default, selectFirst, selectSecond, selectThird);
            }

            /// <summary>为指定槽位创建只包含按下沿和保持态的确定性按钮状态。</summary>
            private InputButtonState CreateSelectionState(int slotIndex)
            {
                bool selected = RequestedSlotIndex == slotIndex;
                return new InputButtonState(selected, selected, false);
            }

            /// <inheritdoc />
            public void Dispose()
            {
                IsDisposed = true;
            }
        }
    }
}
