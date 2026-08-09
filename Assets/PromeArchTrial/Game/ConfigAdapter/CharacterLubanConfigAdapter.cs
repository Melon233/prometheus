using System;
using System.Collections.Generic;
using PromeArchTrial.Config;
using PromeArchTrial.Game.Character;
using LubanAction = PromeArchTrial.Config.gameplay.Action;
using LubanActionKind = PromeArchTrial.Config.gameplay.ActionKind;
using LubanActionSet = PromeArchTrial.Config.gameplay.ActionSet;
using LubanBattleRule = PromeArchTrial.Config.gameplay.BattleRule;
using LubanCharacter = PromeArchTrial.Config.gameplay.Character;
using LubanCharacterProperty = PromeArchTrial.Config.gameplay.CharacterProperty;
using LubanDodge = PromeArchTrial.Config.gameplay.Dodge;
using LubanLocomotion = PromeArchTrial.Config.gameplay.Locomotion;

namespace PromeArchTrial.Game.ConfigAdapter
{
    /// <summary>
    /// 把客户端与服务器各自生成的 Luban cs-bin 表转换为完全相同的纯 C# 角色运行时配置；该入口直接选择 Character 行，不引入 rootId 或 ScriptableObject 根配置。
    /// </summary>
    public static class CharacterLubanConfigAdapter
    {
        /// <summary>为合成的 Land 动作保留稳定且不与策划动作表混用的运行时编号。</summary>
        private const int SyntheticLandActionId = int.MaxValue - 2;

        /// <summary>为合成的前闪动作保留稳定且不与策划动作表混用的运行时编号。</summary>
        private const int SyntheticForwardDodgeActionId = int.MaxValue - 1;

        /// <summary>为合成的后闪动作保留稳定且不与策划动作表混用的运行时编号。</summary>
        private const int SyntheticBackwardDodgeActionId = int.MaxValue;

        /// <summary>普通攻击与移动攻击都必须严格提供四段，索引顺序即连段顺序。</summary>
        private const int RequiredComboSegmentCount = 4;

