using System;
using System.IO;
using SingularityGroup.HotReload.DTO;
using SingularityGroup.HotReload.Editor.Cli;
using SingularityGroup.HotReload.EditorDependencies;
using UnityEditor;
using UnityEngine;
#if UNITY_2019_4_OR_NEWER
using System.Reflection;
using Unity.CodeEditor;
#endif

namespace SingularityGroup.HotReload.Editor {
    static class InstallUtility {
        static string installFlagPath = PackageConst.LibraryCachePath + "/installFlag.txt";

        public static void DebugClearInstallState() {
            File.Delete(installFlagPath);
        }

        // HandleEditorStart is only called on editor start, not on domain reload
        public static void HandleEditorStart(string updatedFromVersion) {
            var showOnStartup = HotReloadPrefs.ShowOnStartup;
            if (showOnStartup == ShowOnStartupEnum.Always || (showOnStartup == ShowOnStartupEnum.OnNewVersion && !String.IsNullOrEmpty(updatedFromVersion))) {
                if (!HotReloadPrefs.DeactivateHotReload) {
                    HotReloadWindow.Open();
                }
            }
            if (HotReloadPrefs.LaunchOnEditorStart && !HotReloadPrefs.DeactivateHotReload) {
                EditorCodePatcher.DownloadAndRun().Forget();
            }
            
            EditorCodePatcher.SendEditorTelemetryIfEnabled(new Stat(StatSource.Client, StatLevel.Debug, StatFeature.Editor, StatEventType.Start));
        }

        public static void CheckForNewInstall() {
            if(File.Exists(installFlagPath) || MultiplayerPlaymodeHelper.IsClone) {
                return;
            }
            Directory.CreateDirectory(Path.GetDirectoryName(installFlagPath));
            using(File.Create(installFlagPath)) { }
            //Avoid opening the window on domain reload
            EditorApplication.delayCall += HandleNewInstall;
        }
        
        static void HandleNewInstall() {
            if (EditorCodePatcher.licenseType == UnityLicenseType.UnityPro) {
                RedeemLicenseHelper.I.StartRegistration();
            }
            HotReloadPrefs.AllowDisableUnityAutoRefresh = true;
            HotReloadPrefs.AllAssetChanges = true;
            HotReloadPrefs.AutoRecompileUnsupportedChanges = true;
            HotReloadPrefs.AutoRecompileUnsupportedChangesOnExitPlayMode = true;
#if UNITY_EDITOR_WIN
            HotReloadPrefs.UseWatchman = false;
#endif
            if (HotReloadCli.CanOpenInBackground) {
                HotReloadPrefs.DisableConsoleWindow = true;
            }
            HotReloadSuggestionsHelper.SetSuggestionsShown(HotReloadSuggestionKind.TelemetryCollection);
        }
    }
}