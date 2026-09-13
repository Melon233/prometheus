using System;
using System.Collections.Generic;
using UnityEngine;
using Xuan.Prometheus.Growth;
using Cfg = Prometheus.Config;

namespace Xuan.Prometheus.Component
{
    /// <summary>保存 CharaLevelLogic 启动时应用的角色等级与突破阶段调试数据。</summary>
    [Serializable]
    public sealed class CharaLevelDebugData
    {
        /// <summary>配置启动时应用的角色等级；实际生效值仍受当前突破阶段的等级上限约束。</summary>
        [SerializeField, Min(1)] private int level = 1;
        /// <summary>配置启动时应用的突破阶段。</summary>
        [SerializeField, Min(0)] private int ascensionPhase;

        /// <summary>获取至少为一级的调试等级。</summary>
        public int Level => Mathf.Max(1, level);

        /// <summary>获取非负调试突破阶段。</summary>
        public int AscensionPhase => Mathf.Max(0, ascensionPhase);
    }

    /// <summary>
    /// 持有角色等级、经验与突破阶段，并按配表推导等级上限与三项基础属性增量。
    ///
    /// 等级由**逐级经验表**推导（累加 `expToNext`），而非由一条归一化曲线映射：
    /// 逐级表能表达「某几级特别贵」与每级的摩拉消耗，曲线不能。
    /// 突破是独立于等级的离散阶段：它不提升等级，只抬高等级上限、提供属性加值与第二属性档位。
    /// </summary>
    public sealed class CharaLevelComponent : Component, IEntityBinderComponent
    {
        /// <summary>引用角色等级 Logic 的只读 ScriptableObject 配置，当前只用于取角色标识。</summary>
        private CharaLevelConfig config;
        /// <summary>配置启动时应用到运行时数据的 Debug 等级与突破阶段。</summary>
        private CharaLevelDebugData debugData = new CharaLevelDebugData();

        /// <summary>保存当前 Entity 生命周期独占的养成表索引。</summary>
        private CharacterGrowthTables growthTables;
        /// <summary>保存当前角色的静态定义快照。</summary>
        private Cfg.CharacterBaseRow characterRow;
        /// <summary>保存当前角色允许达到的最大突破阶段。</summary>
        private int maximumPhase;

        /// <summary>保存角色当前等级。</summary>
        private int currentLevel = 1;
        /// <summary>保存角色在当前等级上已累积、尚未升级的经验。</summary>
        private int currentLevelExperience;
        /// <summary>保存角色当前突破阶段。</summary>
        private int currentAscensionPhase;
        /// <summary>标记运行时数据已经由 CharaLevelLogic 初始化。</summary>
        private bool initialized;

        /// <summary>向 UI 与存档观察者暴露等级脏通知。</summary>
        private readonly ModifiableProperty levelProperty = new ModifiableProperty();
        /// <summary>向 UI 与存档观察者暴露当前等级内经验脏通知。</summary>
        private readonly ModifiableProperty levelExperienceProperty = new ModifiableProperty();
        /// <summary>向 UI 与存档观察者暴露突破阶段脏通知。</summary>
        private readonly ModifiableProperty ascensionPhaseProperty = new ModifiableProperty();

        /// <summary>当等级或突破阶段发生变化时通知 CharaLevelLogic 重建属性投影。</summary>
        internal event Action AttributesChanged;

        /// <summary>当突破完成时通知 CharaLevelLogic 解锁本阶段的固有天赋；参数为固有天赋标识。</summary>
        internal event Action<string> PassiveTalentUnlocked;

        /// <summary>获取角色 Prefab 引用的只读等级配置；缺少引用时抛出明确异常。</summary>
        public CharaLevelConfig Config => config != null ? config : throw new InvalidOperationException("CharaLevelComponent requires a CharaLevelConfig reference.");

        /// <summary>获取当前角色标识。</summary>
        public string CharacterId => Config.CharacterId;

        /// <summary>获取角色当前等级。</summary>
        public int CurrentLevel => currentLevel;

        /// <summary>获取角色在当前等级上已累积的经验。</summary>
        public int CurrentLevelExperience => currentLevelExperience;

        /// <summary>获取角色当前突破阶段。</summary>
        public int AscensionPhase => currentAscensionPhase;

        /// <summary>获取当前突破阶段允许达到的等级上限。</summary>
        public int LevelCap => currentAscensionPhase <= 0 ? FirstPhaseRequiredLevel : growthTables.GetAscension(CharacterId, currentAscensionPhase).UnlockLevelCap;

        /// <summary>获取角色允许达到的最大突破阶段。</summary>
        public int MaximumAscensionPhase => maximumPhase;

