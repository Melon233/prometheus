using UnityEngine;

namespace Polyart
{

    public class HarvestedState : BasePlantGrowthState
    {
        private GameObject prefab;

        public HarvestedState(HarvestingStateMachine.PlantsGrowthState key, HarvestingStateMachine.PlantsGrowthContext context) : base(key, context)
        {
        }

        public override void EnterState()
        {
            prefab = GameObject.Instantiate(context.harvestedStage, context.transform);
        }

        public override void ExitState()
        {
            throw new System.NotImplementedException();
        }

        public override HarvestingStateMachine.PlantsGrowthState GetNextState()
        {
            return HarvestingStateMachine.PlantsGrowthState.Harvested;
        }

        public override void UpdateState()
        {

        }
    }
}