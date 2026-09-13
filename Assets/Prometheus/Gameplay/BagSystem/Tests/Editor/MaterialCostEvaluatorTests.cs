using System;
using System.Collections.Generic;
using NUnit.Framework;

namespace Xuan.Prometheus.Bag.Tests
{
    /// <summary>验证材料消耗校验的纯函数行为：它只回答够不够，且不接触背包、网络与配表。</summary>
    public sealed class MaterialCostEvaluatorTests
    {
        /// <summary>用固定持有量构造查询委托，使每个用例的输入完全可见。</summary>
        private static Func<string, int> Owning(params (string MaterialId, int Count)[] entries)
        {
            Dictionary<string, int> owned = new Dictionary<string, int>();
            foreach ((string materialId, int count) in entries) owned[materialId] = count;
            return id => owned.TryGetValue(id, out int quantity) ? quantity : 0;
        }

        [Test]
        public void Evaluate_WithNoCost_IsSatisfied()
        {
            MaterialCostCheck check = MaterialCostEvaluator.Evaluate(Array.Empty<MaterialCost>(), Owning());

            Assert.That(check.IsSatisfied, Is.True);
            Assert.That(check.Shortages, Is.Empty);
        }

        [Test]
        public void Evaluate_WithExactQuantity_IsSatisfied()
        {
            MaterialCost[] costs = { new MaterialCost("MAT_MORA", 20000) };

            MaterialCostCheck check = MaterialCostEvaluator.Evaluate(costs, Owning(("MAT_MORA", 20000)));

            Assert.That(check.IsSatisfied, Is.True);
        }

        [Test]
        public void Evaluate_WithMissingMaterial_ReportsFullRequirementAsShortage()
        {
            MaterialCost[] costs = { new MaterialCost("MAT_PYRO_GEM_SLIVER", 1) };

            MaterialCostCheck check = MaterialCostEvaluator.Evaluate(costs, Owning());

            Assert.That(check.IsSatisfied, Is.False);
            Assert.That(check.Shortages.Count, Is.EqualTo(1));
            Assert.That(check.Shortages[0].MaterialId, Is.EqualTo("MAT_PYRO_GEM_SLIVER"));
            Assert.That(check.Shortages[0].Owned, Is.EqualTo(0));
            Assert.That(check.Shortages[0].Missing, Is.EqualTo(1));
        }

        [Test]
        public void Evaluate_WithMultipleShortages_ReportsAllOfThem()
        {
            // 界面需要一次性展示"还缺什么"，因此校验必须收集全部缺口而不是遇到第一条就返回。
            MaterialCost[] costs =
            {
                new MaterialCost("MAT_MORA", 20000),
                new MaterialCost("MAT_PYRO_GEM_SLIVER", 1),
                new MaterialCost("MAT_MONSTER_A_1", 3)
            };

            MaterialCostCheck check = MaterialCostEvaluator.Evaluate(costs, Owning(("MAT_MORA", 5000)));

            Assert.That(check.IsSatisfied, Is.False);
            Assert.That(check.Shortages.Count, Is.EqualTo(3));
        }

        [Test]
        public void Evaluate_WithRepeatedMaterial_AccumulatesRequirement()
        {
            // 分两条各自够、合起来不够，是最容易漏判的一种情况。
            MaterialCost[] costs =
            {
                new MaterialCost("MAT_MONSTER_A_1", 3),
                new MaterialCost("MAT_MONSTER_A_1", 3)
            };

            MaterialCostCheck check = MaterialCostEvaluator.Evaluate(costs, Owning(("MAT_MONSTER_A_1", 5)));

            Assert.That(check.IsSatisfied, Is.False);
            Assert.That(check.Shortages.Count, Is.EqualTo(1));
            Assert.That(check.Shortages[0].Required, Is.EqualTo(6));
            Assert.That(check.Shortages[0].Missing, Is.EqualTo(1));
        }

        [Test]
        public void MaterialCost_WithNonPositiveCount_Throws()
        {
            // 零消耗条目是配表错误：它应当从消耗列表中省略，而不是以 0 参与计算。
            Assert.That(() => new MaterialCost("MAT_MORA", 0), Throws.TypeOf<ArgumentOutOfRangeException>());
            Assert.That(() => new MaterialCost("MAT_MORA", -1), Throws.TypeOf<ArgumentOutOfRangeException>());
        }

        [Test]
        public void MaterialCost_WithEmptyId_Throws()
        {
            Assert.That(() => new MaterialCost(string.Empty, 1), Throws.TypeOf<ArgumentException>());
        }
    }
}