        /// <summary>获取角色是否已经达到当前阶段的等级上限，即正在等待突破。</summary>
        public bool IsAwaitingAscension => currentLevel >= LevelCap && currentAscensionPhase < maximumPhase;

        /// <summary>获取角色是否已经满级（最大突破阶段且达到其等级上限）。</summary>
        public bool IsMaxLevel => currentAscensionPhase >= maximumPhase && currentLevel >= LevelCap;

        /// <summary>获取可监听的角色等级属性。</summary>
        public ModifiableProperty LevelProperty => levelProperty;

        /// <summary>获取可监听的当前等级内经验属性。</summary>
        public ModifiableProperty LevelExperienceProperty => levelExperienceProperty;

        /// <summary>获取可监听的突破阶段属性。</summary>
        public ModifiableProperty AscensionPhaseProperty => ascensionPhaseProperty;

        /// <summary>获取运行时数据是否已经完成初始化。</summary>
        public bool IsInitialized => initialized;

        /// <summary>从唯一根 PlayerBinder 获取等级配置和只读 Debug 模板。</summary>
        public void Bind(Logic.EntityBinder binder)
        {
            PlayerBinder playerBinder = binder as PlayerBinder ?? throw new InvalidOperationException($"CharaLevelComponent requires PlayerBinder but received '{binder?.GetType().FullName}'.");
            config = playerBinder.CharaLevelConfig;
            debugData = playerBinder.CharaLevelDebugData ?? new CharaLevelDebugData();
        }

        /// <summary>解除等级配置和 Debug 模板引用。</summary>
        public void Unbind()
        {
            config = null;
            debugData = null;
        }

        /// <summary>由 CharaLevelLogic 建立表索引并应用 Debug 启动状态。</summary>
        internal void InitializeRuntimeData(Cfg.Tables tables)
        {
            if (initialized) return;
            growthTables = new CharacterGrowthTables(tables);
            characterRow = growthTables.GetCharacter(Config.CharacterId);
            maximumPhase = growthTables.GetMaximumPhase(Config.CharacterId);

            CharaLevelDebugData safeDebugData = debugData ?? new CharaLevelDebugData();
            currentAscensionPhase = Mathf.Clamp(safeDebugData.AscensionPhase, 0, maximumPhase);
            currentLevel = Mathf.Clamp(safeDebugData.Level, 1, LevelCap);
            currentLevelExperience = 0;
            initialized = true;

            levelProperty.SetValue(currentLevel);
            levelExperienceProperty.SetValue(currentLevelExperience);
            ascensionPhaseProperty.SetValue(currentAscensionPhase);
        }

        /// <summary>
        /// 投入经验并按逐级表连续升级；返回实际被接受的经验。
        ///
        /// 达到当前阶段的等级上限后停止升级，但**剩余经验仍然保留在当前等级上**，
        /// 突破后会继续参与升级——等待突破期间投入的经验不应被浪费。
        /// 只有真正满级（最大阶段且达到其上限）才拒绝继续投入。
        /// </summary>
        /// <param name="requestedExperience">本次投入的非负经验。</param>
        /// <returns>实际接受的经验；满级时返回 0。</returns>
        public int AddExperience(int requestedExperience)
        {
            EnsureInitialized();
            if (requestedExperience <= 0) return 0;
            if (IsMaxLevel) return 0;

            int previousLevel = currentLevel;
            currentLevelExperience += requestedExperience;

            // 逐级消耗：每跨过一级就扣掉该级的 expToNext，直到经验不足或撞上当前阶段的等级上限。
            while (currentLevel < LevelCap)
            {
                int required = growthTables.GetLevel(characterRow.GrowthCurveId, currentLevel).ExpToNext;
                if (required <= 0 || currentLevelExperience < required) break;
                currentLevelExperience -= required;
                currentLevel++;
            }

            levelExperienceProperty.SetValue(currentLevelExperience);
            if (previousLevel != currentLevel)
            {
                levelProperty.SetValue(currentLevel);
                AttributesChanged?.Invoke();
            }
            return requestedExperience;
        }

        /// <summary>获取下一次突破需要消耗的材料；已达最大阶段时返回空列表。</summary>
        public IReadOnlyList<MaterialCost> GetNextAscensionCost()
        {
            EnsureInitialized();
            if (currentAscensionPhase >= maximumPhase) return Array.Empty<MaterialCost>();
            Cfg.CharacterAscensionRow row = growthTables.GetAscension(CharacterId, currentAscensionPhase + 1);
            List<MaterialCost> costs = new List<MaterialCost>(row.MaterialIds.Count + 1);
            if (row.Mora > 0) costs.Add(new MaterialCost(MoraMaterialId, row.Mora));
            for (int index = 0; index < row.MaterialIds.Count; index++) costs.Add(new MaterialCost(row.MaterialIds[index], row.MaterialCounts[index]));
            return costs;
        }

