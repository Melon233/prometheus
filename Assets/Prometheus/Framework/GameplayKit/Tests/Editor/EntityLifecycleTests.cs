using System;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using Xuan.Prometheus.Asset;
using Xuan.Prometheus.Component;
using Xuan.Prometheus.Logic;

namespace Xuan.Prometheus.Tests
{
    /// <summary>验证 EntitySystem 注册、稳定 Logic 顺序、字段监听、帧内回收、异常清理和幂等释放协议。</summary>
    public sealed class EntityLifecycleTests
    {
        private readonly List<GameObject> cleanupObjects = new List<GameObject>();
        private AssetKit assetKit;
        private GameplayKit gameplayKit;
        private EntitySystem entitySystem;

        /// <summary>每个测试创建独立 GameplayKit，并取得其内建 EntitySystem，而不初始化 YooAsset。</summary>
        [SetUp]
        public void SetUp()
        {
            assetKit = new AssetKit();
            gameplayKit = new GameplayKit(assetKit);
            entitySystem = gameplayKit.GetSystem<EntitySystem>();
        }

        /// <summary>按运行时真实依赖顺序释放 GameplayKit、AssetKit 和未被 Entity 回收的临时对象。</summary>
        [TearDown]
        public void TearDown()
        {
            gameplayKit?.Dispose();
            gameplayKit = null;
            entitySystem = null;
            assetKit?.Dispose();
            assetKit = null;
            for (int index = cleanupObjects.Count - 1; index >= 0; index--)
            {
                if (cleanupObjects[index] != null) UnityEngine.Object.DestroyImmediate(cleanupObjects[index]);
            }
            cleanupObjects.Clear();
        }

        /// <summary>验证相同 OrderTag 的 Logic 在初始化和逐帧更新中始终保持 Entity 注册顺序。</summary>
        [Test]
        public void EqualOrderLogics_KeepStableRegistrationOrder()
        {
            List<string> calls = new List<string>();
            FirstRecordingLogic first = new FirstRecordingLogic(calls);
            SecondRecordingLogic second = new SecondRecordingLogic(calls);
            ThirdRecordingLogic third = new ThirdRecordingLogic(calls);
            TestEntity entity = CreateEntity(first, second, third);
            entitySystem.AddEntity(entity);
            entity.AfterNew();
            entity.OnUpdate(0.1f);
            CollectionAssert.AreEqual(new[] { "First.AfterNew", "Second.AfterNew", "Third.AfterNew", "First.Update", "Second.Update", "Third.Update" }, calls);
        }

        /// <summary>验证 GameplayKit 在配置前已经提供唯一 EntitySystem，且实体回收会同步释放归属于该实体的字段监听。</summary>
        [Test]
        public void EntitySystem_IsBuiltInAndEntityRemovalDisposesOwnedListeners()
        {
            Assert.That(gameplayKit.GetSystem<EntitySystem>(), Is.SameAs(entitySystem));
            ObservableComponent component = new ObservableComponent();
            TestEntity entity = CreateEntityWithComponent(component);
            int entityId = entitySystem.AddEntity(entity);
            entity.AfterNew();
            Assert.That(entitySystem.Count, Is.EqualTo(1));
            int dirtyCount = 0;
            ListenHandle handle = entitySystem.Listen<ObservableComponent>(entityId, value => value.ValueProperty, _ => dirtyCount++);
            Assert.That(dirtyCount, Is.EqualTo(1));
            Assert.That(handle.IsDisposed, Is.False);
            Assert.That(entitySystem.RemoveEntity(entityId), Is.True);
            Assert.That(entitySystem.Count, Is.Zero);
            Assert.That(handle.IsDisposed, Is.True);
            component.ValueProperty.SetBaseValue(1f);
            Assert.That(dirtyCount, Is.EqualTo(1), "Entity 回收后字段不得继续持有外部监听回调。");
        }

        /// <summary>验证首次回收请求立即停止更新，并由 GameplayKit 在安全边界执行一次禁用、注销和移除。</summary>
        [Test]
        public void RequestDispose_IsIdempotentAndRemovesAtSafeBoundary()
        {
            List<string> calls = new List<string>();
            FirstRecordingLogic logic = new FirstRecordingLogic(calls);
            RecordingComponent component = new RecordingComponent();
            TestEntity entity = CreateEntityWithComponent(component, logic);
            int entityId = entitySystem.AddEntity(entity);
            entity.AfterNew();
            entity.OnUpdate(0.1f);
            Assert.That(entity.RequestDispose(0f), Is.True);
            Assert.That(entity.RequestDispose(0f), Is.False);
            Assert.That(entity.LifecycleState, Is.EqualTo(EntityLifecycleState.DespawnRequested));
            Assert.That(entitySystem.TryGetEntity(entityId, out _), Is.True);
            entity.OnUpdate(0.1f);
            Assert.That(logic.UpdateCount, Is.EqualTo(1));
            gameplayKit.OnUpdate(0f);
            Assert.That(entitySystem.TryGetEntity(entityId, out _), Is.False);
            Assert.That(entity.LifecycleState, Is.EqualTo(EntityLifecycleState.Disposed));
            Assert.That(logic.DisableCount, Is.EqualTo(1));
            Assert.That(logic.DisposeCount, Is.EqualTo(1));
            Assert.That(logic.Entity, Is.Null);
            Assert.That(component.Entity, Is.Null);
            Assert.That(entity.RequestDispose(0f), Is.False);
            Assert.That(entitySystem.RemoveEntity(entityId), Is.False);
        }

