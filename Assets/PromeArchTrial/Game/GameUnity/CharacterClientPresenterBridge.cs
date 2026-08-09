using System;
using PromeArchTrial.Game.Character;
using PromeArchTrial.Game.Unity.Config;
using PromeArchTrial.Presentation.Character;
using UnityEngine;

namespace PromeArchTrial.Game.Unity
{
    /// <summary>
    /// 把客户端预测完整状态单向转换为独立表现契约，并绘制网络、预测和角色操作诊断；该组件不参与任何 gameplay 判定。
    /// </summary>
    public sealed class CharacterClientPresenterBridge : MonoBehaviour
    {
        // 组合根注入的三个引用分别提供预测状态、纯表现入口和 Luban 客户端动画绑定。
        private ClientBattleSession session;
        private YefaCharacterPresenter presenter;
        private CharacterLubanPresentationConfig presentationConfig;

        // 表现缓存用于在零水平朝向时保持最后朝向，并保证同一动作实例只切换一次普通/移动攻击绑定。
        private CharacterFacingDirection lastFacing = CharacterFacingDirection.Right;
        private uint configuredAttackSequence;
        private bool usesMovingAttackBindings;

        /// <summary>由组合根注入客户端会话和净化后的 Yefa 角色表现组件。</summary>
        public void Configure(ClientBattleSession battleSession, YefaCharacterPresenter characterPresenter, CharacterLubanPresentationConfig characterPresentationConfig)
        {
            session = battleSession != null ? battleSession : throw new ArgumentNullException(nameof(battleSession));
            presenter = characterPresenter != null ? characterPresenter : throw new ArgumentNullException(nameof(characterPresenter));
            presentationConfig = characterPresentationConfig ?? throw new ArgumentNullException(nameof(characterPresentationConfig));
            presenter.ConfigureAnimationBindings(presentationConfig.NormalAttackBindings);
        }

        /// <summary>在本帧全部预测和回滚完成后应用一次只读表现快照，确保权威纠正立即可见。</summary>
        private void LateUpdate()
        {
            if (session == null || presenter == null || !session.HasAuthoritativeBaseline) return;
            CharacterState state = session.CurrentState;
            if (state.Tick < 0) return;
            SelectAttackAnimationBindings(state);
            presenter.ApplySnapshot(CreatePresentationSnapshot(state));
        }

        /// <summary>在一次四段普攻开始时根据起手移动状态选择普通或移动动画，并在动作结束后恢复普通绑定。</summary>
        private void SelectAttackAnimationBindings(CharacterState state)
        {
            bool isNormalAttack = state.ActionKind >= CharacterActionKind.Attack1 && state.ActionKind <= CharacterActionKind.Attack4;
            if (!isNormalAttack)
            {
                if (usesMovingAttackBindings)
                {
                    presenter.ConfigureAnimationBindings(presentationConfig.NormalAttackBindings);
                    usesMovingAttackBindings = false;
                }
                configuredAttackSequence = 0U;
                return;
            }
            uint actionSequence = unchecked((uint)(state.Tick - state.ActionElapsedTicks + 1));
            bool shouldUseMovingBindings = state.UsesMovingAttackVariant;
            if (configuredAttackSequence == actionSequence && shouldUseMovingBindings == usesMovingAttackBindings) return;
            configuredAttackSequence = actionSequence;
            if (shouldUseMovingBindings == usesMovingAttackBindings) return;
            presenter.ConfigureAnimationBindings(shouldUseMovingBindings ? presentationConfig.MovingAttackBindings : presentationConfig.NormalAttackBindings);
            usesMovingAttackBindings = shouldUseMovingBindings;
        }

        /// <summary>把纯 C# 角色状态显式映射为不依赖 GameNative 的表现层快照。</summary>
        private CharacterPresentationSnapshot CreatePresentationSnapshot(CharacterState state)
        {
            CharacterRuntimeConfig config = session.RuntimeConfig;
            Vector3 position = new Vector3((float)CharacterFixedPoint.ToUnits(state.Position.X), (float)CharacterFixedPoint.ToUnits(state.Position.Y), (float)CharacterFixedPoint.ToUnits(state.Position.Z));
            if (state.FacingXRaw < 0L) lastFacing = CharacterFacingDirection.Left;
            else if (state.FacingXRaw > 0L) lastFacing = CharacterFacingDirection.Right;
            CharacterLocomotionPresentationState locomotion = MapLocomotion(state);
            CharacterActionPresentationState action = MapAction(state);
            int actionTick = state.ActionElapsedTicks;
            int actionDurationTicks = state.ActionKind == CharacterActionKind.None ? 0 : config.GetAction(state.ActionKind).TotalTicks;
            uint actionSequence = action == CharacterActionPresentationState.None ? 0U : CreateActionSequence(state, action);
            float normalizedTime = CharacterPresentationSnapshot.CalculateNormalizedActionTime(actionTick, actionDurationTicks);
            return new CharacterPresentationSnapshot((uint)state.Tick, position, lastFacing, locomotion, action, actionSequence, actionTick, actionDurationTicks, normalizedTime, state.Hp, config.Stats.MaxHp, session.DamageEventSequence, session.LatestDamageAmount, session.LatestDamageWasCritical);
        }

