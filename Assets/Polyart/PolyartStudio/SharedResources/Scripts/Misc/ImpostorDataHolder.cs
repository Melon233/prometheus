namespace Polyart
{
    using UnityEngine;

    [ExecuteAlways]
    public class ImpostorDataHolder : MonoBehaviour
    {
        public Material material;
        public MaterialPropertyBlock mpb;

        // --- Textures ---
        public Texture MainTex;
        public Texture NormalMap;

        // --- Impostor Grid Params ---
        public float GridSize;
        public float HorizontalSegments;
        public float VerticalSegments;
        public float VerticalOffset;
        public float VerticalStep;

        // --- Color Customization ---
        public float Hue;
        public float Saturation;
        public float Value;
        public float Contrast;
        public float Smoothness;
        public float Metallic;
        public float NormalStrength;

        // --- Opacity ---
        public float AlphaClipThreshold;

        public Vector3 quadScale = Vector3.one;

        private void OnEnable()
        {
            SetMaterialPropertyBlockData();
            BindMaterialPropertyBlock();
        }

        public void SetImpostorMaterial(Material material)
        {
            if (material == null)
            {
                Debug.LogError("Polyart Impostor Material is null");
                return;
            }

            this.material = material;

            MeshRenderer meshRenderer = GetComponent<MeshRenderer>();
            if (meshRenderer == null)
            {
                Debug.LogWarning($"No Mesh Renderer found in impostor {gameObject.name}");
                return;
            }

            meshRenderer.sharedMaterial = this.material;
        }

        public void SetMaterialPropertyBlockData()
        {
            if (MainTex == null)
            {
                Debug.LogWarning($"Skipping MPB data setup for {gameObject.name}: MainTex is null.");
                return;
            }
            if (NormalMap == null)
            {
                Debug.LogWarning($"Skipping MPB data setup for {gameObject.name}: NormalMap is null.");
                return;
            }

            if (mpb == null)
                mpb = new MaterialPropertyBlock();
            else
                mpb.Clear();

            // --- Textures ---
            mpb.SetTexture("_MainTex", MainTex);
            mpb.SetTexture("_NormalMap", NormalMap);

            // --- Impostor Grid Params ---
            mpb.SetFloat("_GridSize", GridSize);
            mpb.SetFloat("_HorizontalSegments", HorizontalSegments);
            mpb.SetFloat("_VerticalSegments", VerticalSegments);
            mpb.SetFloat("_VerticalOffset", VerticalOffset);
            mpb.SetFloat("_VerticalStep", VerticalStep);

            // --- Color Customization ---
            mpb.SetFloat("_Hue", Hue);
            mpb.SetFloat("_Saturation", Saturation);
            mpb.SetFloat("_Value", Value);
            mpb.SetFloat("_Contrast", Contrast);
            mpb.SetFloat("_Smoothness", Smoothness);
            mpb.SetFloat("_Metallic", Metallic);
            mpb.SetFloat("_NormalStrength", NormalStrength);

            // --- Opacity ---
            mpb.SetFloat("_AlphaClipThreshold", AlphaClipThreshold);

            mpb.SetVector("_QuadScale", quadScale);
        }

        public void BindMaterialPropertyBlock()
        {
            MeshRenderer meshRenderer = GetComponent<MeshRenderer>();
            if (meshRenderer == null)
            {
                Debug.LogWarning($"No Mesh Renderer found in impostor {gameObject.name}");
                return;
            }

            // Bind MPB data to the renderer
            meshRenderer.SetPropertyBlock(mpb);
        }

        /// <summary>
        /// Populates the float/vector fields from the existing Material and sets texture references.
        /// </summary>
        public void ExtractMaterialValues(Material existingMaterial, Texture savedMainTex, Texture savedNormalMap)
        {
            if (existingMaterial == null)
            {
                Debug.LogError("The provided Material is null.");
                return;
            }

            // --- Textures ---
            MainTex = savedMainTex;
            NormalMap = savedNormalMap;

            // --- Impostor Grid Params ---
            GridSize = existingMaterial.GetFloat("_GridSize");
            HorizontalSegments = existingMaterial.GetFloat("_HorizontalSegments");
            VerticalSegments = existingMaterial.GetFloat("_VerticalSegments");
            VerticalOffset = existingMaterial.GetFloat("_VerticalOffset");
            VerticalStep = existingMaterial.GetFloat("_VerticalStep");

            // --- Color Customization ---
            Hue = existingMaterial.GetFloat("_Hue");
            Saturation = existingMaterial.GetFloat("_Saturation");
            Value = existingMaterial.GetFloat("_Value");
            Contrast = existingMaterial.GetFloat("_Contrast");
            Smoothness = existingMaterial.GetFloat("_Smoothness");
            Metallic = existingMaterial.GetFloat("_Metallic");
            NormalStrength = existingMaterial.GetFloat("_NormalStrength");

            // --- Opacity ---
            AlphaClipThreshold = existingMaterial.GetFloat("_AlphaClipThreshold");
        }

        public void copyMeshScale(Mesh mesh)
        {
            if (mesh == null) return;

            Bounds bounds = mesh.bounds;
            Vector3 meshScale = bounds.size;
            meshScale.z = 1f;

            quadScale = meshScale;
        }
    }
}