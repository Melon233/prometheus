using UnityEngine;
using Xuan.Prometheus.Component;
using Xuan.Prometheus.Logic.Talent;

namespace Xuan.Prometheus.Logic
{
    public class TalentLogic : Logic, ITriggerHandler, IAttacker
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
                effectComp.toAddEffects.Add(new StiffnessEffect(Entity, effectComp.Entity, 2f));
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
            // Entity.TryGetComp(out specialAtkComp);
            // Entity.TryGetComp(out skillComp);
            // Entity.TryGetComp(out ultComp);
            // Entity.TryGetComp(out coreTalentComp);
            atkExecutor = spineComp.animationLib.atkExecutor;
            groundMoveExecutor = spineComp.animationLib.groundMoveExecutor;
            ultimateExecutor = spineComp.animationLib.ultimateExecutor;
            skillExecutor = spineComp.animationLib.skillExecutor;
            atkComp.atkCollider.handler = this;
            atkComp.minComboInterval = 0.5f;
        }

        public override bool CanEnable()
        {
            return inputComp.wasAtkPressedThisFrame || inputComp.wasSkillPressedThisFrame || inputComp.wasUltPressedThisFrame;
        }

        public override bool CanDisable()
        {
            if (atkComp.curTrackEntry?.Animation == null) return true;
            return atkComp.curTrackEntry.NormalizedTime() >= 1f;
        }

        public override void OnUpdate(float dt)
        {
            if (inputComp.wasAtkPressedThisFrame && atkComp.canCombo)
            {
                spineComp.SetFaceDir(inputComp.moveDir);
                atkComp.curTrackEntry = atkExecutor.Execute(atkComp.nextComboIndex, inputComp.moveDir != Vector2.zero);
                atkComp.nextComboIndex++;
                atkComp.canCombo = false;
                atkComp.elapsedComboTime = 0;
            }

            if (inputComp.wasSkillPressedThisFrame)
            {
                atkComp.curTrackEntry = skillExecutor.Execute();
            }
            if (inputComp.wasUltPressedThisFrame)
            {
                atkComp.curTrackEntry = ultimateExecutor.Execute();
            }
        }


        public override void OnDispose()
        {
        }

        public override void OnEnable()
        {
            Entity.BlockLogic<GroundMoveLogic>();
            Entity.BlockLogic<JumpLogic>();
            Entity.BlockLogic<MotionLogic>();
            Entity.BlockLogic<RotateLogic>();
            Entity.BlockLogic<DodgeLogic>();
        }

        public override void OnDisable()
        {
            Entity.UnBlockLogic<GroundMoveLogic>();
            Entity.UnBlockLogic<MotionLogic>();
            Entity.UnBlockLogic<RotateLogic>();
            Entity.UnBlockLogic<JumpLogic>();
            Entity.UnBlockLogic<DodgeLogic>();
        }
    }
}