using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using Xuan.Prometheus.Component;
using Xuan.Prometheus.Effects;

namespace Xuan.Prometheus.Tests.Architecture
{
    /// <summary>
    /// 钉住会被资产按**整数下标**序列化的枚举布局。
    ///
    /// 这类枚举一旦被插入、删除或重排，已有资产不会报错、不会崩溃——它们会静默读到相邻成员。
    /// 一个在中间插了新属性位的提交，表现是「某些角色的暴击率变成了跳跃速度」，
    /// 而所有测试照常通过、Inspector 照常显示。这是本项目里最难排查的一类改动。
    ///
    /// 因此这里保存一份**声明顺序的黄金名单**：新增成员只能追加到末尾，并同步追加到名单末尾。
    /// 失败信息会直接告诉改动者哪一位错位了。
    /// </summary>
    public sealed class SerializedEnumLayoutTests
    {
        /// <summary>
        /// `PropertyType` 的声明顺序。Effect 资产里的 `PropertyModifierOperation.type` 存的就是这里的下标。
        ///
        /// `Toughness` 已被 `StaggerResistance` 取代但必须保留占位（08 第 2.4 节）：删掉它会让其后
        /// 六十余个成员整体前移一位。
        /// </summary>
        private static readonly string[] PropertyTypeLayout =
        {
            "Atk", "Def", "MoveSpeed", "AtkSpeed",
            "CritRate", "CritDmg", "MaxHp", "AirMoveSpeed",
            "JumpSpeed", "Gravity", "CoreEnergyLimit", "UltEnergyLimit",
            "Toughness", "DamageBoost", "DamageTakenBoost", "ElementalMastery",
            "EnergyRecharge", "HealingBonus", "IncomingHealingBonus", "ShieldStrength",
            "CooldownReduction", "StaminaConsumptionReduction", "ElementalEnergyLimit", "PyroDamageBonus",
            "HydroDamageBonus", "ElectroDamageBonus", "CryoDamageBonus", "DendroDamageBonus",
            "AnemoDamageBonus", "GeoDamageBonus", "PhysicalDamageBonus", "AllDamageBonus",
            "NormalAttackBonus", "ChargedAttackBonus", "PlungeAttackBonus", "SkillBonus",
            "BurstBonus", "PyroResistance", "HydroResistance", "ElectroResistance",
            "CryoResistance", "DendroResistance", "AnemoResistance", "GeoResistance",
            "PhysicalResistance", "PyroResistanceReduction", "HydroResistanceReduction", "ElectroResistanceReduction",
            "CryoResistanceReduction", "DendroResistanceReduction", "AnemoResistanceReduction", "GeoResistanceReduction",
            "PhysicalResistanceReduction", "DefenseReduction", "DefenseIgnore", "DamageReduction",
            "StaggerResistance", "SuperArmor", "Stamina", "VaporizeBonus",
            "MeltBonus", "OverloadedBonus", "SuperconductBonus", "ElectroChargedBonus",
            "SwirlBonus", "ShatteredBonus", "BurningBonus", "BloomBonus",
            "HyperbloomBonus", "BurgeonBonus", "CrystallizeBonus", "AggravateBonus",
            "SpreadBonus", "AllTransformativeBonus",
        };

        /// <summary>`PropertyModifierMode` 的声明顺序；Effect 资产存的是它的下标。</summary>
        private static readonly string[] PropertyModifierModeLayout = { "Boost", "Offset", "Base" };

        /// <summary>
        /// `EffectValueSource` 与 `EffectPropertyValue` 的显式取值。
        /// 它们在数值公式里被序列化，且 `EffectValueSource` 的取值不连续（Property = 8），必须逐个钉。
        /// </summary>
        private static readonly (string Name, int Value)[] EffectValueSourceLayout =
        {
            ("One", 0), ("SignalValue", 1), ("SignalRequestedValue", 2), ("Property", 8),
        };

        /// <summary>`EffectConditionType` 的声明顺序；触发规则资产存的是它的下标。</summary>
        private static readonly string[] EffectConditionTypeLayout =
        {
            "Always", "CasterExists", "TargetExists", "SourceExists",
            "HasAllTags", "HasAnyTags", "LacksAnyTags", "ValueGreaterThan",
            "ValueGreaterThanOrEqual", "DamageElementEquals", "DamageTriggeredReaction", "DamageReactionEquals",
            "DamageWasCritical",
        };

        [Test]
        public void PropertyType_LayoutIsStable()
        {
            AssertDeclarationOrder<PropertyType>(PropertyTypeLayout);
        }

        [Test]
        public void PropertyModifierMode_LayoutIsStable()
        {
            AssertDeclarationOrder<PropertyModifierMode>(PropertyModifierModeLayout);
        }

        [Test]
        public void EffectConditionType_LayoutIsStable()
        {
            AssertDeclarationOrder<EffectConditionType>(EffectConditionTypeLayout);
        }

        [Test]
        public void EffectValueSource_ExplicitValuesAreStable()
        {
            foreach ((string name, int value) in EffectValueSourceLayout)
                Assert.That((int)Enum.Parse(typeof(EffectValueSource), name), Is.EqualTo(value), $"EffectValueSource.{name} 的序列化取值不得改变。");
            Assert.That(Enum.GetNames(typeof(EffectValueSource)).Length, Is.EqualTo(EffectValueSourceLayout.Length), "新增 EffectValueSource 成员后请同步本名单。");
        }

        /// <summary>
        /// 逐位比对枚举的声明顺序与黄金名单，并在失败时直接指出第一处错位。
        /// </summary>
        private static void AssertDeclarationOrder<TEnum>(IReadOnlyList<string> expected) where TEnum : struct, Enum
        {
            string[] actual = Enum.GetNames(typeof(TEnum));
            int shared = Math.Min(actual.Length, expected.Count);
            for (int index = 0; index < shared; index++)
                Assert.That(actual[index], Is.EqualTo(expected[index]),
                    $"{typeof(TEnum).Name} 第 {index} 位应为 '{expected[index]}' 而不是 '{actual[index]}'。" +
                    "该枚举按下标序列化进资产：新增成员只能追加到末尾，并同步追加到本测试的黄金名单。");

            Assert.That(actual.Length, Is.EqualTo(expected.Count),
                $"{typeof(TEnum).Name} 成员数从 {expected.Count} 变为 {actual.Length}；" +
                $"若是追加新成员，请把 {string.Join("、", actual.Skip(expected.Count))} 补进本测试的黄金名单。");
        }
    }
}
