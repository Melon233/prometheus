using UnityEditor;
using UnityEngine;

namespace EasyAudioCutter
{
    [InitializeOnLoad]
    public static class AudioCutterProjectButton
    {
        private const string ButtonText = "✂ Edit";
        private const float ButtonWidth = 70f;

        static AudioCutterProjectButton()
        {
            EditorApplication.projectWindowItemOnGUI += DrawButtonOnSelectedAudioClip;
        }

        private static void DrawButtonOnSelectedAudioClip(string guid, Rect selectionRect)
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            AudioClip clip = AssetDatabase.LoadAssetAtPath<AudioClip>(path);

            if (clip == null)
                return;

            bool isSelected = Selection.activeObject == clip;

            if (!isSelected)
                return;


            Rect buttonRect = new Rect(selectionRect);

            if (selectionRect.width > 150)
            {
                buttonRect.x = selectionRect.xMax - ButtonWidth - 2f;
                buttonRect.width = ButtonWidth;
                buttonRect.height = selectionRect.height;
            }
            else
            {
                buttonRect.x = selectionRect.x + (selectionRect.width / 2f) - (ButtonWidth / 2f);
                buttonRect.width = ButtonWidth;
                buttonRect.y = selectionRect.y + 5f;
                buttonRect.height = 25f;
            }

            GUIStyle buttonStyle = EditorStyles.miniButton;
            /*
            if (GUI.Button(buttonRect, ButtonText, buttonStyle))
            {
                EasyAudioCutter window = EditorWindow.GetWindow<EasyAudioCutter>("Easy Audio Cutter");
                window.Show();
                window.Initialize(clip);
                Event.current.Use();
            }
            */
        }
    }
}