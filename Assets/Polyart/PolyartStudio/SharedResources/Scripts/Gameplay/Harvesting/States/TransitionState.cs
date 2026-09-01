using System.Collections.Generic;
using UnityEngine;

namespace Polyart {
    public class TransitionState : BasePlantGrowthState
    {
        private int stageNum;
        private GameObject meshOut, meshIn;
        private const float transitionDuration = 3f;
        private float transitionAlpha;
        private List<Material> inMaterials, outMaterials;
        private Shader foliageTransitionShader, propTransitionShader;

        public TransitionState(HarvestingStateMachine.PlantsGrowthState key, HarvestingStateMachine.PlantsGrowthContext context, Shader foliageTransitionShader, Shader propTransitionShader) : base(key, context)
        {
            stageNum = 0;
            this.foliageTransitionShader = foliageTransitionShader;
            this.propTransitionShader = propTransitionShader;
        }

        public override void EnterState()
        {
            stageNum++;

            meshOut = context.currMesh;
            meshIn = GameObject.Instantiate(context.plantStages[stageNum].mesh, context.transform);

            SetMaterials(true);
            SetMaterials(false);

            transitionAlpha = transitionDuration;
        }

        public override void ExitState()
        {
            GameObject.Destroy(meshOut);
            context.currMesh = meshIn;
        }

        public override HarvestingStateMachine.PlantsGrowthState GetNextState()
        {
            if (transitionAlpha > 0f)
                return stateKey;

            if (stageNum == context.plantStages.Count - 1)
                return HarvestingStateMachine.PlantsGrowthState.Grown;

            return HarvestingStateMachine.PlantsGrowthState.Growing;
        }

        public override void UpdateState()
        {
            foreach (Material mat in outMaterials)
            {
                mat.SetFloat("_TransitionAlpha", transitionAlpha / transitionDuration);
            }
            foreach (Material mat in inMaterials)
            {
                mat.SetFloat("_TransitionAlpha", 1f - (transitionAlpha / transitionDuration));
            }

            transitionAlpha -= Time.deltaTime;
        }

        private void SetMaterials(bool isIn)
        {
            GameObject go;
            if (isIn)
            {
                go = meshIn;
                inMaterials = new List<Material>();
            }
            else
            {
                go = meshOut;
                outMaterials = new List<Material>();
            }

            LODGroup lodGroup = go.GetComponent<LODGroup>();
            if (lodGroup != null)
            {
                LOD[] lods = lodGroup.GetLODs();
                foreach (LOD lod in lods)
                {
                    foreach (Renderer renderer in lod.renderers)
                    {
                        if (renderer != null)
                        {
                            // Iterate through all materials of the renderer
                            foreach (Material mat in renderer.materials)
                            {
                                if (mat != null)
                                {
                                    if (mat.shader.name.ToLower().Contains("prop"))                                    
                                        mat.shader = propTransitionShader;                                    
                                    else
                                        mat.shader = foliageTransitionShader;

                                    if (isIn)
                                        inMaterials.Add(mat);
                                    else
                                        outMaterials.Add(mat);
                                }
                            }
                        }
                    }
                }
            }
            else
            {
                Renderer renderer = go.GetComponent<Renderer>();
                // Iterate through all materials of the renderer
                foreach (Material mat in renderer.materials)
                {
                    if (mat != null)
                    {
                        if (mat.shader.name.ToLower().Contains("prop"))
                            mat.shader = propTransitionShader;
                        else
                            mat.shader = foliageTransitionShader;

                        if (isIn)
                            inMaterials.Add(mat);
                        else
                            outMaterials.Add(mat);
                    }
                }
            }
        }

    }

}