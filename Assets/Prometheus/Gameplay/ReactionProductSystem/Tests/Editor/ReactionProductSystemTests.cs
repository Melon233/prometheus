using System;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using Xuan.Prometheus.Component;
using Xuan.Prometheus.Effects;
using Xuan.Prometheus.Elements;
using Xuan.Prometheus.Logic;
using Xuan.Prometheus.Shields;
using Cfg = global::Prometheus.Config;

namespace Xuan.Prometheus.Reactions.Tests
{
    /// <summary>
    /// 验证伪元素状态的产物落地：冻结真的让目标动不了，草原核到期真的炸出草伤，
    /// 而状态被反应消耗时**不**引爆。
    ///
    /// 用例走真实导出的配表与真实的 `EffectLibrary` 资产，因此同时校验了资产是否配好。
    /// </summary>
    public sealed class ReactionProductSystemTests
    {
        private ConfigKit configKit;
        private ElementSystem elementSystem;
        private ReactionProductSystem productSystem;
        private StubEntitySystem entitySystem;
        private StubEffectSystem effectSystem;

        private GameObject attackerObject;
        private GameObject targetObject;
        private PropertyConfig attackerConfig;
        private PropertyConfig targetConfig;
        private PropertyComponent attackerProperty;
        private PropertyComponent targetProperty;
        private TestEntity attacker;
        private TestEntity target;

        private const int AttackerId = 1;
        private const int TargetId = 2;

        /// <summary>按表名从 AssetDatabase 读取导出的二进制表，替代运行时的资源包加载。</summary>
        private static Luban.ByteBuf LoadTable(string tableName)
        {
            TextAsset asset = AssetDatabase.LoadAssetAtPath<TextAsset>($"Assets/BundleResources/Table/{tableName}.bytes");
            Assert.That(asset, Is.Not.Null, $"无法加载导出的配表：{tableName}.bytes；请先执行 Prometheus/Luban/导出配表。");
            return new Luban.ByteBuf(asset.bytes);
        }

        [SetUp]
        public void SetUp()
        {
            configKit = new ConfigKit();
            Core.Config = configKit;
            configKit.LoadFrom(LoadTable);

            attackerObject = new GameObject("ReactionProductTest.Attacker");
            targetObject = new GameObject("ReactionProductTest.Target");
            attackerConfig = ScriptableObject.CreateInstance<PropertyConfig>();
            targetConfig = ScriptableObject.CreateInstance<PropertyConfig>();
            attackerConfig.hp = 100f;
            targetConfig.hp = 100000f;
            attackerProperty = new PropertyComponent();
            targetProperty = new PropertyComponent();
            attackerProperty.Initialize(attackerConfig);
            targetProperty.Initialize(targetConfig);
            attacker = new TestEntity(attackerObject, attackerProperty);
            target = new TestEntity(targetObject, targetProperty);

            elementSystem = new ElementSystem();
            elementSystem.AfterNew();
            // 实体编号由宿主写入，因此替身实体系统同时扮演 IEntityOwner。
            entitySystem = new StubEntitySystem();
            entitySystem.Register(AttackerId, attacker);
            entitySystem.Register(TargetId, target);

            EffectLibrary library = AssetDatabase.LoadAssetAtPath<EffectLibrary>("Assets/BundleResources/Config/Effect/EffectLibrary.asset");
            Assert.That(library, Is.Not.Null, "正式效果库缺失；请先执行 Prometheus/Effect System 下的资产生成菜单。");
            effectSystem = new StubEffectSystem(library, new EffectRuntime(1977, elementSystem, new ShieldSystem()));

            productSystem = new ReactionProductSystem(elementSystem, entitySystem, effectSystem);
            productSystem.AfterNew();
        }

        [TearDown]
        public void TearDown()
        {
            productSystem?.Dispose();
            effectSystem?.Dispose();
            elementSystem?.Dispose();
            configKit?.Dispose();
            Core.Config = null;
            UnityEngine.Object.DestroyImmediate(attackerConfig);
            UnityEngine.Object.DestroyImmediate(targetConfig);
            UnityEngine.Object.DestroyImmediate(attackerObject);
            UnityEngine.Object.DestroyImmediate(targetObject);
        }

