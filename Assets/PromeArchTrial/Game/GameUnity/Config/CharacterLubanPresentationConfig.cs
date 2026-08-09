using System;
using System.Collections.Generic;
using PromeArchTrial.Presentation.Character;
using LubanAction = PromeArchTrial.Config.gameplay.Action;
using LubanActionSet = PromeArchTrial.Config.gameplay.ActionSet;
using LubanAnimationClip = PromeArchTrial.Config.gameplay.AnimationClip;
using LubanCharacter = PromeArchTrial.Config.gameplay.Character;
using LubanCharacterPresentation = PromeArchTrial.Config.gameplay.CharacterPresentation;
using LubanTables = PromeArchTrial.Config.Tables;

namespace PromeArchTrial.Game.Unity.Config
{
    /// <summary>
    /// 保存客户端专用的角色 prefab 与 Spine 动画绑定；共享模拟配置不依赖该对象，因此服务器无需生成或加载表现字段。
    /// </summary>
    public sealed class CharacterLubanPresentationConfig
    {
        /// <summary>创建一个已经完成 Luban ref 校验的客户端角色表现配置。</summary>
        public CharacterLubanPresentationConfig(int characterId, string prefabAssetPath, CharacterAnimationPresentationBindings normalAttackBindings, CharacterAnimationPresentationBindings movingAttackBindings)
        {
            if (characterId <= 0) throw new ArgumentOutOfRangeException(nameof(characterId), "Character ID must be positive.");
            if (string.IsNullOrWhiteSpace(prefabAssetPath)) throw new ArgumentException("Character prefab asset path cannot be empty.", nameof(prefabAssetPath));
            CharacterId = characterId;
            PrefabAssetPath = prefabAssetPath;
            NormalAttackBindings = normalAttackBindings;
            MovingAttackBindings = movingAttackBindings;
        }

        /// <summary>获取直接选择的 Character 表行编号。</summary>
        public int CharacterId { get; }

        /// <summary>获取 Unity 角色表现 prefab 的资产路径或地址。</summary>
        public string PrefabAssetPath { get; }

        /// <summary>获取站立普攻版本的完整动画绑定。</summary>
        public CharacterAnimationPresentationBindings NormalAttackBindings { get; }

        /// <summary>获取移动普攻版本的完整动画绑定；除四段普攻动画外，其余字段与站立版本相同。</summary>
        public CharacterAnimationPresentationBindings MovingAttackBindings { get; }

    }

