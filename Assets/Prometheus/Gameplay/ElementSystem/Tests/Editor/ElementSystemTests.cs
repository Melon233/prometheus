using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using Cfg = Prometheus.Config;

namespace Xuan.Prometheus.Elements.Tests
{
    /// <summary>
    /// 按 Docs/Design/Combat/04 第 8 节与 04A 第 11 节的验收用例验证元素附着与反应。
    /// 用例走真实配表（导出的 .bytes），因此同时校验了表内容与代码行为的一致性。
    /// </summary>
    public sealed class ElementSystemTests
    {
        /// <summary>被测系统；每个用例独占一份，状态互不影响。</summary>
        private ElementSystem system;
        /// <summary>测试独占的配表 Kit。</summary>
        private ConfigKit configKit;

        /// <summary>攻击方实体编号。</summary>
        private const int Source = 1;
        /// <summary>目标实体编号。</summary>
        private const int Target = 2;

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
            system = new ElementSystem();
            system.AfterNew();
        }

        [TearDown]
        public void TearDown()
        {
            system?.Dispose();
            system = null;
            configKit?.Dispose();
            configKit = null;
            Core.Config = null;
        }

        /// <summary>构造一次施加请求；默认走共享 ICD，与普攻的语义一致。</summary>
        private static ElementApplyRequest Request(Cfg.ElementType element, Cfg.GaugeStrength strength = Cfg.GaugeStrength.Weak, string talentId = "NormalAttack", Cfg.IcdPolicy policy = Cfg.IcdPolicy.Shared)
        {
            return new ElementApplyRequest
            {
                SourceEntityId = Source,
                TargetEntityId = Target,
                TalentId = talentId,
                IcdPolicy = policy,
                Element = element,
                Strength = strength
            };
        }

        /// <summary>取出目标身上某元素的当前附着量；不存在时返回 0。</summary>
        private float GaugeOf(Cfg.AuraKey key)
        {
            ElementAuraSet auras = system.QueryAura(Target).Auras;
            return auras != null && auras.TryGet(key, out ElementAura aura) ? aura.Gauge : 0f;
        }

        // ---------- E-01 ~ E-03：附着量、衰减税与刷新 ----------

        [Test]
        public void E01_WeakApplication_WritesEightyPercentAndDecaysOverNineAndHalfSeconds()
        {
            system.Apply(Request(Cfg.ElementType.Pyro));

            Assert.That(GaugeOf(Cfg.AuraKey.Pyro), Is.EqualTo(0.8f).Within(0.0001f), "1U 经 0.8 衰减税后写入 0.8U。");

            system.OnUpdate(9.5f);
            Assert.That(GaugeOf(Cfg.AuraKey.Pyro), Is.Zero, "1U 档位的总持续时间为 9.5 秒。");
        }

        [Test]
        public void E02_SameElementReapplied_RefreshesGaugeAndDecayRate()
        {
            system.Apply(Request(Cfg.ElementType.Pyro));
            system.OnUpdate(5f);
            Assert.That(GaugeOf(Cfg.AuraKey.Pyro), Is.LessThan(0.8f));

            system.Apply(Request(Cfg.ElementType.Pyro, talentId: "Skill", policy: Cfg.IcdPolicy.None));
            Assert.That(GaugeOf(Cfg.AuraKey.Pyro), Is.EqualTo(0.8f).Within(0.0001f), "同元素刷新后附着量回到单次写入量。");

            system.OnUpdate(9.5f);
            Assert.That(GaugeOf(Cfg.AuraKey.Pyro), Is.Zero, "衰减速率同时被重设为新一次施加对应的值。");
        }