        /// <summary>施加一次元素，走每次必附着的策略以免 ICD 干扰场景构造。</summary>
        private void Apply(Cfg.ElementType element, Cfg.GaugeStrength strength, string talentId = "Tests.Talent")
        {
            elementSystem.Apply(new ElementApplyRequest
            {
                SourceEntityId = AttackerId,
                TargetEntityId = TargetId,
                TalentId = talentId,
                IcdPolicy = Cfg.IcdPolicy.None,
                Element = element,
                Strength = strength
            });
        }

        /// <summary>取出目标身上某个状态的剩余秒数；不存在时返回 0。</summary>
        private float StateOf(Cfg.AuraKey key)
        {
            ElementAuraSet auras = elementSystem.QueryAura(TargetId).Auras;
            return auras != null && auras.TryGet(key, out ElementAura aura) ? aura.Gauge : 0f;
        }

        /// <summary>判断目标当前是否挂着指定的产物 Effect。</summary>
        private bool HasProduct(EffectDefinition definition)
        {
            IReadOnlyList<EffectInstance> active = effectSystem.Runtime.GetActiveEffects(target);
            for (int index = 0; index < active.Count; index++)
                if (ReferenceEquals(active[index].Definition, definition)) return true;
            return false;
        }

        /// <summary>把水冰打成冻结。</summary>
        private void Freeze()
        {
            Apply(Cfg.ElementType.Cryo, Cfg.GaugeStrength.Strong, "Tests.Cryo");
            Apply(Cfg.ElementType.Hydro, Cfg.GaugeStrength.Weak, "Tests.Hydro");
        }

        /// <summary>把水草打成草原核。</summary>
        private void Bloom()
        {
            Apply(Cfg.ElementType.Hydro, Cfg.GaugeStrength.Weak, "Tests.Hydro");
            Apply(Cfg.ElementType.Dendro, Cfg.GaugeStrength.Weak, "Tests.Dendro");
        }

        // ---------- 冻结 ----------

        /// <summary>冻结写入时施加产物 Effect，目标因此真的被禁止行动。</summary>
        [Test]
        public void Frozen_AppliesControlStateForAsLongAsTheStateLives()
        {
            Assert.That(targetProperty.ActiveControlStates, Is.EqualTo(ControlState.None));

            Freeze();

            Assert.That(StateOf(Cfg.AuraKey.Frozen), Is.GreaterThan(0f));
            Assert.That(HasProduct(effectSystem.DefaultLibrary.GetReactionProduct("Eff_Frozen")), Is.True, "冻结必须挂上产物 Effect。");
            Assert.That(targetProperty.ActiveControlStates & ControlState.Stun, Is.EqualTo(ControlState.Stun), "冻结的目标必须被禁止行动。");
        }

        /// <summary>冻结到期后产物被撤下，控制状态随之解除。</summary>
        [Test]
        public void Frozen_ReleasesControlStateWhenTheStateExpires()
        {
            Freeze();
            float duration = StateOf(Cfg.AuraKey.Frozen);

            elementSystem.OnUpdate(duration + 0.1f);

            Assert.That(StateOf(Cfg.AuraKey.Frozen), Is.Zero);
            Assert.That(HasProduct(effectSystem.DefaultLibrary.GetReactionProduct("Eff_Frozen")), Is.False);
            Assert.That(targetProperty.ActiveControlStates, Is.EqualTo(ControlState.None), "状态没了就不能还冻着。");
            Assert.That(productSystem.ActiveProductCount, Is.Zero, "产物记录不得泄漏。");
        }

        /// <summary>碎冰解除冻结时产物同样被撤下。</summary>
        [Test]
        public void Frozen_ReleasesControlStateWhenShattered()
        {
            Freeze();

            Apply(Cfg.ElementType.Physical, Cfg.GaugeStrength.None, "Tests.Shatter");

            Assert.That(StateOf(Cfg.AuraKey.Frozen), Is.Zero);
            Assert.That(targetProperty.ActiveControlStates, Is.EqualTo(ControlState.None));
            Assert.That(productSystem.ActiveProductCount, Is.Zero);
        }