        /// <summary>验证一个 Logic 在 OnUpdate 中请求回收后，同帧后续 Logic 不会再启用或执行。</summary>
        [Test]
        public void DisposeRequestedDuringUpdate_StopsRemainingLogicImmediately()
        {
            List<string> calls = new List<string>();
            DisposeRequestLogic first = new DisposeRequestLogic(calls);
            TrailingRecordingLogic trailing = new TrailingRecordingLogic(calls);
            TestEntity entity = CreateEntity(first, trailing);
            int entityId = entitySystem.AddEntity(entity);
            entity.AfterNew();
            entity.OnUpdate(0.1f);
            Assert.That(first.UpdateCount, Is.EqualTo(1));
            Assert.That(trailing.EnableCount, Is.EqualTo(0));
            Assert.That(trailing.UpdateCount, Is.EqualTo(0));
            Assert.That(entity.LifecycleState, Is.EqualTo(EntityLifecycleState.DespawnRequested));
            gameplayKit.OnUpdate(0f);
            Assert.That(entitySystem.TryGetEntity(entityId, out _), Is.False);
            Assert.That(first.DisposeCount, Is.EqualTo(1));
            Assert.That(trailing.DisposeCount, Is.EqualTo(1));
        }

        /// <summary>验证 Logic 初始化中途抛出异常时，已开始初始化的 Logic 仍会按安全边界各注销一次。</summary>
        [Test]
        public void InitializationFailure_RequestsCleanupAndDisposesStartedLogicsOnce()
        {
            List<string> calls = new List<string>();
            FirstRecordingLogic first = new FirstRecordingLogic(calls);
            ThrowingInitializationLogic throwing = new ThrowingInitializationLogic(calls);
            TestEntity entity = CreateEntity(first, throwing);
            int entityId = entitySystem.AddEntity(entity);
            Assert.Throws<InvalidOperationException>(() => entity.AfterNew());
            Assert.That(entity.LifecycleState, Is.EqualTo(EntityLifecycleState.DespawnRequested));
            gameplayKit.OnUpdate(0f);
            Assert.That(entitySystem.TryGetEntity(entityId, out _), Is.False);
            Assert.That(first.DisposeCount, Is.EqualTo(1));
            Assert.That(throwing.DisposeCount, Is.EqualTo(1));
        }

        /// <summary>验证 OnEnable 抛出异常时 Enable 标志会回滚，最终回收不会误调用未完成启用的 OnDisable。</summary>
        [Test]
        public void EnableFailure_RollsBackEnableStateBeforeCleanup()
        {
            List<string> calls = new List<string>();
            ThrowingEnableLogic throwing = new ThrowingEnableLogic(calls);
            TestEntity entity = CreateEntity(throwing);
            entitySystem.AddEntity(entity);
            entity.AfterNew();
            Assert.Throws<InvalidOperationException>(() => entity.OnUpdate(0.1f));
            Assert.That(throwing.Enable, Is.False);
            Assert.That(entity.RequestDispose(0f), Is.True);
            gameplayKit.OnUpdate(0f);
            Assert.That(throwing.DisableCount, Is.EqualTo(0));
            Assert.That(throwing.DisposeCount, Is.EqualTo(1));
        }

        /// <summary>创建只包含指定 Logic 的最小 Entity 和 GameObject。</summary>
        private TestEntity CreateEntity(params ILogic[] logics)
        {
            GameObject gameObject = new GameObject("EntityLifecycleTests.Entity");
            cleanupObjects.Add(gameObject);
            return new TestEntity(gameObject, null, logics);
        }

        /// <summary>创建同时包含一个可观察组件与指定 Logic 的最小 Entity。</summary>
        private TestEntity CreateEntityWithComponent(Component.Component component, params ILogic[] logics)
        {
            GameObject gameObject = new GameObject("EntityLifecycleTests.EntityWithComponent");
            cleanupObjects.Add(gameObject);
            return new TestEntity(gameObject, component, logics);
        }

        /// <summary>用于验证 Entity 回收后会主动断开 Component.Entity 引用。</summary>
        private sealed class RecordingComponent : Component.Component
        {
        }