        [Test]
        public void E03_StrongerThenWeaker_KeepsMaxGaugeButTakesNewDecayRate()
        {
            system.Apply(Request(Cfg.ElementType.Pyro, Cfg.GaugeStrength.Medium, policy: Cfg.IcdPolicy.None));
            Assert.That(GaugeOf(Cfg.AuraKey.Pyro), Is.EqualTo(1.6f).Within(0.0001f));

            system.Apply(Request(Cfg.ElementType.Pyro, Cfg.GaugeStrength.Weak, policy: Cfg.IcdPolicy.None));
            Assert.That(GaugeOf(Cfg.AuraKey.Pyro), Is.EqualTo(1.6f).Within(0.0001f), "附着量取两者较大值。");

            // 速率取 1U 档（0.8 / 9.5），因此 9.5 秒只掉 0.8，仍有剩余。
            system.OnUpdate(9.5f);
            Assert.That(GaugeOf(Cfg.AuraKey.Pyro), Is.EqualTo(0.8f).Within(0.001f), "衰减速率取新一次施加对应的值，而不是较大那次的。");
        }

        // ---------- E-04 ~ E-05：ICD ----------

        [Test]
        public void E04_FiveConsecutiveHits_ApplyOnFirstAndFourth()
        {
            // 第 1 次附着，第 2、3 次被挡，第 4 次重新附着，第 5 次被挡。
            Assert.That(system.Apply(Request(Cfg.ElementType.Pyro)).Applied, Is.False, "首次命中没有反应，但附着已写入。");
            Assert.That(GaugeOf(Cfg.AuraKey.Pyro), Is.EqualTo(0.8f).Within(0.0001f));

            system.OnUpdate(0.1f);
            system.Apply(Request(Cfg.ElementType.Pyro));
            system.OnUpdate(0.1f);
            system.Apply(Request(Cfg.ElementType.Pyro));

            // 第 2、3 次被 ICD 挡下，因此附着只经历了衰减，没有被刷新。
            float afterBlockedHits = GaugeOf(Cfg.AuraKey.Pyro);
            Assert.That(afterBlockedHits, Is.LessThan(0.8f));

            system.OnUpdate(0.1f);
            system.Apply(Request(Cfg.ElementType.Pyro));
            Assert.That(GaugeOf(Cfg.AuraKey.Pyro), Is.EqualTo(0.8f).Within(0.0001f), "第 4 次命中重新附着。");
        }

        [Test]
        public void E05_NormalAttackAndSkill_HaveIndependentIcd()
        {
            system.Apply(Request(Cfg.ElementType.Pyro, talentId: "NormalAttack"));
            system.OnUpdate(0.1f);
            system.Apply(Request(Cfg.ElementType.Pyro, talentId: "NormalAttack"));
            system.OnUpdate(0.1f);

            // 普攻此刻处于窗口内且计数为 2；战技是另一条 ICD，应当照常附着。
            system.OnUpdate(5f);
            Assert.That(GaugeOf(Cfg.AuraKey.Pyro), Is.LessThan(0.8f));
            system.Apply(Request(Cfg.ElementType.Pyro, talentId: "Skill"));
            Assert.That(GaugeOf(Cfg.AuraKey.Pyro), Is.EqualTo(0.8f).Within(0.0001f), "战技的 ICD 与普攻互不影响。");
        }

        [Test]
        public void IcdWindow_ResetsAfterTimeout()
        {
            system.Apply(Request(Cfg.ElementType.Pyro));
            system.OnUpdate(0.1f);
            system.Apply(Request(Cfg.ElementType.Pyro));

            // 超过 2.5 秒窗口后计数重置，下一次命中重新附着。
            system.OnUpdate(2.6f);
            system.Apply(Request(Cfg.ElementType.Pyro));
            Assert.That(GaugeOf(Cfg.AuraKey.Pyro), Is.EqualTo(0.8f).Within(0.0001f));
        }

        // ---------- E-06 ~ E-07：增幅反应的方向性 ----------

