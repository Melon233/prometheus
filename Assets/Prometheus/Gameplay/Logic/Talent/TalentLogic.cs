using UnityEngine;
using Xuan.Prometheus.Component;
using Xuan.Prometheus.Effects;
using Xuan.Prometheus.Logic.Talent;

namespace Xuan.Prometheus.Logic
{
    public class TalentLogic : Logic, ITriggerHandler
    {
        InputComponent inputComp;
        SpineComponent spineComp;
        SpineComponent motionComp;
        AttackComponent atkComp;
        SpecialAttackComponent specialAtkComp;
        SkillComponent skillComp;
        UltimateComponent ultComp;
        CoreTalentComponent coreTalentComp;
        AttackExecutor atkExecutor;
        GroundMoveExecutor groundMoveExecutor;
        UltimateExecutor ultimateExecutor;
        SkillExecutor skillExecutor;
        SpecialAttackExecutor specialAttackExecutor;
        PropertyComponent propComp;
        EventComponent evtComp;
        EffectComponent effectComp;

        /// <summary>
        /// 将玩家命中转换为带完整数值和语义标签的信号，由已注册规则组合即时伤害、燃烧和眩晕。
        /// </summary>
        public void OnTriggerEnter(Collider other)
        {
            if (!Entity.IsActive || other == null) return;
            if (other.CompareTag("Enemy"))
            {
                PropertyComponent targetProperty = other.GetComponent<PropertyComponent>();
                if (targetProperty == null || targetProperty.Entity == null || targetProperty.IsDead || !targetProperty.Entity.IsActive)
                {
                    Debug.LogWarning($"无法获取敌人 PropertyComponent 或实体绑定：{other.name}");
                    return;
                }
                float requestedDamage = propComp.GetCalculatedDamage();
                EffectSignal signal = new EffectSignal(EffectSignalType.HitConfirmed, Entity, targetProperty.Entity, Entity, requestedDamage, requestedDamage, EffectTag.Attack | EffectTag.NormalAttack | EffectTag.Fire | EffectTag.Control, "Player.NormalAttack", position: other.transform.position);
                effectComp.Runtime.Publish(signal);
            }
        }

        public override void AfterNew()
        {
            OrderTag = OrderTag.Gameplay;
            Entity.TryGetComp(out inputComp);
            Entity.TryGetComp(out spineComp);
            Entity.TryGetComp(out atkComp);
            Entity.TryGetComp(out motionComp);
            Entity.TryGetComp(out propComp);
            Entity.TryGetComp(out evtComp);
            Entity.TryGetComp(out specialAtkComp);
            Entity.TryGetComp(out skillComp);
            Entity.TryGetComp(out ultComp);
            Entity.TryGetComp(out coreTalentComp);
            Entity.TryGetComp(out effectComp);
            atkExecutor = spineComp.animationLib.atkExecutor;
            groundMoveExecutor = spineComp.animationLib.groundMoveExecutor;
            ultimateExecutor = spineComp.animationLib.ultimateExecutor;
            skillExecutor = spineComp.animationLib.skillExecutor;
            specialAttackExecutor = spineComp.animationLib.specialAttackExecutor;
            evtComp.AddListener<MotionBlockerStartEvent>(OnMotionBlockerStart);
            evtComp.AddListener<MotionBlockerEndEvent>(OnMotionBlockerEnd);
            atkComp.atkCollider.handler = this;
            skillComp.colliderProxy.handler = this;
            ultComp.colliderProxy.handler = this;
            specialAtkComp.colliderProxy.handler = this;
            effectComp.RegisterCombatFlowTriggers(Entity);
        }

        private void OnMotionBlockerStart(MotionBlockerStartEvent @event)
        {
            Entity.BlockLogic<GroundMoveLogic>();
            Entity.BlockLogic<JumpLogic>();
            Entity.BlockLogic<MotionLogic>();
            Entity.BlockLogic<RotateLogic>();
            Entity.BlockLogic<DodgeLogic>();
            atkComp.isBlocking = true;
        }
        private void OnMotionBlockerEnd(MotionBlockerEndEvent @event)
        {
            Entity.UnBlockLogic<GroundMoveLogic>();
            Entity.UnBlockLogic<MotionLogic>();
            Entity.UnBlockLogic<RotateLogic>();
            Entity.UnBlockLogic<JumpLogic>();
            Entity.UnBlockLogic<DodgeLogic>();
            atkComp.isBlocking = false;
        }


        public override bool CanEnable()
        {
            return true;
            // return inputComp.wasAtkPressedThisFrame || inputComp.wasSkillPressedThisFrame || inputComp.wasUltPressedThisFrame;
        }

        public override bool CanDisable()
        {
            return false;
            // if (atkComp.curTrackEntry?.Animation == null) return true;
            // return atkComp.curTrackEntry.NormalizedTime() >= 1f;
        }

