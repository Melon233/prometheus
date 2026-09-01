#if UNITY_EDITOR

using UnityEngine;
using UnityEditor;
using System.Collections.Generic;

namespace Polyart
{

    [CustomEditor(typeof(HarvestingStateMachine))]
    public class HarvestingStateMachineCustomInspector : Editor
    {
        List<bool> stageFoldouts = new List<bool>();

        public override void OnInspectorGUI()
        {
            HarvestingStateMachine harvestingStateMachine = (HarvestingStateMachine)serializedObject.targetObject;

            GUILayout.Space(15);

            GUIStyle largeLabelStyle = new GUIStyle(EditorStyles.label)
            {
                fontSize = 36,
                fontStyle = FontStyle.Bold,
                normal = { textColor = UnityEngine.Color.white },
                alignment = TextAnchor.MiddleCenter
            };

            GUILayout.Label("Plant Growth", largeLabelStyle);

            EditorGUILayout.Space(15, true);

            // Custom styles for enabled and disabled buttons
            GUIStyle enabledStyle = new GUIStyle(GUI.skin.button);
            enabledStyle.normal.textColor = Color.white;
            enabledStyle.fontSize = 12;

            for (int i = stageFoldouts.Count - 1; i < harvestingStateMachine.numStages; i++)
            {
                stageFoldouts.Add(true);
            }

            string foldoutTitle;
            for (int i = 0; i < harvestingStateMachine.numStages; i++) 
            {
                if (i == 0)
                    foldoutTitle = "Seed Stage";
                else if (i == harvestingStateMachine.numStages - 1 && i != 0)
                    foldoutTitle = "Fully Grown Stage";
                else
                    foldoutTitle = $"Growing Stage {i}";

                stageFoldouts[i] = EditorGUILayout.Foldout(stageFoldouts[i], foldoutTitle);
                if (stageFoldouts[i])
                {
                    EditorGUI.indentLevel++;
                    HarvestingStateMachine.PlantStageInfo temp = harvestingStateMachine.plantStages[i];
                    temp.mesh = (GameObject)EditorGUILayout.ObjectField("Mesh", harvestingStateMachine.plantStages[i].mesh, typeof(GameObject), false);
                    if (i != harvestingStateMachine.numStages - 1 || i == 0)
                        temp.timeToGrow = EditorGUILayout.FloatField("Time to Grow", harvestingStateMachine.plantStages[i].timeToGrow);
                    harvestingStateMachine.plantStages[i] = temp;
                    EditorGUI.indentLevel--;
                }
            }

            EditorGUILayout.Space(8);

            GUILayout.BeginHorizontal();
            if (harvestingStateMachine.numStages == 0)
                GUI.enabled = false;
            if (GUILayout.Button("Remove Stage"))
            {
                int num = harvestingStateMachine.numStages;
                if (num > 0)
                    harvestingStateMachine.plantStages.RemoveAt(harvestingStateMachine.numStages - 1);
                num--;
                if (num < 0)
                    num = 0;            
                harvestingStateMachine.numStages = num;
            }
            GUI.enabled = true;
            if (GUILayout.Button("Add Stage"))
            {
                harvestingStateMachine.numStages++;
                harvestingStateMachine.plantStages.Add(new HarvestingStateMachine.PlantStageInfo(5f));
            }
            GUILayout.EndHorizontal();

            GUILayout.Space(10);

            EditorGUILayout.PropertyField(serializedObject.FindProperty("harvestedStage"));

            GUILayout.Space(10);

            serializedObject.ApplyModifiedProperties();
        }
    }

}
#endif