        [Test]
        public void E06_HydroOntoPyro_TriggersReverseVaporizeAndConsumesDoubleGauge()
        {
            system.Apply(Request(Cfg.ElementType.Pyro, policy: Cfg.IcdPolicy.None));
            Assert.That(GaugeOf(Cfg.AuraKey.Pyro), Is.EqualTo(0.8f).Within(0.0001f));

            ReactionResult result = system.Apply(Request(Cfg.ElementType.Hydro, policy: Cfg.IcdPolicy.None));

            Assert.That(result.Triggered, Is.True);
            Assert.That(result.Row.ReactionId, Is.EqualTo("VaporizeReverse"));
            Assert.That(result.Row.Multiplier, Is.EqualTo(1.5f).Within(0.0001f));
            Assert.That(result.ConsumedGauge, Is.EqualTo(0.8f).Within(0.0001f), "逆向蒸发消耗系数 2.0，0.8 × 2 = 1.6 大于现有 0.8，因此全部消耗。");
            Assert.That(GaugeOf(Cfg.AuraKey.Pyro), Is.Zero);
            Assert.That(GaugeOf(Cfg.AuraKey.Hydro), Is.Zero, "触发元素被完全消耗，不写入附着。");
        }

        [Test]
        public void E07_PyroOntoHydro_TriggersForwardVaporizeAndConsumesHalfGauge()
        {
            system.Apply(Request(Cfg.ElementType.Hydro, Cfg.GaugeStrength.Medium, policy: Cfg.IcdPolicy.None));
            Assert.That(GaugeOf(Cfg.AuraKey.Hydro), Is.EqualTo(1.6f).Within(0.0001f));

            ReactionResult result = system.Apply(Request(Cfg.ElementType.Pyro, policy: Cfg.IcdPolicy.None));

            Assert.That(result.Row.ReactionId, Is.EqualTo("VaporizeForward"));
            Assert.That(result.Row.Multiplier, Is.EqualTo(2.0f).Within(0.0001f));
            Assert.That(result.ConsumedGauge, Is.EqualTo(0.4f).Within(0.0001f), "正向蒸发消耗系数 0.5，0.8 × 0.5 = 0.4。");
            Assert.That(GaugeOf(Cfg.AuraKey.Hydro), Is.EqualTo(1.2f).Within(0.0001f));
        }

        // ---------- E-08：剧变 ----------

        [Test]
        public void E08_PyroOntoElectro_TriggersOverloadedWithPyroDamage()
        {
            system.Apply(Request(Cfg.ElementType.Electro, policy: Cfg.IcdPolicy.None));

            ReactionResult result = system.Apply(Request(Cfg.ElementType.Pyro, policy: Cfg.IcdPolicy.None));

            Assert.That(result.Row.ReactionId, Is.EqualTo("OverloadedPyro"));
            Assert.That(result.Row.Category, Is.EqualTo(Cfg.ReactionCategory.Transformative));
            Assert.That(result.Row.DamageElement, Is.EqualTo(Cfg.ElementType.Pyro));
            Assert.That(result.Row.EffectId, Is.EqualTo("Eff_Overloaded_Knockback"));
        }

        // ---------- E-11：扩散 ----------

        [Test]
        public void E11_AnemoOntoPyro_SwirlsAndLeavesNoAnemoAura()
        {
            system.Apply(Request(Cfg.ElementType.Pyro, policy: Cfg.IcdPolicy.None));

            ReactionResult result = system.Apply(Request(Cfg.ElementType.Anemo, policy: Cfg.IcdPolicy.None));

            Assert.That(result.Row.ReactionId, Is.EqualTo("SwirlPyro"));
            Assert.That(GaugeOf(Cfg.AuraKey.Anemo), Is.Zero, "风只作为触发方，自身不留存附着。");
            Assert.That(GaugeOf(Cfg.AuraKey.Pyro), Is.EqualTo(0.4f).Within(0.0001f), "扩散消耗系数 0.5。");
        }

        [Test]
        public void GeoApplication_LeavesNoAura()
        {
            system.Apply(Request(Cfg.ElementType.Geo, policy: Cfg.IcdPolicy.None));
            Assert.That(GaugeOf(Cfg.AuraKey.Geo), Is.Zero, "岩与风同样不留存附着。");
        }

        // ---------- RM-02：优先级 ----------

