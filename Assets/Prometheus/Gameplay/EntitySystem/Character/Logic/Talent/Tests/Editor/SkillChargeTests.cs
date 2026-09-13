using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using Xuan.Prometheus.Component;

namespace Xuan.Prometheus.Logic.Talent.Tests
{
    /// <summary>
    /// 元素战技的充能层数与冷却缩减。
    ///
    /// 多段充能是原神里一整类角色的核心手感（连放两段 E 再等回复），
    /// 单层冷却模型表达不了它——本组用例锁的正是「每层独立回复」这条。
    ///
    /// 通过反射写入私有序列化字段，与 `EffectTestConfigurationExtensions` 同一手法：
    /// 正式运行时配置保持只读，不为了测试扩大公开 API。
    /// </summary>
    public sealed class SkillChargeTests
    {
        private TalentConfig config;
        private SkillComponent skill;
        private PropertyConfig propertyConfig;
        private PropertyComponent property;

        private const float Cooldown = 5f;

        [SetUp]
        public void SetUp()
        {
            config = ScriptableObject.CreateInstance<TalentConfig>();
            propertyConfig = ScriptableObject.CreateInstance<PropertyConfig>();
            property = new PropertyComponent();
            property.Initialize(propertyConfig);
            skill = new SkillComponent();
            SetPrivateField(config, "skillCooldown", Cooldown);
            SetPrivateField(skill, "talentConfig", config);
            SetPrivateField(skill, "propertyComponent", property);
        }

        [TearDown]
        public void TearDown()
        {
            Object.DestroyImmediate(config);
            Object.DestroyImmediate(propertyConfig);
        }

        /// <summary>按指定层数重建运行态。</summary>
        private void UseCharges(int chargeCount)
        {
            SetPrivateField(config, "skillChargeCount", chargeCount);
            skill.InitializeRuntimeState();
        }

        /// <summary>写入私有序列化字段。</summary>
        private static void SetPrivateField(object target, string fieldName, object value)
        {
            FieldInfo field = target.GetType().GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(field, Is.Not.Null, $"字段 '{fieldName}' 不存在。");
            field.SetValue(target, value);
        }

        // ---------- 默认与边界 ----------

        /// <summary>
        /// 未配置层数的旧资产按 1 层处理。
        ///
        /// 这个字段晚于已有资产加入，反序列化会得到 0；0 层的语义是「技能永远放不出来」，
        /// 静默禁用一个技能是最难反查的一类失败，因此读取时必须钳到至少 1。
        /// </summary>
        [Test]
        public void ChargeCount_FromLegacyAssetWithZero_FallsBackToOne()
        {
            UseCharges(0);

            Assert.That(skill.MaxCharges, Is.EqualTo(1));
            Assert.That(skill.CurrentCharges, Is.EqualTo(1), "充能技能出场即满层。");
            Assert.That(skill.IsCooldownReady, Is.True);
        }

        // ---------- 单层行为保持不变 ----------

        /// <summary>单层技能释放后进入冷却，冷却结束恢复可用。</summary>
        [Test]
        public void SingleCharge_BehavesLikeAPlainCooldown()
        {
            UseCharges(1);

            skill.BeginCooldown();
            Assert.That(skill.IsCooldownReady, Is.False);
            Assert.That(skill.CooldownRemaining, Is.EqualTo(Cooldown).Within(0.0001f));

            skill.AdvanceCooldown(Cooldown);
            Assert.That(skill.IsCooldownReady, Is.True);
            Assert.That(skill.CurrentCharges, Is.EqualTo(1));
        }

        // ---------- 多层充能 ----------

        /// <summary>多层技能可以连续释放到层数耗尽。</summary>
        [Test]
        public void MultipleCharges_CanBeSpentBackToBack()
        {
            UseCharges(2);

            skill.BeginCooldown();
            Assert.That(skill.CurrentCharges, Is.EqualTo(1));
            Assert.That(skill.IsCooldownReady, Is.True, "还有一层就还能放。");

            skill.BeginCooldown();
            Assert.That(skill.CurrentCharges, Is.Zero);
            Assert.That(skill.IsCooldownReady, Is.False);
        }

