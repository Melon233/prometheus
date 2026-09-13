using System;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using Xuan.Prometheus.Component;
using Xuan.Prometheus.Logic;

namespace Xuan.Prometheus.Stamina.Tests
{
    /// <summary>
    /// 按 Docs/Design/Combat/02 第 5 节验证体力池：全有全无的一次性消耗、可扣空的持续消耗、
    /// 延迟恢复，以及按发起者折算的消耗降低。
    /// </summary>
    public sealed class StaminaSystemTests
    {
        private StaminaSystem system;
        private StubEntitySystem entitySystem;
        private GameObject holderObject;
        private PropertyConfig config;
        private PropertyComponent property;

        private const int Actor = 1;

        [SetUp]
        public void SetUp()
        {
            holderObject = new GameObject("StaminaTest.Actor");
            config = ScriptableObject.CreateInstance<PropertyConfig>();
            property = new PropertyComponent();
            property.Initialize(config);
            entitySystem = new StubEntitySystem();
            entitySystem.Register(Actor, new TestEntity(holderObject, property));
            system = new StaminaSystem(entitySystem);
        }

        [TearDown]
        public void TearDown()
        {
            system?.Dispose();
            system = null;
            UnityEngine.Object.DestroyImmediate(config);
            UnityEngine.Object.DestroyImmediate(holderObject);
        }

        // ---------- 一次性消耗 ----------

        /// <summary>体力充足时扣除请求量。</summary>
        [Test]
        public void TryConsume_WithEnoughStamina_DeductsTheCost()
        {
            Assert.That(system.TryConsume(Actor, 20f), Is.True);
            Assert.That(system.Current, Is.EqualTo(StaminaSystem.DefaultMax - 20f).Within(0.0001f));
        }

        /// <summary>
        /// 体力不足时**一点都不扣**。
        ///
        /// 原神里体力不够就放不出重击，而不是放出一个削弱版本；扣一半会让调用方
        /// 必须处理一个不存在的中间态。
        /// </summary>
        [Test]
        public void TryConsume_WithoutEnoughStamina_DeductsNothing()
        {
            system.ConsumeContinuous(Actor, StaminaSystem.DefaultMax - 5f, 1f);
            float before = system.Current;

            Assert.That(system.TryConsume(Actor, 20f), Is.False);
            Assert.That(system.Current, Is.EqualTo(before).Within(0.0001f));
        }

        /// <summary>查询不改变任何状态。</summary>
        [Test]
        public void CanAfford_DoesNotChangeState()
        {
            float before = system.Current;

            Assert.That(system.CanAfford(Actor, 20f), Is.True);
            Assert.That(system.CanAfford(Actor, StaminaSystem.DefaultMax + 1f), Is.False);
            Assert.That(system.Current, Is.EqualTo(before).Within(0.0001f));
        }

        // ---------- 持续消耗 ----------

        /// <summary>持续消耗按速率乘时间扣除。</summary>
        [Test]
        public void ConsumeContinuous_DeductsRateTimesDelta()
        {
            float consumed = system.ConsumeContinuous(Actor, 40f, 0.5f);

            Assert.That(consumed, Is.EqualTo(20f).Within(0.0001f));
            Assert.That(system.Current, Is.EqualTo(StaminaSystem.DefaultMax - 20f).Within(0.0001f));
        }

        /// <summary>
        /// 持续消耗允许把池子扣空，并如实返回实际扣除量。
        /// 冲刺该在体力见底的那一刻停下，而不是最后一帧整体失败。
        /// </summary>
        [Test]
        public void ConsumeContinuous_CanDrainThePoolAndReportsWhatItTook()
        {
            float consumed = system.ConsumeContinuous(Actor, StaminaSystem.DefaultMax * 2f, 1f);

            Assert.That(consumed, Is.EqualTo(StaminaSystem.DefaultMax).Within(0.0001f));
            Assert.That(system.Current, Is.Zero);
            Assert.That(system.ConsumeContinuous(Actor, 10f, 1f), Is.Zero, "空池子再扣为零。");
        }

        // ---------- 恢复 ----------

        /// <summary>消耗之后的延迟窗口内不恢复。</summary>
        [Test]
        public void Recovery_DoesNotStartBeforeTheDelayElapses()
        {
            system.TryConsume(Actor, 100f);
            float afterConsume = system.Current;

            system.OnUpdate(StaminaSystem.RecoveryDelaySeconds * 0.5f);

            Assert.That(system.Current, Is.EqualTo(afterConsume).Within(0.0001f));
        }

        /// <summary>延迟过后按速率恢复，并在上限处停住。</summary>
        [Test]
        public void Recovery_ResumesAfterDelayAndStopsAtMax()
        {
            system.TryConsume(Actor, 100f);
            // 这一帧恰好走到阈值，阈值之后的时间为零，因此还不恢复。
            system.OnUpdate(StaminaSystem.RecoveryDelaySeconds);
            Assert.That(system.Current, Is.EqualTo(StaminaSystem.DefaultMax - 100f).Within(0.0001f));

            system.OnUpdate(1f);
            Assert.That(system.Current, Is.EqualTo(StaminaSystem.DefaultMax - 100f + StaminaSystem.RecoveryRatePerSecond).Within(0.0001f));

            system.OnUpdate(100f);
            Assert.That(system.Current, Is.EqualTo(StaminaSystem.DefaultMax).Within(0.0001f));
        }

