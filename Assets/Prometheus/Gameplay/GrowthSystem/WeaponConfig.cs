using System.Collections.Generic;
using UnityEngine;

namespace Xuan.Prometheus.Growth
{
    /// <summary>集中保存武器 Logic 的成长曲线、词条定义和档位预设共享配置，实例数据由 WeaponComponent 持有。</summary>
    [CreateAssetMenu(menuName = "Prometheus/Growth/Weapon Config", fileName = "WeaponConfig")]
    public sealed class WeaponConfig : ScriptableObject
    {
        /// <summary>配置武器允许达到的最高等级，武器初始等级固定为一级。</summary>
        [SerializeField, Min(1)] private int maximumLevel = 90;
        /// <summary>配置归一化武器累计经验到归一化武器等级进度的映射曲线。</summary>
        [SerializeField] private AnimationCurve experienceCurve = AnimationCurve.Linear(0f, 0f, 1f, 1f);
        /// <summary>配置武器达到最高等级时需要的累计总经验。</summary>
        [SerializeField, Min(0.0001f)] private float maximumExperience = 8900f;
        /// <summary>配置当前武器拥有的主副词条定义。</summary>
        [SerializeField] private List<TierDefinition> weaponTiers = new List<TierDefinition>();
        /// <summary>配置五种允许属性各整数档位的满级固定值和满级系数值预设。</summary>
        [SerializeField] private List<TierPreset> tierPresets = new List<TierPreset>();

        /// <summary>获取至少为一级的武器等级上限。</summary>
        public int MaximumLevel => Mathf.Max(1, maximumLevel);

        /// <summary>获取共享只读武器经验曲线；Component 初始化时必须创建深拷贝。</summary>
        public AnimationCurve ExperienceCurve => experienceCurve;

        /// <summary>获取正数武器满级累计经验。</summary>
        public float MaximumExperience => Mathf.Max(0.0001f, maximumExperience);

        /// <summary>获取共享只读武器词条定义；Component 初始化时只创建 TierInstance 深拷贝。</summary>
        public IReadOnlyList<TierDefinition> WeaponTiers => weaponTiers ?? (weaponTiers = new List<TierDefinition>());

        /// <summary>获取共享只读档位预设；Component 初始化时必须创建深拷贝。</summary>
        public IReadOnlyList<TierPreset> TierPresets => tierPresets ?? (tierPresets = new List<TierPreset>());

        /// <summary>在 Inspector 修改时恢复合法配置边界、默认曲线和非空词条集合。</summary>
        private void OnValidate()
        {
            maximumLevel = Mathf.Max(1, maximumLevel);
            maximumExperience = Mathf.Max(0.0001f, maximumExperience);
            if (experienceCurve == null || experienceCurve.length == 0) experienceCurve = AnimationCurve.Linear(0f, 0f, 1f, 1f);
            if (weaponTiers == null) weaponTiers = new List<TierDefinition>();
            if (tierPresets == null) tierPresets = new List<TierPreset>();
        }
    }
}
