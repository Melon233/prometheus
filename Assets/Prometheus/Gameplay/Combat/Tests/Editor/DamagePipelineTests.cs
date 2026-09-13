using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using Xuan.Prometheus.Component;
using Xuan.Prometheus.Elements;
using Cfg = global::Prometheus.Config;

namespace Xuan.Prometheus.Combat.Tests
{
    /// <summary>
    /// 验证 `DamagePipeline` 把元素系统、反应公式与伤害公式串成一条路径时，
    /// 选槽、乘区归属与反应分类都落在 Docs/Design/Combat/04 与 05 规定的位置上。
    ///
    /// 用例走**真实导出的配表**，因此同时校验了反应矩阵内容与接线行为的一致性。
    /// </summary>
    public sealed class DamagePipelineTests
    {
        /// <summary>攻击方属性面板。</summary>
        private PropertyComponent attacker;
        /// <summary>目标属性面板。</summary>
        private PropertyComponent target;
        /// <summary>攻击方只读配置。</summary>
        private PropertyConfig attackerConfig;
        /// <summary>目标只读配置。</summary>
        private PropertyConfig targetConfig;
        /// <summary>测试独占的配表 Kit。</summary>
        private ConfigKit configKit;
        /// <summary>测试独占的元素系统。</summary>
        private ElementSystem elementSystem;

        /// <summary>攻击方实体编号。</summary>
        private const int AttackerId = 1;
        /// <summary>目标实体编号。</summary>
        private const int TargetId = 2;
        /// <summary>同级攻防下的防御区常数 `(1+100) / ((1+100) + (1+100))`。</summary>
        private const float SameLevelDefense = 0.5f;

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
            elementSystem = new ElementSystem();
            elementSystem.AfterNew();

            attackerConfig = ScriptableObject.CreateInstance<PropertyConfig>();
            targetConfig = ScriptableObject.CreateInstance<PropertyConfig>();
            attackerConfig.hp = 100f;
            targetConfig.hp = 100f;
            attacker = new PropertyComponent();
            target = new PropertyComponent();
            attacker.Initialize(attackerConfig);
            target.Initialize(targetConfig);
            // 暴击会让每个用例都要额外解释一次乘区，默认关掉；需要暴击的用例自己打开。
            attacker.SetBaseValue(PropertyType.CritRate, 0f);
            attacker.SetBaseValue(PropertyType.CritDmg, 0f);
        }

        [TearDown]
        public void TearDown()
        {
            elementSystem?.Dispose();
            elementSystem = null;
            configKit?.Dispose();
            configKit = null;
            Core.Config = null;
            Object.DestroyImmediate(attackerConfig);
            Object.DestroyImmediate(targetConfig);
        }

        /// <summary>构造一次伤害请求；默认不附着、不暴击，使每个用例只改动它关心的那一项。</summary>
        private DamageRequest Request(Cfg.ElementType element = Cfg.ElementType.Pyro, float baseDamage = 1000f, Cfg.GaugeStrength strength = Cfg.GaugeStrength.None, DamageActionType actionType = DamageActionType.Skill, float criticalRoll = 1f)
        {
            return new DamageRequest
            {
                Attacker = attacker,
                Target = target,
                AttackerEntityId = AttackerId,
                TargetEntityId = TargetId,
                AttackerLevel = DamagePipeline.DefaultLevel,
                TargetLevel = DamagePipeline.DefaultLevel,
                ActionType = actionType,
                Element = element,
                BaseDamage = baseDamage,
                GaugeStrength = strength,
                IcdPolicy = Cfg.IcdPolicy.None,
                TalentId = "Tests.Talent",
                CriticalRoll = criticalRoll
            };
        }

        /// <summary>给目标挂一层指定元素的附着，用于构造反应场景。</summary>
        private void ApplyAura(Cfg.ElementType element)
        {
            elementSystem.Apply(new ElementApplyRequest
            {
                SourceEntityId = AttackerId,
                TargetEntityId = TargetId,
                TalentId = "Tests.AuraSetup",
                IcdPolicy = Cfg.IcdPolicy.None,
                Element = element,
                Strength = Cfg.GaugeStrength.Strong
            });
        }

