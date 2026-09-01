using System.Collections.Generic;
using UnityEngine;

#if UNITY_EDITOR
using UnityEditor;
#endif

namespace Polyart
{
    public class HarvestingStateMachine : StateManager<HarvestingStateMachine.PlantsGrowthState>
    {
        public enum PlantsGrowthState
        {
            Growing,
            Transition,
            Grown,
            Harvested
        }
        
        public class PlantsGrowthContext
        {
            public int numStages;
            public List<PlantStageInfo> plantStages;
            public bool hasHarvestedState;
            public GameObject harvestedStage;
            public Transform transform;
            public GameObject currMesh;
        }

        [System.Serializable]
        public struct PlantStageInfo
        {
            public GameObject mesh;
            public float timeToGrow;

            public PlantStageInfo(float time)
            {
                mesh = null;
                timeToGrow = time;
            }
        }

        PlantsGrowthContext context;
        public List<PlantStageInfo> plantStages;
        public int numStages;
        [Tooltip("Leave null if you dont want to use one.")]
        public GameObject harvestedStage;
        public GameObject beforePlayMesh;
        public Shader foliageTransitionShader, propTransitionShader, outlineShader;
        public GameObject particle;
        public float[] plantStageTimeVariation;
        
        private void Awake()
        {
            if (plantStages == null)
            {
                plantStages = new List<PlantStageInfo>(); 
            }
        }

#if UNITY_EDITOR
        private void OnValidate()
        {
            if (plantStages == null) 
            {
                plantStages = new List<PlantStageInfo>();
            }

            if (beforePlayMesh == null)
            {
                if (plantStages[0].mesh != null)
                {
                    EditorApplication.delayCall += () => { beforePlayMesh = GameObject.Instantiate(plantStages[0].mesh, transform); };                    
                }    
            }
        }
#endif

        public void RandomizeTimeToGrow(float maxTimeVariation)
        {
            plantStageTimeVariation = new float[plantStages.Count];

            for (int i = 0; i < plantStages.Count; i++)
            {
                plantStageTimeVariation[i] = Random.Range(0f, maxTimeVariation);
            }
        }

        private void Start()
        {
            Destroy(beforePlayMesh);

            if (states != null)
            {
                states.Clear();
            }

            context = new PlantsGrowthContext();
            context.numStages = numStages;
            context.plantStages = plantStages;
            context.transform = transform;
            context.hasHarvestedState = harvestedStage != null;
            context.harvestedStage = harvestedStage;
            context.currMesh = Instantiate(plantStages[0].mesh, transform);

            if (plantStageTimeVariation != null)
            {
                if (plantStageTimeVariation.Length > 0)
                    for (int i = 0; i < plantStages.Count; i++)
                    {
                        PlantStageInfo plantStageInfo = plantStages[i];
                        plantStageInfo.timeToGrow += plantStageTimeVariation[i];
                        context.plantStages[i] = plantStageInfo;
                    }
            }

            states.Add(PlantsGrowthState.Growing, new GrowingState(PlantsGrowthState.Growing, context));
            states.Add(PlantsGrowthState.Transition, new TransitionState(PlantsGrowthState.Transition, context, foliageTransitionShader, propTransitionShader));
            states.Add(PlantsGrowthState.Grown, new GrownState(PlantsGrowthState.Grown, context, outlineShader, particle));
            states.Add(PlantsGrowthState.Harvested, new HarvestedState(PlantsGrowthState.Harvested, context));

            currentState = states[PlantsGrowthState.Growing];
            currentState.EnterState();
        }
    }

}