        /// <summary>把已完成 ResolveRef 的 Luban 表中指定 Character 行编译为客户端预测与服务器权威模拟共同消费的配置。</summary>
        public static CharacterRuntimeConfig Compile(Tables tables, int characterId)
        {
            if (tables == null) throw new ArgumentNullException(nameof(tables));
            if (characterId <= 0) throw new ArgumentOutOfRangeException(nameof(characterId), "Character ID must be positive.");
            LubanCharacter character = tables.TbCharacter.GetOrDefault(characterId);
            if (character == null) throw new KeyNotFoundException($"Luban Character row {characterId} does not exist.");
            LubanBattleRule battleRule = RequireBattleRuleReference(character);
            LubanCharacterProperty property = RequirePropertyReference(character);
            LubanLocomotion locomotion = RequireLocomotionReference(character);
            LubanDodge dodge = RequireDodgeReference(character);
            LubanActionSet actionSet = RequireActionSetReference(character);
            ValidateComboReferenceCounts(actionSet);

            List<CharacterActionRuntimeConfig> runtimeActions = new List<CharacterActionRuntimeConfig>(10);
            HashSet<int> selectedActionIds = new HashSet<int>();
            CharacterActionKind[] comboKinds = { CharacterActionKind.Attack1, CharacterActionKind.Attack2, CharacterActionKind.Attack3, CharacterActionKind.Attack4 };
            for (int segmentIndex = 0; segmentIndex < RequiredComboSegmentCount; segmentIndex++)
            {
                LubanAction normalAction = RequireIndexedActionReference(actionSet.NormalAttackIds, actionSet.NormalAttackIds_Ref, segmentIndex, $"ActionSet[{actionSet.Id}].normal_attack_ids");
                LubanAction movingAction = RequireIndexedActionReference(actionSet.MovingAttackIds, actionSet.MovingAttackIds_Ref, segmentIndex, $"ActionSet[{actionSet.Id}].moving_attack_ids");
                RegisterSelectedActionId(selectedActionIds, normalAction, $"normal combo segment {segmentIndex + 1}");
                RegisterSelectedActionId(selectedActionIds, movingAction, $"moving combo segment {segmentIndex + 1}");
                RequireLubanActionKind(normalAction, LubanActionKind.NORMAL_ATTACK, $"normal combo segment {segmentIndex + 1}");
                RequireLubanActionKind(movingAction, LubanActionKind.MOVING_ATTACK, $"moving combo segment {segmentIndex + 1}");
                ValidateMovingActionGameplayMatchesNormal(normalAction, movingAction, segmentIndex + 1);
                runtimeActions.Add(CreateRuntimeAction(normalAction, comboKinds[segmentIndex]));
            }

            LubanAction skillAction = RequireActionReference(actionSet.SkillActionId, actionSet.SkillActionId_Ref, $"ActionSet[{actionSet.Id}].skill_action_id");
            LubanAction specialAction = RequireActionReference(actionSet.SpecialActionId, actionSet.SpecialActionId_Ref, $"ActionSet[{actionSet.Id}].special_action_id");
            LubanAction ultimateAction = RequireActionReference(actionSet.UltimateActionId, actionSet.UltimateActionId_Ref, $"ActionSet[{actionSet.Id}].ultimate_action_id");
            RegisterSelectedActionId(selectedActionIds, skillAction, "skill action");
            RegisterSelectedActionId(selectedActionIds, specialAction, "special action");
            RegisterSelectedActionId(selectedActionIds, ultimateAction, "ultimate action");
            RequireLubanActionKind(skillAction, LubanActionKind.SKILL, "skill action");
            RequireLubanActionKind(specialAction, LubanActionKind.SPECIAL_ATTACK, "special action");
            RequireLubanActionKind(ultimateAction, LubanActionKind.ULTIMATE, "ultimate action");
            RejectSyntheticActionIdCollision(selectedActionIds);
            runtimeActions.Add(CreateRuntimeAction(specialAction, CharacterActionKind.HeavyAttack));
            runtimeActions.Add(CreateRuntimeAction(skillAction, CharacterActionKind.Skill));
            runtimeActions.Add(CreateRuntimeAction(ultimateAction, CharacterActionKind.Ultimate));
            runtimeActions.Add(CreateLandAction(battleRule));
            runtimeActions.Add(CreateDodgeAction(SyntheticForwardDodgeActionId, CharacterActionKind.DodgeForward, dodge.ForwardSpeedMilliUnitsPerSecond, dodge, battleRule));
            runtimeActions.Add(CreateDodgeAction(SyntheticBackwardDodgeActionId, CharacterActionKind.DodgeBackward, dodge.BackwardSpeedMilliUnitsPerSecond, dodge, battleRule));

            CharacterStatsRuntimeConfig statsConfig = new CharacterStatsRuntimeConfig(property.MaxHp, property.Attack, property.Defense, property.AttackSpeedPermille, property.CriticalDamagePermille, property.CriticalRatePermille, property.CoreEnergyLimit, property.UltimateEnergyLimit);
            CharacterLocomotionRuntimeConfig locomotionConfig = new CharacterLocomotionRuntimeConfig(MilliUnitsToRaw(locomotion.WalkSpeedMilliUnitsPerSecond), MilliUnitsToRaw(locomotion.RunSpeedMilliUnitsPerSecond), MilliUnitsToRaw(locomotion.SprintSpeedMilliUnitsPerSecond), MilliUnitsToRaw(locomotion.AirMoveSpeedMilliUnitsPerSecond), MilliUnitsToRaw(locomotion.JumpSpeedMilliUnitsPerSecond), MilliUnitsToRaw(locomotion.GravityMilliUnitsPerSecondSquared), MilliUnitsToRaw(battleRule.ReconciliationDistanceMilliUnits));
            CharacterCombatRuntimeConfig combatConfig = new CharacterCombatRuntimeConfig(battleRule.ComboResetTicks, battleRule.SpecialHoldTicks, battleRule.AttackBufferTicks);
            return new CharacterRuntimeConfig(battleRule.TickRate, battleRule.InputTimeoutTicks, battleRule.PredictionHistoryTicks, statsConfig, locomotionConfig, combatConfig, runtimeActions);
        }

        /// <summary>把表格中的千分之一世界单位转换为模拟核心使用的百万分之一世界单位，整数倍率确保客户端和服务器不会产生浮点差异。</summary>
        public static long MilliUnitsToRaw(int milliUnits)
        {
            const long milliUnitsPerUnit = 1000L;
            if (CharacterFixedPoint.PositionScale % milliUnitsPerUnit != 0L) throw new InvalidOperationException("Character fixed-point scale must be divisible by one thousand.");
            return checked((long)milliUnits * (CharacterFixedPoint.PositionScale / milliUnitsPerUnit));
        }