        // ---------- 主公式：乘区归属 ----------

        /// <summary>无反应时只有防御区生效，其余乘区保持中性。</summary>
        [Test]
        public void NoReaction_AppliesDefenseZoneOnly()
        {
            DamageResolution resolution = DamagePipeline.Resolve(Request(), elementSystem);
            Assert.That(resolution.Reacted, Is.False);
            Assert.That(resolution.Damage, Is.EqualTo(1000f * SameLevelDefense).Within(0.0001f));
            Assert.That(resolution.TransformativeDamage, Is.Zero);
        }

        /// <summary>
        /// 元素伤害加成按本次伤害的元素选槽：火伤加成只放大火伤，不影响水伤。
        /// 这是 05A 三十余个属性位存在的理由，选错槽在实机里表现为「某个元素的加成永远不生效」。
        /// </summary>
        [Test]
        public void ElementDamageBonus_AppliesOnlyToItsOwnElement()
        {
            attacker.AddModifier(PropertyType.PyroDamageBonus, PropertyModifierMode.Offset, 0.5f);

            float pyro = DamagePipeline.Resolve(Request(Cfg.ElementType.Pyro), elementSystem).Damage;
            float hydro = DamagePipeline.Resolve(Request(Cfg.ElementType.Hydro), elementSystem).Damage;

            Assert.That(pyro, Is.EqualTo(1000f * 1.5f * SameLevelDefense).Within(0.0001f));
            Assert.That(hydro, Is.EqualTo(1000f * SameLevelDefense).Within(0.0001f));
        }

        /// <summary>全伤害加成、元素加成与动作加成同处一个乘区相加，而不是各乘一次。</summary>
        [Test]
        public void DamageBonuses_SumIntoOneZoneInsteadOfMultiplying()
        {
            attacker.AddModifier(PropertyType.AllDamageBonus, PropertyModifierMode.Offset, 0.2f);
            attacker.AddModifier(PropertyType.PyroDamageBonus, PropertyModifierMode.Offset, 0.3f);
            attacker.AddModifier(PropertyType.SkillBonus, PropertyModifierMode.Offset, 0.5f);

            DamageResolution resolution = DamagePipeline.Resolve(Request(actionType: DamageActionType.Skill), elementSystem);

            // 相加是 1 + 1.0 = 2.0；若误写成相乘会得到 1.2 × 1.3 × 1.5 = 2.34。
            Assert.That(resolution.Damage, Is.EqualTo(1000f * 2f * SameLevelDefense).Within(0.0001f));
        }

        /// <summary>动作加成按动作类别选槽：元素战技加成不影响元素爆发。</summary>
        [Test]
        public void ActionBonus_AppliesOnlyToItsOwnAction()
        {
            attacker.AddModifier(PropertyType.SkillBonus, PropertyModifierMode.Offset, 0.5f);

            float skill = DamagePipeline.Resolve(Request(actionType: DamageActionType.Skill), elementSystem).Damage;
            float burst = DamagePipeline.Resolve(Request(actionType: DamageActionType.Ultimate), elementSystem).Damage;

            Assert.That(skill, Is.EqualTo(1000f * 1.5f * SameLevelDefense).Within(0.0001f));
            Assert.That(burst, Is.EqualTo(1000f * SameLevelDefense).Within(0.0001f));
        }

        /// <summary>目标抗性按元素选槽，并先扣除同元素的抗性削减。</summary>
        [Test]
        public void Resistance_SelectsSlotByElementAndSubtractsReduction()
        {
            target.AddModifier(PropertyType.PyroResistance, PropertyModifierMode.Offset, 0.5f);
            target.AddModifier(PropertyType.PyroResistanceReduction, PropertyModifierMode.Offset, 0.2f);

            DamageResolution resolution = DamagePipeline.Resolve(Request(Cfg.ElementType.Pyro), elementSystem);

            // 最终抗性 0.3 落在中段，抗性区 = 1 - 0.3。
            Assert.That(resolution.Damage, Is.EqualTo(1000f * SameLevelDefense * 0.7f).Within(0.0001f));
        }

