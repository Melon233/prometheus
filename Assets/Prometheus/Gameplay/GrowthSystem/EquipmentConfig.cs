using System.Collections.Generic;
using UnityEngine;

namespace Xuan.Prometheus.Growth
{
    /// <summary>集中保存装备 Logic 的槽位、成长曲线和词条档位共享配置，实例数据由 EquipmentComponent 持有。</summary>
    [CreateAssetMenu(menuName = "Prometheus/Growth/Equipment Config", fileName = "EquipmentConfig")]
    public sealed class EquipmentConfig : ScriptableObject
    {
        /// <summary>配置角色拥有的装备槽位数量。</summary>
        [SerializeField, Min(0)] private int equipmentSlotCount = 5;
        /// <summary>配置每件装备允许的副词条槽位数量。</summary>
        [SerializeField, Min(0)] private int subTierSlotCount = 4;
        /// <summary>配置每件装备允许达到的最高等级，装备初始等级固定为零。</summary>
        [SerializeField, Min(1)] private int maximumLevel = 20;
        /// <summary>配置归一化装备累计经验到归一化装备等级进度的映射曲线。</summary>
        [SerializeField] private AnimationCurve experienceCurve = AnimationCurve.Linear(0f, 0f, 1f, 1f);
        /// <summary>配置单件装备达到最高等级时需要的累计总经验。</summary>
        [SerializeField, Min(0.0001f)] private float maximumExperience = 2000f;
        /// <summary>配置五种允许属性各整数档位的满级固定值和满级系数值预设。</summary>
        [SerializeField] private List<TierPreset> tierPresets = new List<TierPreset>();

        /// <summary>获取非负装备槽位数量。</summary>
        public int EquipmentSlotCount => Mathf.Max(0, equipmentSlotCount);

        /// <summary>获取非负副词条槽位数量。</summary>
        public int SubTierSlotCount => Mathf.Max(0, subTierSlotCount);

        /// <summary>获取至少为一级的装备等级上限。</summary>
        public int MaximumLevel => Mathf.Max(1, maximumLevel);

        /// <summary>获取共享只读装备经验曲线；Component 初始化时必须创建深拷贝。</summary>
        public AnimationCurve ExperienceCurve => experienceCurve;

        /// <summary>获取正数装备满级累计经验。</summary>
        public float MaximumExperience => Mathf.Max(0.0001f, maximumExperience);

        /// <summary>获取共享只读档位预设；Component 初始化时必须创建深拷贝。</summary>
        public IReadOnlyList<TierPreset> TierPresets => tierPresets ?? (tierPresets = new List<TierPreset>());

        /// <summary>在 Inspector 修改时恢复合法配置边界、默认曲线和非空预设集合。</summary>
        private void OnValidate()
        {
            equipmentSlotCount = Mathf.Max(0, equipmentSlotCount);
            subTierSlotCount = Mathf.Max(0, subTierSlotCount);
            maximumLevel = Mathf.Max(1, maximumLevel);
            maximumExperience = Mathf.Max(0.0001f, maximumExperience);
            if (experienceCurve == null || experienceCurve.length == 0) experienceCurve = AnimationCurve.Linear(0f, 0f, 1f, 1f);
            if (tierPresets == null) tierPresets = new List<TierPreset>();
        }
    }
}