        /// <summary>校验并取得 Character 对 BattleRule 的已解析引用。</summary>
        private static LubanBattleRule RequireBattleRuleReference(LubanCharacter character)
        {
            LubanBattleRule value = character.BattleRuleId_Ref;
            if (value == null) throw new InvalidOperationException($"Character[{character.Id}].battle_rule_id={character.BattleRuleId} did not resolve.");
            if (value.Id != character.BattleRuleId) throw new InvalidOperationException($"Character[{character.Id}].battle_rule_id resolved to unexpected row {value.Id}.");
            return value;
        }

        /// <summary>校验并取得 Character 对 CharacterProperty 的已解析引用。</summary>
        private static LubanCharacterProperty RequirePropertyReference(LubanCharacter character)
        {
            LubanCharacterProperty value = character.PropertyId_Ref;
            if (value == null) throw new InvalidOperationException($"Character[{character.Id}].property_id={character.PropertyId} did not resolve.");
            if (value.Id != character.PropertyId) throw new InvalidOperationException($"Character[{character.Id}].property_id resolved to unexpected row {value.Id}.");
            return value;
        }

        /// <summary>校验并取得 Character 对 Locomotion 的已解析引用。</summary>
        private static LubanLocomotion RequireLocomotionReference(LubanCharacter character)
        {
            LubanLocomotion value = character.LocomotionId_Ref;
            if (value == null) throw new InvalidOperationException($"Character[{character.Id}].locomotion_id={character.LocomotionId} did not resolve.");
            if (value.Id != character.LocomotionId) throw new InvalidOperationException($"Character[{character.Id}].locomotion_id resolved to unexpected row {value.Id}.");
            return value;
        }

        /// <summary>校验并取得 Character 对 Dodge 的已解析引用。</summary>
        private static LubanDodge RequireDodgeReference(LubanCharacter character)
        {
            LubanDodge value = character.DodgeId_Ref;
            if (value == null) throw new InvalidOperationException($"Character[{character.Id}].dodge_id={character.DodgeId} did not resolve.");
            if (value.Id != character.DodgeId) throw new InvalidOperationException($"Character[{character.Id}].dodge_id resolved to unexpected row {value.Id}.");
            return value;
        }

        /// <summary>校验并取得 Character 对 ActionSet 的已解析引用。</summary>
        private static LubanActionSet RequireActionSetReference(LubanCharacter character)
        {
            LubanActionSet value = character.ActionSetId_Ref;
            if (value == null) throw new InvalidOperationException($"Character[{character.Id}].action_set_id={character.ActionSetId} did not resolve.");
            if (value.Id != character.ActionSetId) throw new InvalidOperationException($"Character[{character.Id}].action_set_id resolved to unexpected row {value.Id}.");
            return value;
        }

        /// <summary>确保普通与移动攻击都提供了严格四段的原始 ID 和 ResolveRef 结果。</summary>
        private static void ValidateComboReferenceCounts(LubanActionSet actionSet)
        {
            if (actionSet.NormalAttackIds == null || actionSet.NormalAttackIds.Count != RequiredComboSegmentCount) throw new InvalidOperationException($"ActionSet[{actionSet.Id}].normal_attack_ids must contain exactly {RequiredComboSegmentCount} rows.");
            if (actionSet.NormalAttackIds_Ref == null || actionSet.NormalAttackIds_Ref.Count != RequiredComboSegmentCount) throw new InvalidOperationException($"ActionSet[{actionSet.Id}].normal_attack_ids references were not resolved to exactly {RequiredComboSegmentCount} rows.");
            if (actionSet.MovingAttackIds == null || actionSet.MovingAttackIds.Count != RequiredComboSegmentCount) throw new InvalidOperationException($"ActionSet[{actionSet.Id}].moving_attack_ids must contain exactly {RequiredComboSegmentCount} rows.");
            if (actionSet.MovingAttackIds_Ref == null || actionSet.MovingAttackIds_Ref.Count != RequiredComboSegmentCount) throw new InvalidOperationException($"ActionSet[{actionSet.Id}].moving_attack_ids references were not resolved to exactly {RequiredComboSegmentCount} rows.");
        }

        /// <summary>从一组 Luban ref 列表取出指定动作并验证原始 ID 与解析结果一致。</summary>
        private static LubanAction RequireIndexedActionReference(IReadOnlyList<int> ids, IReadOnlyList<LubanAction> references, int index, string fieldPath)
        {
            LubanAction value = references[index];
            int expectedId = ids[index];
            if (value == null) throw new InvalidOperationException($"{fieldPath}[{index}]={expectedId} did not resolve.");
            if (value.Id != expectedId) throw new InvalidOperationException($"{fieldPath}[{index}] resolved to unexpected Action row {value.Id}.");
            return value;
        }