        /// <summary>暴击由调用方掷骰决定，掷骰值严格小于暴击率才算暴击。</summary>
        [Test]
        public void CriticalRoll_IsDecidedByCallerNotByPipeline()
        {
            attacker.SetBaseValue(PropertyType.CritRate, 0.5f);
            attacker.SetBaseValue(PropertyType.CritDmg, 1f);

            DamageResolution hit = DamagePipeline.Resolve(Request(criticalRoll: 0.49f), elementSystem);
            DamageResolution miss = DamagePipeline.Resolve(Request(criticalRoll: 0.5f), elementSystem);

            Assert.That(hit.IsCritical, Is.True);
            Assert.That(hit.Damage, Is.EqualTo(1000f * 2f * SameLevelDefense).Within(0.0001f));
            Assert.That(miss.IsCritical, Is.False);
            Assert.That(miss.Damage, Is.EqualTo(1000f * SameLevelDefense).Within(0.0001f));
        }

        // ---------- 反应：四个类别各进各的乘区 ----------

        /// <summary>火打水是正向蒸发，增幅倍率 2.0 写进独立乘区。</summary>
        [Test]
        public void ForwardVaporize_MultipliesAsIndependentZone()
        {
            ApplyAura(Cfg.ElementType.Hydro);

            DamageResolution resolution = DamagePipeline.Resolve(Request(Cfg.ElementType.Pyro), elementSystem);

            Assert.That(resolution.Reacted, Is.True);
            Assert.That(resolution.Reaction.ReactionId, Is.EqualTo("VaporizeForward"));
            Assert.That(resolution.Damage, Is.EqualTo(1000f * 2f * SameLevelDefense).Within(0.0001f));
        }

        /// <summary>水打火是逆向蒸发，倍率 1.5；方向是顺序敏感的，不能与正向合并成一行配表。</summary>
        [Test]
        public void ReverseVaporize_UsesLowerMultiplierThanForward()
        {
            ApplyAura(Cfg.ElementType.Pyro);

            DamageResolution resolution = DamagePipeline.Resolve(Request(Cfg.ElementType.Hydro), elementSystem);

            Assert.That(resolution.Reaction.ReactionId, Is.EqualTo("VaporizeReverse"));
            Assert.That(resolution.Damage, Is.EqualTo(1000f * 1.5f * SameLevelDefense).Within(0.0001f));
        }

        /// <summary>元素精通放大增幅倍率，走 `2.78 × EM / (EM + 1400)` 这一项。</summary>
        [Test]
        public void ElementalMastery_ScalesAmplifyingReaction()
        {
            attacker.SetBaseValue(PropertyType.ElementalMastery, 200f);
            ApplyAura(Cfg.ElementType.Hydro);

            DamageResolution resolution = DamagePipeline.Resolve(Request(Cfg.ElementType.Pyro), elementSystem);

            float expectedAmplify = ReactionFormula.Amplifying(2f, 200f, 0f);
            Assert.That(resolution.Damage, Is.EqualTo(1000f * expectedAmplify * SameLevelDefense).Within(0.0001f));
        }

        /// <summary>反应专属加成按配表的 reactionBonusAttr 列选槽，只影响该反应。</summary>
        [Test]
        public void ReactionBonus_SelectsSlotByConfiguredAttributeName()
        {
            attacker.AddModifier(PropertyType.VaporizeBonus, PropertyModifierMode.Offset, 0.15f);
            ApplyAura(Cfg.ElementType.Hydro);

            DamageResolution resolution = DamagePipeline.Resolve(Request(Cfg.ElementType.Pyro), elementSystem);

            Assert.That(resolution.Damage, Is.EqualTo(1000f * ReactionFormula.Amplifying(2f, 0f, 0.15f) * SameLevelDefense).Within(0.0001f));
        }

