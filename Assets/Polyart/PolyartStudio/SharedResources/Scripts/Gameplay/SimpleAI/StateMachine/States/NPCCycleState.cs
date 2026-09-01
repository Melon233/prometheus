using UnityEngine;
using static Polyart.NPCStateMachine;

namespace Polyart
{
    public class NPCCycleState : NPCStateBase
    {
        private float loopTimeMin = 3f, loopTimeMax = 5f, loopTimeLeft, blendOutTime = 1.0f;
        private bool isBlendingOut = false;
        private NPCAnimationController.CycleSettings cycleSettings;

        public NPCCycleState(NPCStateContext context, NPCStateMachine.NPCState key, bool isStateImplemented, float midDuration, float maxDuration, 
                                AnimationClip startAnim, AnimationClip loopAnim, AnimationClip endAnim) : base(context, key, isStateImplemented)
        {
            this.loopTimeMin = midDuration;
            this.loopTimeMax = maxDuration;

            cycleSettings = new NPCAnimationController.CycleSettings(0.3f, startAnim, loopAnim, 0.3f, endAnim);
        }

        public override void EnterState()
        {
            context.animController.StartCycle(cycleSettings);

            loopTimeLeft = cycleSettings.blendInClip.length;
            loopTimeLeft += Random.Range(loopTimeMin, loopTimeMax);
            isBlendingOut = false;
        }

        public override void ExitState()
        {

        }

        public override NPCStateMachine.NPCState GetNextState()
        {
            if (cycleSettings.blendInClip == null || isStateImplemented == false)
                return NPCState.Idle;

            if (loopTimeLeft <= 0)
            {
                if (!isBlendingOut)
                { 
                    context.animController.EndCycle();

                    blendOutTime = cycleSettings.blendOutClip.length;
                    isBlendingOut = true;

                    return this.stateKey;
                }
                if (blendOutTime <= 0) 
                {
                    return NPCStateMachine.NPCState.Idle;
                }
            }

            return this.stateKey;
        }

        public override void UpdateState()
        {
            loopTimeLeft -= Time.deltaTime;
            blendOutTime -= Time.deltaTime;
        }
    }
}