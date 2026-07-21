#if UNITY_6000_3_OR_NEWER
using System.Collections.Generic;
using SingularityGroup.HotReload.Editor.Localization;
using UnityEditor;
using UnityEditor.Toolbars;
using UnityEngine;

namespace SingularityGroup.HotReload.Editor {

    internal static class HotReloadToolbar {

        const string k_ElementId = "HotReload";

        static readonly Spinner _spinner = new Spinner(100);

        static HotReloadToolbar() {

            bool _lastShowingRedDot = HotReloadState.ShowingRedDot;
            EditorIndicationState.IndicationStatus _lastIndicationStatus = EditorIndicationState.CurrentIndicationStatus;
            EditorApplication.update += () => {
                if (_lastShowingRedDot == HotReloadState.ShowingRedDot && _lastIndicationStatus == EditorIndicationState.CurrentIndicationStatus) {
                    return;
                }
                _lastShowingRedDot = HotReloadState.ShowingRedDot;
                _lastIndicationStatus = EditorIndicationState.CurrentIndicationStatus;
                MainToolbar.Refresh(k_ElementId);
            };
        }

        [MainToolbarElement(
            k_ElementId,
            defaultDockPosition = MainToolbarDockPosition.Right)]
        static IEnumerable<MainToolbarElement> CreateToolbar() {
            // ── Logo button ────────────────────────────────────────────────
            yield return new MainToolbarButton(
                new MainToolbarContent(
                    "",
                    GetLogoIcon(),
                    HotReloadState.ShowingRedDot ? $"Hot Realod\n{Translations.Timeline.OpenToViewNewEventsTooltip}" : "Hot Reload"),
                OnLogoClick);
            
            
            // ── Indication button ────────────────────────────────────────────
            yield return new MainToolbarButton(
                new MainToolbarContent(
                    "",
                    GetIndicationIcon(),
                    string.Format(Translations.Timeline.IndicationTooltip, EditorIndicationState.IndicationStatusText)),
                OnIndicationClick);


            // ── Recompile button ─────────────────────────────────────────────
            yield return new MainToolbarButton(
                new MainToolbarContent(
                    "",
                    GUIHelper.GetInvertibleIcon(InvertibleIcon.Recompile),
                    Translations.Miscellaneous.OverlayTooltipRecompile),
                HotReloadRunTab.RecompileWithChecks);
        }

        static void OnLogoClick() {
            HotReloadWindow.Open();
            if (HotReloadWindow.Current) {
                HotReloadWindow.Current.SelectTab(typeof(HotReloadRunTab));
            }
        }

        static void OnIndicationClick() =>
            HotReloadEventPopup.Open(PopupSource.Overlay, Event.current.mousePosition);

        static Texture2D GetIndicationIcon() {
            if (EditorIndicationState.IndicationIconPath == null || EditorIndicationState.SpinnerActive) {
                return _spinner.GetIcon();
            }
            return GUIHelper.GetLocalIcon(EditorIndicationState.IndicationIconPath);
        }

        static Texture2D GetLogoIcon() => HotReloadState.ShowingRedDot ? 
            GUIHelper.GetInvertibleIcon(InvertibleIcon.LogoNew) :
            GUIHelper.GetInvertibleIcon(InvertibleIcon.Logo);
    }
}
#endif
