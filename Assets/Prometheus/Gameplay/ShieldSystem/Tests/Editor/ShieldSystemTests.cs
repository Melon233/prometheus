using NUnit.Framework;
using Cfg = global::Prometheus.Config;

namespace Xuan.Prometheus.Shields.Tests
{
    /// <summary>
    /// 按 Docs/Design/Combat/05 第 6 节验证护盾吸收：
    /// 元素亲和 2.5 倍效率、多层按剩余量从高到低消耗、互斥组同时只存一层。
    ///
    /// 全部用例不构造 Entity、不读配表：护盾系统只认识「有多少、挡多少、什么时候碎」。
    /// </summary>
    public sealed class ShieldSystemTests
    {
        private ShieldSystem system;

        private const int Target = 1;

        [SetUp]
        public void SetUp()
        {
            system = new ShieldSystem();
        }

        [TearDown]
        public void TearDown()
        {
            system?.Dispose();
            system = null;
        }

        // ---------- 基础吸收 ----------

        /// <summary>无元素亲和的护盾按一倍效率吸收，剩余伤害交给生命值。</summary>
        [Test]
        public void Absorb_WithoutElementAffinity_UsesOneToOneEfficiency()
        {
            system.AddLayer(Target, string.Empty, Cfg.ElementType.None, 100f);

            float absorbed = system.Absorb(Target, 30f, Cfg.ElementType.Pyro);

            Assert.That(absorbed, Is.EqualTo(30f).Within(0.0001f));
            Assert.That(system.GetTotalRemaining(Target), Is.EqualTo(70f).Within(0.0001f));
        }

        /// <summary>伤害超过护盾容量时只吸收护盾能挡下的部分，余下的溢出给调用方。</summary>
        [Test]
        public void Absorb_BeyondCapacity_OnlyAbsorbsWhatTheShieldCanTake()
        {
            system.AddLayer(Target, string.Empty, Cfg.ElementType.None, 100f);

            float absorbed = system.Absorb(Target, 250f, Cfg.ElementType.Physical);

            Assert.That(absorbed, Is.EqualTo(100f).Within(0.0001f));
            Assert.That(system.GetTotalRemaining(Target), Is.Zero);
            Assert.That(system.QueryLayers(Target).Count, Is.Zero, "耗尽的护盾层必须被移除。");
        }

        /// <summary>没有护盾时吸收为零，且不会创建空的护盾记录。</summary>
        [Test]
        public void Absorb_WithoutAnyShield_AbsorbsNothing()
        {
            Assert.That(system.Absorb(Target, 100f, Cfg.ElementType.Pyro), Is.Zero);
            Assert.That(system.QueryLayers(Target).Count, Is.Zero);
        }

        // ---------- 元素亲和 ----------

        /// <summary>
        /// 元素亲和护盾对同元素伤害按 2.5 倍效率吸收：
        /// 名义量 100 的火护盾能挡下 250 点火伤（05 第 6 节，验收用例 D-11）。
        /// </summary>
        [Test]
        public void Absorb_MatchingElement_AbsorbsAtTwoPointFiveEfficiency()
        {
            system.AddLayer(Target, string.Empty, Cfg.ElementType.Pyro, 100f);

            float absorbed = system.Absorb(Target, 250f, Cfg.ElementType.Pyro);

            Assert.That(absorbed, Is.EqualTo(250f).Within(0.0001f));
            Assert.That(system.GetTotalRemaining(Target), Is.Zero);
        }

        /// <summary>同元素伤害消耗的是**名义量**：挡下 2.5 点只扣 1 点护盾。</summary>
        [Test]
        public void Absorb_MatchingElement_ConsumesNominalAmountNotAbsorbedDamage()
        {
            system.AddLayer(Target, string.Empty, Cfg.ElementType.Pyro, 100f);

            system.Absorb(Target, 50f, Cfg.ElementType.Pyro);

            Assert.That(system.GetTotalRemaining(Target), Is.EqualTo(80f).Within(0.0001f));
        }

        /// <summary>元素不匹配时退回一倍效率，亲和不是全局增益。</summary>
        [Test]
        public void Absorb_MismatchedElement_FallsBackToOneToOne()
        {
            system.AddLayer(Target, string.Empty, Cfg.ElementType.Pyro, 100f);

            float absorbed = system.Absorb(Target, 250f, Cfg.ElementType.Hydro);

            Assert.That(absorbed, Is.EqualTo(100f).Within(0.0001f));
        }

        // ---------- 多层 ----------

        /// <summary>多层护盾按剩余量从高到低消耗：厚的先挡，一次大伤害不会把薄盾一起打碎。</summary>
        [Test]
        public void Absorb_WithMultipleLayers_ConsumesTheThickestFirst()
        {
            ShieldLayer thin = system.AddLayer(Target, string.Empty, Cfg.ElementType.None, 20f);
            ShieldLayer thick = system.AddLayer(Target, string.Empty, Cfg.ElementType.None, 100f);

            system.Absorb(Target, 50f, Cfg.ElementType.Physical);

            Assert.That(thick.Remaining, Is.EqualTo(50f).Within(0.0001f));
            Assert.That(thin.Remaining, Is.EqualTo(20f).Within(0.0001f), "厚盾还撑得住时不应动薄盾。");
        }

