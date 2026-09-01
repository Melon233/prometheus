using System.Collections.Generic;
using UnityEngine;

namespace Polyart
{
    [ExecuteInEditMode]
    public class CropSpawnManager : MonoBehaviour
    {
        public struct CropSpawnInfo
        {
            public Vector3 position;
            public Quaternion rotation;

            public CropSpawnInfo(Vector3 pos, Quaternion rot)
            {
                position = pos;
                rotation = rot;
            }
        }

        private List<CropSpawnInfo> cropSpawnPoints = new List<CropSpawnInfo>();
        public GameObject[] crops;

        public HarvestingStateMachine cropPrefab;
        public int numRows =2, numCols = 4;
        public float rowSpace = 0.5f, colSpace = 0f;
        public float maxTimeVariation = 6f;

        private float boundsMax;
        private Vector3 boundsExtend;

        private Vector3 prevPos;
        private Quaternion prevRot;
        private float prevRows, prevCols;

        private void OnValidate()
        {
            if (maxTimeVariation < 0) maxTimeVariation = 0;

            if (cropPrefab == null)
                return;

            //if (prevCols != numCols || prevRows != numRows)
            //{
                Invoke("SpawnPrefabs", 0f);
            //}
            //else
            //{
            //    UpdateCrops();
            //}

            prevRows = numRows;
            prevCols = numCols;   
        }

        private void DestroyChildren()
        {
            for (int i = transform.childCount - 1; i >= 0; i--)
            {
                var child = transform.GetChild(i);

                if (!Application.isPlaying)
                    DestroyImmediate(child.gameObject);
                else 
                    Destroy(child.gameObject);                
            }
        }

        private void SpawnPrefabs()
        {
            if (!cropPrefab) return;

            if (Application.isPlaying) return;

            DestroyChildren();

            crops = new GameObject[numCols * numRows];

            GameObject prefab = cropPrefab.plantStages[cropPrefab.plantStages.Count - 1].mesh;

            for (int i = 0; i < crops.Length; i++)
            {
                GameObject crop = Instantiate(prefab, transform);
                crop.name = $"EditorCrop_{i}";
                crops[i] = crop;
            }

            UpdateCrops();
        }

        private void Update()
        {
            if (prevPos != transform.position || prevRot != transform.rotation)
            {
                SpawnPrefabs();
            }

            prevPos = transform.position;
            prevRot = transform.rotation;
        }

        public void UpdateCrops()
        {
            if (cropPrefab == null)
                return;

            Bounds bounds;
            if (!GetCropBounds(out bounds))
                return;

            cropSpawnPoints.Clear(); // Clear previous data

            boundsMax = (new Vector3(bounds.extents.x * 2f, 0f, bounds.extents.z * 2f)).magnitude;
            boundsExtend = bounds.extents;

            for (int i = 0; i < numRows; i++)
            {
                for (int j = 0; j < numCols; j++)
                {
                    // Get world-space position
                    Vector3 worldPos = transform.TransformPoint(new Vector3(i * boundsMax + i * rowSpace, 2f, j * boundsMax + j * colSpace));

                    // Raycast downward to find the ground position
                    if (Physics.Raycast(worldPos, Vector3.down, out RaycastHit hit, 10f))
                    {
                        worldPos = hit.point; // Move position to the ground

                        // Generate a deterministic seed based on row and column
                        int seed = i * 1000 + j * 10;
                        Random.InitState(seed);

                        // Generate a random rotation on the Y-axis
                        float randomYRotation = Random.Range(0f, 360f);
                        Quaternion objectRotation = Quaternion.FromToRotation(Vector3.up, hit.normal) * Quaternion.Euler(0, randomYRotation, 0);

                        // Store the projected position and calculated rotation
                        cropSpawnPoints.Add(new CropSpawnInfo(worldPos, objectRotation));

                        crops[i].SetActive(true);
                    }
                    else
                    {
                        cropSpawnPoints.Add(new CropSpawnInfo(worldPos, Quaternion.identity));
                        crops[i].SetActive(false);
                    }
                }
            }

            for (int i = 0; i< crops.Length; i++)
            {
                crops[i].transform.position = cropSpawnPoints[i].position;
                crops[i].transform.rotation = cropSpawnPoints[i].rotation;
            }
        }

        private bool GetCropBounds(out Bounds bounds)
        {
            bounds = new Bounds();

            GameObject grownCrop = cropPrefab.plantStages[cropPrefab.plantStages.Count - 1].mesh;
            if (grownCrop == null)
                return false;

            LODGroup lg = grownCrop.GetComponent<LODGroup>();
            bool hasLods = lg != null;
            if (hasLods)
            {
                hasLods = false;
                var lods = lg.GetLODs();
                if (lods != null)
                {
                    if (lods[0].renderers != null)
                    {
                        bounds = lods[0].renderers[0].bounds;
                        hasLods = true;
                    }
                }
            }
            if (!hasLods)
            {
                var renderer = grownCrop.GetComponent<Renderer>();
                if (renderer != null)
                    bounds = renderer.bounds;
                else
                    return false;
            }

            return true;
        }

        private void Start()
        {
            DestroyChildren();

            crops = new GameObject[numCols * numRows];

            for (int i = 0; i < crops.Length; i++)
            {
                GameObject crop = Instantiate(cropPrefab.gameObject, transform);
                crop.name = $"{cropPrefab.gameObject.name}_{i}";

                HarvestingStateMachine hsm =  crop.GetComponent<HarvestingStateMachine>();
                hsm.RandomizeTimeToGrow( maxTimeVariation);

                crops[i] = crop;

                Debug.Log(crop.name);
            }

            UpdateCrops();
        }

        private void OnDrawGizmos()
        {
            if (cropSpawnPoints != null && cropSpawnPoints.Count > 0)
            {
                Gizmos.color = Color.green; // Set Gizmos color for projected positions

                foreach (var spawnInfo in cropSpawnPoints)
                {
                    // Combine with an additional 45-degree Y-axis rotation
                    Quaternion additionalRotation = Quaternion.Euler(0, 0, 0);
                    Quaternion finalRotation = spawnInfo.rotation * additionalRotation;

                    // Apply position and rotation transformation
                    Matrix4x4 rotationMatrix = Matrix4x4.TRS(spawnInfo.position, finalRotation, Vector3.one);
                    Gizmos.matrix = rotationMatrix;

                    // Draw the cube at the transformed position
                    Gizmos.DrawWireCube(Vector3.zero, boundsExtend * 2); // Bounds size (extents * 2)

                    // Draw bounds as lines (for clarity)
                    Gizmos.color = Color.red;
                    Gizmos.DrawLine(Vector3.zero, Vector3.up * boundsExtend.y * 2);
                }

                // Reset Gizmos matrix to default
                Gizmos.matrix = Matrix4x4.identity;
            }
        }
        
    }
}