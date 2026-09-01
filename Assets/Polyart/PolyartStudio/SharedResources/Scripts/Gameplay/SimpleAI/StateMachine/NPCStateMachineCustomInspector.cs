#if UNITY_EDITOR

using UnityEngine;
using UnityEditor;

namespace Polyart
{
    [CustomEditor(typeof(NPCStateMachine))]
    public class NPCStateMachineCustomInspector : Editor
    {
        NPCStateMachine animalStateMachine;
        private void OnEnable()
        {
            animalStateMachine = (NPCStateMachine)serializedObject.targetObject;
        }

        private Texture2D MakeTex(int width, int height, Color col)
        {
            Color[] pix = new Color[width * height];
            for (int i = 0; i < pix.Length; i++)
                pix[i] = col;

            Texture2D result = new Texture2D(width, height);
            result.SetPixels(pix);
            result.Apply();
            return result;
        }

        public override void OnInspectorGUI()
        {
            animalStateMachine = (NPCStateMachine)serializedObject.targetObject;

            GUILayout.Space(15);

            GUIStyle largeLabelStyle = new GUIStyle(EditorStyles.label)
            {
                fontSize = 36,
                fontStyle = FontStyle.Bold,
                normal = { textColor = UnityEngine.Color.white },
                alignment = TextAnchor.MiddleCenter
            };

            GUILayout.Label("NPC Behaviour", largeLabelStyle);

            EditorGUILayout.Space(15, true);

            // Custom styles for enabled and disabled buttons
            GUIStyle enabledStyle = new GUIStyle(GUI.skin.button);
            enabledStyle.normal.textColor = Color.white;
            enabledStyle.fontSize = 12;
            enabledStyle.normal.background = MakeTex(2, 2, new Color(0f, 0.4f, 0f));

            GUIStyle disabledStyle = new GUIStyle(GUI.skin.button);
            disabledStyle.normal.textColor = Color.white;
            disabledStyle.normal.background = MakeTex(2, 2, new Color(0.4f, 0f, 0f));

            for (int i=0; i<animalStateMachine.supportedStates.Length;i++)
            {
                EditorGUILayout.Space(5);

                bool hasState = animalStateMachine.supportedStates[i];

                if (((NPCStateMachine.NPCState)i).Equals(NPCStateMachine.NPCState.Idle))
                    GUI.enabled = false;

                if (GUILayout.Button(hasState ? $"NPC has {((NPCStateMachine.NPCState)i).ToString()} State" : $"NPC does not have { ((NPCStateMachine.NPCState)i).ToString()} State", hasState ? enabledStyle : disabledStyle))
                {
                    animalStateMachine.supportedStates[i] = !animalStateMachine.supportedStates[i];     
                }

                GUI.enabled = true;

                if (animalStateMachine.supportedStates[i])
                {
                    EditorGUILayout.Space(5);

                    switch ((NPCStateMachine.NPCState)i)
                    {
                        case NPCStateMachine.NPCState.Idle:
                            EditorGUILayout.PropertyField(serializedObject.FindProperty("idleDurationMin"));
                            EditorGUILayout.PropertyField(serializedObject.FindProperty("idleDurationMax"));
                            EditorGUILayout.PropertyField(serializedObject.FindProperty("idleAnim"));
                            EditorGUILayout.Space(2);
                            EditorGUILayout.PropertyField(serializedObject.FindProperty("idleWeight"));
                            break;
                        case NPCStateMachine.NPCState.Walk:
                            EditorGUILayout.PropertyField(serializedObject.FindProperty("walkSpeed"));
                            EditorGUILayout.PropertyField(serializedObject.FindProperty("walkAnim"));
                            EditorGUILayout.Space(2);
                            EditorGUILayout.PropertyField(serializedObject.FindProperty("walkWeight"));
                            break;
                        case NPCStateMachine.NPCState.Run:
                            EditorGUILayout.PropertyField(serializedObject.FindProperty("runSpeed"));
                            EditorGUILayout.PropertyField(serializedObject.FindProperty("runAnim"));
                            EditorGUILayout.Space(2);
                            EditorGUILayout.PropertyField(serializedObject.FindProperty("runWeight"));
                            break;
                        case NPCStateMachine.NPCState.Rest:
                            EditorGUILayout.PropertyField(serializedObject.FindProperty("restDurationMin"));
                            EditorGUILayout.PropertyField(serializedObject.FindProperty("restDurationMax"));
                            EditorGUILayout.PropertyField(serializedObject.FindProperty("restStartAnim"));
                            EditorGUILayout.PropertyField(serializedObject.FindProperty("restLoopAnim"));
                            EditorGUILayout.PropertyField(serializedObject.FindProperty("restEndAnim"));
                            EditorGUILayout.Space(2);
                            EditorGUILayout.PropertyField(serializedObject.FindProperty("restWeight"));
                            break;
                        case NPCStateMachine.NPCState.Sleep:
                            EditorGUILayout.PropertyField(serializedObject.FindProperty("sleepDurationMin"));
                            EditorGUILayout.PropertyField(serializedObject.FindProperty("sleepDurationMax"));
                            EditorGUILayout.PropertyField(serializedObject.FindProperty("sleepStartAnim"));
                            EditorGUILayout.PropertyField(serializedObject.FindProperty("sleepLoopAnim"));
                            EditorGUILayout.PropertyField(serializedObject.FindProperty("sleepEndAnim"));
                            EditorGUILayout.Space(2);
                            EditorGUILayout.PropertyField(serializedObject.FindProperty("sleepWeight"));
                            break;
                        case NPCStateMachine.NPCState.Eat:
                            EditorGUILayout.PropertyField(serializedObject.FindProperty("eatAnim"));
                            EditorGUILayout.Space(2);
                            EditorGUILayout.PropertyField(serializedObject.FindProperty("eatWeight"));
                            break;
                        default:
                            Debug.LogWarning("Please implement all NPC States");
                            break;
                    }
                }
            }
            
            GUILayout.Space(10);
            serializedObject.ApplyModifiedProperties();
        }
    }
}

#endif