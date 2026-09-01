using UnityEngine;
using UnityEngine.AI;
using static Polyart.NPCStateMachine;

namespace Polyart
{
    public class NPCMoveState : NPCStateBase
    {
        public float range = 15f;  // Radius for random points
        private float agentSpeed;
        private AnimationClip anim;

        public NPCMoveState(NPCStateContext context, NPCStateMachine.NPCState key, bool isStateImplemented, AnimationClip anim, float speed) : base(context, key, isStateImplemented)
        {
            this.anim = anim;
            agentSpeed = speed;
        }

        public override void EnterState()
        {
            if (anim == null)
                return;

            Vector3 randomPoint = RandomNavSphere(context.agent.transform.position, range, -1);
            context.animController.SetMoveAnim(anim);
            context.agent.isStopped = false;
            context.agent.SetDestination(randomPoint);
            context.agent.speed = agentSpeed;
        }

        Vector3 RandomNavSphere(Vector3 origin, float distance, int layerMask)
        {
            Vector3 randomDirection = Random.insideUnitSphere * distance;
            randomDirection += origin;

            NavMeshHit hit;
            if (NavMesh.SamplePosition(randomDirection, out hit, distance, layerMask))
            {
                return hit.position;  // Return the valid point on NavMesh
            }

            return origin;  // If no valid point, return original position
        }

        public override void ExitState()
        {
            context.agent.isStopped = true;
        }

        public override NPCStateMachine.NPCState GetNextState()
        {
            if (anim == null || isStateImplemented == false)
                return NPCState.Idle;

            if (context.agent.remainingDistance > 0f)
                return this.stateKey;
            else
                return NPCState.Idle;
        }

        public override void UpdateState()
        {
            context.animController.UpdateWalkSpeed(context.agent.velocity.magnitude, agentSpeed, context.instantBlend);
        }
    }
}