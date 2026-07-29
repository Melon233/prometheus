using System;
using UnityEngine;
using Xuan.Prometheus.Component;
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
        public void OnTriggerEnter(Collider other)
        {
            if (other.CompareTag("Enemy"))
            {
                var effectComp = other.GetComponent<EffectComponent>();
                if (effectComp == null || effectComp.Entity == null)
                {
                    Debug.LogWarning($"无法获取敌人 EffectComponent：{other.name}");
                    return;
                }
                Debug.Log($"攻击命中：{effectComp.name}");
                effectComp.toAddEffects.Add(new DamageEffect(Entity, effectComp.Entity, propComp.GetAttackDamage()));
                effectComp.toAddEffects.Add(new FireDotEffect(Entity, effectComp.Entity));
                effectComp.toAddEffects.Add(new StiffnessEffect(Entity, effectComp.Entity, 3f));
            }
        }

        public override void AfterNew()
        {
            LogicGroup = OrderTag.Gameplay;
            Entity.TryGetComp(out inputComp);
            Entity.TryGetComp(out spineComp);
            Entity.TryGetComp(out atkComp);
            Entity.TryGetComp(out motionComp);
            Entity.TryGetComp(out propComp);
            Entity.TryGetComp(out evtComp);
            Entity.TryGetComp(out specialAtkComp);
            Entity.TryGetComp(out skillComp);
            Entity.TryGetComp(out ultComp);
            // Entity.TryGetComp(out coreTalentComp);
            atkExecutor = spineComp.animationLib.atkExecutor;
            groundMoveExecutor = spineComp.animationLib.groundMoveExecutor;
            ultimateExecutor = spineComp.animationLib.ultimateExecutor;
            skillExecutor = spineComp.animationLib.skillExecutor;
            specialAttackExecutor = spineComp.animationLib.specialAttackExecutor;
            evtComp.AddListener<MotionBlockerStartEvent>(OnMotionBlockerStart);
            evtComp.AddListener<MotionBlockerEndEvent>(OnMotionBlockerEnd);
            // Entity.AddLogic<GroundMoveLogic>();
            atkComp.atkCollider.handler = this;
            skillComp.colliderProxy.handler = this;
            ultComp.colliderProxy.handler = this;
            specialAtkComp.colliderProxy.handler = this;
            atkComp.minComboInterval = 0.5f;
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
            var t = atkComp.elapsedComboTime += dt;

            if (t > atkComp.maxComboInterval)
            {
                atkComp.canCombo = true;
                atkComp.nextComboIndex = 0;
            }
            else if (t > atkComp.minComboInterval)
            {
                atkComp.canCombo = true;
                if (atkComp.nextComboIndex > atkComp.maxComboIndex) atkComp.nextComboIndex = 0;
            }

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
                atkComp.curTrackEntry = atkExecutor.Execute(atkComp.nextComboIndex, inputComp.moveDir != Vector2.zero);
                atkComp.curTrackEntry.OnStop(() => evtComp.Invoke<MotionBlockerEndEvent>(new()));
                atkComp.nextComboIndex++;
                atkComp.canCombo = false;
                atkComp.elapsedComboTime = 0;
            }

            if (inputComp.wasSkillPressedThisFrame)
            {
                evtComp.Invoke<MotionBlockerStartEvent>(new());
                atkComp.curTrackEntry = skillExecutor.Execute();
                atkComp.curTrackEntry.OnStop(() => evtComp.Invoke<MotionBlockerEndEvent>(new()));
            }
            if (inputComp.wasUltPressedThisFrame)
            {
                evtComp.Invoke<MotionBlockerStartEvent>(new());
                atkComp.curTrackEntry = ultimateExecutor.Execute();
                atkComp.curTrackEntry.OnStop(() => evtComp.Invoke<MotionBlockerEndEvent>(new()));
            }
        }


        public override void OnDispose()
        {
        }

        public override void OnEnable()
        {

        }

        public override void OnDisable()
        {

        }
    }
}