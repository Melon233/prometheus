using System.Diagnostics;
using UnityEngine;
using UnityEditor;


namespace SingularityGroup.HotReload.Editor {
    public enum PopupSource {
        Window,
        Overlay,
    }
    public class HotReloadEventPopup : PopupWindowContent {
        public static HotReloadEventPopup I = new HotReloadEventPopup();
        private Vector2 _PopupScrollPos;
        public bool open { get; private set; }
        private PopupSource source;
        private HotReloadRunTabState currentState;
        
        public static void Open(PopupSource source, Vector2 pos) {
            I.source = source;
            PopupWindow.Show(new Rect(pos.x, pos.y, 0, 0), I);
        }
        
        public override Vector2 GetWindowSize() {
            if (HotReloadRunTab.ShouldRenderConsumption(currentState)
                && (HotReloadWindowStyles.windowScreenWidth <= Constants.ConsumptionsHideWidth
                || HotReloadWindowStyles.windowScreenHeight <= Constants.ConsumptionsHideHeight
                || source == PopupSource.Overlay)
            ) {
                return new Vector2(600, 450);
            } else {
                return new Vector2(500, 375);
            }
        }
        
        public void Repaint() {
            if (open) {
                PopupWindow.GetWindow<PopupWindow>().Repaint();
            }
        }

        public override void OnGUI(Rect rect) {
            if (Event.current.type == EventType.Layout) {
                currentState = HotReloadRunTabState.Current;
            }
            if (HotReloadWindowStyles.windowScreenWidth <= Constants.UpgradeLicenseNoteHideWidth
                || HotReloadWindowStyles.windowScreenHeight <= Constants.UpgradeLicenseNoteHideHeight
                || source == PopupSource.Overlay
            ) {
                HotReloadRunTab.RenderUpgradeLicenseNote(currentState, HotReloadWindowStyles.UpgradeLicenseButtonOverlayStyle);
            }
            using (new EditorGUILayout.HorizontalScope(EditorStyles.helpBox)) {
                using (var scope = new EditorGUILayout.ScrollViewScope(_PopupScrollPos, GUIStyle.none, GUI.skin.verticalScrollbar, GUILayout.MaxHeight(495))) {
                    _PopupScrollPos.x = scope.scrollPosition.x;
                    _PopupScrollPos.y = scope.scrollPosition.y;

                    
                    var renderDebuggerInfo = Debugger.IsAttached && !CodePatcher.I.debuggerCompatibilityEnabled;
                    if ((HotReloadWindowStyles.windowScreenWidth <= Constants.ConsumptionsHideWidth
                        || HotReloadWindowStyles.windowScreenHeight <= Constants.ConsumptionsHideHeight
                        || source == PopupSource.Overlay) && !renderDebuggerInfo
                    ) {
                        HotReloadRunTab.RenderLicenseInfo(currentState);
                    }
                    if (renderDebuggerInfo) {
                        HotReloadRunTab.RenderDebuggerAttachedInfo(true);
                    }

                    HotReloadRunTab.RenderBars(currentState);
                }
            }

            bool rateAppShown = HotReloadWindow.ShouldShowRateApp();
            if ((HotReloadWindowStyles.windowScreenWidth <= Constants.RateAppHideWidth
                || HotReloadWindowStyles.windowScreenHeight <= Constants.RateAppHideHeight
                || source == PopupSource.Overlay)
                && rateAppShown
            ) {
                HotReloadWindow.RenderRateApp();
            }
            
            if (HotReloadWindowStyles.windowScreenWidth <= Constants.EventFiltersShownHideWidth
                || source == PopupSource.Overlay
            ) {
                using (new EditorGUILayout.HorizontalScope()) {
                    GUILayout.Space(21);
                    HotReloadTimelineHelper.RenderAlertFilters();
                    
                    if (GUILayout.Button(GUIHelper.GetInvertibleIcon(InvertibleIcon.BugReport), GUILayout.MaxHeight(20), GUILayout.MaxWidth(30))) {
                        ReportWindowAPI.OpenBugReport();
                    }
                }
            }
            HotReloadState.ShowingRedDot = false;
        }
        
        public override void OnOpen() {
            open = true;
        }
        
        public override void OnClose() {
            _PopupScrollPos = Vector2.zero;
            open = false;
        }
    }
}