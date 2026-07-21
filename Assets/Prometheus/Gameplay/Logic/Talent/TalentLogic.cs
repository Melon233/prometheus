using UnityEngine;
using UnityEngine.UIElements;
using Xuan.Prometheus.Component;
using Xuan.Prometheus.Logic.Talent;
using Animation = Xuan.Prometheus.Component.Animation;

namespace Xuan.Prometheus.Logic
{
    public class TalentLogic : Logic, ICollisionHandler
    {
        InputComponent inputComp;
        SpineComponent spineComp;
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


        public void OnTriggerEnter(Collider other)
        {
            if (other.CompareTag("Enemy"))
            {
                var comp = other.GetComponent<PropertyComponent>();
                comp.OnTakeDamage(30);
                // DmgShower.Ins.ShowDamage(30.ToString(), other.transform.position);
                if (!comp.Entity.HasLogic<FireDotLogic>())
                {
                    other.GetComponent<PropertyComponent>().Entity.AddCompRuntime<FireDotComponent>();
                    other.GetComponent<PropertyComponent>().Entity.AddLogicRuntime<FireDotLogic>();
                }
            }
        }

        public override void AfterNew()
        {
            LogicGroup = LogicGroup.Gameplay;
            Entity.TryGetComp(out inputComp);
            Entity.TryGetComp(out spineComp);
            Entity.TryGetComp(out atkComp);
            Entity.TryGetComp(out specialAtkComp);
            Entity.TryGetComp(out skillComp);
            Entity.TryGetComp(out ultComp);
            Entity.TryGetComp(out coreTalentComp);
            atkExecutor = spineComp.charaAniLib.atkExecutor;
            groundMoveExecutor = spineComp.charaAniLib.groundMoveExecutor;
            ultimateExecutor = spineComp.charaAniLib.ultimateExecutor;
            skillExecutor = spineComp.charaAniLib.skillExecutor;
            spineComp.AddEventListener((entry, evt) =>
            {
                if (evt.ToString() == "hit_start")
                    atkComp.atkCollider.cod.enabled = true;
                else if (evt.ToString() == "hit_end") atkComp.atkCollider.cod.enabled = false;
            });

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
                atkComp.curTrackEntry = atkExecutor.Execute(atkComp.nextComboIndex, inputComp.moveDir != Vector2.zero);
                atkComp.nextComboIndex++;
                atkComp.canCombo = false;
                atkComp.elapsedComboTime = 0;
            }

            if (inputComp.wasSkillPressedThisFrame)
            {
                atkComp.curTrackEntry = skillExecutor.Execute(spineComp);
            }
            if (inputComp.wasUltPressedThisFrame)
            {
                atkComp.curTrackEntry = ultimateExecutor.Execute(spineComp);
            }
        }


        public override void OnDispose()
        {
        }

        public override void OnEnable()
        {
            Entity.BlockLogic<GroundMoveLogic>();
            // Entity.BlockLogic<JumpLogic>();
            Entity.BlockLogic<MotionLogic>();
            Entity.BlockLogic<RotateLogic>();
        }

        public override void OnDisable()
        {
            Entity.UnBlockLogic<GroundMoveLogic>();
            Entity.UnBlockLogic<MotionLogic>();
            Entity.UnBlockLogic<RotateLogic>();
            // Entity.UnBlockLogic<JumpLogic>();
        }
    }
}