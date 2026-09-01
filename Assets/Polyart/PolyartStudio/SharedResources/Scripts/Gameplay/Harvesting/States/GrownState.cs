using UnityEngine;

namespace Polyart
{
    public class GrownState : BasePlantGrowthState
    {
        private GameObject prefab, outlinePrefab;
        private bool isHarvested = false;
        private Shader outlineShader;
        private Camera cam;
        private Bounds bounds;
        private GameObject particle;

        public GrownState(HarvestingStateMachine.PlantsGrowthState key, HarvestingStateMachine.PlantsGrowthContext context, Shader outlineShader, GameObject particle) : base(key, context)
        {
            this.outlineShader = outlineShader;
            this.particle = particle;
        }

        public override void EnterState()
        {
            // Instantiate the final growth stage mesh
            prefab = context.currMesh;


            string layerName = "Interactable";
            int layer = LayerMask.NameToLayer(layerName);
            if (layer == -1)
            {
                Debug.LogWarning($"Layer '{layerName}' not found. Defaulting to layer 0. This Means NO interactions will happen!");
                return;
            }
            prefab.layer = layer;

            HarvestInteraction harvestInteraction = prefab.AddComponent<HarvestInteraction>();

            // Subscribe to the OnHarvested event
            harvestInteraction.OnHarvested += HandleHarvested;

            // Add BoxCollider using LODGroup bounds if available
            LODGroup lodGroup = prefab.GetComponent<LODGroup>();
            if (lodGroup != null)
            {
                LOD[] lods = lodGroup.GetLODs();
                if (lods.Length > 0 && lods[0].renderers.Length > 0)
                {
                    Renderer renderer = lods[0].renderers[0];
                    bounds = renderer.bounds;

                    BoxCollider collider = prefab.AddComponent<BoxCollider>();
                    collider.size = bounds.size;
                    collider.center = prefab.transform.InverseTransformPoint(bounds.center);
                    collider.includeLayers = LayerMask.GetMask("TransparentEffects");
                    collider.excludeLayers = LayerMask.GetMask("Default");
                }
            }

            outlinePrefab = GameObject.Instantiate(context.plantStages[context.numStages - 1].mesh, context.transform);
            SetOutlineMaterials();
            outlinePrefab.SetActive(false);

            cam = Camera.main;
        }

        // Event handler for when the plant is harvested
        private void HandleHarvested(InteractionData data)
        {
            isHarvested = true;

            GameObject.Instantiate(particle, context.transform.position, context.transform.rotation);

            // Destroy the harvested plant
            GameObject.Destroy(prefab);
            GameObject.Destroy(outlinePrefab);
        }

        public override void ExitState()
        {

        }

        public override HarvestingStateMachine.PlantsGrowthState GetNextState()
        {
            if (isHarvested)
            {
                if (context.hasHarvestedState)
                    return HarvestingStateMachine.PlantsGrowthState.Harvested;
                else
                    GameObject.Destroy(context.transform.gameObject);
            }

            return HarvestingStateMachine.PlantsGrowthState.Grown;
        }

        public override void UpdateState()
        {
            if (!cam || !prefab)
                return;

            // Create a ray from the camera
            Ray ray = new Ray(cam.transform.position, cam.transform.forward);

            // Perform raycast with max distance of 4 meters
            outlinePrefab.SetActive(bounds.IntersectRay(ray, out float distance) && distance <= 4f);
        }

        private void SetOutlineMaterials()
        {
            LODGroup lodGroup = outlinePrefab.GetComponent<LODGroup>();
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
                                        mat.shader = outlineShader;
                                }
                            }
                        }
                    }
                }
            }
            else
            {
                Renderer renderer = outlinePrefab.GetComponent<Renderer>();
                // Iterate through all materials of the renderer
                foreach (Material mat in renderer.materials)
                {
                    if (mat != null)
                    {
                        if (mat.shader.name.ToLower().Contains("prop"))
                            mat.shader = outlineShader;
                    }
                }
            }
        }
    }
}