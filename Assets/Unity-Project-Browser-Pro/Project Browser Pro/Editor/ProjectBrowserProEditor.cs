using UnityEditor;
using UnityEngine;
using Newtonsoft.Json;
using System.Data;

namespace PHProductions
{

    public class ProjectBrowserProEditor : EditorWindow
    {

        private Vector2 _scrollPosition;
        private const string MENU_NAME_NextObject = "Window/Project Browser Pro/Select Next Item #%E";
        private const string MENU_NAME_PreviousObject = "Window/Project Browser Pro/Select Previous Item #%Q";
        private const string MENU_NAME_NextObjectLocked = "Window/Project Browser Pro/Select Next Item (Locked) #&E";
        private const string MENU_NAME_PreviousObjectLocked = "Window/Project Browser Pro/Select Previous Item (Locked) #&Q";
        private const string MENU_NAME_ToggleProjectBrowserHelperArrows = "Window/Project Browser Pro/(Toggle) Show Project Browser Helper Arrows (Beta) %#-";
        private const string MENU_NAME_ToggleAssetDetails = "Window/Project Browser Pro/(Toggle) Show Asset Details";
        private const string MENU_NAME_DepthLevel_1 = "Window/Project Browser Pro/Depth Level/Level 1 - Folders Only";
        private const string MENU_NAME_DepthLevel_2 = "Window/Project Browser Pro/Depth Level/Level 2 - Folders and Browser Assets";
        private const string MENU_NAME_DepthLevel_3 = "Window/Project Browser Pro/Depth Level/Level 3 - Folders, Browser Assets and Hierarchy Objects";
        #region Save/Load Assets
        private const string MENU_NAME_SaveAsset_1 = "Window/Project Browser Pro/Quicksave/Assign Save Slot/1 %&1";
        private const string MENU_NAME_SaveAsset_2 = "Window/Project Browser Pro/Quicksave/Assign Save Slot/2 %&2";
        private const string MENU_NAME_SaveAsset_3 = "Window/Project Browser Pro/Quicksave/Assign Save Slot/3 %&3";
        private const string MENU_NAME_SaveAsset_4 = "Window/Project Browser Pro/Quicksave/Assign Save Slot/4 %&4";
        private const string MENU_NAME_SaveAsset_5 = "Window/Project Browser Pro/Quicksave/Assign Save Slot/5 %&5";
        private const string MENU_NAME_SaveAsset_6 = "Window/Project Browser Pro/Quicksave/Assign Save Slot/6 %&6";
        private const string MENU_NAME_SaveAsset_7 = "Window/Project Browser Pro/Quicksave/Assign Save Slot/7 %&7";
        private const string MENU_NAME_SaveAsset_8 = "Window/Project Browser Pro/Quicksave/Assign Save Slot/8 %&8";
        private const string MENU_NAME_SaveAsset_9 = "Window/Project Browser Pro/Quicksave/Assign Save Slot/9 %&9";

        private const string MENU_NAME_LoadAsset_1 = "Window/Project Browser Pro/Quicksave/Load Save Slot/1 %#1";
        private const string MENU_NAME_LoadAsset_2 = "Window/Project Browser Pro/Quicksave/Load Save Slot/2 %#2";
        private const string MENU_NAME_LoadAsset_3 = "Window/Project Browser Pro/Quicksave/Load Save Slot/3 %#3";
        private const string MENU_NAME_LoadAsset_4 = "Window/Project Browser Pro/Quicksave/Load Save Slot/4 %#4";
        private const string MENU_NAME_LoadAsset_5 = "Window/Project Browser Pro/Quicksave/Load Save Slot/5 %#5";
        private const string MENU_NAME_LoadAsset_6 = "Window/Project Browser Pro/Quicksave/Load Save Slot/6 %#6";
        private const string MENU_NAME_LoadAsset_7 = "Window/Project Browser Pro/Quicksave/Load Save Slot/7 %#7";
        private const string MENU_NAME_LoadAsset_8 = "Window/Project Browser Pro/Quicksave/Load Save Slot/8 %#8";
        private const string MENU_NAME_LoadAsset_9 = "Window/Project Browser Pro/Quicksave/Load Save Slot/9 %#9";
        #endregion
        private static ProjectBrowserProEditor instance;
        private static bool show_History = true;
        private static bool show_SavedAssets = false;