        /// <summary>
        /// 消耗第二层时**不重置**已经在走的冷却。
        ///
        /// 重置会让连放两层的玩家比只放一层的玩家更晚拿回第一层——
        /// 多段充能的意义正是「先放掉、边打边回」，重置等于取消了这个收益。
        /// </summary>
        [Test]
        public void SpendingASecondCharge_DoesNotRestartTheRunningCooldown()
        {
            UseCharges(2);

            skill.BeginCooldown();
            skill.AdvanceCooldown(3f);
            float remainingBefore = skill.CooldownRemaining;

            skill.BeginCooldown();

            Assert.That(skill.CooldownRemaining, Is.EqualTo(remainingBefore).Within(0.0001f));
        }

        /// <summary>层数逐个回复，且下一层的计时立刻接上，不需要额外一帧。</summary>
        [Test]
        public void Charges_RecoverOneAtATimeWithoutAGapBetweenThem()
        {
            UseCharges(2);
            skill.BeginCooldown();
            skill.BeginCooldown();
            Assert.That(skill.CurrentCharges, Is.Zero);

            skill.AdvanceCooldown(Cooldown);
            Assert.That(skill.CurrentCharges, Is.EqualTo(1));
            Assert.That(skill.CooldownRemaining, Is.EqualTo(Cooldown).Within(0.0001f), "第二层的计时应当立刻开始。");

            skill.AdvanceCooldown(Cooldown);
            Assert.That(skill.CurrentCharges, Is.EqualTo(2));
            Assert.That(skill.CooldownRemaining, Is.Zero, "满层后不再计时。");
        }

        /// <summary>满层时推进冷却不做任何事，避免空转刷新 HUD。</summary>
        [Test]
        public void AdvanceCooldown_AtFullCharges_DoesNothing()
        {
            UseCharges(2);

            Assert.That(skill.AdvanceCooldown(1f), Is.False);
            Assert.That(skill.CurrentCharges, Is.EqualTo(2));
        }

        // ---------- 冷却缩减 ----------

        /// <summary>冷却缩减按属性面板折算进入冷却时写入的时长。</summary>
        [Test]
        public void CooldownReduction_ShortensTheCooldownWrittenOnCast()
        {
            UseCharges(1);
            property.AddModifier(PropertyType.CooldownReduction, PropertyModifierMode.Offset, 0.2f);

            skill.BeginCooldown();

            Assert.That(skill.CooldownRemaining, Is.EqualTo(Cooldown * 0.8f).Within(0.0001f));
        }

        /// <summary>
        /// 缩减在**进入冷却的那一刻**结算一次，不追溯已经在走的冷却。
        ///
        /// 每帧按当前缩减折算剩余时间的话，冷却期间换上一件减 CD 装备就能立刻缩短本次冷却，
        /// 这是可被玩家利用的行为。
        /// </summary>
        [Test]
        public void CooldownReduction_GainedMidCooldown_DoesNotShortenTheRunningOne()
        {
            UseCharges(1);
            skill.BeginCooldown();
            float remainingBefore = skill.CooldownRemaining;

            property.AddModifier(PropertyType.CooldownReduction, PropertyModifierMode.Offset, 0.5f);

            Assert.That(skill.CooldownRemaining, Is.EqualTo(remainingBefore).Within(0.0001f));
        }

        /// <summary>缩减达到百分之百时没有冷却，但仍然逐层消耗充能。</summary>
        [Test]
        public void CooldownReduction_AtFull_RemovesTheCooldownButStillSpendsCharges()
        {
            UseCharges(2);
            property.AddModifier(PropertyType.CooldownReduction, PropertyModifierMode.Offset, 1f);

            skill.BeginCooldown();

            Assert.That(skill.CooldownRemaining, Is.Zero);
            Assert.That(skill.CurrentCharges, Is.EqualTo(1), "没有冷却不等于不消耗层数。");
        }
    }
}
