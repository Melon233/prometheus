using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace Polyart
{

    public class GrowingState : BasePlantGrowthState
    {
        private float timeRemained =1f;
        private int stageNum;
        private GameObject prefab; 

        public GrowingState(HarvestingStateMachine.PlantsGrowthState key, HarvestingStateMachine.PlantsGrowthContext context) : base(key, context)
        {
            stageNum = -1;
        }

        public override void EnterState()
        {
            stageNum++;

            timeRemained = context.plantStages[stageNum].timeToGrow;

            prefab = context.currMesh;
        }

        public override void ExitState()
        {
            context.currMesh = prefab;
        }

        public override HarvestingStateMachine.PlantsGrowthState GetNextState()
        {
            if (timeRemained < 0)
            {
                return HarvestingStateMachine.PlantsGrowthState.Transition;
            }

            return HarvestingStateMachine.PlantsGrowthState.Growing;
        }

        public override void UpdateState()
        {
            timeRemained -= Time.deltaTime;
        }
    }

}