        /// <summary>校验单个 Action 引用不为空且解析到原始 ID 指定的行。</summary>
        private static LubanAction RequireActionReference(int expectedId, LubanAction value, string fieldPath)
        {
            if (value == null) throw new InvalidOperationException($"{fieldPath}={expectedId} did not resolve.");
            if (value.Id != expectedId) throw new InvalidOperationException($"{fieldPath} resolved to unexpected Action row {value.Id}.");
            return value;
        }

        /// <summary>验证动作表类别与 ActionSet 字段的语义一致，避免错误行被静默映射为另一种运行时动作。</summary>
        private static void RequireLubanActionKind(LubanAction action, LubanActionKind expectedKind, string usage)
        {
            if (action.Kind != expectedKind) throw new InvalidOperationException($"Action[{action.Id}] used as {usage} must have kind {expectedKind}, but has {action.Kind}.");
        }

        /// <summary>登记本角色选择的表动作 ID，并确保每一个引用都指向独立动作行。</summary>
        private static void RegisterSelectedActionId(HashSet<int> selectedActionIds, LubanAction action, string usage)
        {
            if (!selectedActionIds.Add(action.Id)) throw new InvalidOperationException($"Action[{action.Id}] is referenced more than once; duplicate usage detected at {usage}.");
        }

        /// <summary>拒绝策划动作占用合成动作的保留编号，保证 CharacterRuntimeConfig 的动作 ID 全局唯一。</summary>
        private static void RejectSyntheticActionIdCollision(HashSet<int> selectedActionIds)
        {
            if (selectedActionIds.Contains(SyntheticLandActionId) || selectedActionIds.Contains(SyntheticForwardDodgeActionId) || selectedActionIds.Contains(SyntheticBackwardDodgeActionId)) throw new InvalidOperationException($"Action IDs {SyntheticLandActionId}, {SyntheticForwardDodgeActionId}, and {SyntheticBackwardDodgeActionId} are reserved for synthesized Land and Dodge actions.");
        }

        /// <summary>逐字段验证移动攻击与同段普通攻击的全部双端共享数值相同；当前移动攻击只允许替换客户端动画引用。</summary>
        private static void ValidateMovingActionGameplayMatchesNormal(LubanAction normalAction, LubanAction movingAction, int segmentNumber)
        {
            RequireEqual(normalAction.WindupTicks, movingAction.WindupTicks, nameof(LubanAction.WindupTicks), normalAction, movingAction, segmentNumber);
            RequireEqual(normalAction.ActiveTicks, movingAction.ActiveTicks, nameof(LubanAction.ActiveTicks), normalAction, movingAction, segmentNumber);
            RequireEqual(normalAction.RecoveryTicks, movingAction.RecoveryTicks, nameof(LubanAction.RecoveryTicks), normalAction, movingAction, segmentNumber);
            RequireEqual(normalAction.CooldownTicks, movingAction.CooldownTicks, nameof(LubanAction.CooldownTicks), normalAction, movingAction, segmentNumber);
            RequireEqual(normalAction.InvincibleStartTick, movingAction.InvincibleStartTick, nameof(LubanAction.InvincibleStartTick), normalAction, movingAction, segmentNumber);
            RequireEqual(normalAction.InvincibleEndTick, movingAction.InvincibleEndTick, nameof(LubanAction.InvincibleEndTick), normalAction, movingAction, segmentNumber);
            RequireEqual(normalAction.MotionStartTick, movingAction.MotionStartTick, nameof(LubanAction.MotionStartTick), normalAction, movingAction, segmentNumber);
            RequireEqual(normalAction.MotionEndTick, movingAction.MotionEndTick, nameof(LubanAction.MotionEndTick), normalAction, movingAction, segmentNumber);
            RequireEqual(normalAction.ForwardDisplacementMilliUnits, movingAction.ForwardDisplacementMilliUnits, nameof(LubanAction.ForwardDisplacementMilliUnits), normalAction, movingAction, segmentNumber);
            RequireEqual(normalAction.DamagePermille, movingAction.DamagePermille, nameof(LubanAction.DamagePermille), normalAction, movingAction, segmentNumber);
            RequireEqual(normalAction.HitRangeMilliUnits, movingAction.HitRangeMilliUnits, nameof(LubanAction.HitRangeMilliUnits), normalAction, movingAction, segmentNumber);
            RequireEqual(normalAction.CoreEnergyCost, movingAction.CoreEnergyCost, nameof(LubanAction.CoreEnergyCost), normalAction, movingAction, segmentNumber);
            RequireEqual(normalAction.UltimateEnergyCost, movingAction.UltimateEnergyCost, nameof(LubanAction.UltimateEnergyCost), normalAction, movingAction, segmentNumber);
            RequireEqual(normalAction.CoreEnergyGainOnConfirmedHit, movingAction.CoreEnergyGainOnConfirmedHit, nameof(LubanAction.CoreEnergyGainOnConfirmedHit), normalAction, movingAction, segmentNumber);
            RequireEqual(normalAction.UltimateEnergyGainOnConfirmedHit, movingAction.UltimateEnergyGainOnConfirmedHit, nameof(LubanAction.UltimateEnergyGainOnConfirmedHit), normalAction, movingAction, segmentNumber);
        }