        [Test]
        public void RM02_WithHydroAndElectro_PyroPrefersVaporizeOverOverloaded()
        {
            system.Apply(Request(Cfg.ElementType.Hydro, Cfg.GaugeStrength.Medium, policy: Cfg.IcdPolicy.None));
            system.Apply(Request(Cfg.ElementType.Electro, Cfg.GaugeStrength.Medium, policy: Cfg.IcdPolicy.None));
            Assert.That(GaugeOf(Cfg.AuraKey.Hydro), Is.GreaterThan(0f));
            Assert.That(GaugeOf(Cfg.AuraKey.Electro), Is.GreaterThan(0f));

            ReactionResult result = system.Apply(Request(Cfg.ElementType.Pyro, policy: Cfg.IcdPolicy.None));

            Assert.That(result.Row.ReactionId, Is.EqualTo("VaporizeForward"), "蒸发优先级 100 高于超载 90。");
            Assert.That(GaugeOf(Cfg.AuraKey.Electro), Is.GreaterThan(0f), "单次攻击只触发一种反应，雷附着不受影响。");
        }

        // ---------- 感电：不一次性消耗，双方共存 ----------

        [Test]
        public void ElectroCharged_KeepsBothAurasAlive()
        {
            system.Apply(Request(Cfg.ElementType.Hydro, Cfg.GaugeStrength.Medium, policy: Cfg.IcdPolicy.None));

            ReactionResult result = system.Apply(Request(Cfg.ElementType.Electro, Cfg.GaugeStrength.Medium, policy: Cfg.IcdPolicy.None));

            Assert.That(result.Row.ReactionId, Is.EqualTo("ElectroChargedElectro"));
            Assert.That(result.ConsumedGauge, Is.Zero, "感电不一次性消耗附着。");
            Assert.That(GaugeOf(Cfg.AuraKey.Hydro), Is.GreaterThan(0f));
            Assert.That(GaugeOf(Cfg.AuraKey.Electro), Is.GreaterThan(0f), "触发元素未被消耗，因此同时写入附着。");
        }

        // ---------- 无附着与查询 ----------

        [Test]
        public void Apply_OnCleanTarget_TriggersNothing()
        {
            ReactionResult result = system.Apply(Request(Cfg.ElementType.Pyro, policy: Cfg.IcdPolicy.None));

            Assert.That(result.Triggered, Is.False);
            Assert.That(GaugeOf(Cfg.AuraKey.Pyro), Is.EqualTo(0.8f).Within(0.0001f));
        }

        [Test]
        public void ClearAura_RemovesEverythingOnTarget()
        {
            system.Apply(Request(Cfg.ElementType.Pyro, policy: Cfg.IcdPolicy.None));
            system.Apply(Request(Cfg.ElementType.Cryo, policy: Cfg.IcdPolicy.None));

            system.ClearAura(Target);

            Assert.That(system.QueryAura(Target).Auras.Count, Is.Zero);
        }


        // ---------- 伪元素状态机 ----------

        /// <summary>水冰相遇写入冻结，时长按 04 第 5.6 节的闭式解 2√(5g+4)−4 求得。</summary>
        [Test]
        public void Freeze_WritesStateWithClosedFormDuration()
        {
            system.Apply(Request(Cfg.ElementType.Cryo, Cfg.GaugeStrength.Strong, policy: Cfg.IcdPolicy.None));
            ReactionResult result = system.Apply(Request(Cfg.ElementType.Hydro, Cfg.GaugeStrength.Weak, policy: Cfg.IcdPolicy.None));

            Assert.That(result.Triggered, Is.True);
            Assert.That(result.Row.ReactionId, Is.EqualTo("FrozenHydro"));

            // 冻结附着量取双方较小的一份：冰 3.2U 与本次水 0.8U 中的 0.8U。
            float expected = 2f * Mathf.Sqrt(5f * 0.8f + 4f) - 4f;
            Assert.That(GaugeOf(Cfg.AuraKey.Frozen), Is.EqualTo(expected).Within(0.0001f));
        }

        /// <summary>冻结把两侧较小的那份附着合并进状态，因此水与冰都要扣掉它。</summary>
        [Test]
        public void Freeze_ConsumesTheSmallerGaugeFromBothSides()
        {
            system.Apply(Request(Cfg.ElementType.Cryo, Cfg.GaugeStrength.Strong, policy: Cfg.IcdPolicy.None));
            system.Apply(Request(Cfg.ElementType.Hydro, Cfg.GaugeStrength.Weak, policy: Cfg.IcdPolicy.None));

            // 冰 3.2U 扣掉合并的 0.8U 后剩 2.4U；水 0.8U 全部并入冻结，不再留存。
            Assert.That(GaugeOf(Cfg.AuraKey.Cryo), Is.EqualTo(2.4f).Within(0.0001f));
            Assert.That(GaugeOf(Cfg.AuraKey.Hydro), Is.Zero);
        }

