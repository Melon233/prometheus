#if UNITY_EDITOR

using UnityEditor;
using UnityEngine;

namespace Polyart
{
    [CustomEditor(typeof(CropSpawnManager))]
    public class CropsSpawnerCustomInspector : Editor
    {
        public override void OnInspectorGUI()
        {
            GUILayout.Space(15);

            GUIStyle largeLabelStyle = new GUIStyle(EditorStyles.label)
            {
                fontSize = 36,
                fontStyle = FontStyle.Bold,
                normal = { textColor = UnityEngine.Color.white },
                alignment = TextAnchor.MiddleCenter
            };

            GUILayout.Label("Crop Spawner", largeLabelStyle);

            EditorGUILayout.Space(15);

            GUILayout.Label("Crop Prefab:", GUILayout.Width(75)); // Adjust label width
            EditorGUILayout.PropertyField(serializedObject.FindProperty("cropPrefab"), GUIContent.none);

            GUILayout.Space(5);

            EditorGUILayout.BeginHorizontal();

            GUILayout.Label("Num Rows:", GUILayout.Width(75)); // Adjust label width
            EditorGUILayout.PropertyField(serializedObject.FindProperty("numRows"), GUIContent.none);

            GUILayout.Label("Num Cols:", GUILayout.Width(75)); // Adjust label width
            EditorGUILayout.PropertyField(serializedObject.FindProperty("numCols"), GUIContent.none);
            GUILayout.EndHorizontal();

            EditorGUILayout.BeginHorizontal();

            GUILayout.Label("Row Space:", GUILayout.Width(75)); // Adjust label width
            EditorGUILayout.PropertyField(serializedObject.FindProperty("rowSpace"), GUIContent.none); // Remove default label

            GUILayout.Label("Col Space:", GUILayout.Width(75));
            EditorGUILayout.PropertyField(serializedObject.FindProperty("colSpace"), GUIContent.none);

            EditorGUILayout.EndHorizontal();

            GUILayout.Space(5);

            EditorGUILayout.PropertyField(serializedObject.FindProperty("maxTimeVariation"));

            GUILayout.Space(10);

            serializedObject.ApplyModifiedProperties();
        }
    }
}
#endif