        /// <summary>输出包含具体字段与行号的移动攻击共享数值差异。</summary>
        private static void RequireEqual(int normalValue, int movingValue, string fieldName, LubanAction normalAction, LubanAction movingAction, int segmentNumber)
        {
            if (normalValue != movingValue) throw new InvalidOperationException($"Combo segment {segmentNumber} moving Action[{movingAction.Id}].{fieldName}={movingValue} must equal normal Action[{normalAction.Id}].{fieldName}={normalValue}; moving rows may currently differ only in client animation references.");
        }

        /// <summary>把一个动作表行映射为明确的运行时动作类别，并执行定点距离转换。</summary>
        private static CharacterActionRuntimeConfig CreateRuntimeAction(LubanAction action, CharacterActionKind runtimeKind)
        {
            return new CharacterActionRuntimeConfig(action.Id, runtimeKind, action.WindupTicks, action.ActiveTicks, action.RecoveryTicks, action.CooldownTicks, action.InvincibleStartTick, action.InvincibleEndTick, action.MotionStartTick, action.MotionEndTick, MilliUnitsToRaw(action.ForwardDisplacementMilliUnits), action.DamagePermille, MilliUnitsToRaw(action.HitRangeMilliUnits), action.CoreEnergyCost, action.UltimateEnergyCost, action.CoreEnergyGainOnConfirmedHit, action.UltimateEnergyGainOnConfirmedHit);
        }

        /// <summary>根据 BattleRule 的落地恢复 Tick 合成不造成伤害且不产生位移的 Land 动作。</summary>
        private static CharacterActionRuntimeConfig CreateLandAction(LubanBattleRule battleRule)
        {
            if (battleRule.LandRecoveryTicks <= 0) throw new InvalidOperationException($"BattleRule[{battleRule.Id}].land_recovery_ticks must be positive.");
            return new CharacterActionRuntimeConfig(SyntheticLandActionId, CharacterActionKind.Land, 0, 0, battleRule.LandRecoveryTicks, 0, -1, -1, 0, 0, 0L, 0, 0L, 0, 0, 0, 0);
        }

        /// <summary>根据 Dodge 速度与持续 Tick 合成确定性闪避动作，总位移严格按速度乘持续时间再除以 TickRate 计算。</summary>
        private static CharacterActionRuntimeConfig CreateDodgeAction(int actionId, CharacterActionKind runtimeKind, int speedMilliUnitsPerSecond, LubanDodge dodge, LubanBattleRule battleRule)
        {
            if (speedMilliUnitsPerSecond < 0) throw new InvalidOperationException($"Dodge[{dodge.Id}] speed for {runtimeKind} cannot be negative.");
            if (dodge.DurationTicks <= 0) throw new InvalidOperationException($"Dodge[{dodge.Id}].duration_ticks must be positive.");
            if (battleRule.TickRate <= 0) throw new InvalidOperationException($"BattleRule[{battleRule.Id}].tick_rate must be positive.");
            long speedRawPerSecond = MilliUnitsToRaw(speedMilliUnitsPerSecond);
            long displacementRaw = checked(speedRawPerSecond * dodge.DurationTicks / battleRule.TickRate);
            return new CharacterActionRuntimeConfig(actionId, runtimeKind, 0, dodge.DurationTicks, 0, dodge.CooldownTicks, dodge.InvulnerableStartTick, dodge.InvulnerableEndTick, 0, dodge.DurationTicks, displacementRaw, 0, 0L, 0, 0, 0, 0);
        }
    }
}