        /// <summary>冻结状态按剩余秒数倒数，到期后自行消失。</summary>
        [Test]
        public void Freeze_ExpiresAfterItsDuration()
        {
            system.Apply(Request(Cfg.ElementType.Cryo, Cfg.GaugeStrength.Strong, policy: Cfg.IcdPolicy.None));
            system.Apply(Request(Cfg.ElementType.Hydro, Cfg.GaugeStrength.Weak, policy: Cfg.IcdPolicy.None));
            float duration = GaugeOf(Cfg.AuraKey.Frozen);

            system.OnUpdate(duration - 0.1f);
            Assert.That(GaugeOf(Cfg.AuraKey.Frozen), Is.GreaterThan(0f));

            system.OnUpdate(0.2f);
            Assert.That(GaugeOf(Cfg.AuraKey.Frozen), Is.Zero);
        }

        /// <summary>
        /// 碎冰命中冻结目标时把状态整条解除，而不是按比例扣秒数。
        /// 状态条存的是剩余时间而非附着量，按比例扣没有意义。
        /// </summary>
        [Test]
        public void Shattered_RemovesTheFrozenStateEntirely()
        {
            system.Apply(Request(Cfg.ElementType.Cryo, Cfg.GaugeStrength.Strong, policy: Cfg.IcdPolicy.None));
            system.Apply(Request(Cfg.ElementType.Hydro, Cfg.GaugeStrength.Weak, policy: Cfg.IcdPolicy.None));

            ReactionResult result = system.Apply(Request(Cfg.ElementType.Physical, Cfg.GaugeStrength.None, policy: Cfg.IcdPolicy.None));

            Assert.That(result.Row.ReactionId, Is.EqualTo("Shattered"));
            Assert.That(GaugeOf(Cfg.AuraKey.Frozen), Is.Zero);
        }

        /// <summary>草雷相遇写入原激化，且按 04 第 5.4 节不完全消耗双方附着。</summary>
        [Test]
        public void Quicken_WritesStateAndKeepsBothAuras()
        {
            system.Apply(Request(Cfg.ElementType.Dendro, Cfg.GaugeStrength.Weak, policy: Cfg.IcdPolicy.None));
            ReactionResult result = system.Apply(Request(Cfg.ElementType.Electro, Cfg.GaugeStrength.Strong, policy: Cfg.IcdPolicy.None));

            Assert.That(result.Row.ReactionId, Is.EqualTo("QuickenElectro"));
            Assert.That(GaugeOf(Cfg.AuraKey.Quickened), Is.GreaterThan(0f));
            Assert.That(GaugeOf(Cfg.AuraKey.Dendro), Is.EqualTo(0.8f).Within(0.0001f), "原激化不消耗草附着。");
            Assert.That(GaugeOf(Cfg.AuraKey.Electro), Is.EqualTo(3.2f).Within(0.0001f), "原激化不消耗触发元素。");
        }

