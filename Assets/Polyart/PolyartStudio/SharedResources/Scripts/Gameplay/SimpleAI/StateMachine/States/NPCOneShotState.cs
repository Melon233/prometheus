using UnityEngine;
using static Polyart.NPCStateMachine;

namespace Polyart
{

    public class NPCOneShotState : NPCStateBase
    {
        private NPCAnimationController.OneShotSettings oneShotSettings;
        private float animationLength, timeLeft;

        public NPCOneShotState(NPCStateContext context, NPCState key, bool isStateImplemented, AnimationClip animation) : base(context, key, isStateImplemented)
        {
            animationLength = animation.length;
            float blendTime = animationLength > 0.5f ? 0.2f : animationLength / 10f;

            oneShotSettings = new NPCAnimationController.OneShotSettings(animation, blendTime);
        }

        public override void EnterState()
        {
            context.animController.StartOneShot(oneShotSettings);

            timeLeft = animationLength;
        }

        public override void ExitState()
        {
            
        }

        public override NPCState GetNextState()
        {
            if (timeLeft <= 0f)
            {
                return NPCState.Idle;
            }

            return this.stateKey;
        }

        public override void UpdateState()
        {
            timeLeft -= Time.deltaTime;
        }
    }
}