        /// <summary>
        /// 跨越延迟阈值的那一帧只按阈值之后的那一段恢复，因此恢复量不随帧率变化。
        /// 直接用整个 dt 会让帧率越低恢复越快——这类偏差只在低帧率设备上显形。
        /// </summary>
        [Test]
        public void Recovery_OnTheFrameThatCrossesTheDelay_OnlyCountsTimeAfterIt()
        {
            system.TryConsume(Actor, 100f);
            float afterConsume = system.Current;

            // 一帧就跨过延迟并多出 1 秒：应当只恢复这多出来的 1 秒。
            system.OnUpdate(StaminaSystem.RecoveryDelaySeconds + 1f);

            Assert.That(system.Current, Is.EqualTo(afterConsume + StaminaSystem.RecoveryRatePerSecond).Within(0.0001f));
        }

        /// <summary>新的消耗重置恢复延迟，使连续消耗不会一边扣一边回。</summary>
        [Test]
        public void Recovery_DelayRestartsOnEveryConsume()
        {
            system.TryConsume(Actor, 100f);
            system.OnUpdate(StaminaSystem.RecoveryDelaySeconds);
            system.TryConsume(Actor, 10f);
            float afterSecondConsume = system.Current;

            system.OnUpdate(StaminaSystem.RecoveryDelaySeconds * 0.5f);

            Assert.That(system.Current, Is.EqualTo(afterSecondConsume).Within(0.0001f));
        }

        // ---------- 消耗降低 ----------

        /// <summary>体力消耗降低按发起者的面板折算。</summary>
        [Test]
        public void Consumption_IsScaledByTheActorsReduction()
        {
            property.AddModifier(PropertyType.StaminaConsumptionReduction, PropertyModifierMode.Offset, 0.25f);

            system.TryConsume(Actor, 20f);

            Assert.That(system.Current, Is.EqualTo(StaminaSystem.DefaultMax - 15f).Within(0.0001f));
        }

        /// <summary>降低达到百分之百时消耗归零，但仍算作成功。</summary>
        [Test]
        public void Consumption_WithFullReduction_CostsNothing()
        {
            property.AddModifier(PropertyType.StaminaConsumptionReduction, PropertyModifierMode.Offset, 1f);

            Assert.That(system.TryConsume(Actor, 20f), Is.True);
            Assert.That(system.Current, Is.EqualTo(StaminaSystem.DefaultMax).Within(0.0001f));
        }

        /// <summary>没有属性面板的发起者按原价消耗，不因缺少组件而免费。</summary>
        [Test]
        public void Consumption_WithUnknownActor_UsesTheFullCost()
        {
            system.TryConsume(999, 20f);

            Assert.That(system.Current, Is.EqualTo(StaminaSystem.DefaultMax - 20f).Within(0.0001f));
        }

        // ---------- 事实广播 ----------

        /// <summary>消耗与恢复都广播变化，供 HUD 订阅。</summary>
        [Test]
        public void Changed_IsRaisedForBothConsumeAndRecovery()
        {
            List<StaminaChangedEvent> observed = new List<StaminaChangedEvent>();
            system.Changed += observed.Add;

            system.TryConsume(Actor, 20f);
            system.OnUpdate(StaminaSystem.RecoveryDelaySeconds + 1f);

            Assert.That(observed.Count, Is.EqualTo(2));
            Assert.That(observed[0].Delta, Is.Negative);
            Assert.That(observed[1].Delta, Is.Positive);
            Assert.That(observed[1].Max, Is.EqualTo(StaminaSystem.DefaultMax).Within(0.0001f));
        }

        // ---------- 测试替身 ----------

        /// <summary>按编号查实体的最小替身。</summary>
        private sealed class StubEntitySystem : XSystem, IEntitySystem, IEntityOwner
        {
            private readonly Dictionary<int, Entity> entities = new Dictionary<int, Entity>();

            public void Register(int entityId, Entity entity)
            {
                entity.BindEntityId(entityId, this);
                entities[entityId] = entity;
            }

            public int Count => entities.Count;
            public bool IsDisposed { get; private set; }

            public bool TryGetEntity(int entityId, out Entity entity) => entities.TryGetValue(entityId, out entity);

            public SlimeEntity SpawnEnemy(Vector3 worldPosition) => throw new NotSupportedException();
            public int AddEntity(Entity entity) => throw new NotSupportedException();
            public bool RemoveEntity(int entityId) => entities.Remove(entityId);
            public bool RequestRemoveEntity(int entityId, float destroyDelay = 0f) => entities.Remove(entityId);
            public ListenHandle Listen<TComponent>(int entityId, Func<TComponent, ModifiableProperty> fieldSelector, Action<TComponent> onDirty, bool invokeImmediately = true) where TComponent : IComponent => throw new NotSupportedException();

            public override void Dispose() => IsDisposed = true;
        }

        /// <summary>只带属性面板的测试实体。</summary>
        private sealed class TestEntity : Entity
        {
            public TestEntity(GameObject gameObject, PropertyComponent property)
            {
                bindGo = gameObject;
                AddComp(property);
            }
        }
    }
}