        /// <summary>刷新冻结不会把产物撤下再重加，否则控制状态会出现一帧空窗。</summary>
        [Test]
        public void Frozen_RefreshKeepsTheSameProductInstance()
        {
            Freeze();
            IReadOnlyList<EffectInstance> before = effectSystem.Runtime.GetActiveEffects(target);
            long instanceId = before[0].InstanceId;

            Apply(Cfg.ElementType.Cryo, Cfg.GaugeStrength.Strong, "Tests.Cryo2");
            Apply(Cfg.ElementType.Hydro, Cfg.GaugeStrength.Weak, "Tests.Hydro2");

            IReadOnlyList<EffectInstance> after = effectSystem.Runtime.GetActiveEffects(target);
            Assert.That(after.Count, Is.EqualTo(1));
            Assert.That(after[0].InstanceId, Is.EqualTo(instanceId), "刷新状态不应重建产物 Effect。");
            Assert.That(targetProperty.ActiveControlStates & ControlState.Stun, Is.EqualTo(ControlState.Stun));
        }

        // ---------- 草原核 ----------

        /// <summary>草原核自然到期时引爆，对目标结算一笔剧变草伤。</summary>
        [Test]
        public void DendroCore_DetonatesWhenItExpiresNaturally()
        {
            Bloom();
            Assert.That(HasProduct(effectSystem.DefaultLibrary.GetReactionProduct("Eff_Bloom_Core")), Is.True);
            float hpBefore = targetProperty.Hp;

            elementSystem.OnUpdate(StateOf(Cfg.AuraKey.DendroCore) + 0.1f);

            Assert.That(HasProduct(effectSystem.DefaultLibrary.GetReactionProduct("Eff_Bloom_Core")), Is.False);
            Assert.That(targetProperty.Hp, Is.LessThan(hpBefore), "草原核到期必须炸出伤害。");

            // 引爆走的是剧变公式：等级系数 × 反应倍率，与攻击力、增伤、暴击无关。
            Cfg.ReactionMatrixRow row = Core.Config.Tables.TbReactionMatrix.Get("BloomDendro");
            float levelCoefficient = Core.Config.Tables.TbReactionLevelCoefficient.Get(Combat.DamagePipeline.DefaultLevel).Coefficient;
            Assert.That(hpBefore - targetProperty.Hp, Is.EqualTo(levelCoefficient * row.Multiplier).Within(0.01f));
        }

        /// <summary>
        /// 草原核被超绽放消耗时**不**引爆：那一笔伤害由超绽放自己作为剧变反应结算，
        /// 在这里再炸一次会让同一个核打出两笔伤害。
        /// </summary>
        [Test]
        public void DendroCore_DoesNotDetonateWhenConsumedByHyperbloom()
        {
            Bloom();
            float hpBefore = targetProperty.Hp;

            Apply(Cfg.ElementType.Electro, Cfg.GaugeStrength.None, "Tests.Hyperbloom");

            Assert.That(StateOf(Cfg.AuraKey.DendroCore), Is.Zero);
            Assert.That(HasProduct(effectSystem.DefaultLibrary.GetReactionProduct("Eff_Bloom_Core")), Is.False);
            Assert.That(targetProperty.Hp, Is.EqualTo(hpBefore).Within(0.0001f), "被消耗的草原核不得自行引爆。");
        }

        // ---------- 原激化与清场 ----------

        /// <summary>原激化只挂标记，不施加任何控制状态——它是增伤状态而不是控制。</summary>
        [Test]
        public void Quickened_AppliesMarkerWithoutControlState()
        {
            Apply(Cfg.ElementType.Dendro, Cfg.GaugeStrength.Weak, "Tests.Dendro");
            Apply(Cfg.ElementType.Electro, Cfg.GaugeStrength.Weak, "Tests.Electro");

            Assert.That(HasProduct(effectSystem.DefaultLibrary.GetReactionProduct("Eff_Quicken")), Is.True);
            Assert.That(targetProperty.ActiveControlStates, Is.EqualTo(ControlState.None));
        }