    /// <summary>
    /// 只在 Unity 客户端读取 Luban 的 client 分组字段，并通过生成代码的 *_Ref 结果构建与 Presenter 解耦的动画绑定。
    /// </summary>
    public static class CharacterLubanPresentationConfigBuilder
    {
        /// <summary>构建指定 Character 行的 prefab 与站立/移动两套攻击动画绑定。</summary>
        public static CharacterLubanPresentationConfig Build(LubanTables tables, int characterId)
        {
            if (tables == null) throw new ArgumentNullException(nameof(tables));
            if (characterId <= 0) throw new ArgumentOutOfRangeException(nameof(characterId), "Character ID must be positive.");
            LubanCharacter character = tables.TbCharacter.GetOrDefault(characterId);
            if (character == null) throw new KeyNotFoundException($"Luban Character row {characterId} does not exist.");
            LubanCharacterPresentation presentation = character.PresentationId_Ref;
            if (presentation == null) throw new InvalidOperationException($"Character[{character.Id}].presentation_id={character.PresentationId} did not resolve in the client tables.");
            if (presentation.Id != character.PresentationId) throw new InvalidOperationException($"Character[{character.Id}].presentation_id resolved to unexpected CharacterPresentation row {presentation.Id}.");
            LubanActionSet actionSet = character.ActionSetId_Ref;
            if (actionSet == null) throw new InvalidOperationException($"Character[{character.Id}].action_set_id={character.ActionSetId} did not resolve in the client tables.");
            if (actionSet.Id != character.ActionSetId) throw new InvalidOperationException($"Character[{character.Id}].action_set_id resolved to unexpected ActionSet row {actionSet.Id}.");
            ValidateFourAttackReferences(actionSet.NormalAttackIds, actionSet.NormalAttackIds_Ref, $"ActionSet[{actionSet.Id}].normal_attack_ids");
            ValidateFourAttackReferences(actionSet.MovingAttackIds, actionSet.MovingAttackIds_Ref, $"ActionSet[{actionSet.Id}].moving_attack_ids");

            string idle = RequirePresentationClipName(presentation.IdleClipId, presentation.IdleClipId_Ref, $"CharacterPresentation[{presentation.Id}].idle_clip_id");
            string walk = RequirePresentationClipName(presentation.WalkClipId, presentation.WalkClipId_Ref, $"CharacterPresentation[{presentation.Id}].walk_clip_id");
            string run = RequirePresentationClipName(presentation.RunClipId, presentation.RunClipId_Ref, $"CharacterPresentation[{presentation.Id}].run_clip_id");
            string sprint = RequirePresentationClipName(presentation.SprintClipId, presentation.SprintClipId_Ref, $"CharacterPresentation[{presentation.Id}].sprint_clip_id");
            string jump = RequirePresentationClipName(presentation.JumpClipId, presentation.JumpClipId_Ref, $"CharacterPresentation[{presentation.Id}].jump_clip_id");
            string rise = RequirePresentationClipName(presentation.RiseClipId, presentation.RiseClipId_Ref, $"CharacterPresentation[{presentation.Id}].rise_clip_id");
            string fall = RequirePresentationClipName(presentation.FallClipId, presentation.FallClipId_Ref, $"CharacterPresentation[{presentation.Id}].fall_clip_id");
            string land = RequirePresentationClipName(presentation.LandClipId, presentation.LandClipId_Ref, $"CharacterPresentation[{presentation.Id}].land_clip_id");
            string forwardDodge = RequirePresentationClipName(presentation.ForwardDodgeClipId, presentation.ForwardDodgeClipId_Ref, $"CharacterPresentation[{presentation.Id}].forward_dodge_clip_id");
            string backwardDodge = RequirePresentationClipName(presentation.BackwardDodgeClipId, presentation.BackwardDodgeClipId_Ref, $"CharacterPresentation[{presentation.Id}].backward_dodge_clip_id");
            string hitReaction = RequirePresentationClipName(presentation.AttackedClipId, presentation.AttackedClipId_Ref, $"CharacterPresentation[{presentation.Id}].attacked_clip_id");
            string death = RequirePresentationClipName(presentation.DeathClipId, presentation.DeathClipId_Ref, $"CharacterPresentation[{presentation.Id}].death_clip_id");
            string[] normalAttacks = BuildAttackAnimationNames(actionSet.NormalAttackIds, actionSet.NormalAttackIds_Ref, $"ActionSet[{actionSet.Id}].normal_attack_ids");
            string[] movingAttacks = BuildAttackAnimationNames(actionSet.MovingAttackIds, actionSet.MovingAttackIds_Ref, $"ActionSet[{actionSet.Id}].moving_attack_ids");
            LubanAction specialAction = RequireActionReference(actionSet.SpecialActionId, actionSet.SpecialActionId_Ref, $"ActionSet[{actionSet.Id}].special_action_id");
            LubanAction skillAction = RequireActionReference(actionSet.SkillActionId, actionSet.SkillActionId_Ref, $"ActionSet[{actionSet.Id}].skill_action_id");
            LubanAction ultimateAction = RequireActionReference(actionSet.UltimateActionId, actionSet.UltimateActionId_Ref, $"ActionSet[{actionSet.Id}].ultimate_action_id");
            string heavyAttack = RequireActionClipName(specialAction, 0, $"Action[{specialAction.Id}].animation_clip_ids");
            string branchStartup = RequireActionClipName(skillAction, 0, $"Action[{skillAction.Id}].animation_clip_ids");
            string branchBody = RequireLastActionClipName(skillAction, $"Action[{skillAction.Id}].animation_clip_ids");
            if (skillAction.AnimationClipIds.Count < 2) throw new InvalidOperationException($"Action[{skillAction.Id}].animation_clip_ids must contain at least a startup ref followed by a body ref.");
            if (string.Equals(branchStartup, branchBody, StringComparison.Ordinal)) throw new InvalidOperationException($"Action[{skillAction.Id}].animation_clip_ids must use different first startup and last body animations.");
            string ultimate = RequireActionClipName(ultimateAction, 0, $"Action[{ultimateAction.Id}].animation_clip_ids");
            CharacterAnimationPresentationBindings normalBindings = CreateBindings(idle, walk, run, sprint, jump, rise, fall, land, forwardDodge, backwardDodge, normalAttacks, heavyAttack, branchStartup, branchBody, ultimate, hitReaction, death);
            CharacterAnimationPresentationBindings movingBindings = CreateBindings(idle, walk, run, sprint, jump, rise, fall, land, forwardDodge, backwardDodge, movingAttacks, heavyAttack, branchStartup, branchBody, ultimate, hitReaction, death);
            return new CharacterLubanPresentationConfig(character.Id, presentation.PrefabAssetPath, normalBindings, movingBindings);
        }

