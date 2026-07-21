#if UNITY_2021_3_OR_NEWER
using System;
using System.Collections.Generic;
using System.Reflection;
using SingularityGroup.HotReload.Editor.Localization;
using UnityEditor.Overlays;
using UnityEngine.UIElements;
using UnityEditor;
using UnityEngine;
using UnityEditor.Toolbars;

namespace SingularityGroup.HotReload.Editor {
    [Overlay(typeof(SceneView), Translations.MenuItems.OverlayDescription, true)]
    [Icon("Assets/HotReload/Editor/Resources/Icon_DarkMode.png")]
    internal class HotReloadOverlay : ToolbarOverlay {
        HotReloadOverlay() : base(HotReloadToolbarLogoButton.id, HotReloadToolbarIndicationButton.id, HotReloadToolbarRecompileButton.id) {
            EditorApplication.update += Update;
        }
        
        [EditorToolbarElement(id, typeof(SceneView))]
        class HotReloadToolbarLogoButton : EditorToolbarButton, IAccessContainerWindow {
            internal const string id = "HotReloadOverlay/LogoButton";
            public EditorWindow containerWindow { get; set; }
            
            bool lastShowingRedDot;
            
            internal HotReloadToolbarLogoButton() {
                icon = HotReloadState.ShowingRedDot ? GUIHelper.GetInvertibleIcon(InvertibleIcon.LogoNew) : GUIHelper.GetInvertibleIcon(InvertibleIcon.Logo);
                tooltip = "Hot Reload";
                clicked += OnClick;
                EditorApplication.update += Update;
            }

            void OnClick() {
                HotReloadWindow.Open();
                if (HotReloadWindow.Current) {
                    HotReloadWindow.Current.SelectTab(typeof(HotReloadRunTab));
                }
            }
       
            void Update() {
                if (lastShowingRedDot != HotReloadState.ShowingRedDot) {
                    icon = HotReloadState.ShowingRedDot ? GUIHelper.GetInvertibleIcon(InvertibleIcon.LogoNew) : GUIHelper.GetInvertibleIcon(InvertibleIcon.Logo);
                    lastShowingRedDot = HotReloadState.ShowingRedDot;
                }
            }

            ~HotReloadToolbarLogoButton() {
                clicked -= OnClick;
                EditorApplication.update -= Update;
            }
        }
        
        EditorIndicationState.IndicationStatus lastIndicationStatus;
        
        [EditorToolbarElement(id, typeof(SceneView))]
        class HotReloadToolbarIndicationButton : EditorToolbarButton, IAccessContainerWindow {
            internal const string id = "HotReloadOverlay/IndicationButton";
            public EditorWindow containerWindow { get; set; }

            EditorIndicationState.IndicationStatus lastIndicationStatus;
            
            internal HotReloadToolbarIndicationButton() {
                icon = GetIndicationIcon();
                tooltip = string.Format(Translations.Timeline.IndicationTooltip, EditorIndicationState.IndicationStatusText);
                clicked += OnClick;
                EditorApplication.update += Update;
            }

            void OnClick() {
                HotReloadEventPopup.Open(PopupSource.Overlay, Event.current.mousePosition);
            }
       
            void Update() {
                if (lastIndicationStatus != EditorIndicationState.CurrentIndicationStatus) {
                    icon = GetIndicationIcon();
                    tooltip = string.Format(Translations.Timeline.IndicationTooltip, EditorIndicationState.IndicationStatusText);
                    lastIndicationStatus = EditorIndicationState.CurrentIndicationStatus;
                }
            }

            ~HotReloadToolbarIndicationButton() {
                clicked -= OnClick;
                EditorApplication.update -= Update;
            }
        }
        
        
        [EditorToolbarElement(id, typeof(SceneView))]
        class HotReloadToolbarRecompileButton : EditorToolbarButton, IAccessContainerWindow {
            internal const string id = "HotReloadOverlay/RecompileButton";
            
            public EditorWindow containerWindow { get; set; }
            
            private Texture2D refreshIcon => GUIHelper.GetInvertibleIcon(InvertibleIcon.Recompile);
            internal HotReloadToolbarRecompileButton() {
                icon = refreshIcon;
                tooltip = Translations.Miscellaneous.OverlayTooltipRecompile;
                clicked += HotReloadRunTab.RecompileWithChecks;
            }
        }

        private static Texture2D latestIcon;
        private static Dictionary<string, Texture2D> iconTextures = new Dictionary<string, Texture2D>();
        private static Spinner spinner = new Spinner(100);
        private static Texture2D GetIndicationIcon() {
            if (EditorIndicationState.IndicationIconPath == null || EditorIndicationState.SpinnerActive) {
                latestIcon = spinner.GetIcon();
            } else {
                latestIcon = GUIHelper.GetLocalIcon(EditorIndicationState.IndicationIconPath);
            }
            return latestIcon;
        }

        private static Image indicationIcon;
        private static Label indicationText;

        bool initialized;
        /// <summary>
        /// Create Hot Reload overlay panel.
        /// </summary>
        public override VisualElement CreatePanelContent() {
#if UNITY_2022_1_OR_NEWER
            return CreateContent(Layout.HorizontalToolbar);
#elif UNITY_2021_3_OR_NEWER
            return (VisualElement)typeof(Overlay).GetMethod("CreateContent", BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Public, null, new Type[] {typeof(Layout)}, null)?.Invoke(this, new object[] {
                Layout.HorizontalToolbar,
            });
#endif
        }

        static bool _repaint;
        static bool _instantRepaint;
        static DateTime _lastRepaint;
        private void Update() {
            if (!initialized) {
                return;
            }
            if (lastIndicationStatus != EditorIndicationState.CurrentIndicationStatus) {
                indicationIcon.image = GetIndicationIcon();
                indicationText.text = EditorIndicationState.IndicationStatusText;
                lastIndicationStatus = EditorIndicationState.CurrentIndicationStatus;
            }
            try {
                if (HotReloadEventPopup.I.open 
                    && EditorWindow.mouseOverWindow
                    && EditorWindow.mouseOverWindow?.GetType() == typeof(UnityEditor.PopupWindow)
                ) {
                    _repaint = true;
                }
            } catch (NullReferenceException) {
                // Unity randomly throws nullrefs when EditorWindow.mouseOverWindow gets accessed
            }
            if (_repaint && DateTime.UtcNow - _lastRepaint > TimeSpan.FromMilliseconds(33)) {
                _repaint = false;
                _instantRepaint = true;
            }
            if (_instantRepaint) {
                HotReloadEventPopup.I.Repaint();
            }
        }

        ~HotReloadOverlay() {
            EditorApplication.update -= Update;
        }
    }
}
#endif
