#if UNITY_EDITOR

using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace VLB_Installer
{
    [CustomEditor(typeof(Installer))]
    public class InstallerEditor : Editor
    {
        readonly string CurrentVersion = "2.0.2";
        readonly string PackagePath = "Assets/VolumetricLightBeamPackage/VolumetricLightBeam.unitypackage";
        readonly string[] OldPluginPath = new string[]
        {
            "Assets/VolumetricLightBeam",
            "Assets/Plugins/VolumetricLightBeam"
        };

        protected override void OnHeaderGUI()
        {
            GUILayout.BeginVertical("In BigTitle");
            EditorGUILayout.Separator();
            EditorGUILayout.LabelField("Volumetric Light Beam - Plugin Installer", EditorStyles.boldLabel);
            EditorGUILayout.Separator();
            GUILayout.EndVertical();
        }

        public override void OnInspectorGUI()
        {
            if (GUILayout.Button(new GUIContent("Plugin Documentation", "Open the online documentation.")))
            {
                UnityEditor.Help.BrowseURL("http://saladgamer.com/vlb-doc/");
            }
            EditorGUILayout.Separator();

            if (GUILayout.Button(string.Format("Install version {0}", CurrentVersion)))
            {
                Install();
            }

            EditorGUILayout.HelpBox(
                "Clicking on install will:\n- Remove the previous version of the plugin (if found)\n- Import the new 'VolumetricLightBeam.unitypackage'.",
                MessageType.Info);


            EditorGUILayout.Separator();

            EditorGUILayout.HelpBox(
                "We highly recommend to restart Unity after installing the new version, otherwise you can encounter script or shader errors.",
                MessageType.Warning);

            if (GUILayout.Button("Restart Unity"))
            {
                RestartUnity();
            }
        }

        void Install()
        {
            Debug.Log("Opening empty scene...");
            EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo();
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            AssetDatabase.Refresh();

            Debug.Log("Removing previous VolumetricLightBeam folder...");
            foreach(var path in OldPluginPath)
                AssetDatabase.DeleteAsset(path);
            AssetDatabase.Refresh();

            Debug.LogFormat("Importing package {0}...", PackagePath);
            AssetDatabase.ImportPackage(PackagePath, false);
            AssetDatabase.Refresh();

            Debug.Log("Done!");
        }

        void RestartUnity()
        {
            EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo();
            AssetDatabase.Refresh();
            EditorApplication.OpenProject(System.IO.Directory.GetCurrentDirectory());
        }
    }
}
#endif