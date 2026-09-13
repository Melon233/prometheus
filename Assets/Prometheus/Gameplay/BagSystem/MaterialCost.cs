using System;
using System.Collections.Generic;

namespace Xuan.Prometheus
{
    /// <summary>描述一次养成操作需要消耗的一种材料及其数量。</summary>
    public readonly struct MaterialCost
    {
        /// <summary>获取材料标识，对应 `TbMaterial` 的主键。</summary>
        public readonly string MaterialId;

        /// <summary>获取需要消耗的数量。</summary>
        public readonly int Count;

        /// <summary>创建一条材料消耗；数量必须为正，零或负数是配表错误而非"不消耗"。</summary>
        public MaterialCost(string materialId, int count)
        {
            if (string.IsNullOrEmpty(materialId)) throw new ArgumentException("MaterialId must not be empty.", nameof(materialId));
            if (count <= 0) throw new ArgumentOutOfRangeException(nameof(count), count, "Material cost must be positive; zero cost entries should be omitted from the cost list.");
            MaterialId = materialId;
            Count = count;
        }
    }

    /// <summary>描述一种材料的持有量不足以支付消耗时的缺口。</summary>
    public readonly struct MaterialShortage
    {
        /// <summary>获取缺口对应的材料标识。</summary>
        public readonly string MaterialId;

        /// <summary>获取本次需要的数量。</summary>
        public readonly int Required;

        /// <summary>获取当前持有的数量。</summary>
        public readonly int Owned;

        /// <summary>获取还差多少；本结构只在 Required 大于 Owned 时产生，因此该值恒为正。</summary>
        public int Missing => Required - Owned;

        /// <summary>创建一条材料缺口记录。</summary>
        internal MaterialShortage(string materialId, int required, int owned)
        {
            MaterialId = materialId;
            Required = required;
            Owned = owned;
        }
    }

    /// <summary>
    /// 描述一次消耗校验的完整结果。
    /// 不满足时携带**全部**缺口而不是第一条，使界面可以一次性展示所有缺什么、差多少。
    /// </summary>
    public sealed class MaterialCostCheck
    {
        /// <summary>表示无消耗的操作恒为满足的共享结果，避免为空消耗列表反复分配。</summary>
        public static readonly MaterialCostCheck Satisfied = new MaterialCostCheck(Array.Empty<MaterialShortage>());

        /// <summary>保存本次校验产生的全部缺口；满足时为空集合。</summary>
        private readonly IReadOnlyList<MaterialShortage> shortages;

        /// <summary>创建校验结果。</summary>
        internal MaterialCostCheck(IReadOnlyList<MaterialShortage> shortageList)
        {
            shortages = shortageList;
        }

        /// <summary>获取本次消耗是否可以支付。</summary>
        public bool IsSatisfied => shortages.Count == 0;

        /// <summary>获取全部缺口；满足时为空。</summary>
        public IReadOnlyList<MaterialShortage> Shortages => shortages;
    }

    /// <summary>
    /// 材料消耗的纯函数校验器。
    ///
    /// 它只回答"够不够"，**不扣除任何东西**：背包是服务器权威的，扣减必须由服务器执行，
    /// 客户端校验的作用是在发请求前挡掉明显不成立的操作并给出缺口提示。
    /// 因为是纯函数，它可以脱离背包、网络和配表单独验证。
    /// </summary>
    public static class MaterialCostEvaluator
    {
        /// <summary>
        /// 按持有量查询委托校验一组消耗。
        /// 同一材料在消耗列表中出现多次时按累计需求判定，避免"分两条各自够、合起来不够"的漏判。
        /// </summary>
        /// <param name="costs">本次操作的全部消耗；为空表示无消耗。</param>
        /// <param name="ownedLookup">按材料标识返回当前持有量的委托。</param>
        /// <returns>校验结果；不满足时携带全部缺口。</returns>
        public static MaterialCostCheck Evaluate(IReadOnlyList<MaterialCost> costs, Func<string, int> ownedLookup)
        {
            if (ownedLookup == null) throw new ArgumentNullException(nameof(ownedLookup));
            if (costs == null || costs.Count == 0) return MaterialCostCheck.Satisfied;

            // 先按材料合并需求，再逐项比对，使重复条目形成累计需求而不是各自独立判定。
            Dictionary<string, int> required = new Dictionary<string, int>(costs.Count);
            for (int index = 0; index < costs.Count; index++)
            {
                MaterialCost cost = costs[index];
                required.TryGetValue(cost.MaterialId, out int accumulated);
                required[cost.MaterialId] = accumulated + cost.Count;
            }

            List<MaterialShortage> shortages = null;
            foreach (KeyValuePair<string, int> entry in required)
            {
                int owned = ownedLookup(entry.Key);
                if (owned >= entry.Value) continue;
                shortages ??= new List<MaterialShortage>();
                shortages.Add(new MaterialShortage(entry.Key, entry.Value, owned));
            }

            return shortages == null ? MaterialCostCheck.Satisfied : new MaterialCostCheck(shortages);
        }
    }
}