        /// <summary>一层挡不住时溢出到下一层，总吸收量是各层容量之和。</summary>
        [Test]
        public void Absorb_OverflowsFromOneLayerIntoTheNext()
        {
            system.AddLayer(Target, string.Empty, Cfg.ElementType.None, 100f);
            system.AddLayer(Target, string.Empty, Cfg.ElementType.None, 30f);

            float absorbed = system.Absorb(Target, 200f, Cfg.ElementType.Physical);

            Assert.That(absorbed, Is.EqualTo(130f).Within(0.0001f));
            Assert.That(system.QueryLayers(Target).Count, Is.Zero);
        }

        /// <summary>各层按自己的元素亲和独立计算效率，不共享。</summary>
        [Test]
        public void Absorb_EachLayerUsesItsOwnEfficiency()
        {
            system.AddLayer(Target, string.Empty, Cfg.ElementType.Pyro, 100f);
            system.AddLayer(Target, string.Empty, Cfg.ElementType.None, 100f);

            // 火护盾挡 250，无亲和护盾再挡 100，合计 350。
            float absorbed = system.Absorb(Target, 400f, Cfg.ElementType.Pyro);

            Assert.That(absorbed, Is.EqualTo(350f).Within(0.0001f));
        }

        // ---------- 互斥组与句柄 ----------

        /// <summary>同一互斥组同时只能存在一层，新的替换旧的——结晶护盾靠它表达「同时只能存在一个」。</summary>
        [Test]
        public void AddLayer_WithSameGroupKey_ReplacesTheExistingLayer()
        {
            system.AddLayer(Target, "Crystallize", Cfg.ElementType.Pyro, 100f);
            system.AddLayer(Target, "Crystallize", Cfg.ElementType.Cryo, 60f);

            Assert.That(system.QueryLayers(Target).Count, Is.EqualTo(1));
            Assert.That(system.QueryLayers(Target)[0].Element, Is.EqualTo(Cfg.ElementType.Cryo));
            Assert.That(system.GetTotalRemaining(Target), Is.EqualTo(60f).Within(0.0001f));
        }

        /// <summary>空互斥组不互斥，多个来源的护盾可以并存。</summary>
        [Test]
        public void AddLayer_WithoutGroupKey_CoexistsWithOtherLayers()
        {
            system.AddLayer(Target, string.Empty, Cfg.ElementType.None, 100f);
            system.AddLayer(Target, string.Empty, Cfg.ElementType.None, 60f);

            Assert.That(system.QueryLayers(Target).Count, Is.EqualTo(2));
        }

        /// <summary>按对象身份移除只撤下自己那一层，不影响同元素的其他来源。</summary>
        [Test]
        public void RemoveLayer_OnlyRemovesItsOwnContribution()
        {
            ShieldLayer mine = system.AddLayer(Target, string.Empty, Cfg.ElementType.Pyro, 100f);
            system.AddLayer(Target, string.Empty, Cfg.ElementType.Pyro, 60f);

            Assert.That(system.RemoveLayer(Target, mine), Is.True);
            Assert.That(system.QueryLayers(Target).Count, Is.EqualTo(1));
            Assert.That(system.GetTotalRemaining(Target), Is.EqualTo(60f).Within(0.0001f));
        }

        /// <summary>移除一层已经被打碎或被同组替换掉的护盾是安全的空操作。</summary>
        [Test]
        public void RemoveLayer_AfterItWasBrokenOrReplaced_IsANoOp()
        {
            ShieldLayer broken = system.AddLayer(Target, "Crystallize", Cfg.ElementType.None, 10f);
            system.Absorb(Target, 50f, Cfg.ElementType.Physical);

            Assert.That(system.RemoveLayer(Target, broken), Is.False);

            ShieldLayer replaced = system.AddLayer(Target, "Crystallize", Cfg.ElementType.None, 10f);
            system.AddLayer(Target, "Crystallize", Cfg.ElementType.None, 20f);
            Assert.That(system.RemoveLayer(Target, replaced), Is.False);
            Assert.That(system.GetTotalRemaining(Target), Is.EqualTo(20f).Within(0.0001f));
        }

        /// <summary>清场移除目标全部护盾。</summary>
        [Test]
        public void ClearShields_RemovesEveryLayer()
        {
            system.AddLayer(Target, string.Empty, Cfg.ElementType.None, 100f);
            system.AddLayer(Target, "Crystallize", Cfg.ElementType.Pyro, 60f);

            system.ClearShields(Target);

            Assert.That(system.GetTotalRemaining(Target), Is.Zero);
        }

        /// <summary>护盾按实体隔离，一个目标的护盾不会替另一个挡伤害。</summary>
        [Test]
        public void Shields_AreIsolatedPerEntity()
        {
            system.AddLayer(Target, string.Empty, Cfg.ElementType.None, 100f);

            Assert.That(system.Absorb(2, 50f, Cfg.ElementType.Physical), Is.Zero);
            Assert.That(system.GetTotalRemaining(Target), Is.EqualTo(100f).Within(0.0001f));
        }
    }
}
