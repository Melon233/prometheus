using System.Collections.Generic;
using UnityEngine;
using System.IO;
using UnityEditor;
using System.Reflection;
using Object = UnityEngine.Object;
using Newtonsoft.Json;
using System;
using NUnit.Framework;
using Unity.VisualScripting.YamlDotNet.Serialization;
using System.Linq;

namespace PHProductions
{
#if UNITY_EDITOR 
    [InitializeOnLoad]
#endif

    [Serializable]
    public class SerializableObjectReference
    {
        public string assetPath;

        public SerializableObjectReference(string path)
        {
            assetPath = path;
        }
    }

    public class ObjectArrayWrapper
    {
        public Object[] array;

        public ObjectArrayWrapper(Object[] array)
        {
            this.array = array;
        }
    }

    public static class SelectionData
    {
        public static string filePath;
        public static List<ObjectArrayWrapper> SelectionHistory = new List<ObjectArrayWrapper>();
        public static ObjectArrayWrapper[] SavedAssets = new ObjectArrayWrapper[10];
        public static void SaveData()   
        {
            string json = null;

            // Serialize Selection History List
            List<SerializableObjectReference> serializable_History = new List<SerializableObjectReference>();
            foreach (var arr in SelectionHistory)
            {
                SerializableObjectReference[] serializableArray = Array.ConvertAll<UnityEngine.Object, SerializableObjectReference>(arr.array, obj => new SerializableObjectReference(AssetDatabase.GetAssetPath(obj)));
                foreach (var obj in serializableArray) serializable_History.Add(obj);
            }
            // Convert the list to JSON format   
            json = SerializeObjectArray(serializable_History.ToArray());

            // Save the JSON string to PlayerPrefs
            PlayerPrefs.SetString("SavedHistory", json);
            PlayerPrefs.Save();

            // Serialize Saved Asset List
            
            for (int i = 1; i <= SelectionData.SavedAssets.Length - 1; i++)
            {  
                if(SelectionData.SavedAssets[i] != null) 
                {
                    List<SerializableObjectReference> serializable_SavedAssets = new List<SerializableObjectReference>();
                    SerializableObjectReference[] serializableArray = Array.ConvertAll<UnityEngine.Object, SerializableObjectReference>(SelectionData.SavedAssets[i].array, obj => new SerializableObjectReference(AssetDatabase.GetAssetPath(obj)));
                    foreach (var obj in serializableArray) serializable_SavedAssets.Add(obj);
                    // Convert the list to JSON format   
                    json = SerializeObjectArray(serializable_SavedAssets.ToArray());
                    // Save the JSON string to PlayerPrefs 
                    PlayerPrefs.SetString("SaveSlot" + i, json);
                    PlayerPrefs.Save();
                }
            }
        }
        public static List<ObjectArrayWrapper> LoadHistory(out int index)
        {
            List<ObjectArrayWrapper> selectionHistory = new List<ObjectArrayWrapper>();

            // Check if the key exists in PlayerPrefs
            if (PlayerPrefs.HasKey("SavedHistory"))
            {
                // Load the JSON string from PlayerPrefs
                List<SerializableObjectReference> deserializedHistory = DeserializeToList(PlayerPrefs.GetString("SavedHistory"));
                foreach (var obj in deserializedHistory)
                {
                    if (obj != null && !string.IsNullOrEmpty(obj.assetPath))
                    {
                        UnityEngine.Object loadedAsset = AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(obj.assetPath);
                        selectionHistory.Add(new ObjectArrayWrapper(new UnityEngine.Object[] { loadedAsset }));
                    }
                }
            }
            index = selectionHistory.Count - 1;  
            return selectionHistory; 
        }

        public static void LoadQuicksave()
        { 
            
            for (int i = 1; i <= SelectionData.SavedAssets.Length - 1; i++)
            {
                // Check if the key exists in PlayerPrefs
                if (PlayerPrefs.HasKey("SaveSlot" + i))
                {
                    List<Object> selectionDeserialized = new List<Object>();
                    // Load the JSON string from PlayerPrefs
                    List<SerializableObjectReference> deserializedHistory = DeserializeToList(PlayerPrefs.GetString("SaveSlot" + i));
                    foreach (var obj in deserializedHistory)
                    {
                        if (obj != null && !string.IsNullOrEmpty(obj.assetPath))
                        {
                            UnityEngine.Object loadedAsset = AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(obj.assetPath);
                            selectionDeserialized.Add(loadedAsset);
                        } 
                    }
                    SelectionData.SavedAssets[i] = new ObjectArrayWrapper(selectionDeserialized.ToArray());
                }
            }
        }

