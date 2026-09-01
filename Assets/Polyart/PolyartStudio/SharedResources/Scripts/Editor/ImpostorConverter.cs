#if UNITY_EDITOR

namespace Polyart
{
    using System.Collections.Generic;
    using UnityEditor;
    using UnityEngine;

    public class ImpostorConverter : EditorWindow
    {
        private const string HARDCODED_MATERIAL_GUID = "cd9b29a17da10cf4bb9b978e2e892462";
        private const string HARDCODED_MESH_GUID = "34e5c368b3fa0d747ab08b6228911286";

        public List<GameObject> impostors = new List<GameObject>();
        public Material polyartImpostorMaterial;
        public Mesh polyartImpostorQuadMesh;

        private void LoadHardcodedAssets()
        {
            // Load Material
            if (polyartImpostorMaterial == null)
            {
                string materialPath = AssetDatabase.GUIDToAssetPath(HARDCODED_MATERIAL_GUID);
                if (!string.IsNullOrEmpty(materialPath))
                    polyartImpostorMaterial = AssetDatabase.LoadAssetAtPath<Material>(materialPath);
                else
                    Debug.LogError("Failed to find Material asset for GUID: " + HARDCODED_MATERIAL_GUID);
            }
            // Load Mesh
            if (polyartImpostorQuadMesh == null)
            {
                string meshPath = AssetDatabase.GUIDToAssetPath(HARDCODED_MESH_GUID);
                if (!string.IsNullOrEmpty(meshPath))
                    polyartImpostorQuadMesh = AssetDatabase.LoadAssetAtPath<Mesh>(meshPath);
                else
                    Debug.LogError("Failed to find Mesh asset for GUID: " + HARDCODED_MESH_GUID);
            }
        }

        [MenuItem("Window/Polyart/ImpostorConverter")]
        public static void ShowWindow()
        {
            ImpostorConverter win = GetWindow<ImpostorConverter>(typeof(ImpostorConverter));
            win.LoadHardcodedAssets();
            win.titleContent = new GUIContent("Impostor Converter");
            win.minSize = new Vector2(400, 500);
        }

        void OnGUI()
        {
            DrawImpostorsList();

            //polyartImpostorMaterial = (Material)EditorGUILayout.ObjectField("Base Material", polyartImpostorMaterial, typeof(Material), false);
            //polyartImpostorQuadMesh = (Mesh)EditorGUILayout.ObjectField("Base Mesh", polyartImpostorQuadMesh, typeof(Mesh), false);

            if (GUILayout.Button("Convert Impostors"))
            {
                ExecuteConversion();
            }

            // Drag and drop area handled by one common Rect
            HandleDragAndDrop(new Rect(0, 0, position.width, position.height));
        }