        public override void OnUpdate(float dt)
        {
            if ((atkComp.elapsedComboTime += dt) > atkComp.maxComboInterval)
                atkComp.nextComboIndex = 0;

            if (inputComp.wasAtkPressed && specialAtkComp.canSpecial)
            {
                specialAtkComp.specialTimer.OnUpdate(dt);
                if (specialAtkComp.specialTimer.IsTimeOut)
                {
                    specialAtkComp.specialTimer.Reset();
                    specialAtkComp.canSpecial = false;
                    evtComp.Invoke<MotionBlockerStartEvent>(new());
                    atkComp.curTrackEntry = specialAttackExecutor.Execute();
                    atkComp.curTrackEntry.OnStop(() => evtComp.Invoke<MotionBlockerEndEvent>(new()));
                }
            }
            if (!inputComp.wasAtkPressed)
            {
                specialAtkComp.canSpecial = true;
                specialAtkComp.specialTimer.Reset();
            }
            if (inputComp.wasAtkPressedThisFrame && atkComp.canCombo)
            {
                spineComp.SetFaceDir(inputComp.moveDir);
                evtComp.Invoke<MotionBlockerStartEvent>(new());
                atkComp.curTrackEntry = atkExecutor.Execute(atkComp.nextComboIndex, inputComp.moveDir != Vector2.zero, propComp.AtkSpeed);
                atkComp.curTrackEntry.OnStop(() => evtComp.Invoke<MotionBlockerEndEvent>(new()));
                atkComp.nextComboIndex++;
                if (atkComp.nextComboIndex > atkComp.maxComboIndex) atkComp.nextComboIndex = 0;
                atkComp.elapsedComboTime = 0;
            }

            if (propComp.CanUseActiveSkill && inputComp.wasSkillPressedThisFrame)
            {
                evtComp.Invoke<MotionBlockerStartEvent>(new());
                atkComp.curTrackEntry = skillExecutor.Execute();
                atkComp.curTrackEntry.OnStop(() => evtComp.Invoke<MotionBlockerEndEvent>(new()));
            }
            if (propComp.CanUseActiveSkill && inputComp.wasUltPressedThisFrame)
            {
                evtComp.Invoke<MotionBlockerStartEvent>(new());
                atkComp.curTrackEntry = ultimateExecutor.Execute();
                atkComp.curTrackEntry.OnStop(() => evtComp.Invoke<MotionBlockerEndEvent>(new()));
            }
        }


        /// <summary>回收时关闭全部命中盒、解绑 ColliderProxy 并对称注销事件，阻止死亡动画期间继续造成伤害。</summary>
        public override void OnDispose()
        {
            DisableAttackColliders();
            if (atkComp != null && atkComp.atkCollider != null && ReferenceEquals(atkComp.atkCollider.handler, this)) atkComp.atkCollider.handler = null;
            if (specialAtkComp != null && specialAtkComp.colliderProxy != null && ReferenceEquals(specialAtkComp.colliderProxy.handler, this)) specialAtkComp.colliderProxy.handler = null;
            if (skillComp != null && skillComp.colliderProxy != null && ReferenceEquals(skillComp.colliderProxy.handler, this)) skillComp.colliderProxy.handler = null;
            if (ultComp != null && ultComp.colliderProxy != null && ReferenceEquals(ultComp.colliderProxy.handler, this)) ultComp.colliderProxy.handler = null;
            if (evtComp != null) evtComp.RemoveListener<MotionBlockerStartEvent>(OnMotionBlockerStart);
            if (evtComp != null) evtComp.RemoveListener<MotionBlockerEndEvent>(OnMotionBlockerEnd);
            effectComp = null;
        }

        public override void OnEnable()
        {

        }

        /// <summary>中断普通控制状态下的攻击表现；死亡回收只关闭命中盒，不覆盖已经开始播放的死亡动画。</summary>
        public override void OnDisable()
        {
            DisableAttackColliders();
            if (!propComp.IsDead && atkComp.curTrackEntry != null && !atkComp.curTrackEntry.IsComplete) spineComp.Stop(0, 0f);
            atkComp.curTrackEntry = null;
            if (atkComp.isBlocking) evtComp.Invoke(new MotionBlockerEndEvent());
        }

        /// <summary>
        /// 在 Stun 中断攻击或技能时立即关闭全部命中盒，防止动画已停止但碰撞体继续造成伤害。
        /// </summary>
        private void DisableAttackColliders()
        {
            if (atkComp != null && atkComp.atkCollider != null && atkComp.atkCollider.cod != null) atkComp.atkCollider.cod.enabled = false;
            if (specialAtkComp != null && specialAtkComp.colliderProxy != null && specialAtkComp.colliderProxy.cod != null) specialAtkComp.colliderProxy.cod.enabled = false;
            if (skillComp != null && skillComp.colliderProxy != null && skillComp.colliderProxy.cod != null) skillComp.colliderProxy.cod.enabled = false;
            if (ultComp != null && ultComp.colliderProxy != null && ultComp.colliderProxy.cod != null) ultComp.colliderProxy.cod.enabled = false;
        }
    }
}