        /// <summary>清场会撤下全部产物，且不引爆草原核。</summary>
        [Test]
        public void ClearAura_RemovesEveryProductWithoutDetonating()
        {
            Freeze();
            Bloom();
            float hpBefore = targetProperty.Hp;

            elementSystem.ClearAura(TargetId);

            Assert.That(productSystem.ActiveProductCount, Is.Zero);
            Assert.That(targetProperty.ActiveControlStates, Is.EqualTo(ControlState.None));
            Assert.That(targetProperty.Hp, Is.EqualTo(hpBefore).Within(0.0001f), "清场不是引爆。");
        }

        // ---------- 结晶护盾 ----------

        /// <summary>
        /// 岩打火触发结晶：护盾归**施加者**，量按等级系数 × 1.5 × (1 + 精通项) 求得，
        /// 元素是被结晶的那个附着而不是触发方的岩。
        /// </summary>
        [Test]
        public void Crystallize_GrantsAnElementalShieldToTheCaster()
        {
            Apply(Cfg.ElementType.Pyro, Cfg.GaugeStrength.Strong, "Tests.Pyro");

            Combat.DamageResolution resolution = Combat.DamagePipeline.Resolve(new Combat.DamageRequest
            {
                Attacker = attackerProperty,
                Target = targetProperty,
                AttackerEntityId = AttackerId,
                TargetEntityId = TargetId,
                AttackerLevel = Combat.DamagePipeline.DefaultLevel,
                TargetLevel = Combat.DamagePipeline.DefaultLevel,
                ActionType = DamageActionType.Skill,
                Element = Cfg.ElementType.Geo,
                BaseDamage = 0f,
                GaugeStrength = Cfg.GaugeStrength.None,
                IcdPolicy = Cfg.IcdPolicy.None,
                TalentId = "Tests.Geo",
                CriticalRoll = 1f
            }, elementSystem);

            Assert.That(resolution.Reaction.ReactionId, Is.EqualTo("CrystallizePyro"));
            Assert.That(resolution.ProductEffectId, Is.EqualTo("Eff_Crystallize"));
            Assert.That(resolution.ProductElement, Is.EqualTo(Cfg.ElementType.Pyro), "结晶护盾的元素是被结晶的附着，不是触发方的岩。");

            float levelCoefficient = Core.Config.Tables.TbReactionLevelCoefficient.Get(Combat.DamagePipeline.DefaultLevel).Coefficient;
            Cfg.ReactionMatrixRow row = Core.Config.Tables.TbReactionMatrix.Get("CrystallizePyro");
            Assert.That(resolution.ProductValue, Is.EqualTo(levelCoefficient * row.Multiplier).Within(0.01f), "精通为 0 时精通项退化，护盾量即等级系数 × 倍率。");
        }

        // ---------- 测试替身 ----------

        /// <summary>按编号查实体的最小替身；本用例不需要实体系统的生成与回收能力。</summary>
        private sealed class StubEntitySystem : XSystem, IEntitySystem, IEntityOwner
        {
            private readonly Dictionary<int, Entity> entities = new Dictionary<int, Entity>();

            /// <summary>按指定编号登记一个实体，并以自身作为宿主写入运行时编号。</summary>
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

        /// <summary>持有真实 EffectRuntime 与真实效果库的最小替身，跳过异步资源加载。</summary>
        private sealed class StubEffectSystem : XSystem, IEffectSystem
        {
            public StubEffectSystem(EffectLibrary library, EffectRuntime runtime)
            {
                DefaultLibrary = library;
                Runtime = runtime;
            }

            public bool IsDisposed { get; private set; }
            public EffectRuntime Runtime { get; }
            public EffectLibrary DefaultLibrary { get; }

            public override void Dispose()
            {
                Runtime?.Dispose();
                IsDisposed = true;
            }
        }

        /// <summary>带固定编号的测试实体。</summary>
        private sealed class TestEntity : Entity
        {
            public TestEntity(GameObject gameObject, PropertyComponent property)
            {
                bindGo = gameObject;
                AddComp(property);
                AddComp<EventComponent>();
            }
        }
    }
}