        /// <summary>显式处理 Jump、Rise、Fall 和 Land 的表现归并，避免跨程序集直接转换枚举。</summary>
        private static CharacterLocomotionPresentationState MapLocomotion(CharacterState state)
        {
            if (state.IsDead) return CharacterLocomotionPresentationState.Dead;
            switch (state.LocomotionState)
            {
                case CharacterLocomotionState.Idle: return CharacterLocomotionPresentationState.Idle;
                case CharacterLocomotionState.Walk: return CharacterLocomotionPresentationState.Walk;
                case CharacterLocomotionState.Run: return CharacterLocomotionPresentationState.Run;
                case CharacterLocomotionState.Sprint: return CharacterLocomotionPresentationState.Sprint;
                case CharacterLocomotionState.Jump:
                case CharacterLocomotionState.Rise: return CharacterLocomotionPresentationState.Rising;
                case CharacterLocomotionState.Fall: return CharacterLocomotionPresentationState.Falling;
                case CharacterLocomotionState.Land: return CharacterLocomotionPresentationState.Landing;
                case CharacterLocomotionState.Dead: return CharacterLocomotionPresentationState.Dead;
                default: throw new ArgumentOutOfRangeException(nameof(state), $"Unknown character locomotion state {state.LocomotionState}.");
            }
        }

        /// <summary>把共享动作状态映射为表现动作，并用服务器命中事件短暂覆盖为受击动作。</summary>
        private CharacterActionPresentationState MapAction(CharacterState state)
        {
            if (state.IsDead) return CharacterActionPresentationState.Death;
            if (session.IsHitReactionActive) return CharacterActionPresentationState.HitReaction;
            if (state.ActionKind == CharacterActionKind.None && state.LocomotionState == CharacterLocomotionState.Jump) return CharacterActionPresentationState.JumpStart;
            switch (state.ActionKind)
            {
                case CharacterActionKind.None:
                case CharacterActionKind.Land: return CharacterActionPresentationState.None;
                case CharacterActionKind.DodgeForward: return CharacterActionPresentationState.DodgeForward;
                case CharacterActionKind.DodgeBackward: return CharacterActionPresentationState.DodgeBackward;
                case CharacterActionKind.Attack1: return CharacterActionPresentationState.Attack1;
                case CharacterActionKind.Attack2: return CharacterActionPresentationState.Attack2;
                case CharacterActionKind.Attack3: return CharacterActionPresentationState.Attack3;
                case CharacterActionKind.Attack4: return CharacterActionPresentationState.Attack4;
                case CharacterActionKind.HeavyAttack: return CharacterActionPresentationState.HeavyAttack;
                case CharacterActionKind.Skill: return CharacterActionPresentationState.Skill;
                case CharacterActionKind.Ultimate: return CharacterActionPresentationState.Ultimate;
                default: throw new ArgumentOutOfRangeException(nameof(state), $"Unknown character action kind {state.ActionKind}.");
            }
        }

        /// <summary>从确定性动作起始 Tick 或权威伤害事件序号派生稳定重播键，回滚重放不会产生伪动作实例。</summary>
        private uint CreateActionSequence(CharacterState state, CharacterActionPresentationState action)
        {
            if (action == CharacterActionPresentationState.HitReaction) return session.DamageEventSequence;
            if (action == CharacterActionPresentationState.Death) return unchecked((uint)state.Tick + 1U);
            if (action == CharacterActionPresentationState.JumpStart) return unchecked((uint)state.Tick + 1U);
            return unchecked((uint)(state.Tick - state.ActionElapsedTicks + 1));
        }

        /// <summary>绘制无需额外 Canvas 资产的操作、连接、延迟、预测和角色状态面板。</summary>
        private void OnGUI()
        {
            if (session == null) return;
            string pingText = session.PingMilliseconds < 0.0d ? "--" : session.PingMilliseconds.ToString("F1");
            CharacterState state = session.CurrentState;
            GUILayout.BeginArea(new Rect(16f, 16f, 680f, 245f), GUI.skin.box);
            GUILayout.Label("PromeArchTrial - Luban + Protobuf Server Authoritative Character");
            GUILayout.Label("WASD Move | Ctrl Walk | Shift Sprint | Space Jump | RMB Dodge | LMB Tap Combo / Hold Heavy | E Skill | R Ultimate");
            GUILayout.Label($"Connection: {session.ConnectionState} | {session.StatusText}");
            GUILayout.Label($"ClientTick: {session.ClientTick} | ServerTick: {session.ServerTick} | AckTick: {session.AcknowledgedClientTick} | Ping: {pingText} ms");
            GUILayout.Label($"PositionError: {session.LastPositionError:F4} | Threshold Rollbacks: {session.RollbackCount} | Full Corrections: {session.CorrectionCount}");
            if (session.HasAuthoritativeBaseline) GUILayout.Label($"State: {state.LocomotionState}/{state.ActionKind} | HP: {state.Hp}/{session.RuntimeConfig.Stats.MaxHp} | Core: {state.CoreEnergy} | Ultimate: {state.UltimateEnergy} | Hash: 0x{state.StableHash:X16}");
            GUILayout.EndArea();
        }
    }
}
