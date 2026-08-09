using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace PromeArchTrial.Game.Character
{
    /// <summary>
    /// 保存角色基础战斗属性的不可变运行时快照，生产环境应由 Luban 生成表的适配器构造。
    /// </summary>
    public sealed class CharacterStatsRuntimeConfig
    {
        /// <summary>创建并校验角色基础战斗属性。</summary>
        public CharacterStatsRuntimeConfig(int maxHp, int attack, int defense, int attackSpeedPermille, int criticalDamagePermille, int criticalRatePermille, int maxCoreEnergy, int maxUltimateEnergy)
        {
            if (maxHp <= 0) throw new ArgumentOutOfRangeException(nameof(maxHp), "Maximum HP must be positive.");
            if (attack < 0) throw new ArgumentOutOfRangeException(nameof(attack), "Attack cannot be negative.");
            if (defense < 0) throw new ArgumentOutOfRangeException(nameof(defense), "Defense cannot be negative.");
            if (attackSpeedPermille <= 0) throw new ArgumentOutOfRangeException(nameof(attackSpeedPermille), "Attack speed permille must be positive.");
            if (criticalDamagePermille < 0) throw new ArgumentOutOfRangeException(nameof(criticalDamagePermille), "Critical damage permille cannot be negative.");
            if (criticalRatePermille < 0 || criticalRatePermille > 1000) throw new ArgumentOutOfRangeException(nameof(criticalRatePermille), "Critical rate permille must be between 0 and 1000.");
            if (maxCoreEnergy < 0) throw new ArgumentOutOfRangeException(nameof(maxCoreEnergy), "Maximum core energy cannot be negative.");
            if (maxUltimateEnergy < 0) throw new ArgumentOutOfRangeException(nameof(maxUltimateEnergy), "Maximum ultimate energy cannot be negative.");
            MaxHp = maxHp;
            Attack = attack;
            Defense = defense;
            AttackSpeedPermille = attackSpeedPermille;
            CriticalDamagePermille = criticalDamagePermille;
            CriticalRatePermille = criticalRatePermille;
            MaxCoreEnergy = maxCoreEnergy;
            MaxUltimateEnergy = maxUltimateEnergy;
        }

        /// <summary>获取最大生命值。</summary>
        public int MaxHp { get; }

        /// <summary>获取用于伤害公式的基础攻击力。</summary>
        public int Attack { get; }

        /// <summary>获取用于伤害公式的基础防御力。</summary>
        public int Defense { get; }

        /// <summary>获取基础攻击速度的千分比倍率，动作 Tick 适配器可在生成运行时动作配置时消费该值。</summary>
        public int AttackSpeedPermille { get; }

        /// <summary>获取暴击伤害的千分比倍率，权威世界完成确定性暴击判定后消费该值。</summary>
        public int CriticalDamagePermille { get; }

        /// <summary>获取零到一千的暴击概率，模拟核心本身不掷随机数，由权威世界使用确定性随机源消费。</summary>
        public int CriticalRatePermille { get; }

        /// <summary>获取核心能量上限。</summary>
        public int MaxCoreEnergy { get; }

        /// <summary>获取终结能量上限。</summary>
        public int MaxUltimateEnergy { get; }

        /// <summary>把全部共享字段按稳定顺序加入配置内容哈希。</summary>
        internal void AppendHash(ref CharacterStableHashBuilder builder)
        {
            builder.Add(MaxHp);
            builder.Add(Attack);
            builder.Add(Defense);
            builder.Add(AttackSpeedPermille);
            builder.Add(CriticalDamagePermille);
            builder.Add(CriticalRatePermille);
            builder.Add(MaxCoreEnergy);
            builder.Add(MaxUltimateEnergy);
        }
    }

    /// <summary>
    /// 保存角色移动、跳跃、重力与预测阈值的不可变运行时快照。
    /// </summary>
    public sealed class CharacterLocomotionRuntimeConfig
    {
        /// <summary>创建并校验角色移动相关配置，所有速度和距离均使用角色定点数尺度。</summary>
        public CharacterLocomotionRuntimeConfig(long walkSpeedRaw, long runSpeedRaw, long sprintSpeedRaw, long airMoveSpeedRaw, long jumpSpeedRaw, long gravityRaw, long reconciliationDistanceRaw)
        {
            if (walkSpeedRaw < 0L) throw new ArgumentOutOfRangeException(nameof(walkSpeedRaw), "Walk speed cannot be negative.");
            if (runSpeedRaw < walkSpeedRaw) throw new ArgumentOutOfRangeException(nameof(runSpeedRaw), "Run speed cannot be lower than walk speed.");
            if (sprintSpeedRaw < runSpeedRaw) throw new ArgumentOutOfRangeException(nameof(sprintSpeedRaw), "Sprint speed cannot be lower than run speed.");
            if (airMoveSpeedRaw < 0L) throw new ArgumentOutOfRangeException(nameof(airMoveSpeedRaw), "Air movement speed cannot be negative.");
            if (jumpSpeedRaw <= 0L) throw new ArgumentOutOfRangeException(nameof(jumpSpeedRaw), "Jump speed must be positive.");
            if (gravityRaw <= 0L) throw new ArgumentOutOfRangeException(nameof(gravityRaw), "Gravity must be positive.");
            if (reconciliationDistanceRaw < 0L) throw new ArgumentOutOfRangeException(nameof(reconciliationDistanceRaw), "Reconciliation distance cannot be negative.");
            WalkSpeedRaw = walkSpeedRaw;
            RunSpeedRaw = runSpeedRaw;
            SprintSpeedRaw = sprintSpeedRaw;
            AirMoveSpeedRaw = airMoveSpeedRaw;
            JumpSpeedRaw = jumpSpeedRaw;
            GravityRaw = gravityRaw;
            ReconciliationDistanceRaw = reconciliationDistanceRaw;
        }

        /// <summary>获取每秒行走距离的定点值。</summary>
        public long WalkSpeedRaw { get; }

        /// <summary>获取每秒跑步距离的定点值。</summary>
        public long RunSpeedRaw { get; }

        /// <summary>获取每秒冲刺距离的定点值。</summary>
        public long SprintSpeedRaw { get; }

        /// <summary>获取每秒空中横向移动距离的定点值。</summary>
        public long AirMoveSpeedRaw { get; }

        /// <summary>获取起跳瞬间的每秒竖直速度定点值。</summary>
        public long JumpSpeedRaw { get; }

        /// <summary>获取每秒平方重力加速度的定点值。</summary>
        public long GravityRaw { get; }

        /// <summary>获取触发预测修正的严格距离阈值定点值。</summary>
        public long ReconciliationDistanceRaw { get; }

        /// <summary>获取以世界单位表示且仅供表现或诊断使用的预测修正阈值。</summary>
        public double ReconciliationDistanceUnits => CharacterFixedPoint.ToUnits(ReconciliationDistanceRaw);

        /// <summary>把全部共享字段按稳定顺序加入配置内容哈希。</summary>
        internal void AppendHash(ref CharacterStableHashBuilder builder)
        {
            builder.Add(WalkSpeedRaw);
            builder.Add(RunSpeedRaw);
            builder.Add(SprintSpeedRaw);
            builder.Add(AirMoveSpeedRaw);
            builder.Add(JumpSpeedRaw);
            builder.Add(GravityRaw);
            builder.Add(ReconciliationDistanceRaw);
        }
    }

    /// <summary>
    /// 保存连击、蓄力与模拟资源规则的不可变运行时快照。
    /// </summary>
    public sealed class CharacterCombatRuntimeConfig
    {
        /// <summary>创建并校验角色通用战斗规则。</summary>
        public CharacterCombatRuntimeConfig(int comboTimeoutTicks, int heavyAttackChargeTicks, int attackBufferTicks)
        {
            if (comboTimeoutTicks <= 0) throw new ArgumentOutOfRangeException(nameof(comboTimeoutTicks), "Combo timeout must be positive.");
            if (heavyAttackChargeTicks <= 0) throw new ArgumentOutOfRangeException(nameof(heavyAttackChargeTicks), "Heavy attack charge duration must be positive.");
            if (attackBufferTicks <= 0) throw new ArgumentOutOfRangeException(nameof(attackBufferTicks), "Attack input buffer duration must be positive.");
            ComboTimeoutTicks = comboTimeoutTicks;
            HeavyAttackChargeTicks = heavyAttackChargeTicks;
            AttackBufferTicks = attackBufferTicks;
        }

        /// <summary>获取上一段普攻完成后允许接续下一段的 Tick 数。</summary>
        public int ComboTimeoutTicks { get; }

        /// <summary>获取攻击键需要持续保持多少 Tick 才会触发蓄力重击。</summary>
        public int HeavyAttackChargeTicks { get; }

        /// <summary>获取轻击释放在角色暂时不可行动时最多保留的固定 Tick 数。</summary>
        public int AttackBufferTicks { get; }

        /// <summary>把全部共享字段按稳定顺序加入配置内容哈希。</summary>
        internal void AppendHash(ref CharacterStableHashBuilder builder)
        {
            builder.Add(ComboTimeoutTicks);
            builder.Add(HeavyAttackChargeTicks);
            builder.Add(AttackBufferTicks);
        }
    }

    /// <summary>
    /// 保存单个动作前摇、命中、后摇、冷却、无敌、位移与资源规则的不可变运行时快照。
    /// </summary>
    public sealed class CharacterActionRuntimeConfig
    {
        /// <summary>创建并校验一个由固定 Tick 驱动的角色动作配置。</summary>
        public CharacterActionRuntimeConfig(int id, CharacterActionKind kind, int windupTicks, int activeTicks, int recoveryTicks, int cooldownTicks, int invincibleStartTick, int invincibleEndTick, int motionStartTick, int motionEndTick, long forwardDisplacementRaw, int damagePermille, long hitRangeRaw, int coreEnergyCost, int ultimateEnergyCost, int coreEnergyGainOnConfirmedHit, int ultimateEnergyGainOnConfirmedHit)
        {
            if (id <= 0) throw new ArgumentOutOfRangeException(nameof(id), "Action row id must be positive.");
            if (kind == CharacterActionKind.None) throw new ArgumentOutOfRangeException(nameof(kind), "The None action cannot have a runtime row.");
            if (windupTicks < 0) throw new ArgumentOutOfRangeException(nameof(windupTicks), "Windup ticks cannot be negative.");
            if (activeTicks < 0) throw new ArgumentOutOfRangeException(nameof(activeTicks), "Active ticks cannot be negative.");
            if (recoveryTicks < 0) throw new ArgumentOutOfRangeException(nameof(recoveryTicks), "Recovery ticks cannot be negative.");
            int totalTicks = checked(windupTicks + activeTicks + recoveryTicks);
            if (totalTicks <= 0) throw new ArgumentOutOfRangeException(nameof(recoveryTicks), "An action must contain at least one simulation tick.");
            if (cooldownTicks < 0) throw new ArgumentOutOfRangeException(nameof(cooldownTicks), "Cooldown ticks cannot be negative.");
            bool invincibilityDisabled = invincibleStartTick == -1 && invincibleEndTick == -1;
            if (!invincibilityDisabled && (invincibleStartTick < 0 || invincibleEndTick < invincibleStartTick || invincibleEndTick >= totalTicks)) throw new ArgumentOutOfRangeException(nameof(invincibleStartTick), "Invincibility ticks must both be -1 or form an inclusive range inside the action.");
            if (motionStartTick < 0 || motionEndTick < motionStartTick || motionEndTick > totalTicks) throw new ArgumentOutOfRangeException(nameof(motionStartTick), "Motion range must be a half-open range inside the action.");
            if (forwardDisplacementRaw < 0L) throw new ArgumentOutOfRangeException(nameof(forwardDisplacementRaw), "Forward displacement cannot be negative.");
            if (forwardDisplacementRaw > 0L && motionEndTick == motionStartTick) throw new ArgumentOutOfRangeException(nameof(motionEndTick), "A non-zero displacement requires at least one motion tick.");
            if (damagePermille < 0) throw new ArgumentOutOfRangeException(nameof(damagePermille), "Damage multiplier cannot be negative.");
            if (hitRangeRaw < 0L) throw new ArgumentOutOfRangeException(nameof(hitRangeRaw), "Hit range cannot be negative.");
            if (activeTicks == 0 && (damagePermille > 0 || hitRangeRaw > 0L)) throw new ArgumentException("An action without active ticks cannot define damage or hit range.");
            if (coreEnergyCost < 0) throw new ArgumentOutOfRangeException(nameof(coreEnergyCost), "Core energy cost cannot be negative.");
            if (ultimateEnergyCost < 0) throw new ArgumentOutOfRangeException(nameof(ultimateEnergyCost), "Ultimate energy cost cannot be negative.");
            if (coreEnergyGainOnConfirmedHit < 0) throw new ArgumentOutOfRangeException(nameof(coreEnergyGainOnConfirmedHit), "Core energy gain cannot be negative.");
            if (ultimateEnergyGainOnConfirmedHit < 0) throw new ArgumentOutOfRangeException(nameof(ultimateEnergyGainOnConfirmedHit), "Ultimate energy gain cannot be negative.");
            Id = id;
            Kind = kind;
            WindupTicks = windupTicks;
            ActiveTicks = activeTicks;
            RecoveryTicks = recoveryTicks;
            CooldownTicks = cooldownTicks;
            InvincibleStartTick = invincibleStartTick;
            InvincibleEndTick = invincibleEndTick;
            MotionStartTick = motionStartTick;
            MotionEndTick = motionEndTick;
            ForwardDisplacementRaw = forwardDisplacementRaw;
            DamagePermille = damagePermille;
            HitRangeRaw = hitRangeRaw;
            CoreEnergyCost = coreEnergyCost;
            UltimateEnergyCost = ultimateEnergyCost;
            CoreEnergyGainOnConfirmedHit = coreEnergyGainOnConfirmedHit;
            UltimateEnergyGainOnConfirmedHit = ultimateEnergyGainOnConfirmedHit;
        }

        /// <summary>获取 Luban 动作表中的稳定行编号。</summary>
        public int Id { get; }

        /// <summary>获取动作种类。</summary>
        public CharacterActionKind Kind { get; }

        /// <summary>获取动作前摇 Tick 数。</summary>
        public int WindupTicks { get; }

        /// <summary>获取动作命中窗口 Tick 数。</summary>
        public int ActiveTicks { get; }

        /// <summary>获取动作后摇 Tick 数。</summary>
        public int RecoveryTicks { get; }

        /// <summary>获取动作开始后对应动作组不可再次触发的 Tick 数。</summary>
        public int CooldownTicks { get; }

        /// <summary>获取无敌区间的起始 Tick，负一表示该动作没有无敌区间。</summary>
        public int InvincibleStartTick { get; }

        /// <summary>获取无敌区间的结束 Tick，范围包含该 Tick，负一表示没有无敌区间。</summary>
        public int InvincibleEndTick { get; }

        /// <summary>获取动作位移半开区间的起始 Tick。</summary>
        public int MotionStartTick { get; }

        /// <summary>获取动作位移半开区间的结束 Tick。</summary>
        public int MotionEndTick { get; }

        /// <summary>获取动作在固定朝向上累计产生的定点位移。</summary>
        public long ForwardDisplacementRaw { get; }

        /// <summary>获取相对角色基础攻击力的千分比伤害倍率，零表示该动作不造成伤害。</summary>
        public int DamagePermille { get; }

        /// <summary>获取权威 BattleWorld 在命中窗口查询目标时使用的定点攻击距离。</summary>
        public long HitRangeRaw { get; }

        /// <summary>获取动作开始时消耗的核心能量。</summary>
        public int CoreEnergyCost { get; }

        /// <summary>获取动作开始时消耗的终结能量。</summary>
        public int UltimateEnergyCost { get; }

        /// <summary>获取每次外部确认命中后获得的核心能量。</summary>
        public int CoreEnergyGainOnConfirmedHit { get; }

        /// <summary>获取每次外部确认命中后获得的终结能量。</summary>
        public int UltimateEnergyGainOnConfirmedHit { get; }

        /// <summary>获取动作总持续 Tick 数。</summary>
        public int TotalTicks => WindupTicks + ActiveTicks + RecoveryTicks;

        /// <summary>获取该动作是否包含可用于命中查询的有效窗口。</summary>
        public bool HasHitWindow => ActiveTicks > 0 && DamagePermille > 0 && HitRangeRaw > 0L;

        /// <summary>判断指定动作内 Tick 是否处于配置的无敌区间。</summary>
        public bool IsInvincibleAt(int actionElapsedTick)
        {
            return InvincibleStartTick >= 0 && actionElapsedTick >= InvincibleStartTick && actionElapsedTick <= InvincibleEndTick;
        }

        /// <summary>根据动作内 Tick 获取前摇、命中或后摇阶段。</summary>
        public CharacterActionPhase GetPhaseAt(int actionElapsedTick)
        {
            if (actionElapsedTick < 0 || actionElapsedTick >= TotalTicks) return CharacterActionPhase.None;
            if (actionElapsedTick < WindupTicks) return CharacterActionPhase.Windup;
            if (actionElapsedTick < WindupTicks + ActiveTicks) return CharacterActionPhase.Active;
            return CharacterActionPhase.Recovery;
        }

        /// <summary>把全部共享字段按稳定顺序加入配置内容哈希。</summary>
        internal void AppendHash(ref CharacterStableHashBuilder builder)
        {
            builder.Add(Id);
            builder.Add((int)Kind);
            builder.Add(WindupTicks);
            builder.Add(ActiveTicks);
            builder.Add(RecoveryTicks);
            builder.Add(CooldownTicks);
            builder.Add(InvincibleStartTick);
            builder.Add(InvincibleEndTick);
            builder.Add(MotionStartTick);
            builder.Add(MotionEndTick);
            builder.Add(ForwardDisplacementRaw);
            builder.Add(DamagePermille);
            builder.Add(HitRangeRaw);
            builder.Add(CoreEnergyCost);
            builder.Add(UltimateEnergyCost);
            builder.Add(CoreEnergyGainOnConfirmedHit);
            builder.Add(UltimateEnergyGainOnConfirmedHit);
        }
    }

    /// <summary>
    /// 聚合客户端与服务器共享的全部角色配置，并在构造后以稳定内容哈希替代旧版 rootId 根配置校验。
    /// </summary>
    public sealed class CharacterRuntimeConfig
    {
        private readonly ReadOnlyDictionary<CharacterActionKind, CharacterActionRuntimeConfig> actions;

        /// <summary>从经过 Luban 引用解析后的不可变子配置构造完整角色运行时配置。</summary>
        public CharacterRuntimeConfig(int tickRate, int inputTimeoutTicks, int predictionHistoryTicks, CharacterStatsRuntimeConfig stats, CharacterLocomotionRuntimeConfig locomotion, CharacterCombatRuntimeConfig combat, IEnumerable<CharacterActionRuntimeConfig> actions)
        {
            if (tickRate != 30) throw new ArgumentOutOfRangeException(nameof(tickRate), "Character simulation requires an exact 30 Hz tick rate.");
            if (inputTimeoutTicks <= 0) throw new ArgumentOutOfRangeException(nameof(inputTimeoutTicks), "Input timeout ticks must be positive.");
            if (predictionHistoryTicks <= 0) throw new ArgumentOutOfRangeException(nameof(predictionHistoryTicks), "Prediction history ticks must be positive.");
            TickRate = tickRate;
            InputTimeoutTicks = inputTimeoutTicks;
            PredictionHistoryTicks = predictionHistoryTicks;
            Stats = stats ?? throw new ArgumentNullException(nameof(stats));
            Locomotion = locomotion ?? throw new ArgumentNullException(nameof(locomotion));
            Combat = combat ?? throw new ArgumentNullException(nameof(combat));
            if (actions == null) throw new ArgumentNullException(nameof(actions));
            Dictionary<CharacterActionKind, CharacterActionRuntimeConfig> actionMap = new Dictionary<CharacterActionKind, CharacterActionRuntimeConfig>();
            HashSet<int> actionIds = new HashSet<int>();
            foreach (CharacterActionRuntimeConfig action in actions)
            {
                if (action == null) throw new ArgumentException("Action collection cannot contain null rows.", nameof(actions));
                if (!actionMap.TryAdd(action.Kind, action)) throw new ArgumentException($"Duplicate action kind {action.Kind}.", nameof(actions));
                if (!actionIds.Add(action.Id)) throw new ArgumentException($"Duplicate action row id {action.Id}.", nameof(actions));
            }
            ValidateRequiredAction(actionMap, CharacterActionKind.Land);
            ValidateRequiredAction(actionMap, CharacterActionKind.DodgeForward);
            ValidateRequiredAction(actionMap, CharacterActionKind.DodgeBackward);
            ValidateRequiredAction(actionMap, CharacterActionKind.Attack1);
            ValidateRequiredAction(actionMap, CharacterActionKind.Attack2);
            ValidateRequiredAction(actionMap, CharacterActionKind.Attack3);
            ValidateRequiredAction(actionMap, CharacterActionKind.Attack4);
            ValidateRequiredAction(actionMap, CharacterActionKind.HeavyAttack);
            ValidateRequiredAction(actionMap, CharacterActionKind.Skill);
            ValidateRequiredAction(actionMap, CharacterActionKind.Ultimate);
            this.actions = new ReadOnlyDictionary<CharacterActionKind, CharacterActionRuntimeConfig>(actionMap);
            ContentHash = ComputeContentHash();
        }

        /// <summary>获取客户端与服务器共同使用的固定模拟频率。</summary>
        public int TickRate { get; }

        /// <summary>获取权威 BattleWorld 在缺少新命令后保持最后移动输入的最大 Tick 数。</summary>
        public int InputTimeoutTicks { get; }

        /// <summary>获取客户端为权威恢复和命令重放保留的最大未确认 Tick 数。</summary>
        public int PredictionHistoryTicks { get; }

        /// <summary>获取基础战斗属性。</summary>
        public CharacterStatsRuntimeConfig Stats { get; }

        /// <summary>获取移动和预测属性。</summary>
        public CharacterLocomotionRuntimeConfig Locomotion { get; }

        /// <summary>获取通用战斗规则。</summary>
        public CharacterCombatRuntimeConfig Combat { get; }

        /// <summary>获取按动作种类索引且不可修改的动作配置集合。</summary>
        public IReadOnlyDictionary<CharacterActionKind, CharacterActionRuntimeConfig> Actions => actions;

        /// <summary>获取覆盖所有共享配置字段和动作行的稳定内容哈希，此哈希不包含任何 rootId。</summary>
        public ulong ContentHash { get; }

        /// <summary>获取固定模拟 Tick 的秒数，此浮点值仅供 Unity 调度层累积时间使用。</summary>
        public double TickIntervalSeconds => 1.0d / TickRate;

        /// <summary>获取指定动作种类的必需运行时配置。</summary>
        public CharacterActionRuntimeConfig GetAction(CharacterActionKind kind)
        {
            if (!actions.TryGetValue(kind, out CharacterActionRuntimeConfig action)) throw new KeyNotFoundException($"No runtime action config exists for {kind}.");
            return action;
        }

        /// <summary>尝试获取指定动作种类的运行时配置。</summary>
        public bool TryGetAction(CharacterActionKind kind, out CharacterActionRuntimeConfig action)
        {
            return actions.TryGetValue(kind, out action);
        }

        /// <summary>校验必需动作是否存在。</summary>
        private static void ValidateRequiredAction(Dictionary<CharacterActionKind, CharacterActionRuntimeConfig> actionMap, CharacterActionKind kind)
        {
            if (!actionMap.ContainsKey(kind)) throw new ArgumentException($"Required action config {kind} is missing.", nameof(actionMap));
        }

        /// <summary>按枚举值排序动作行后计算稳定配置内容哈希。</summary>
        private ulong ComputeContentHash()
        {
            CharacterStableHashBuilder builder = CharacterStableHashBuilder.Create();
            builder.Add(TickRate);
            builder.Add(InputTimeoutTicks);
            builder.Add(PredictionHistoryTicks);
            Stats.AppendHash(ref builder);
            Locomotion.AppendHash(ref builder);
            Combat.AppendHash(ref builder);
            List<CharacterActionRuntimeConfig> sortedActions = new List<CharacterActionRuntimeConfig>(actions.Values);
            sortedActions.Sort((left, right) => ((int)left.Kind).CompareTo((int)right.Kind));
            builder.Add(sortedActions.Count);
            for (int index = 0; index < sortedActions.Count; index++) sortedActions[index].AppendHash(ref builder);
            return builder.ToHash();
        }
    }

    /// <summary>
    /// 提供与旧版 Yefa 数值接近的测试和迁移期默认配置；正式客户端与服务器必须改由 Luban 生成表适配器构造相同运行时类型。
    /// </summary>
    public static class LegacyYefaConfigFactory
    {
        /// <summary>创建固定三十赫兹且包含走跑跳闪避、四段普攻、蓄力重击、技能和终结技的迁移期配置。</summary>
        public static CharacterRuntimeConfig Create()
        {
            CharacterStatsRuntimeConfig stats = new CharacterStatsRuntimeConfig(3000, 10, 10, 1000, 1000, 500, 100, 100);
            CharacterLocomotionRuntimeConfig locomotion = new CharacterLocomotionRuntimeConfig(Units(2m), Units(3m), Units(5m), Units(3m), Units(5m), Units(9.8m), Units(0.1m));
            CharacterCombatRuntimeConfig combat = new CharacterCombatRuntimeConfig(60, 15, 6);
            CharacterActionRuntimeConfig[] actions =
            {
                Action(1001, CharacterActionKind.Land, 0, 0, 6, 0, -1, -1, 0, 0, 0m, 0, 0m, 0, 0, 0, 0),
                Action(1002, CharacterActionKind.DodgeForward, 0, 8, 4, 15, 0, 7, 0, 8, 2.5m, 0, 0m, 0, 0, 0, 0),
                Action(1003, CharacterActionKind.DodgeBackward, 0, 8, 4, 15, 0, 7, 0, 8, 2.0m, 0, 0m, 0, 0, 0, 0),
                Action(1101, CharacterActionKind.Attack1, 6, 1, 8, 0, -1, -1, 3, 7, 0.6m, 1000, 1.5m, 0, 0, 5, 3),
                Action(1102, CharacterActionKind.Attack2, 5, 1, 9, 0, -1, -1, 3, 7, 0.7m, 1100, 1.6m, 0, 0, 5, 3),
                Action(1103, CharacterActionKind.Attack3, 7, 1, 10, 0, -1, -1, 4, 9, 0.8m, 1250, 1.7m, 0, 0, 6, 4),
                Action(1104, CharacterActionKind.Attack4, 8, 2, 12, 0, -1, -1, 5, 11, 1.0m, 1600, 1.9m, 0, 0, 8, 5),
                Action(1201, CharacterActionKind.HeavyAttack, 10, 2, 15, 15, -1, -1, 5, 13, 1.5m, 2200, 2.2m, 0, 0, 12, 8),
                Action(1301, CharacterActionKind.Skill, 9, 3, 18, 120, -1, -1, 4, 12, 0.5m, 3000, 3.0m, 0, 0, 15, 10),
                Action(1401, CharacterActionKind.Ultimate, 15, 5, 30, 300, 0, 19, 0, 0, 0m, 6000, 5.0m, 0, 0, 0, 0)
            };
            return new CharacterRuntimeConfig(30, 6, 512, stats, locomotion, combat, actions);
        }

        /// <summary>把迁移配置中的十进制世界单位转换为定点整数。</summary>
        private static long Units(decimal value)
        {
            return CharacterFixedPoint.FromUnits(value);
        }

        /// <summary>以十进制位移简化迁移期动作行的创建。</summary>
        private static CharacterActionRuntimeConfig Action(int id, CharacterActionKind kind, int windupTicks, int activeTicks, int recoveryTicks, int cooldownTicks, int invincibleStartTick, int invincibleEndTick, int motionStartTick, int motionEndTick, decimal forwardDisplacementUnits, int damagePermille, decimal hitRangeUnits, int coreEnergyCost, int ultimateEnergyCost, int coreEnergyGain, int ultimateEnergyGain)
        {
            return new CharacterActionRuntimeConfig(id, kind, windupTicks, activeTicks, recoveryTicks, cooldownTicks, invincibleStartTick, invincibleEndTick, motionStartTick, motionEndTick, Units(forwardDisplacementUnits), damagePermille, Units(hitRangeUnits), coreEnergyCost, ultimateEnergyCost, coreEnergyGain, ultimateEnergyGain);
        }
    }
}