        [MenuItem("Window/Project Browser Pro/Open Selection History")]
        public static void ShowWindow()
        {
			// Call the initialization method when the window is first shown
			instance = EditorWindow.GetWindow<ProjectBrowserProEditor>();
            instance.Show();
        }
        void OnEnable()
        {
            titleContent = new GUIContent("Project Browser Pro");
            AssemblyReloadEvents.beforeAssemblyReload += OnBeforeAssemblyReload;
        }
        public void OnSelectionChange()
        {
            Repaint();
        }
        private static void OnBeforeAssemblyReload()
        {
            // Save your settings when the assets are being saved 
            SelectionData.SaveData();   
        }
        public static void SaveHistory() 
        { 
            string json = null;  
            // Convert list to JSON and save it to EditorPrefs
            if (SelectionData.SelectionHistory.Count > 0)
            {
                json = JsonConvert.SerializeObject(SelectionData.SelectionHistory);
            } 
            EditorPrefs.SetString("SelectionHistory", json);  
        } 
        [MenuItem(MENU_NAME_NextObject)]
        private static void NextObject()
        {
            ProjectBrowserPro.NextItem(false);
        }
        [MenuItem(MENU_NAME_PreviousObject)]
        private static void PreviousObject()
        {
			ProjectBrowserPro.PreviousItem(false);
        }
        [MenuItem(MENU_NAME_NextObjectLocked)]
        private static void NextObjectLocked()
        {
			ProjectBrowserPro.NextItem(true);
        }
        [MenuItem(MENU_NAME_PreviousObjectLocked)]
        private static void PreviousObjectLocked()
        {
			ProjectBrowserPro.PreviousItem(true);
        }
        [MenuItem(MENU_NAME_ToggleProjectBrowserHelperArrows)]  
        private static void ToggleProjectBrowserHelperArrows()
        {
			ProjectBrowserPro.ToggleProjectBrowserHelperArrows();
            AssetDatabase.Refresh();
            Menu.SetChecked(MENU_NAME_ToggleProjectBrowserHelperArrows,ProjectBrowserPro.ShowHelperArrows);
        }
        [MenuItem(MENU_NAME_ToggleAssetDetails)]
        private static void ToggleAssetDetails()
        {
			ProjectBrowserPro.ToggleAssetDetails();
            AssetDatabase.Refresh();
            Menu.SetChecked(MENU_NAME_ToggleAssetDetails,ProjectBrowserPro.ShowAssetDetails);
        }
        [MenuItem(MENU_NAME_DepthLevel_1)]
        private static void DepthLevel1()
        {
            ProjectBrowserPro.ToggleDepthLevel1();
            Menu.SetChecked(MENU_NAME_DepthLevel_1, ProjectBrowserPro.Tracking_Depth_1);
            Menu.SetChecked(MENU_NAME_DepthLevel_2, ProjectBrowserPro.Tracking_Depth_2);
            Menu.SetChecked(MENU_NAME_DepthLevel_3, ProjectBrowserPro.Tracking_Depth_3);
        }
        [MenuItem(MENU_NAME_DepthLevel_2)]
        private static void DepthLevel2()
        {
            ProjectBrowserPro.ToggleDepthLevel2();
            Menu.SetChecked(MENU_NAME_DepthLevel_1, ProjectBrowserPro.Tracking_Depth_1);
            Menu.SetChecked(MENU_NAME_DepthLevel_2, ProjectBrowserPro.Tracking_Depth_2);
            Menu.SetChecked(MENU_NAME_DepthLevel_3, ProjectBrowserPro.Tracking_Depth_3);
        } 
        [MenuItem(MENU_NAME_DepthLevel_3)]
        private static void DepthLevel3()
        {
			ProjectBrowserPro.ToggleDepthLevel3();
            Menu.SetChecked(MENU_NAME_DepthLevel_1, ProjectBrowserPro.Tracking_Depth_1);
            Menu.SetChecked(MENU_NAME_DepthLevel_2, ProjectBrowserPro.Tracking_Depth_2);
            Menu.SetChecked(MENU_NAME_DepthLevel_3, ProjectBrowserPro.Tracking_Depth_3);
        }

        #region Quicksave
        [MenuItem(MENU_NAME_SaveAsset_1)]
        private static void SaveAsset_1()
        {
            SaveAsset(1);
        }
        [MenuItem(MENU_NAME_SaveAsset_2)]
        private static void SaveAsset_2()
        {
            SaveAsset(2);
        }
        [MenuItem(MENU_NAME_SaveAsset_3)]
        private static void SaveAsset_3()
        {
            SaveAsset(3);
        }
        [MenuItem(MENU_NAME_SaveAsset_4)]
        private static void SaveAsset_4()
        {
            SaveAsset(4);
        }
        [MenuItem(MENU_NAME_SaveAsset_5)]
        private static void SaveAsset_5()
        {
            SaveAsset(5);
        }
        [MenuItem(MENU_NAME_SaveAsset_6)]
        private static void SaveAsset_6()
        {
            SaveAsset(6);
        }
        [MenuItem(MENU_NAME_SaveAsset_7)]
        private static void SaveAsset_7()
        {
            SaveAsset(7);
        }
        [MenuItem(MENU_NAME_SaveAsset_8)]
        private static void SaveAsset_8()
        {
            SaveAsset(8);
        }
        [MenuItem(MENU_NAME_SaveAsset_9)]
        private static void SaveAsset_9()
        {
            SaveAsset(9);
        }