        /// <summary>获取当前是否满足突破的**等级**条件；材料条件由调用方经 IBagSystem 校验。</summary>
        public bool CanAscendByLevel()
        {
            EnsureInitialized();
            if (currentAscensionPhase >= maximumPhase) return false;
            return currentLevel >= growthTables.GetAscension(CharacterId, currentAscensionPhase + 1).RequiredLevel;
        }

        /// <summary>
        /// 执行一次突破。
        ///
        /// 只校验等级条件——材料扣减是服务器权威的，本方法不接触背包。
        /// 调用方必须在服务器确认扣减成功之后才调用它，否则会产生本地与服务器不一致的阶段。
        /// </summary>
        /// <returns>突破是否执行；等级不足或已达最大阶段时返回 false 且不产生任何副作用。</returns>
        public bool TryAscend()
        {
            EnsureInitialized();
            if (!CanAscendByLevel()) return false;

            currentAscensionPhase++;
            ascensionPhaseProperty.SetValue(currentAscensionPhase);
            AttributesChanged?.Invoke();

            // 突破抬高了等级上限，此前积压在当前等级上的经验应当立刻继续参与升级。
            ContinueLevelUpWithBankedExperience();

            Cfg.CharacterAscensionRow row = growthTables.GetAscension(CharacterId, currentAscensionPhase);
            if (!string.IsNullOrEmpty(row.UnlockPassiveTalentId)) PassiveTalentUnlocked?.Invoke(row.UnlockPassiveTalentId);
            return true;
        }

        /// <summary>按当前等级与突破阶段求出三项基础属性的增量；一级零突破时三项均为 0。</summary>
        public void GetAttributeIncrease(out float hp, out float attack, out float defence)
        {
            EnsureInitialized();
            float coefficient = growthTables.GetLevel(characterRow.GrowthCurveId, currentLevel).Coefficient;
            hp = characterRow.BaseHp * (coefficient - 1f);
            attack = characterRow.BaseAtk * (coefficient - 1f);
            defence = characterRow.BaseDef * (coefficient - 1f);
            for (int phase = 1; phase <= currentAscensionPhase; phase++)
            {
                Cfg.CharacterAscensionRow row = growthTables.GetAscension(CharacterId, phase);
                hp += row.AddHp;
                attack += row.AddAtk;
                defence += row.AddDef;
            }
        }

        /// <summary>获取第二属性的标识与当前数值；未配置第二属性或档位为 0 时数值为 0。</summary>
        public void GetSecondaryAttribute(out string attributeId, out float value)
        {
            EnsureInitialized();
            attributeId = characterRow.AscensionSecondaryAttr;
            int tier = currentAscensionPhase <= 0 ? 0 : growthTables.GetAscension(CharacterId, currentAscensionPhase).SecondaryAttrTier;
            value = tier * characterRow.SecondaryAttrPerTier;
        }

        /// <summary>突破后继续消耗积压经验，使等待突破期间投入的经验不被浪费。</summary>
        private void ContinueLevelUpWithBankedExperience()
        {
            if (currentLevelExperience <= 0) return;
            int previousLevel = currentLevel;
            while (currentLevel < LevelCap)
            {
                int required = growthTables.GetLevel(characterRow.GrowthCurveId, currentLevel).ExpToNext;
                if (required <= 0 || currentLevelExperience < required) break;
                currentLevelExperience -= required;
                currentLevel++;
            }
            levelExperienceProperty.SetValue(currentLevelExperience);
            if (previousLevel != currentLevel) levelProperty.SetValue(currentLevel);
        }

        /// <summary>零突破阶段的等级上限来自第一次突破的 requiredLevel，使上限序列只有配表一个来源。</summary>
        private int FirstPhaseRequiredLevel => maximumPhase <= 0 ? 1 : growthTables.GetAscension(CharacterId, 1).RequiredLevel;

        /// <summary>摩拉在背包中的物品标识；突破消耗把摩拉与其它材料统一表达为 MaterialCost。</summary>
        private const string MoraMaterialId = "MAT_MORA";

        /// <summary>防止外部系统在 CharaLevelLogic 初始化前读写养成数据。</summary>
        private void EnsureInitialized()
        {
            if (!initialized) throw new InvalidOperationException("CharaLevelComponent runtime data has not been initialized by CharaLevelLogic.");
        }
    }
}