        /// <summary>
        /// 剧变是独立的第二笔伤害：主伤害不被放大，剧变本身也不吃伤害加成、不吃暴击、不吃防御区。
        /// </summary>
        [Test]
        public void TransformativeReaction_SettlesAsSeparateDamageOutsideMainFormula()
        {
            attacker.AddModifier(PropertyType.AllDamageBonus, PropertyModifierMode.Offset, 1f);
            attacker.SetBaseValue(PropertyType.CritRate, 1f);
            attacker.SetBaseValue(PropertyType.CritDmg, 1f);
            ApplyAura(Cfg.ElementType.Pyro);

            DamageResolution resolution = DamagePipeline.Resolve(Request(Cfg.ElementType.Electro, criticalRoll: 0f), elementSystem);

            Assert.That(resolution.Reaction.Category, Is.EqualTo(Cfg.ReactionCategory.Transformative));
            Assert.That(resolution.TransformativeDamage, Is.GreaterThan(0f));
            Assert.That(resolution.TransformativeElement, Is.EqualTo(resolution.Reaction.DamageElement));

            // 主伤害照常吃增伤与暴击，说明剧变没有污染主公式。
            Assert.That(resolution.Damage, Is.EqualTo(1000f * 2f * 2f * SameLevelDefense).Within(0.0001f));

            // 剧变只由等级系数、反应倍率、精通与抗性决定，与上面的增伤和暴击无关。
            float levelCoefficient = Core.Config.Tables.TbReactionLevelCoefficient.Get(DamagePipeline.DefaultLevel).Coefficient;
            Assert.That(resolution.TransformativeDamage, Is.EqualTo(levelCoefficient * resolution.Reaction.Multiplier).Within(0.0001f));
        }

        /// <summary>剧变额外吃全剧变加成，且它与该反应的专属加成相加而非相乘。</summary>
        [Test]
        public void TransformativeReaction_AddsAllTransformativeBonusToItsOwnBonus()
        {
            attacker.AddModifier(PropertyType.AllTransformativeBonus, PropertyModifierMode.Offset, 0.2f);
            attacker.AddModifier(PropertyType.OverloadedBonus, PropertyModifierMode.Offset, 0.3f);
            ApplyAura(Cfg.ElementType.Pyro);

            DamageResolution resolution = DamagePipeline.Resolve(Request(Cfg.ElementType.Electro), elementSystem);

            float levelCoefficient = Core.Config.Tables.TbReactionLevelCoefficient.Get(DamagePipeline.DefaultLevel).Coefficient;
            float expected = levelCoefficient * resolution.Reaction.Multiplier * ReactionFormula.TransformativeScale(0f, 0.5f);
            Assert.That(resolution.TransformativeDamage, Is.EqualTo(expected).Within(0.0001f));
        }

        /// <summary>
        /// 激化是加算而不是乘区：它并入基础伤害，随后与基础伤害一起吃增伤。
        ///
        /// 构造超激化需要目标「带原激化状态但不带草附着」——草附着还在时
        /// `QuickenElectro`（优先级 87）会压过 `Aggravate`（86），因为一次命中只触发一种反应。
        /// </summary>
        [Test]
        public void CatalyzeReaction_AddsIntoBaseDamageBeforeBonusZone()
        {
            attacker.AddModifier(PropertyType.AllDamageBonus, PropertyModifierMode.Offset, 1f);

            // 草雷相遇写入原激化：草用弱档，使它先于原激化状态衰减干净。
            elementSystem.Apply(new ElementApplyRequest
            {
                SourceEntityId = AttackerId, TargetEntityId = TargetId, TalentId = "Tests.Dendro",
                IcdPolicy = Cfg.IcdPolicy.None, Element = Cfg.ElementType.Dendro, Strength = Cfg.GaugeStrength.Weak
            });
            elementSystem.Apply(new ElementApplyRequest
            {
                SourceEntityId = AttackerId, TargetEntityId = TargetId, TalentId = "Tests.Quicken",
                IcdPolicy = Cfg.IcdPolicy.None, Element = Cfg.ElementType.Electro, Strength = Cfg.GaugeStrength.Strong
            });
            Assert.That(elementSystem.QueryAura(TargetId).Auras.TryGet(Cfg.AuraKey.Quickened, out _), Is.True, "草雷相遇必须写入原激化状态。");

            // 弱档草附着 9.5 秒耗尽；原激化按 0.8U 插值得 15 秒，此刻仍在。
            elementSystem.OnUpdate(10f);
            Assert.That(elementSystem.QueryAura(TargetId).Auras.TryGet(Cfg.AuraKey.Dendro, out _), Is.False);
            Assert.That(elementSystem.QueryAura(TargetId).Auras.TryGet(Cfg.AuraKey.Quickened, out _), Is.True);

            DamageResolution resolution = DamagePipeline.Resolve(Request(Cfg.ElementType.Electro), elementSystem);

            Assert.That(resolution.Reaction.ReactionId, Is.EqualTo("Aggravate"));
            Assert.That(resolution.Reaction.Category, Is.EqualTo(Cfg.ReactionCategory.Catalyze));

            // 激化值并入基础伤害后，与基础伤害一起吃 ×2 的增伤——
            // 若误写成独立乘区，结果会是 1000 × 2 + 激化值。
            float levelCoefficient = Core.Config.Tables.TbReactionLevelCoefficient.Get(DamagePipeline.DefaultLevel).Coefficient;
            float catalyze = ReactionFormula.Catalyze(levelCoefficient, resolution.Reaction.Multiplier, 0f, 0f);
            Assert.That(resolution.Damage, Is.EqualTo((1000f + catalyze) * 2f * SameLevelDefense).Within(0.0001f));
            Assert.That(resolution.TransformativeDamage, Is.Zero, "激化不产生独立伤害。");
        }