        [MenuItem(MENU_NAME_LoadAsset_1)]
        private static void Load_Asset_1()
        {
            ProjectBrowserPro.LoadAssets(1);
        }
        [MenuItem(MENU_NAME_LoadAsset_2)]
        private static void Load_Asset_2()
        {
            ProjectBrowserPro.LoadAssets(2);
        }
        [MenuItem(MENU_NAME_LoadAsset_3)]
        private static void Load_Asset_3()
        {
            ProjectBrowserPro.LoadAssets(3);
        }
        [MenuItem(MENU_NAME_LoadAsset_4)]
        private static void Load_Asset_4()
        {
            ProjectBrowserPro.LoadAssets(4);
        }
        [MenuItem(MENU_NAME_LoadAsset_5)]
        private static void Load_Asset_5()
        {
            ProjectBrowserPro.LoadAssets(5);
        }
        [MenuItem(MENU_NAME_LoadAsset_6)]
        private static void Load_Asset_6()
        {
            ProjectBrowserPro.LoadAssets(6);
        }
        [MenuItem(MENU_NAME_LoadAsset_7)]
        private static void Load_Asset_7()
        {
            ProjectBrowserPro.LoadAssets(7);
        }
        [MenuItem(MENU_NAME_LoadAsset_8)]
        private static void Load_Asset_8()
        {
            ProjectBrowserPro.LoadAssets(8);
        }
        [MenuItem(MENU_NAME_LoadAsset_9)]
        private static void Load_Asset_9()
        {
            ProjectBrowserPro.LoadAssets(9);
        }
        #endregion
        public static void SaveAsset(int index)
        {
            SelectionData.SavedAssets[index] = new ObjectArrayWrapper(Selection.objects);
            RepaintAll();
        }

        private void OnGUI()
        {
            UnityEngine.Object[] selectedObjects = Selection.objects;
            DrawHeaderButtons();
            if (show_History)
            {
                DrawSelectionHistory();
            }

            if (show_SavedAssets)
            {
                DrawSavedAssets();
            }

        }
        private static void DrawHeaderButtons()
        {
            EditorGUILayout.Space();

            EditorGUILayout.BeginHorizontal();

            // Creates a button to select the previous item in selection history
            GUI.skin.button.alignment = TextAnchor.MiddleCenter;
            if (GUILayout.Button("<", GetSmallerButtonStyle()))
            {
                ProjectBrowserPro.PreviousItem(false);
            }

            // Creates a button to select the next item in selection history
            if (GUILayout.Button(">", GetSmallerButtonStyle()))
            {
                ProjectBrowserPro.NextItem(false);
            }

            // Flexible space to push buttons to the right side
            GUILayout.FlexibleSpace();

            if (GUILayout.Button("History"))
            {
                show_History = true;
                show_SavedAssets = false;
            }

            if (GUILayout.Button("Quicksave"))
            {
                show_History = false;
                show_SavedAssets = true;
            }

            EditorGUILayout.EndHorizontal();

            EditorGUILayout.Space();
        }



