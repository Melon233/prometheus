using System;
using UnityEngine;
using static Polyart.NPCStateMachine;

namespace Polyart
{
    public class NPCIdleState : NPCStateBase
    {
        private float idleTimer = 2f;
        private float idleDurationMin;
        private float idleDurationMax;
        private AnimationClip idleAnim;

        public NPCIdleState(NPCStateContext context, NPCStateMachine.NPCState key, bool isStateImplemented ,AnimationClip idleAnim, float idleDurationMin, float idleDurationMax) : base(context, key, isStateImplemented)
        {
            this.idleAnim = idleAnim;
            this.idleDurationMin = idleDurationMin;
            this.idleDurationMax = idleDurationMax;            
        }

        public override void EnterState()
        {
            if (idleAnim == null)
            {
                Debug.LogError("NPC does not have Idle Animation!");
                return;
            }
            if (isStateImplemented == false)
            {
                Debug.LogError("NPC doesnt have an Idle State!");
                return;
            }

            context.animController.SetIdleAnim(idleAnim);
            context.animController.SetIdle();
            idleTimer = UnityEngine.Random.Range(idleDurationMin, idleDurationMax);
        }

        public override void ExitState()
        {

        }

        public override NPCStateMachine.NPCState GetNextState()
        {
            if (idleTimer > 0f)
                return NPCState.Idle;
            else
            {
                return GetRandomEnumFromSubset((NPCState[])Enum.GetValues(typeof(NPCState)));
            }
        }

        public override void UpdateState()
        {
            idleTimer -= Time.deltaTime;            
        }
    }
}