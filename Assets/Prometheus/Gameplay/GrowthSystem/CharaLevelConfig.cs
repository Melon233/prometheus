using UnityEngine;

namespace Xuan.Prometheus.Growth
{
    /// <summary>集中保存角色等级 Logic 的只读共享配置，运行时数据和曲线副本仍由 CharaLevelComponent 持有。</summary>
    [CreateAssetMenu(menuName = "Prometheus/Growth/Chara Level Config", fileName = "CharaLevelConfig")]
    public sealed class CharaLevelConfig : ScriptableObject
    {
        /// <summary>配置角色允许达到的最高等级。</summary>
        [SerializeField, Min(1)] private int maximumLevel = 90;
        /// <summary>配置角色最高等级时由永久 Effect 提供的累计攻击力提升。</summary>
        [SerializeField, Min(0f)] private float maximumLevelAttack = 890f;
        /// <summary>配置归一化累计经验到归一化等级进度的映射曲线。</summary>
        [SerializeField] private AnimationCurve experienceCurve = AnimationCurve.Linear(0f, 0f, 1f, 1f);
        /// <summary>配置角色达到最高等级时需要的累计总经验。</summary>
        [SerializeField, Min(0.0001f)] private float maximumExperience = 8900f;

        /// <summary>获取至少为一级的角色等级上限。</summary>
        public int MaximumLevel => Mathf.Max(1, maximumLevel);

        /// <summary>获取非负的最高等级攻击力提升。</summary>
        public float MaximumLevelAttack => Mathf.Max(0f, maximumLevelAttack);

        /// <summary>获取共享只读经验曲线；Component 初始化时必须创建深拷贝。</summary>
        public AnimationCurve ExperienceCurve => experienceCurve;

        /// <summary>获取正数满级累计经验。</summary>
        public float MaximumExperience => Mathf.Max(0.0001f, maximumExperience);

        /// <summary>在 Inspector 修改时恢复合法配置边界并补齐默认线性曲线。</summary>
        private void OnValidate()
        {
            maximumLevel = Mathf.Max(1, maximumLevel);
            maximumLevelAttack = Mathf.Max(0f, maximumLevelAttack);
            maximumExperience = Mathf.Max(0.0001f, maximumExperience);
            if (experienceCurve == null || experienceCurve.length == 0) experienceCurve = AnimationCurve.Linear(0f, 0f, 1f, 1f);
        }
    }
}