        /// <summary>原激化时长按触发时较小的附着量在配表区间内插值。</summary>
        [Test]
        public void Quicken_ScalesDurationByTheSmallerGauge()
        {
            system.Apply(Request(Cfg.ElementType.Dendro, Cfg.GaugeStrength.Weak, policy: Cfg.IcdPolicy.None));
            system.Apply(Request(Cfg.ElementType.Electro, Cfg.GaugeStrength.Strong, policy: Cfg.IcdPolicy.None));
            float weakPaired = GaugeOf(Cfg.AuraKey.Quickened);

            system.ClearAura(Target);
            system.Apply(Request(Cfg.ElementType.Dendro, Cfg.GaugeStrength.Strong, policy: Cfg.IcdPolicy.None));
            system.Apply(Request(Cfg.ElementType.Electro, Cfg.GaugeStrength.Strong, policy: Cfg.IcdPolicy.None));
            float strongPaired = GaugeOf(Cfg.AuraKey.Quickened);

            Assert.That(strongPaired, Is.GreaterThan(weakPaired));

            Cfg.PseudoElementStateRow row = Core.Config.Tables.TbPseudoElementState.Get(Cfg.AuraKey.Quickened);
            Assert.That(weakPaired, Is.EqualTo(row.MinDurationSeconds + (row.MaxDurationSeconds - row.MinDurationSeconds) * (0.8f / row.ReferenceGauge)).Within(0.0001f));
            Assert.That(strongPaired, Is.EqualTo(row.MaxDurationSeconds).Within(0.0001f), "附着量超过参考值时取上限，不外推。");
        }

        /// <summary>
        /// 原激化状态下雷伤触发超激化，且激化不消耗状态——一次原激化可以支撑多次激化。
        /// 需要草附着先衰减干净，否则 `QuickenElectro`（87）会压过 `Aggravate`（86）。
        /// </summary>
        [Test]
        public void Aggravate_TriggersOnQuickenedStateAndDoesNotConsumeIt()
        {
            system.Apply(Request(Cfg.ElementType.Dendro, Cfg.GaugeStrength.Weak, policy: Cfg.IcdPolicy.None));
            system.Apply(Request(Cfg.ElementType.Electro, Cfg.GaugeStrength.Strong, policy: Cfg.IcdPolicy.None));

            system.OnUpdate(10f);
            Assert.That(GaugeOf(Cfg.AuraKey.Dendro), Is.Zero);

            ReactionResult first = system.Apply(Request(Cfg.ElementType.Electro, Cfg.GaugeStrength.None, policy: Cfg.IcdPolicy.None));
            ReactionResult second = system.Apply(Request(Cfg.ElementType.Electro, Cfg.GaugeStrength.None, policy: Cfg.IcdPolicy.None));

            Assert.That(first.Row.ReactionId, Is.EqualTo("Aggravate"));
            Assert.That(second.Row.ReactionId, Is.EqualTo("Aggravate"));
            Assert.That(GaugeOf(Cfg.AuraKey.Quickened), Is.GreaterThan(0f));
        }

        /// <summary>
        /// 原激化状态下草伤触发蔓激化，与超激化共用同一个状态。
        /// 两侧都用弱档，使草与雷附着都在 9.5 秒耗尽——雷附着还在时
        /// `QuickenDendro`（87）会压过 `Spread`（86）。
        /// </summary>
        [Test]
        public void Spread_TriggersOnQuickenedStateWithDendroTrigger()
        {
            system.Apply(Request(Cfg.ElementType.Dendro, Cfg.GaugeStrength.Weak, policy: Cfg.IcdPolicy.None));
            system.Apply(Request(Cfg.ElementType.Electro, Cfg.GaugeStrength.Weak, policy: Cfg.IcdPolicy.None));
            system.OnUpdate(10f);
            Assert.That(GaugeOf(Cfg.AuraKey.Electro), Is.Zero);

            ReactionResult result = system.Apply(Request(Cfg.ElementType.Dendro, Cfg.GaugeStrength.None, policy: Cfg.IcdPolicy.None));

            Assert.That(result.Row.ReactionId, Is.EqualTo("Spread"));
            Assert.That(result.Row.Category, Is.EqualTo(Cfg.ReactionCategory.Catalyze));
        }

        /// <summary>水草相遇写入草原核，双方附着保留。</summary>
        [Test]
        public void Bloom_WritesDendroCoreAndKeepsBothAuras()
        {
            system.Apply(Request(Cfg.ElementType.Hydro, Cfg.GaugeStrength.Weak, policy: Cfg.IcdPolicy.None));
            ReactionResult result = system.Apply(Request(Cfg.ElementType.Dendro, Cfg.GaugeStrength.Weak, policy: Cfg.IcdPolicy.None));

            Assert.That(result.Row.ReactionId, Is.EqualTo("BloomDendro"));
            Assert.That(GaugeOf(Cfg.AuraKey.DendroCore), Is.EqualTo(Core.Config.Tables.TbPseudoElementState.Get(Cfg.AuraKey.DendroCore).MinDurationSeconds).Within(0.0001f));
            Assert.That(GaugeOf(Cfg.AuraKey.Hydro), Is.EqualTo(0.8f).Within(0.0001f));
            Assert.That(GaugeOf(Cfg.AuraKey.Dendro), Is.EqualTo(0.8f).Within(0.0001f));
        }