        static string SerializeObjectArray(SerializableObjectReference[] array)
        {
            string[] serializedArray = new string[array.Length];
            for (int i = 0; i < array.Length; i++) 
            {
                serializedArray[i] = JsonUtility.ToJson(array[i]);
            }
            return "[" + string.Join(",", serializedArray) + "]";
        }
        static List<SerializableObjectReference> DeserializeToList(string json)
        {
            List<SerializableObjectReference> deserializedList = new List<SerializableObjectReference>();
            // Remove square brackets from JSON string
            json = json.TrimStart('[').TrimEnd(']');
            // Split JSON string by commas
            string[] serializedObjects = json.Split(',');
            foreach (var serializedObject in serializedObjects)
            {
                SerializableObjectReference obj = JsonUtility.FromJson<SerializableObjectReference>(serializedObject);
                deserializedList.Add(obj);
            }
            return deserializedList;
        }
    }
    class ProjectBrowserPro : MonoBehaviour
    {
        #region Variables
        // List to store the selection history
        //[SerializeField] public static List<Object[]> SelectionHistory;
        // Index of the current item in the selection history
        public static int SelectionHistoryIndex = -1;
        // Flag to indicate whether the selection was changed by the editor
        public static bool SelectionChangedByEditor = false;
        // Flag to indicate whether the inspector was locked by the editor
        public static bool InspectorLockedByEditor = false; 
        // Unlock Inspector with next selection
        public static bool UnlockWithNextSelection = false;
        // Width of the icons used in the project browser
        public const int IconWidth = 20;
        // Flag to indicate whether the project browser is registered
        public static bool IsRegistered = false;
        // Flag to indicate whether the selection in the project browser has changed
        public static bool SelectionChanged = false;
        // Flag to indicate whether to show the helper arrows in the project browser
        public static bool ShowHelperArrows = false;
        // Flag to indicate whether to draw Asset Details
        public static bool ShowAssetDetails = false;
        // The current directory being viewed in the project browser
        public static string currentDirectory;
        //Track all objects
        public static bool Tracking_Depth_3 = false;
        //Keep track of folders and project browser objects
        public static bool Tracking_Depth_2 = false;
        //Only keep track of folders
        public static bool Tracking_Depth_1 = true;
        //Maximum amount of elements in the Selection History
        public static int maxAmount = 50;
        #endregion

#if UNITY_EDITOR
        static ProjectBrowserPro()
        {
            LoadPrefs();
            // Add a callback for when a GUI element is drawn in the project window
            EditorApplication.projectWindowItemOnGUI += EditorApplication_ProjectWindowItemOnGUI;
            //EditorApplication.projectWindowItemOnGUI += DrawAssetDetails;
            // Register the selectionChanged callback if it hasn't already been done
            if (!IsRegistered)
            {
                IsRegistered = true;
                Selection.selectionChanged += OnSelectionChanged;
            }
        }
#endif
        public void Start()
        {
            // Set the file path to the persistent data path
            SelectionData.filePath = Path.Combine(Application.persistentDataPath, "selectionHistory.json");
        }
        [UnityEditor.Callbacks.DidReloadScripts]
        private static void OnScriptsReloaded() 
        { 
            // Load your settings when scripts are reloaded
            LoadPrefs();
            SelectionData.SelectionHistory = SelectionData.LoadHistory(out SelectionHistoryIndex);
            SelectionData.LoadQuicksave();
        }  
        private static void OnSelectionChanged()
        {
            //If Tracking_Depth_3 is toggled off, check if the selected object is part of the scene, then ignore it.
            if (!Tracking_Depth_3 && Selection.gameObjects.Length != 0)
            { 
                if (Selection.gameObjects[0].scene.path != null) return;
            }
            //If Tracking_Depth_1 is enabled, ignore all objects other than folders.
            if (Tracking_Depth_1) 
            {
                string assetPath = AssetDatabase.GetAssetPath(Selection.activeObject);
                System.Type assetType = AssetDatabase.GetMainAssetTypeAtPath(assetPath);
                if (!(assetType == typeof(DefaultAsset))) return;
            }

            if (UnlockWithNextSelection)
            {
                ActiveEditorTracker.sharedTracker.isLocked = false;
                UnlockWithNextSelection = false;
            }
            UnityEngine.Object[] currentSelection = Selection.objects;
            // If the selection was changed by the editor, ignore it
            if (SelectionChangedByEditor)
            {
                if ((currentSelection != null) && (currentSelection.Length > 0))
                {
                    SelectionChangedByEditor = false;
                    if (InspectorLockedByEditor)
                    {
                        InspectorLockedByEditor = false;
                        UnlockWithNextSelection = true;
                    }
                    return;
                }
            }
            // Add the current selection to the selection history
            AddToHistory(currentSelection);
        }
        static void AddToHistory(Object[] selection)
        {
            // If the selection is empty, don't add it to the history
            if ((selection == null) || (selection.Length == 0)) return;
            if (Selection.activeObject == GetCurrentObject()) return;
            // If the selection history list hasn't been created yet, create it
            if (SelectionData.SelectionHistory == null) SelectionData.SelectionHistory = new List<ObjectArrayWrapper>();

            if ((GetCurrentObject() != null) && (AssetDatabase.GetMainAssetTypeAtPath(AssetDatabase.GetAssetPath(GetCurrentObject())) == typeof(DefaultAsset)) &&  (AssetDatabase.GetMainAssetTypeAtPath(AssetDatabase.GetAssetPath(Selection.activeObject)) == typeof(DefaultAsset)) && (Path.GetDirectoryName(AssetDatabase.GetAssetPath(Selection.activeObject)) == Path.GetDirectoryName(AssetDatabase.GetAssetPath(GetCurrentObject()))))
            {
                SelectionData.SelectionHistory[SelectionHistoryIndex] = new ObjectArrayWrapper(selection);
            }
            else
            {
                RevertHistory();
                // Add the selection to the history
                SelectionData.SelectionHistory.Add(new ObjectArrayWrapper(selection));
                if (SelectionData.SelectionHistory.Count > maxAmount) SelectionData.SelectionHistory.RemoveAt(0);
                SelectionHistoryIndex = SelectionData.SelectionHistory.Count - 1;
            }
            // Get the path of the current directory in the project browser
            TryGetActiveFolderPath(out currentDirectory);
        }
#if UNITY_EDITOR
        static void EditorApplication_ProjectWindowItemOnGUI(string guid, Rect rect)
        {
            if (ShowAssetDetails) DrawAssetDetails(guid, rect);

            // Only show helper arrows if the ShowHelperArrows flag is set
            if (ShowHelperArrows)
            {
                // If the item is the first in the list (i.e. at the top of the project window), draw two arrows next to it
                if (rect.y == 0 && guid != "Assets")
                {
                    // Create a rectangle for the right arrow button
                    Rect ButtonRight = rect;
                    ButtonRight.x += rect.width;
                    ButtonRight.x -= IconWidth;
                    ButtonRight.width = IconWidth;

                    // Draw the right arrow button
                    DrawMenuIcon(ButtonRight, "forward");

                    // Create a rectangle for the left arrow button
                    Rect ButtonLeft = rect;
                    ButtonLeft.x += ButtonLeft.width;
                    ButtonLeft.x -= IconWidth * 2;
                    ButtonLeft.width = IconWidth;

                    // Draw the left arrow button
                    DrawMenuIcon(ButtonLeft, "back");

                    // Check if the left or right arrow button was clicked
                    if (Event.current.type == EventType.MouseDown && Event.current.button == 0)
                    {
                        // Check if the right arrow button was clicked
                        if (Event.current.mousePosition.x > rect.xMax - IconWidth && Event.current.mousePosition.y > rect.yMin && Event.current.mousePosition.y < rect.yMax)
                        {
                            // Use the event to prevent it from being handled by other methods
                            Event.current.Use();
                            // Call the NextItem method to select the next item in the list
                            NextItem(false);
                        }

                        // Check if the left arrow button was clicked
                        if (Event.current.mousePosition.x > rect.xMax - IconWidth * 2 && Event.current.mousePosition.x < rect.xMax - IconWidth && Event.current.mousePosition.y > rect.yMin && Event.current.mousePosition.y < rect.yMax)
                        {
                            // Use the event to prevent it from being handled by other methods
                            Event.current.Use();
                            // Call the PreviousItem method to select the previous item in the list
                            PreviousItem(false);
                        }
                    }
                }
            }
        }
        private static void DrawAssetDetails(string guid, Rect rect)
        {
            if (Application.isPlaying)
            {
                return;
            }

            if (Event.current.type != EventType.Repaint)
            {
                return;
            }

            if (!IsMainListAsset(rect))
            {
                return;
            }

            var assetPath = AssetDatabase.GUIDToAssetPath(guid);
            if (AssetDatabase.IsValidFolder(assetPath))
            {
                return;
            }

            var asset = AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(assetPath);
            if (asset == null)
            {
                return;
            }

            //Draw Details
            string fullAssetPath = string.Concat(Application.dataPath.Substring(0, Application.dataPath.Length - 7), "/", assetPath);
            rect.x += rect.width;
            GUIStyle leftAlignedStyle = new GUIStyle(EditorStyles.label);
            leftAlignedStyle.alignment = TextAnchor.MiddleLeft;

			float labelWidth = rect.width / 10f; // Divide the available width into three equal parts

            //Size Detail
            rect.x -= labelWidth;
            rect.width = 80;
            long size = new FileInfo(fullAssetPath).Length;
            GUI.Label(rect, new GUIContent(EditorUtility.FormatBytes(size)), leftAlignedStyle);

            //Suffix Detail
            rect.x -= labelWidth * 2;
            rect.width = 80;
            GUI.Label(rect, new GUIContent(Path.GetExtension(fullAssetPath)), leftAlignedStyle);

            //Asset Type Detail
            rect.x -= labelWidth * 3;
            rect.width = 120;
            GUI.Label(rect, new GUIContent(asset.GetType().Name), leftAlignedStyle);
        }
        private static bool IsMainListAsset(Rect rect)
        {
            // Don't draw details if project view shows large preview icons:
            if (rect.height > 20)
            {
                return false;
            }

            // Don't draw details if this asset is a sub asset:
            if (rect.x > 16)
            {
                return false;
            }

            return true;
        }
#endif
        private static void DrawMenuIcon(Rect rect, string IconName)
        {
            var icon = EditorGUIUtility.IconContent(IconName);
            EditorGUI.LabelField(rect, icon);
        }
        public static void ClearSelectionHistory()
        {
            SelectionData.SelectionHistory = null;
            SelectionHistoryIndex = -1;
        }
        public static void NextItem(bool lockInspector)
        {
            if (SelectionData.SelectionHistory != null && SelectionHistoryIndex < (SelectionData.SelectionHistory.Count - 1))
            {
                if (lockInspector) LockInspectorForNextSelection();
                SelectionChangedByEditor = true;
                SelectionHistoryIndex++;
            }

            if (SelectionData.SelectionHistory != null && SelectionHistoryIndex >= 0 && SelectionHistoryIndex < SelectionData.SelectionHistory.Count)
            {

                Selection.objects = SelectionData.SelectionHistory[SelectionHistoryIndex].array;
                TryGetActiveFolderPath(out currentDirectory);
                
                if (Selection.activeObject == GetCurrentObject() && (AssetDatabase.IsValidFolder(AssetDatabase.GetAssetPath(GetCurrentObject()))))
                {
                    AssetDatabase.OpenAsset(Selection.activeObject);
                }
                else
                {
                    EditorGUIUtility.PingObject(GetCurrentObject());
                }
            }
        }
        public static void PreviousItem(bool lockInspector)
        {


            if (SelectionData.SelectionHistory != null)
            {
                TryGetActiveFolderPath(out string currentDirectory);
                if ((Path.GetFileName(AssetDatabase.GetAssetPath(GetCurrentObject())) == Path.GetFileName(currentDirectory)) && (AssetDatabase.IsValidFolder(AssetDatabase.GetAssetPath(GetCurrentObject()))))
                {
                    // If the current object is the last object in the current folder, highlight it without changing the selection history index.
                    //SelectionChangedByEditor = true;
                    if (lockInspector) LockInspectorForNextSelection();
                    EditorGUIUtility.PingObject(GetCurrentObject());
                    RevertHistory();
                    SelectionHistoryIndex = SelectionData.SelectionHistory.Count - 1;
                }
                else
                {
                    if (SelectionHistoryIndex > 0 && SelectionHistoryIndex < SelectionData.SelectionHistory.Count)
                    {
                        if (lockInspector) LockInspectorForNextSelection();
                        if (SelectionHistoryIndex > 0 && SelectionData.SelectionHistory != null)
                        {
                            // Set a flag to indicate that the selection was changed by the editor.
                            SelectionChangedByEditor = true;
                            SelectionHistoryIndex--;
                        }
                        Selection.objects = SelectionData.SelectionHistory[SelectionHistoryIndex].array;
                        EditorGUIUtility.PingObject(GetCurrentObject());
                    }
                }
            }
        }
        public static void RevertHistory()
        {
            // Remove items from the selection history if necessary
            int diff = (SelectionData.SelectionHistory.Count - 1) - SelectionHistoryIndex; 
            if (diff > 0)
            {
                {
                    SelectionData.SelectionHistory.Reverse(SelectionHistoryIndex, SelectionData.SelectionHistory.Count - SelectionHistoryIndex);
                }
            }
        }
        private static bool TryGetActiveFolderPath(out string path)
        {
            // Use reflection to get the private static method TryGetActiveFolderPath of the ProjectWindowUtil class.
            var _tryGetActiveFolderPath = typeof(ProjectWindowUtil).GetMethod("TryGetActiveFolderPath", BindingFlags.Static | BindingFlags.NonPublic);
            // Create an object array containing a single null value as the method parameter.
            object[] args = new object[] { null };
            // Invoke the TryGetActiveFolderPath method with the null parameter, which will populate the `args` array with the active folder path.
            bool found = (bool)_tryGetActiveFolderPath.Invoke(null, args);
            // Set the `path` output parameter to the first element in the `args` array, which is the active folder path.
            path = (string)args[0];
            // Return whether the active folder path was found.
            return found;
        }
        public static void ToggleProjectBrowserHelperArrows()
        {
            ShowHelperArrows = ShowHelperArrows ? false : true;
            SavePrefs();
        }
        public static void ToggleAssetDetails()
        {
            ShowAssetDetails = ShowAssetDetails ? false : true;
            SavePrefs();
        }
        public static void ToggleDepthLevel1()
        {
            Tracking_Depth_1 = true;
            Tracking_Depth_2 = false;
            Tracking_Depth_3 = false;
            SavePrefs();
        }
        public static void ToggleDepthLevel2()
        {
            
            Tracking_Depth_1 = false;
            Tracking_Depth_2 = true;
            Tracking_Depth_3 = false;
            SavePrefs();
        }
        public static void ToggleDepthLevel3()
        {
            Tracking_Depth_1 = false;
            Tracking_Depth_2 = false;
            Tracking_Depth_3 = true;
            SavePrefs();
        }
        public static Object GetCurrentObject()
        {
            if (SelectionData.SelectionHistory == null || SelectionData.SelectionHistory.Count == 0) return null;
            return SelectionData.SelectionHistory[SelectionHistoryIndex].array[0];
        }
        public static void SavePrefs()
        {
            PlayerPrefs.SetInt("ShowAssetDetails", ShowAssetDetails ? 1 : 0);
            PlayerPrefs.SetInt("ShowHelperArrows", ShowHelperArrows ? 1 : 0);
            PlayerPrefs.SetInt("Tracking_Depth_1", Tracking_Depth_1 ? 1 : 0);
            PlayerPrefs.SetInt("Tracking_Depth_2", Tracking_Depth_2 ? 1 : 0);
            PlayerPrefs.SetInt("Tracking_Depth_3", Tracking_Depth_3 ? 1 : 0);
            PlayerPrefs.Save();
        }
        public static void LoadPrefs()
        { 
            ShowAssetDetails = PlayerPrefs.GetInt("ShowAssetDetails", 0) == 1 ? true : false;
            ShowHelperArrows = PlayerPrefs.GetInt("ShowHelperArrows", 0) == 1 ? true : false;
            Tracking_Depth_1 = PlayerPrefs.GetInt("Tracking_Depth_1", 0) == 1 ? true : false;
            Tracking_Depth_2 = PlayerPrefs.GetInt("Tracking_Depth_2", 0) == 1 ? true : false;
            Tracking_Depth_3 = PlayerPrefs.GetInt("Tracking_Depth_3", 0) == 1 ? true : false;
        }
        public static void LockInspectorForNextSelection()
        {
            ActiveEditorTracker.sharedTracker.isLocked = true;
            InspectorLockedByEditor = true;
            UnlockWithNextSelection = false;
        }

        public static void LoadAssets(int index)
        {
            if (SelectionData.SavedAssets[index].array.Length > 0 && SelectionData.SavedAssets[index].array[0] != null)
            {
                Selection.objects = SelectionData.SavedAssets[index].array;
                TryGetActiveFolderPath(out currentDirectory);
                EditorGUIUtility.PingObject(Selection.objects[0]);
            }
        }

    }
}

 