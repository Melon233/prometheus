using System.Collections.Generic;
using UnityEngine;
using Xuan.Prometheus.Component;
using Xuan.Prometheus.Growth;
using Xuan.Prometheus.Logic.Talent;

namespace Xuan.Prometheus
{
    /// <summary>集中保存玩家角色 Prefab 独有的技能、成长与命中引用。</summary>
    public sealed class PlayerBinder : CharacterBinder
    {
        [SerializeField] private TalentConfig specialAttackTalentConfig;
        [SerializeField] private ColliderProxy specialAttackCollider;
        [SerializeField] private string specialAttackAbilityId = "Player.SpecialAttack";
        [SerializeField] private TalentGrowthState specialAttackTalentGrowth = new TalentGrowthState();
        [SerializeField] private TalentConfig skillTalentConfig;
        [SerializeField] private ColliderProxy skillCollider;
        [SerializeField] private string skillAbilityId = "Player.Skill";
        [SerializeField] private TalentGrowthState skillTalentGrowth = new TalentGrowthState();
        [SerializeField] private TalentConfig ultimateTalentConfig;
        [SerializeField] private ColliderProxy ultimateCollider;
        [SerializeField] private string ultimateAbilityId = "Player.Ultimate";
        [SerializeField] private TalentGrowthState ultimateTalentGrowth = new TalentGrowthState();
        [SerializeField] private CharaLevelConfig charaLevelConfig;
        [SerializeField] private CharaLevelDebugData charaLevelDebugData = new CharaLevelDebugData();
        [SerializeField] private EquipmentConfig equipmentConfig;
        [SerializeField] private List<EquipmentDebugData> debugEquipment = new List<EquipmentDebugData>();
        [SerializeField] private WeaponConfig weaponConfig;
        [SerializeField] private WeaponDebugData weaponDebugData = new WeaponDebugData();

        /// <summary>获取特殊攻击天赋配置。</summary>
        public TalentConfig SpecialAttackTalentConfig => specialAttackTalentConfig;
        /// <summary>获取特殊攻击命中代理。</summary>
        public ColliderProxy SpecialAttackCollider => specialAttackCollider;
        /// <summary>获取特殊攻击稳定能力编号。</summary>
        public string SpecialAttackAbilityId => specialAttackAbilityId;
        /// <summary>获取特殊攻击 Debug 等级模板。</summary>
        public TalentGrowthState SpecialAttackTalentGrowth => specialAttackTalentGrowth;
        /// <summary>获取技能天赋配置。</summary>
        public TalentConfig SkillTalentConfig => skillTalentConfig;
        /// <summary>获取技能命中代理。</summary>
        public ColliderProxy SkillCollider => skillCollider;
        /// <summary>获取技能稳定能力编号。</summary>
        public string SkillAbilityId => skillAbilityId;
        /// <summary>获取技能 Debug 等级模板。</summary>
        public TalentGrowthState SkillTalentGrowth => skillTalentGrowth;
        /// <summary>获取大招天赋配置。</summary>
        public TalentConfig UltimateTalentConfig => ultimateTalentConfig;
        /// <summary>获取大招命中代理。</summary>
        public ColliderProxy UltimateCollider => ultimateCollider;
        /// <summary>获取大招稳定能力编号。</summary>
        public string UltimateAbilityId => ultimateAbilityId;
        /// <summary>获取大招 Debug 等级模板。</summary>
        public TalentGrowthState UltimateTalentGrowth => ultimateTalentGrowth;
        /// <summary>获取角色等级配置。</summary>
        public CharaLevelConfig CharaLevelConfig => charaLevelConfig;
        /// <summary>获取角色等级 Debug 数据模板。</summary>
        public CharaLevelDebugData CharaLevelDebugData => charaLevelDebugData;
        /// <summary>获取装备配置。</summary>
        public EquipmentConfig EquipmentConfig => equipmentConfig;
        /// <summary>获取装备 Debug 数据模板。</summary>
        public IReadOnlyList<EquipmentDebugData> DebugEquipment => debugEquipment;
        /// <summary>获取武器配置。</summary>
        public WeaponConfig WeaponConfig => weaponConfig;
        /// <summary>获取武器 Debug 数据模板。</summary>
        public WeaponDebugData WeaponDebugData => weaponDebugData;

        /// <summary>在共享角色引用基础上校验玩家独有的天赋与成长配置。</summary>
        public override void Validate()
        {
            base.Validate();
            if (AttackTalentConfig == null || specialAttackTalentConfig == null || skillTalentConfig == null || ultimateTalentConfig == null) throw new System.InvalidOperationException($"PlayerBinder '{name}' requires all TalentConfig references.");
            if (charaLevelConfig == null || equipmentConfig == null || weaponConfig == null) throw new System.InvalidOperationException($"PlayerBinder '{name}' requires all growth config references.");
        }
    }
}