        /// <summary>雷伤命中草原核触发超绽放，并把核消耗掉。</summary>
        [Test]
        public void Hyperbloom_ConsumesTheDendroCore()
        {
            system.Apply(Request(Cfg.ElementType.Hydro, Cfg.GaugeStrength.Weak, policy: Cfg.IcdPolicy.None));
            system.Apply(Request(Cfg.ElementType.Dendro, Cfg.GaugeStrength.Weak, policy: Cfg.IcdPolicy.None));

            ReactionResult result = system.Apply(Request(Cfg.ElementType.Electro, Cfg.GaugeStrength.None, policy: Cfg.IcdPolicy.None));

            Assert.That(result.Row.ReactionId, Is.EqualTo("Hyperbloom"));
            Assert.That(result.Row.Category, Is.EqualTo(Cfg.ReactionCategory.Transformative));
            Assert.That(GaugeOf(Cfg.AuraKey.DendroCore), Is.Zero);
        }

        /// <summary>
        /// 火伤命中草原核触发烈绽放，同样消耗核。
        ///
        /// 绽放保留双方附着，而火打水的蒸发（100）优先于烈绽放（98），
        /// 因此先让水附着几乎耗尽再触发绽放，再把残量衰减干净。这正是原神把草原核做成
        /// 独立实体而非目标身上一层状态的原因，见 README 的职责边界说明。
        /// </summary>
        [Test]
        public void Burgeon_ConsumesTheDendroCore()
        {
            system.Apply(Request(Cfg.ElementType.Hydro, Cfg.GaugeStrength.Weak, policy: Cfg.IcdPolicy.None));
            system.OnUpdate(9.4f);
            system.Apply(Request(Cfg.ElementType.Dendro, Cfg.GaugeStrength.Weak, policy: Cfg.IcdPolicy.None));
            system.OnUpdate(0.2f);
            Assert.That(GaugeOf(Cfg.AuraKey.Hydro), Is.Zero);
            Assert.That(GaugeOf(Cfg.AuraKey.DendroCore), Is.GreaterThan(0f));

            ReactionResult result = system.Apply(Request(Cfg.ElementType.Pyro, Cfg.GaugeStrength.None, policy: Cfg.IcdPolicy.None));

            Assert.That(result.Row.ReactionId, Is.EqualTo("Burgeon"));
            Assert.That(GaugeOf(Cfg.AuraKey.DendroCore), Is.Zero);
        }

        /// <summary>重复触发同一状态取更长的剩余时间，短的那次不会把已有状态缩短。</summary>
        [Test]
        public void PseudoState_RefreshTakesTheLongerRemainingTime()
        {
            system.Apply(Request(Cfg.ElementType.Dendro, Cfg.GaugeStrength.Strong, policy: Cfg.IcdPolicy.None));
            system.Apply(Request(Cfg.ElementType.Electro, Cfg.GaugeStrength.Strong, policy: Cfg.IcdPolicy.None));
            float longDuration = GaugeOf(Cfg.AuraKey.Quickened);

            // 再用弱档草触发一次：它本身只够撑较短的时间，不应削掉现有状态。
            system.Apply(Request(Cfg.ElementType.Dendro, Cfg.GaugeStrength.Weak, policy: Cfg.IcdPolicy.None));

            Assert.That(GaugeOf(Cfg.AuraKey.Quickened), Is.EqualTo(longDuration).Within(0.0001f));
        }

        [Test]
        public void QueryAura_OnUnknownEntity_ReturnsEmptySnapshot()
        {
            Assert.That(system.QueryAura(999).Auras, Is.Null);
        }
    }
}
