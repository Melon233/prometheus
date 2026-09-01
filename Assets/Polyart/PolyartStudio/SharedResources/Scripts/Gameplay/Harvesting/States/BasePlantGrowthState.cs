namespace Polyart
{

    public abstract class BasePlantGrowthState : BaseState<HarvestingStateMachine.PlantsGrowthState>
    {
        protected HarvestingStateMachine.PlantsGrowthContext context;
        protected BasePlantGrowthState(HarvestingStateMachine.PlantsGrowthState key, HarvestingStateMachine.PlantsGrowthContext context) : base(key)
        {
            this.context = context;
        }
    }
}