using System;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using Xuan.Prometheus.Logic.Talent;
using Cfg = global::Prometheus.Config;

namespace Xuan.Prometheus.Combat.Tests
{
    /// <summary>
    /// 攻击段落表的索引与数据约定。
    ///
    /// 用例走**真实导出的配表**，因此同时校验索引行为与表内容：
    /// 段落表是普攻共享 ICD、逐段打断等级、逐段附着档位的唯一来源，
    /// 填错一行的后果是那一段「手感不对」而伤害照常，实机里几乎无法反查。
    /// </summary>
    public sealed class AttackSegmentTableTests
    {
        private ConfigKit configKit;
        private AttackSegmentTable table;

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
            table = new AttackSegmentTable(Core.Config.Tables);
        }

        [TearDown]
        public void TearDown()
        {
            configKit?.Dispose();
            configKit = null;
            Core.Config = null;
        }

        /// <summary>段落按 (天赋, 连段, 窗口) 三键定位。</summary>
        [Test]
        public void Get_ResolvesByTalentStageAndWindow()
        {
            Cfg.AttackSegmentRow first = table.Get("Yefa.NormalAttack", 0, 0);
            Cfg.AttackSegmentRow second = table.Get("Yefa.NormalAttack", 1, 0);

            Assert.That(first.StageIndex, Is.Zero);
            Assert.That(second.StageIndex, Is.EqualTo(1));
            Assert.That(second.DamageMultiplier, Is.Not.EqualTo(first.DamageMultiplier), "各段倍率独立配置。");
        }

        /// <summary>
        /// 缺行必须抛出而不是静默跳过。
        ///
        /// 静默跳过的后果是这一段既不附着也不打断、伤害却照常结算，
        /// 实机表现为「某一段手感不对」——这正是最难反查的一类失败。
        /// </summary>
        [Test]
        public void Get_OnMissingSegment_Throws()
        {
            Assert.That(() => table.Get("Yefa.NormalAttack", 99, 0), Throws.InstanceOf<InvalidOperationException>());
            Assert.That(table.TryGet("Yefa.NormalAttack", 99, 0, out _), Is.False);
        }

        /// <summary>
        /// 同一天赋的全部连段共用一个 `talentId`，因此共用一条 ICD。
        ///
        /// 这是原神普攻五段共享 ICD 的行为。若各段用不同标识，每一段都会附着，
        /// 元素反应频率会整体偏高。
        /// </summary>
        [Test]
        public void NormalAttackStages_ShareOneTalentIdAndThereforeOneIcd()
        {
            Assert.That(table.GetStageCount("Yefa.NormalAttack"), Is.GreaterThan(1), "普攻应当配置了多段。");
            for (int stage = 0; stage < table.GetStageCount("Yefa.NormalAttack"); stage++)
            {
                Cfg.AttackSegmentRow row = table.Get("Yefa.NormalAttack", stage, 0);
                Assert.That(row.TalentId, Is.EqualTo("Yefa.NormalAttack"));
                Assert.That(row.IcdPolicy, Is.EqualTo(Cfg.IcdPolicy.Shared), "普攻各段必须共享 ICD。");
            }
        }

        /// <summary>
        /// 打断等级按动作类别分级（08 第 2.1 节）：普攻轻于战技，战技轻于爆发。
        /// 这条锁住的是「分级模型真的被用起来了」，而不是全部段落填同一个值。
        /// </summary>
        [Test]
        public void StaggerLevel_IncreasesFromNormalAttackToBurst()
        {
            int normal = table.Get("Yefa.NormalAttack", 0, 0).StaggerLevel;
            int skill = table.Get("Yefa.Skill", 0, 0).StaggerLevel;
            int burst = table.Get("Yefa.Ultimate", 0, 0).StaggerLevel;

            Assert.That(normal, Is.LessThan(skill));
            Assert.That(skill, Is.LessThan(burst));
        }

        /// <summary>附着档位同样按动作类别递增：普攻弱、战技中、爆发强（04 第 3 节）。</summary>
        [Test]
        public void GaugeStrength_IncreasesFromNormalAttackToBurst()
        {
            Assert.That(table.Get("Yefa.NormalAttack", 0, 0).GaugeStrength, Is.EqualTo(Cfg.GaugeStrength.Weak));
            Assert.That(table.Get("Yefa.Skill", 0, 0).GaugeStrength, Is.EqualTo(Cfg.GaugeStrength.Medium));
            Assert.That(table.Get("Yefa.Ultimate", 0, 0).GaugeStrength, Is.EqualTo(Cfg.GaugeStrength.Strong));
        }

        /// <summary>战技与爆发走角色元素，普攻走物理——附魔在结算时覆盖普攻，这是附魔存在的前提。</summary>
        [Test]
        public void Element_FollowsCharacterOnlyForSkillAndBurst()
        {
            Assert.That(table.Get("Yefa.NormalAttack", 0, 0).Element, Is.EqualTo(Cfg.ElementType.Physical));
            Assert.That(table.Get("Yefa.Skill", 0, 0).Element, Is.EqualTo(Cfg.ElementType.FollowCharacter));
            Assert.That(table.Get("Yefa.Ultimate", 0, 0).Element, Is.EqualTo(Cfg.ElementType.FollowCharacter));
        }

        /// <summary>元素爆发每次命中必附着，不受共享 ICD 门控（04A 第 6 节）。</summary>
        [Test]
        public void Ultimate_BypassesSharedIcd()
        {
            Assert.That(table.Get("Yefa.Ultimate", 0, 0).IcdPolicy, Is.EqualTo(Cfg.IcdPolicy.None));
        }

        /// <summary>敌人攻击与玩家共用同一张表，因此敌人不需要另一套数据通道。</summary>
        [Test]
        public void EnemyAttack_IsConfiguredInTheSameTable()
        {
            Cfg.AttackSegmentRow row = table.Get("Enemy.NormalAttack", 0, 0);

            Assert.That(row.StaggerLevel, Is.GreaterThan(0), "敌人攻击应当能打断玩家。");
            Assert.That(row.GaugeStrength, Is.EqualTo(Cfg.GaugeStrength.None), "史莱姆是物理攻击，不附着元素。");
        }

        /// <summary>每个配置了的天赋都必须至少有一段，否则连段循环会退化成永远打第一段。</summary>
        [Test]
        public void GetStageCount_ReportsConfiguredStagesAndZeroForUnknown()
        {
            Assert.That(table.GetStageCount("Yefa.Skill"), Is.EqualTo(1));
            Assert.That(table.GetStageCount("Nobody.NormalAttack"), Is.Zero);
        }
    }
}