        /// <summary>提供一个可监听字段，用于验证 EntitySystem 会随 Entity 回收对应句柄。</summary>
        private sealed class ObservableComponent : Component.Component
        {
            /// <summary>获取测试使用的可监听数值字段。</summary>
            public ModifiableProperty ValueProperty { get; } = new ModifiableProperty();
        }

        /// <summary>允许测试按确定顺序组合普通 Logic，而不依赖任何场景预制体。</summary>
        private sealed class TestEntity : Entity
        {
            /// <summary>创建测试 Entity 并在注册前完成全部组件和 Logic 组合。</summary>
            public TestEntity(GameObject gameObject, Component.Component component, IEnumerable<ILogic> entityLogics)
            {
                bindGo = gameObject;
                if (component != null) AddComp(component);
                foreach (ILogic logic in entityLogics) AddLogic(logic);
            }
        }

        /// <summary>记录标准 Logic 的生命周期次数和调用顺序，供多个回收场景复用。</summary>
        private abstract class RecordingLogic : Logic.Logic
        {
            private readonly List<string> calls;
            private readonly string name;

            /// <summary>创建具有稳定诊断名称的记录 Logic。</summary>
            protected RecordingLogic(List<string> calls, string name)
            {
                this.calls = calls;
                this.name = name;
            }

            /// <summary>获取成功进入 OnEnable 的次数。</summary>
            public int EnableCount { get; protected set; }

            /// <summary>获取执行 OnDisable 的次数。</summary>
            public int DisableCount { get; protected set; }

            /// <summary>获取执行 OnUpdate 的次数。</summary>
            public int UpdateCount { get; protected set; }

            /// <summary>获取执行 OnDispose 的次数。</summary>
            public int DisposeCount { get; protected set; }

            /// <inheritdoc />
            public override void AfterNew()
            {
                calls.Add($"{name}.AfterNew");
            }

            /// <inheritdoc />
            public override bool CanEnable()
            {
                return true;
            }

            /// <inheritdoc />
            public override bool CanDisable()
            {
                return false;
            }

            /// <inheritdoc />
            public override void OnEnable()
            {
                EnableCount++;
            }

            /// <inheritdoc />
            public override void OnDisable()
            {
                DisableCount++;
            }

            /// <inheritdoc />
            public override void OnUpdate(float dt)
            {
                UpdateCount++;
                calls.Add($"{name}.Update");
            }

            /// <inheritdoc />
            public override void OnDispose()
            {
                DisposeCount++;
            }
        }

        /// <summary>第一项同优先级记录 Logic。</summary>
        private sealed class FirstRecordingLogic : RecordingLogic
        {
            public FirstRecordingLogic(List<string> calls) : base(calls, "First")
            {
            }
        }

        /// <summary>第二项同优先级记录 Logic。</summary>
        private sealed class SecondRecordingLogic : RecordingLogic
        {
            public SecondRecordingLogic(List<string> calls) : base(calls, "Second")
            {
            }
        }

        /// <summary>第三项同优先级记录 Logic。</summary>
        private sealed class ThirdRecordingLogic : RecordingLogic
        {
            public ThirdRecordingLogic(List<string> calls) : base(calls, "Third")
            {
            }
        }

        /// <summary>在第一次更新中主动请求 Entity 回收的 Logic。</summary>
        private sealed class DisposeRequestLogic : RecordingLogic
        {
            public DisposeRequestLogic(List<string> calls) : base(calls, "DisposeRequest")
            {
            }

            /// <inheritdoc />
            public override void OnUpdate(float dt)
            {
                base.OnUpdate(dt);
                Entity.RequestDispose(0f);
            }
        }

        /// <summary>用于验证回收请求之后不会再执行的尾部 Logic。</summary>
        private sealed class TrailingRecordingLogic : RecordingLogic
        {
            public TrailingRecordingLogic(List<string> calls) : base(calls, "Trailing")
            {
            }
        }

        /// <summary>模拟 AfterNew 已经开始但最终失败的 Logic。</summary>
        private sealed class ThrowingInitializationLogic : RecordingLogic
        {
            public ThrowingInitializationLogic(List<string> calls) : base(calls, "ThrowingInitialization")
            {
            }

            /// <inheritdoc />
            public override void AfterNew()
            {
                base.AfterNew();
                throw new InvalidOperationException("Expected initialization failure.");
            }
        }

        /// <summary>模拟 OnEnable 失败以验证 Entity 调度器状态回滚的 Logic。</summary>
        private sealed class ThrowingEnableLogic : RecordingLogic
        {
            public ThrowingEnableLogic(List<string> calls) : base(calls, "ThrowingEnable")
            {
            }

            /// <inheritdoc />
            public override void OnEnable()
            {
                throw new InvalidOperationException("Expected enable failure.");
            }
        }
    }
}