        /// <summary>创建 Presenter 使用的不可变绑定对象，并保持构造参数与表字段映射集中可审查。</summary>
        private static CharacterAnimationPresentationBindings CreateBindings(string idle, string walk, string run, string sprint, string jump, string rise, string fall, string land, string forwardDodge, string backwardDodge, IReadOnlyList<string> attacks, string heavyAttack, string skillStartup, string skillBody, string ultimate, string hitReaction, string death)
        {
            return new CharacterAnimationPresentationBindings(idle, walk, run, sprint, jump, rise, fall, land, forwardDodge, backwardDodge, attacks[0], attacks[1], attacks[2], attacks[3], heavyAttack, skillStartup, skillBody, ultimate, hitReaction, death);
        }

        /// <summary>确保四段攻击 ID 与其 Luban ResolveRef 结果数量一致且没有空引用。</summary>
        private static void ValidateFourAttackReferences(IReadOnlyList<int> ids, IReadOnlyList<LubanAction> references, string fieldPath)
        {
            const int requiredCount = 4;
            if (ids == null || ids.Count != requiredCount) throw new InvalidOperationException($"{fieldPath} must contain exactly {requiredCount} rows.");
            if (references == null || references.Count != requiredCount) throw new InvalidOperationException($"{fieldPath} references were not resolved to exactly {requiredCount} rows.");
            for (int index = 0; index < requiredCount; index++)
            {
                if (references[index] == null) throw new InvalidOperationException($"{fieldPath}[{index}]={ids[index]} did not resolve.");
                if (references[index].Id != ids[index]) throw new InvalidOperationException($"{fieldPath}[{index}] resolved to unexpected Action row {references[index].Id}.");
            }
        }

        /// <summary>从四段动作的首个动画 ref 提取 Spine 名称，普通与移动攻击因此可以共享玩法数值而使用不同动画。</summary>
        private static string[] BuildAttackAnimationNames(IReadOnlyList<int> ids, IReadOnlyList<LubanAction> actions, string fieldPath)
        {
            string[] names = new string[4];
            for (int index = 0; index < names.Length; index++) names[index] = RequireActionClipName(actions[index], 0, $"{fieldPath}[{index}]/Action[{ids[index]}].animation_clip_ids");
            return names;
        }

        /// <summary>校验 ActionSet 的单值动作 ref。</summary>
        private static LubanAction RequireActionReference(int expectedId, LubanAction value, string fieldPath)
        {
            if (value == null) throw new InvalidOperationException($"{fieldPath}={expectedId} did not resolve.");
            if (value.Id != expectedId) throw new InvalidOperationException($"{fieldPath} resolved to unexpected Action row {value.Id}.");
            return value;
        }

        /// <summary>通过 Action.animation_clip_ids_Ref 取得指定序号的 Spine 动画名，并验证原始 ID 与 ref 一致。</summary>
        private static string RequireActionClipName(LubanAction action, int clipIndex, string fieldPath)
        {
            if (action.AnimationClipIds == null || action.AnimationClipIds.Count == 0) throw new InvalidOperationException($"{fieldPath} must contain at least one client animation reference.");
            if (action.AnimationClipIds_Ref == null || action.AnimationClipIds_Ref.Count != action.AnimationClipIds.Count) throw new InvalidOperationException($"{fieldPath} references were not fully resolved.");
            if (clipIndex < 0 || clipIndex >= action.AnimationClipIds.Count) throw new InvalidOperationException($"{fieldPath} does not contain required clip index {clipIndex}.");
            return RequirePresentationClipName(action.AnimationClipIds[clipIndex], action.AnimationClipIds_Ref[clipIndex], $"{fieldPath}[{clipIndex}]");
        }

        /// <summary>取得动作动画序列的最后一个 ref，派生攻击可借此把第一项作为起手而把最后一项作为持续主体。</summary>
        private static string RequireLastActionClipName(LubanAction action, string fieldPath)
        {
            if (action.AnimationClipIds == null || action.AnimationClipIds.Count == 0) throw new InvalidOperationException($"{fieldPath} must contain at least one client animation reference.");
            return RequireActionClipName(action, action.AnimationClipIds.Count - 1, fieldPath);
        }

        /// <summary>通过生成字段的 *_Ref 对象读取 SpineAnimationName，禁止退回手工 ID 查表。</summary>
        private static string RequirePresentationClipName(int expectedId, LubanAnimationClip clip, string fieldPath)
        {
            if (clip == null) throw new InvalidOperationException($"{fieldPath}={expectedId} did not resolve.");
            if (clip.Id != expectedId) throw new InvalidOperationException($"{fieldPath} resolved to unexpected AnimationClip row {clip.Id}.");
            if (string.IsNullOrWhiteSpace(clip.SpineAnimationName)) throw new InvalidOperationException($"AnimationClip[{clip.Id}].spine_animation_name cannot be empty.");
            return clip.SpineAnimationName;
        }
    }
}