        /// <summary>
        /// 特殊类反应不产出即时伤害，只透出产物 Effect 标识。
        /// 结晶护盾、冻结与草原核各有独立生命周期，把它们折成一个数字会丢掉这些语义。
        /// </summary>
        [Test]
        public void SpecialReaction_ProducesEffectIdInsteadOfDamage()
        {
            ApplyAura(Cfg.ElementType.Pyro);

            DamageResolution resolution = DamagePipeline.Resolve(Request(Cfg.ElementType.Geo), elementSystem);

            Assert.That(resolution.Reaction.Category, Is.EqualTo(Cfg.ReactionCategory.Special));
            Assert.That(resolution.ProductEffectId, Is.EqualTo("Eff_Crystallize"));
            Assert.That(resolution.TransformativeDamage, Is.Zero);
            Assert.That(resolution.Damage, Is.EqualTo(1000f * SameLevelDefense).Within(0.0001f));
        }

        // ---------- 附魔 ----------

        /// <summary>
        /// 附魔把普攻的元素从物理改写为指定元素，因此选槽与反应判定都跟着改变。
        /// </summary>
        [Test]
        public void ElementInfusion_ChangesNormalAttackElementAndItsReaction()
        {
            attackerConfig.elementAttribute = Cfg.ElementType.Physical;
            Assert.That(attacker.ResolveDamageElement(DamageActionType.NormalAttack), Is.EqualTo(Cfg.ElementType.Physical));

            attacker.AddElementInfusion(Cfg.ElementType.Pyro, DamageActionMask.NormalAttack, 1);
            Assert.That(attacker.ResolveDamageElement(DamageActionType.NormalAttack), Is.EqualTo(Cfg.ElementType.Pyro));

            ApplyAura(Cfg.ElementType.Hydro);
            DamageRequest request = Request(attacker.ResolveDamageElement(DamageActionType.NormalAttack), actionType: DamageActionType.NormalAttack);
            DamageResolution resolution = DamagePipeline.Resolve(request, elementSystem);

            Assert.That(resolution.Element, Is.EqualTo(Cfg.ElementType.Pyro));
            Assert.That(resolution.Reaction.ReactionId, Is.EqualTo("VaporizeForward"));
        }

        // ---------- 结算事实与拆解 ----------

        /// <summary>
        /// 暴击结果必须随结算一起留在 resolution 上：它是「暴击时触发」这类被动的唯一依据，
        /// 算完就丢会让整类词条无法实现。
        /// </summary>
        [Test]
        public void Resolution_CarriesCriticalOutcome()
        {
            attacker.SetBaseValue(PropertyType.CritRate, 0.5f);
            attacker.SetBaseValue(PropertyType.CritDmg, 1f);

            Assert.That(DamagePipeline.Resolve(Request(criticalRoll: 0.1f), elementSystem).IsCritical, Is.True);
            Assert.That(DamagePipeline.Resolve(Request(criticalRoll: 0.9f), elementSystem).IsCritical, Is.False);
        }