        // --- CONSOLIDATED EXECUTION ---
        private void ExecuteConversion()
        {
            if (impostors == null || impostors.Count == 0)
            {
                EditorUtility.DisplayDialog("Conversion Failed", "Please drag prefabs onto the window first.", "OK");
                return;
            }
            if (polyartImpostorMaterial == null || polyartImpostorQuadMesh == null)
            {
                EditorUtility.DisplayDialog("Conversion Failed", "Base Material and/or Quad Mesh are not loaded.", "OK");
                return;
            }

            try
            {
                AssetDatabase.StartAssetEditing();

                foreach (GameObject impostor in impostors)
                {
                    string prefabPath = AssetDatabase.GetAssetPath(impostor);
                    if (string.IsNullOrEmpty(prefabPath)) continue;

                    GameObject prefabRoot = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
                    if (prefabRoot == null) continue;

                    MeshRenderer prefabRenderer = prefabRoot.GetComponent<MeshRenderer>();
                    MeshFilter prefabFilter = prefabRoot.GetComponent<MeshFilter>();

                    if (prefabRenderer == null || prefabFilter == null)
                    {
                        Debug.LogWarning($"Skipping {impostor.name}: Missing Renderer or Filter components.");
                        continue;
                    }

                    // --- 1. LOAD ASSETS FOR EXTRACTION ---
                    Material existingMaterial = null;
                    Texture albedoAtlas = null;
                    Texture normalMapAtlas = null;
                    Mesh originalImpostorMesh = null;

                    Object[] allSubAssets = AssetDatabase.LoadAllAssetRepresentationsAtPath(prefabPath);

                    foreach (Object subAsset in allSubAssets)
                    {
                        if (subAsset == null) continue;

                        if (subAsset is Material && subAsset.name.Contains("Impostor"))
                        {
                            existingMaterial = subAsset as Material;
                        }
                        if (subAsset.name == "AlbedoAtlas" && subAsset is Texture)
                        {
                            albedoAtlas = subAsset as Texture;
                        }
                        else if (subAsset.name == "NormalMapAtlas" && subAsset is Texture)
                        {
                            normalMapAtlas = subAsset as Texture;
                        }
                        else if (subAsset.name == "ImpostorQuad" && subAsset is Mesh)
                        {
                            originalImpostorMesh = subAsset as Mesh;
                        }
                    }

                    if (albedoAtlas == null || normalMapAtlas == null || existingMaterial == null || originalImpostorMesh == null)
                    {
                        Debug.LogError($"Conversion failed for {impostor.name}: Missing Sub-Assets (Material, Textures, or original ImpostorQuad Mesh).");
                        continue;
                    }

                    // --- 2. SETUP DATA HOLDER ---
                    ImpostorDataHolder dataHolder = impostor.GetComponent<ImpostorDataHolder>();
                    if (dataHolder == null) dataHolder = impostor.AddComponent<ImpostorDataHolder>();

                    // 2a. SET TARGET MATERIAL (MUST BE FIRST)
                    // This sets dataHolder.material and prefabRenderer.sharedMaterial
                    dataHolder.SetImpostorMaterial(polyartImpostorMaterial);

                    // 2b. Inject values and textures (Now dataHolder.material is set)
                    dataHolder.ExtractMaterialValues(existingMaterial, albedoAtlas, normalMapAtlas);
                    dataHolder.copyMeshScale(originalImpostorMesh);

                    // 2c. SERIALIZE COMPONENT DATA (Crucial to save texture references)
                    EditorUtility.SetDirty(dataHolder);

                    // 2d. SETUP AND BIND MPB 
                    dataHolder.SetMaterialPropertyBlockData();
                    dataHolder.BindMaterialPropertyBlock();

                    // --- 3. APPLY NEW MESH TO PREFAB COMPONENTS ---

                    // Assign new Mesh
                    prefabFilter.sharedMesh = polyartImpostorQuadMesh;
                    EditorUtility.SetDirty(prefabFilter);

                    // Final serialization marks
                    EditorUtility.SetDirty(prefabRoot);
                }
            }
            catch (System.Exception e)
            {
                Debug.LogError($"Conversion failed with exception: {e.Message}");
            }
            finally
            {
                AssetDatabase.StopAssetEditing();
                AssetDatabase.SaveAssets();
                AssetDatabase.Refresh();
                Debug.Log($"Conversion and Cleanup process completed for {impostors.Count} prefabs.");
            }
        }

        // --- DRAW LIST (Consolidated logic) ---
        private void DrawImpostorsList()
        {
            EditorGUILayout.Space();
            EditorGUILayout.LabelField("--- Drag Prefabs Here ---", EditorStyles.boldLabel);
            EditorGUILayout.Space();

            if (impostors.Count > 0)
            {
                EditorGUILayout.LabelField($"Found {impostors.Count} Prefabs:", EditorStyles.miniLabel);
                foreach (var go in impostors)
                {
                    EditorGUILayout.ObjectField(go, typeof(GameObject), false);
                }
            }
            else
            {
                EditorGUILayout.HelpBox("Drag Prefabs from the Project window onto this window to add them to the list.", MessageType.Info);
            }
        }

        // --- HANDLE DRAG/DROP (Consolidated logic) ---
        private void HandleDragAndDrop(Rect dropArea)
        {
            Event evt = Event.current;
            if (!dropArea.Contains(evt.mousePosition)) return;

            switch (evt.type)
            {
                case EventType.DragUpdated:
                case EventType.DragPerform:
                    DragAndDrop.visualMode = DragAndDrop.objectReferences.Length > 0 ? DragAndDropVisualMode.Copy : DragAndDropVisualMode.Rejected;

                    if (evt.type == EventType.DragPerform)
                    {
                        DragAndDrop.AcceptDrag();
                        impostors.Clear();

                        foreach (Object draggedObject in DragAndDrop.objectReferences)
                        {
                            if (draggedObject is GameObject go && PrefabUtility.IsPartOfPrefabAsset(go))
                            {
                                impostors.Add(go);
                            }
                        }
                        Repaint();
                        evt.Use();
                    }
                    break;
            }
        }
    }
}

#endif