        private void DrawSelectionHistory()
        {
            if (SelectionData.SelectionHistory != null && SelectionData.SelectionHistory.Count > 0)
            {

                GUILayout.Label("Selection History", EditorStyles.boldLabel);

                // Creates a scrollable view of the selection history
                _scrollPosition = EditorGUILayout.BeginScrollView(_scrollPosition, GUILayout.ExpandHeight(true));

                for (int i = SelectionData.SelectionHistory.Count - 1; i >= 0; i--)
                {
                    Object[] selection = SelectionData.SelectionHistory[i].array;

                    EditorGUILayout.Space();

                    for (int j = selection.Length - 1; j >= 0; j--)
                    {
                        Object obj = selection[j];
                        EditorGUILayout.BeginHorizontal();

                        // Highlights the currently selected item in selection history
                        if (i == ProjectBrowserPro.SelectionHistoryIndex)
                        {
                            GUI.backgroundColor = Color.blue;
                        } 
                        else
                        {
                            if (i > ProjectBrowserPro.SelectionHistoryIndex)
                            {
                                GUI.backgroundColor = Color.grey;
                            }
                            else GUI.backgroundColor = Color.white;
                        }

                        // Displays the mini thumbnail of the object and its name
                        Texture2D texture = AssetPreview.GetMiniThumbnail(obj);
                        if (texture != null)
                        {
                            GUILayout.Label(texture, GUILayout.Width(20), GUILayout.Height(20));
                        }
                        GUI.skin.button.alignment = TextAnchor.MiddleLeft;
                        if (obj != null && GUILayout.Button(obj.name, GUILayout.ExpandWidth(false)))
                        {
                            ProjectBrowserPro.SelectionChangedByEditor = true;
                            Selection.activeObject = obj;
                            EditorGUIUtility.PingObject(obj);
                            ProjectBrowserPro.SelectionHistoryIndex = i;
                        }

                        EditorGUILayout.EndHorizontal();
                    }
                }
                EditorGUILayout.EndScrollView();

                GUI.backgroundColor = Color.white;
                if (GUILayout.Button("Clear Selection History"))
                {
                    ProjectBrowserPro.ClearSelectionHistory();
                }
            }
        }
        private Object CreateDummyObject()
        {
            // Create your dummy object here
            Object dummyObject = new Object(); // Replace this with your actual dummy object initialization

            return dummyObject;
        }
        private void DrawSavedAssets()
        {
            if (SelectionData.SavedAssets != null && SelectionData.SavedAssets.Length > 0)
            {

                GUILayout.Label("Quicksave", EditorStyles.boldLabel);
                GUILayout.Label("Left Click  = Load", EditorStyles.boldLabel);
                GUILayout.Label("Right Click = Save", EditorStyles.boldLabel);

                // Creates a scrollable view of the selection history
                _scrollPosition = EditorGUILayout.BeginScrollView(_scrollPosition, GUILayout.ExpandHeight(true));

                for (int i = 1 ; i <= SelectionData.SavedAssets.Length - 1; i++)   
                {
                    if (SelectionData.SavedAssets[i] != null && SelectionData.SavedAssets[i].array.Length > 0) 
                    {
                        Object[] selection = SelectionData.SavedAssets[i].array;
                        EditorGUILayout.Space();

                        for (int j = selection.Length - 1; j >= 0; j--)
                        {
                            Object obj = selection[j];
                            EditorGUILayout.BeginHorizontal();

                            // Add text before the button
                            GUILayout.Label("Quicksave Slot: " + i, GUILayout.Width(120));

                            // Displays the mini thumbnail of the object and its name
                            Texture2D texture = AssetPreview.GetMiniThumbnail(obj);
                            if (texture != null)
                            {
                                GUILayout.Label(texture, GUILayout.Width(20), GUILayout.Height(20));
                            }
                            GUI.skin.button.alignment = TextAnchor.MiddleLeft;
                            if (obj != null && GUILayout.Button(obj.name, GUILayout.ExpandWidth(false)))
                            {
                                if (Event.current.button == 0)
                                {
                                    // Left-click action
                                    ProjectBrowserPro.SelectionChangedByEditor = true;
                                    Selection.activeObject = obj;
                                    EditorGUIUtility.PingObject(obj);
                                    ProjectBrowserPro.SelectionHistoryIndex = i;
                                }
                                // Right-click action
                                else if (Event.current.button == 1)
                                {
                                    SaveAsset(i);
                                }
                            }

                            EditorGUILayout.EndHorizontal();
                        }
                    }
                    else
                    {
                        EditorGUILayout.Space();
                        EditorGUILayout.BeginHorizontal();
                        // Add text before the button
                        GUILayout.Label("Quicksave Slot: " + i, GUILayout.Width(120));
                        GUI.skin.button.alignment = TextAnchor.MiddleLeft;
                        if (GUILayout.Button("Empty", GUILayout.ExpandWidth(false)))
                        {
                            if (Event.current.button == 0)
                            {
                                //Nothing
                            }
                            // Right-click action
                            else if (Event.current.button == 1)
                            {
                                SaveAsset(i);
                            }
                        }

                        EditorGUILayout.EndHorizontal();
                    }
                }
                EditorGUILayout.EndScrollView();

                GUI.backgroundColor = Color.white;
                if (GUILayout.Button("Clear Selection History"))
                {
                    ProjectBrowserPro.ClearSelectionHistory();
                }
            }
        }
        private static GUIStyle GetSmallerButtonStyle()
		{
			GUIStyle style = new GUIStyle(GUI.skin.button); // Copy the default button style
			style.fixedWidth = 20; // Set the desired button width (adjust as needed)
			style.fixedHeight = 20; // Set the desired button height (adjust as needed)
			return style;
		}

        // This method will repaint all open editor windows
        public static void RepaintAll()
        {
            // Get all open editor windows
            EditorWindow[] windows = Resources.FindObjectsOfTypeAll<EditorWindow>();

            // Iterate through each window and repaint them
            foreach (EditorWindow window in windows)
            {
                window.Repaint();
            }
        }
    }
}