        /// <summary>
        /// resolution 必须留下喂给计算器的原始输入，使伤害拆解可以用同一批公开函数复算每个乘区。
        /// 拆解若另写一遍算式，会在公式调整后悄悄说谎。
        /// </summary>
        [Test]
        public void Resolution_KeepsTheExactContextItFedToTheCalculator()
        {
            attacker.AddModifier(PropertyType.PyroDamageBonus, PropertyModifierMode.Offset, 0.5f);
            target.AddModifier(PropertyType.PyroResistance, PropertyModifierMode.Offset, 0.2f);

            DamageResolution resolution = DamagePipeline.Resolve(Request(Cfg.ElementType.Pyro), elementSystem);

            Assert.That(resolution.Context.DamageBonus, Is.EqualTo(0.5f).Within(0.0001f));
            Assert.That(resolution.Context.TargetResistance, Is.EqualTo(0.2f).Within(0.0001f));
            // 用保存下来的上下文原样复算，结果必须与结算值一致。
            Assert.That(DamageCalculator.Evaluate(in resolution.Context), Is.EqualTo(resolution.Damage).Within(0.0001f));
        }

        /// <summary>伤害拆解要点出元素、乘区与最终值，供实机校准配表时回答「这一刀为什么是这个数」。</summary>
        [Test]
        public void Breakdown_DescribesEveryActiveZone()
        {
            attacker.AddModifier(PropertyType.PyroDamageBonus, PropertyModifierMode.Offset, 0.5f);
            ApplyAura(Cfg.ElementType.Hydro);

            DamageResolution resolution = DamagePipeline.Resolve(Request(Cfg.ElementType.Pyro), elementSystem);
            string text = DamageBreakdown.Describe(in resolution);

            Assert.That(text, Does.Contain("Pyro"));
            Assert.That(text, Does.Contain("base"));
            Assert.That(text, Does.Contain("bonus"));
            Assert.That(text, Does.Contain("VaporizeForward"));
            Assert.That(text, Does.Contain("def"));
            Assert.That(text, Does.Contain(resolution.Damage.ToString("0.#")));
        }

        /// <summary>中性乘区不出现在拆解里，否则有效信息会被一串 x1.00 淹没。</summary>
        [Test]
        public void Breakdown_OmitsNeutralZones()
        {
            DamageResolution resolution = DamagePipeline.Resolve(Request(), elementSystem);
            string text = DamageBreakdown.Describe(in resolution);

            Assert.That(text, Does.Not.Contain("bonus"));
            Assert.That(text, Does.Not.Contain("crit"));
            Assert.That(text, Does.Not.Contain("res"));
        }

        // ---------- 附着与 ICD ----------

        /// <summary>ICD 挡下的命中不写附着，因此下一次异元素命中不触发反应。</summary>
        [Test]
        public void IcdBlockedHit_DoesNotAttachAndThereforeDoesNotEnableReaction()
        {
            // 共享 ICD 的生效序列是第 1、4、7 次；这里只打两次，让第 2 次落在窗口内被挡下。
            DamageRequest attach = Request(Cfg.ElementType.Hydro, strength: Cfg.GaugeStrength.Weak, actionType: DamageActionType.NormalAttack);
            attach.IcdPolicy = Cfg.IcdPolicy.Shared;
            attach.TalentId = "Tests.NormalAttack";
            DamagePipeline.Resolve(attach, elementSystem);
            Assert.That(elementSystem.QueryAura(TargetId).Auras.TryGet(Cfg.AuraKey.Hydro, out _), Is.True, "第 1 次命中必须写入附着。");

            // 抹掉刚写入的附着，使下一次命中是否附着可以被单独观察。
            elementSystem.ClearAura(TargetId);
            DamagePipeline.Resolve(attach, elementSystem);
            Assert.That(elementSystem.QueryAura(TargetId).Auras.TryGet(Cfg.AuraKey.Hydro, out _), Is.False, "第 2 次命中处于 ICD 窗口内，不得写入附着。");

            DamageResolution resolution = DamagePipeline.Resolve(Request(Cfg.ElementType.Pyro), elementSystem);
            Assert.That(resolution.Reacted, Is.False, "被 ICD 挡下的水命中没有写入附着，随后的火伤不应触发蒸发。");
        }
    }
}
