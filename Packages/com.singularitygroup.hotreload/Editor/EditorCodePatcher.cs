using System;
using System.Collections.Generic;
#if UNITY_EDITOR_WIN
using System.ComponentModel;
#endif
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using JetBrains.Annotations;
using SingularityGroup.HotReload.DTO;
using SingularityGroup.HotReload.Editor.Cli;
using SingularityGroup.HotReload.Editor.Demo;
using SingularityGroup.HotReload.EditorDependencies;
using UnityEditor;
using UnityEngine;
using Debug = UnityEngine.Debug;
using Task = System.Threading.Tasks.Task;
using System.Reflection;
using System.Runtime.CompilerServices;
using SingularityGroup.HotReload.Localization;
using SingularityGroup.HotReload.Newtonsoft.Json;
using UnityEditor.Build;
using UnityEditor.Compilation;
using UnityEditor.UIElements;
using UnityEditorInternal;
using UnityEngine.UIElements;
using Translations = SingularityGroup.HotReload.Editor.Localization.Translations;

[assembly: InternalsVisibleTo("SingularityGroup.HotReload.IntegrationTests")]

namespace SingularityGroup.HotReload.Editor {
    internal class Config {
        public bool patchEditModeOnlyOnEditorFocus;
        public string[] assetBlacklist;
        public bool changePlaymodeTint;
        public bool disableCompilingFromEditorScripts;
        public bool enableInspectorFreezeFix;
    }
    
    [InitializeOnLoad]
    internal static class EditorCodePatcher {
        static string sessionFilePath = PackageConst.LibraryCachePath + "/sessionId.txt";
        
        internal static readonly ServerDownloader serverDownloader;
        internal static bool _compileError;
        internal static bool _applyingFailed;
        internal static bool _appliedPartially;
        internal static bool _appliedUndetected;
        
        static Timer timer; 
        static bool init;

        internal static UnityLicenseType licenseType { get; private set; }
        internal static bool LoginNotRequired => PackageConst.IsAssetStoreBuild && licenseType != UnityLicenseType.UnityPro;
        internal static bool compileError => _compileError;
        
        internal static PatchStatus patchStatus = PatchStatus.None;
        
        internal static event Action<(MethodPatchResponse, RegisterPatchesResult)> OnPatchHandled;
        
        internal static Config config;

        
        #if ODIN_INSPECTOR
        internal static bool DrawPrefix(Sirenix.OdinInspector.Editor.InspectorProperty __instance) {
            return !UnityFieldHelper.IsFieldHidden(__instance.ParentType, __instance.Name);
        }
        internal static MethodInfo OdinPropertyDrawPrefixInfo = typeof(EditorCodePatcher).GetMethod("DrawPrefix", BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public);
        #if UNITY_2021_1_OR_NEWER
        internal static MethodInfo OdinPropertyDrawInfo = typeof(Sirenix.OdinInspector.Editor.InspectorProperty)?.GetMethod("Draw", 0, BindingFlags.Instance | BindingFlags.Public, null, new Type[]{}, null);
        #else
        internal static MethodInfo OdinPropertyDrawInfo = typeof(Sirenix.OdinInspector.Editor.InspectorProperty)?.GetMethod("Draw", BindingFlags.Instance | BindingFlags.Public, null, new Type[]{}, null);
        #endif
        internal static MethodInfo DrawOdinInspectorInfo = typeof(Sirenix.OdinInspector.Editor.OdinEditor)?.GetMethod("DrawOdinInspector", BindingFlags.NonPublic | BindingFlags.Instance);
        #else
        internal static MethodInfo OdinPropertyDrawPrefixInfo = null;
        internal static MethodInfo OdinPropertyDrawInfo = null;
        internal static MethodInfo DrawOdinInspectorInfo = null;
        #endif

        internal static MethodInfo GetDrawVInspectorInfo() {
            // performance optimization
            if (!Directory.Exists("Assets/vInspector")) {
                return null;
            }
            try {
                var t = Type.GetType("VInspector.AbstractEditor, VInspector");
                return t?.GetMethod("OnInspectorGUI", BindingFlags.Public | BindingFlags.Instance);
            } catch {
                // ignore
            }
            return null;
        }

        internal static ICompileChecker compileChecker;
        static bool quitting;
        static EditorCodePatcher() {
            if(init) {
                //Avoid infinite recursion in case the static constructor gets accessed via `InitPatchesBlocked` below
                return;
            }
            Translations.LoadDefaultLocalization();
            SingularityGroup.HotReload.Localization.Translations.LoadDefaultLocalization();
            if (File.Exists(PackageConst.ConfigFilePath)) {
                config = JsonConvert.DeserializeObject<Config>(File.ReadAllText(PackageConst.ConfigFilePath));
            } else {
                config = new Config();
            }
            init = true;
            UnityHelper.Init();
            //Use synchonization context if possible because it's more reliable.
            ThreadUtility.InitEditor();
            if (!EditorWindowHelper.IsHumanControllingUs()) {
                return;
            }
            
            serverDownloader = new ServerDownloader();
            serverDownloader.CheckIfDownloaded(HotReloadCli.controller);
            SingularityGroup.HotReload.Demo.Demo.I = new EditorDemo();
            if (HotReloadPrefs.DeactivateHotReload) {
                ResetSettings();
                return;
            }
            
            // ReSharper disable ExpressionIsAlwaysNull
            UnityFieldHelper.Init(Log.Warning, HotReloadRunTab.Recompile, DrawOdinInspectorInfo, OdinPropertyDrawInfo, OdinPropertyDrawPrefixInfo, GetDrawVInspectorInfo(), typeof(UnityFieldDrawerPatchHelper), typeof(VisualElement));
            
            timer = new Timer(OnIntervalThreaded, (Action) OnIntervalMainThread, 500, 500);

            if (MultiplayerPlaymodeHelper.IsClone) {
                InitServerInfo();
            } else {
                UpdateHost();
            }
            licenseType = UnityLicenseHelper.GetLicenseType(isChina: PackageConst.DefaultLocale == Locale.SimplifiedChinese);
            compileChecker = CompileChecker.Create();
            compileChecker.onCompilationFinished += OnCompilationFinished;
            EditorApplication.delayCall += InstallUtility.CheckForNewInstall;
            AddEditorFocusChangedHandler(OnEditorFocusChanged);
            // When domain reloads, this is a good time to ensure server has up-to-date project information
            if (ServerHealthCheck.I.IsServerHealthy) {
                EditorApplication.delayCall += TryPrepareBuildInfo;
            }
            HotReloadSuggestionsHelper.Init();
            // reset in case last session didn't shut down properly
            CheckEditorSettings();
            EditorApplication.quitting += ResetSettingsOnQuit;
            
            AssemblyReloadEvents.beforeAssemblyReload += () => {
                HotReloadTimelineHelper.PersistTimeline().Forget();
            };
            
            ServerHealthCheck.instance.CheckHealth();
            if (ServerHealthCheck.I.IsServerHealthy) {
                HotReloadTimelineHelper.InitPersistedEvents().Forget();
            } else {
                HotReloadTimelineHelper.ClearPersistance();
            }

            CompilationPipeline.assemblyCompilationFinished += (string _, CompilerMessage[] messages) => {
                if (MultiplayerPlaymodeHelper.IsClone) {
                    return;
                }
                bool hasCompileError = false;
                foreach (var message in messages) {
                    if (message.type != CompilerMessageType.Error) {
                        continue;
                    }
                    hasCompileError = true;
                    if (!message.message.Contains("Sirenix")) {
                        continue;
                    }
                    if (message.message.Contains("CS0012")
                        || message.message.Contains("CS0234")
                        || message.message.Contains("CS0246")
                        || message.message.Contains("CS9286")
                    ) {
                        #if UNITY_2021_1_OR_NEWER
                        var target = NamedBuildTarget.FromBuildTargetGroup(EditorUserBuildSettings.selectedBuildTargetGroup);
                        var symbols = PlayerSettings.GetScriptingDefineSymbols(target).Split(";").ToList();
                        symbols.Remove("ODIN_INSPECTOR");
                        PlayerSettings.SetScriptingDefineSymbols(target, string.Join(";", symbols));
                        #else
                        var symbols = PlayerSettings.GetScriptingDefineSymbolsForGroup(EditorUserBuildSettings.selectedBuildTargetGroup).Split(';').ToList();
                        symbols.Remove("ODIN_INSPECTOR");
                        PlayerSettings.SetScriptingDefineSymbolsForGroup(EditorUserBuildSettings.selectedBuildTargetGroup, string.Join(";", symbols));
                        #endif
                    }
                }
                if (!hasCompileError) {
                    HotReloadState.EditorAssemblyNames = "";
                    HotReloadState.PlayerAssemblyNames = "";
                    HotReloadState.OmittedAssemblies = "";
                }
            };
            
            CompilationPipeline.compilationFinished += obj => {
                if (MultiplayerPlaymodeHelper.IsClone) {
                    CompileMethodDetourer.Reset();
                    return;
                }
                // reset in case package got removed
                // if it got removed, it will not be enabled again
                // if it wasn't removed, settings will get handled by OnIntervalMainThread
                AutoRefreshSettingChecker.Reset();
                ScriptCompilationSettingChecker.Reset();
                PlaymodeTintSettingChecker.Reset();
                HotReloadRunTab.recompiling = false;
                CompileMethodDetourer.Reset();
                
                
                HotReloadTimelineHelper.CreateReloadFinishedEventEntry(patchedMethodsDisplayNames: new string[]{ Translations.Timeline.FullAssemblyRecompilation }, isCompile: true);
            };
            DetectEditorStart();
            DetectVersionUpdate();
            CodePatcher.I.fieldHandler = new FieldHandler(FieldDrawerUtil.StoreField, UnityFieldHelper.HideField, UnityFieldHelper.RegisterInspectorFieldAttributes);
            if (EditorApplication.isPlayingOrWillChangePlaymode) {
                CodePatcher.I.InitPatchesBlocked();
            }

#pragma warning disable CS0612 // Type or member is obsolete
            if (HotReloadPrefs.RateAppShownLegacy) {
                HotReloadPrefs.RateAppShown = true;
            }
            if (!File.Exists(HotReloadPrefs.showOnStartupPath)) {
                var showOnStartupLegacy = HotReloadPrefs.GetShowOnStartupEnum();
                HotReloadPrefs.ShowOnStartup = showOnStartupLegacy;
            }
#pragma warning restore CS0612 // Type or member is obsolete
            
            HotReloadState.ShowingRedDot = false;

            if (DateTime.Now < new DateTime(2023, 11, 1)) {
                HotReloadSuggestionsHelper.SetSuggestionsShown(HotReloadSuggestionKind.UnityBestDevelopmentToolAward2023);
            } else {
                HotReloadSuggestionsHelper.SetSuggestionInactive(HotReloadSuggestionKind.UnityBestDevelopmentToolAward2023);
            }
            
            EditorApplication.playModeStateChanged += state => {
                if (state == PlayModeStateChange.EnteredEditMode && HotReloadPrefs.AutoRecompileUnsupportedChangesOnExitPlayMode) {
                    if (TryRecompileUnsupportedChanges()) {
                        HotReloadState.RecompiledUnsupportedChangesOnExitPlaymode = true;
                    }
                }
            };
            if (HotReloadState.RecompiledUnsupportedChangesInPlaymode) {
                HotReloadState.RecompiledUnsupportedChangesInPlaymode = false;
                EditorApplication.isPlaying = true;
            }
#if UNITY_2020_1_OR_NEWER
            if (CompilationPipeline.codeOptimization != CodeOptimization.Release) {
                HotReloadSuggestionsHelper.SetSuggestionInactive(HotReloadSuggestionKind.SwitchToDebugModeForInlinedMethods);
            }
#endif
            if (!HotReloadState.EditorCodePatcherInit) {
                ClearPersistence();
                HotReloadState.EditorCodePatcherInit = true;
            }

            CodePatcher.I.debuggerCompatibilityEnabled = !HotReloadPrefs.AutoDisableHotReloadWithDebugger;
            CodePatcher.I.disableTelemetry = HotReloadPrefs.DisableTelemetry;
        }

        static void ResetSettingsOnQuit() {
            quitting = true;
            ResetSettings();
        }
        
        static void ResetSettings() {
            AutoRefreshSettingChecker.Reset();
            ScriptCompilationSettingChecker.Reset();
            PlaymodeTintSettingChecker.Reset();
            HotReloadCli.StopAsync().Forget();
            CompileMethodDetourer.Reset();
        }

        public static bool autoRecompileUnsupportedChangesSupported;
        static void AddEditorFocusChangedHandler(Action<bool> handler) {
            var eventInfo = typeof(EditorApplication).GetEvent("focusChanged", BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public);
            var addMethod = eventInfo?.GetAddMethod(true) ?? eventInfo?.GetAddMethod(false);
            if (addMethod != null) {
                addMethod.Invoke(null, new object[]{ handler });
            }
            autoRecompileUnsupportedChangesSupported = addMethod != null;
        }

        private static void OnEditorFocusChanged(bool hasFocus) {
            if (hasFocus && !HotReloadPrefs.AutoRecompileUnsupportedChangesImmediately) {
                TryRecompileUnsupportedChanges();
            }
        }

        public static bool TryRecompileUnsupportedChanges() {
            if (MultiplayerPlaymodeHelper.IsClone) {
                return false;
            }
            var isPlaying = EditorApplication.isPlaying;

            var hasPartiallyUnsupportedPatches = false;
            foreach (var patchResponse in CodePatcher.I.PatchHistory) {
                if (patchResponse.partiallySupportedChanges == null) {
                    continue;
                }
                hasPartiallyUnsupportedPatches |= HasPartiallySupportedChangesFiltered(patchResponse.partiallySupportedChanges);
            }
            
            if (!HotReloadPrefs.AutoRecompileUnsupportedChanges
                || !CodePatcher.I.anyFailures
                    && (!HotReloadPrefs.AutoRecompilePartiallyUnsupportedChanges || !hasPartiallyUnsupportedPatches)
                || _compileError && !CodePatcher.I.ignoreCompileErrorOnRecompileUnsupported
                || isPlaying && !HotReloadPrefs.AutoRecompileUnsupportedChangesInPlayMode
                || !isPlaying && !HotReloadPrefs.AutoRecompileUnsupportedChangesInEditMode
            ) {
                return false;
            }
            RecompileUnsupportedChanges();
            return true;
        }

        public static void RecompileUnsupportedChanges() {
            if (MultiplayerPlaymodeHelper.IsClone) {
                return;
            }
            if (HotReloadPrefs.ShowCompilingUnsupportedNotifications) {
                EditorWindowHelper.ShowNotification(EditorWindowHelper.NotificationStatus.NeedsRecompile);
            }
            if (EditorApplication.isPlaying) {
                HotReloadState.RecompiledUnsupportedChangesInPlaymode = true;
            }
            HotReloadRunTab.Recompile();
        }

        private static DateTime lastPrepareBuildInfo = DateTime.UtcNow;

        /// Post state for player builds.
        /// Only check build target because user can change build settings whenever.
        internal static void TryPrepareBuildInfo() {
            if (MultiplayerPlaymodeHelper.IsClone) {
                return;
            }
            // Note: we post files state even when build target is wrong
            // because you might connect with a build downloaded onto the device. 
            if ((DateTime.UtcNow - lastPrepareBuildInfo).TotalSeconds > 5) {
                lastPrepareBuildInfo = DateTime.UtcNow;
                var settingsObject = HotReloadSettingsEditor.LoadSettingsOrDefault();
                var so = new SerializedObject(settingsObject);
                if (IncludeInBuildOption.I.GetValue(so)) {
                    HotReloadCli.PrepareBuildInfoAsync().Forget();
                }
            }
        }

        internal static void RecordActiveDaysForRateApp() {
            var unixDay = (int)(DateTimeOffset.UtcNow.ToUnixTimeSeconds() / 86400);
            var activeDays = GetActiveDaysForRateApp();
            if (activeDays.Count < Constants.DaysToRateApp && activeDays.Add(unixDay.ToString())) {
                HotReloadPrefs.ActiveDays = string.Join(",", activeDays);
            }
        }
        
        internal static HashSet<string> GetActiveDaysForRateApp() {
            if (string.IsNullOrEmpty(HotReloadPrefs.ActiveDays)) {
                return new HashSet<string>();
            }
            return new HashSet<string>(HotReloadPrefs.ActiveDays.Split(','));
        }

        // CheckEditorStart distinguishes between domain reload and first editor open
        // We have some separate logic on editor start (InstallUtility.HandleEditorStart)
        private static void DetectEditorStart() {
            if (MultiplayerPlaymodeHelper.IsClone) {
                return;
            }
            var editorId = EditorAnalyticsSessionInfo.id;
            var currVersion = PackageConst.Version;
            Task.Run(() => {
                try {
                    var lines = File.Exists(sessionFilePath) ? File.ReadAllLines(sessionFilePath) : Array.Empty<string>();

                    long prevSessionId = -1;
                    string prevVersion = null;
                    if (lines.Length >= 2) {
                        long.TryParse(lines[1], out prevSessionId);
                    }
                    if (lines.Length >= 3) {
                        prevVersion = lines[2].Trim();
                    }
                    var updatedFromVersion = (prevSessionId != -1 && currVersion != prevVersion) ? prevVersion : null;

                    if (prevSessionId != editorId && prevSessionId != 0) {
                        // back to mainthread
                        ThreadUtility.RunOnMainThread(() => {
                            InstallUtility.HandleEditorStart(updatedFromVersion);

                            var newEditorId = EditorAnalyticsSessionInfo.id;
                            if (newEditorId != 0) {
                                Task.Run(() => {
                                    try {
                                        // editorId isn't available on first domain reload, must do it here
                                        File.WriteAllLines(sessionFilePath, new[] {
                                            "1", // serialization version
                                            newEditorId.ToString(),
                                            currVersion,
                                        });

                                    } catch (IOException) {
                                        // ignore
                                    }
                                });
                            }
                        });
                    }

                } catch (IOException) {
                    // ignore
                } catch (Exception e) {
                    ThreadUtility.LogException(e);
                }
            });
        }
        
        private static void DetectVersionUpdate() {
            if (serverDownloader.CheckIfDownloaded(HotReloadCli.controller) || MultiplayerPlaymodeHelper.IsClone) {
                return;
            }
            ServerHealthCheck.instance.CheckHealth();
            if (!ServerHealthCheck.I.IsServerHealthy) {
                return;
            }
            var restartServer = EditorUtility.DisplayDialog(Translations.Dialogs.DialogTitleRestartServer,
                Translations.Dialogs.DialogMessageRestartUpdate,
                Translations.Dialogs.DialogButtonRestartServer, Translations.Dialogs.DialogButtonDontRestart);
            if (restartServer) {
                RestartCodePatcher().Forget();
            }
        }

        private static void UpdateHost() {
            RequestHelper.SetServerInfo(new PatchServerInfo(RequestHelper.defaultServerHost, HotReloadState.ServerPort, null, Path.GetFullPath(".")));
        }

        static void OnIntervalThreaded(object o) {
            var wasHealhy = ServerHealthCheck.I.IsServerHealthy;
            ServerHealthCheck.instance.CheckHealth();
            if (wasHealhy != ServerHealthCheck.I.IsServerHealthy) {
                InitServerInfo();
            }
            if (MultiplayerPlaymodeHelper.IsClone && ServerHealthCheck.I.IsServerHealthy) {
                // technically need to call this once but for consistency sake we call it every time (overhead should be minimal)
                RequestHelper.RegisterClone().Forget();
            }
            ThreadUtility.RunOnMainThread((Action)o);
            if (serverDownloader.Progress >= 1f) {
                serverDownloader.CheckIfDownloaded(HotReloadCli.controller);
            }
        }

        private static bool _requestingFlushErrors;
        private static long _lastErrorFlush;
        private static async Task RequestFlushErrors() {
            _requestingFlushErrors = true;
            try {
                await RequestFlushErrorsCore();
            } finally {
                _requestingFlushErrors = false;
            }
        }
        
        private static async Task RequestFlushErrorsCore() {
            var pollFrequency = 500;
            // Delay until we've hit the poll request frequency
            var waitMs = (int)Mathf.Clamp(pollFrequency - ((DateTime.Now.Ticks / (float)TimeSpan.TicksPerMillisecond) - _lastErrorFlush), 0, pollFrequency);
            await Task.Delay(waitMs);
            await FlushErrors();
            _lastErrorFlush = DateTime.Now.Ticks / TimeSpan.TicksPerMillisecond;
        }

        public static bool disableServerLogs;
        public static string lastCompileErrorLog;
        static async Task FlushErrors() {
            var response = await RequestHelper.RequestFlushErrors();
            if (response == null || disableServerLogs || MultiplayerPlaymodeHelper.IsClone) {
                return;
            }
            if (!Application.isPlaying && HotReloadPrefs.PauseHotReloadInEditMode) {
                return;
            }
            if (Debugger.IsAttached && !CodePatcher.I.debuggerCompatibilityEnabled) {
                return;
            }
            foreach (var responseWarning in response.warnings) {
                if (responseWarning.Contains("Scripts have compile errors")) {
                    if (compileError) {
                        Log.Error(responseWarning);
                    } else {
                        lastCompileErrorLog = responseWarning;
                    }
                } else {
                    Log.Warning(responseWarning);
                }

                if (responseWarning.Contains("Multidimensional arrays are not supported")) {
                    await ThreadUtility.SwitchToMainThread();
                    HotReloadSuggestionsHelper.SetSuggestionsShown(HotReloadSuggestionKind.MultidimensionalArrays);
                }
            }
            foreach (var responseError in response.errors) {
                Log.Error(responseError);
            }
        }
        
        internal static bool firstPatchAttempted;
        internal static bool loggedDebuggerRecompile;
        static void OnIntervalMainThread() {
            HotReloadSuggestionsHelper.Check();
            
            // Moved from RequestServerInfo to avoid GC allocations when HR is not active
            
            // Repaint if the running Status has changed since the layout changes quite a bit
            if (running != ServerHealthCheck.I.IsServerHealthy) {
                if (HotReloadWindow.Current) {
                    HotReloadRunTab.RepaintInstant();
                }
                running = ServerHealthCheck.I.IsServerHealthy;
            }
            if (!running) {
                startupCompletedAt = null;
            }
            if (!running && !StartedServerRecently()) {
                // Reset startup progress
                startupProgress = null;
            }
            
            if (!ServerHealthCheck.I.IsServerHealthy) {
                stopping = false;
            }
            if (startupProgress?.Item1 == 1) {
                starting = false;
            }
            if (!_requestingFlushErrors && Running) {
                RequestFlushErrors().Forget();
            }
            
            if (!Application.isPlaying && HotReloadPrefs.PauseHotReloadInEditMode) {
                return;
            }
            if (Debugger.IsAttached && !CodePatcher.I.debuggerCompatibilityEnabled) {
                var userAutoRefreshDisabled = AutoRefreshSettingChecker.IsUserAutoRefreshDisabled();
                if (!HotReloadPrefs.DebuggerOnboardingShown) {
                    HotReloadPrefs.DebuggerOnboardingShown = true;
                    if (EditorUtility.DisplayDialogComplex(
                            title: Translations.Dialogs.DialogTitleHotReloadDebuggerDetected,
                            message: userAutoRefreshDisabled ? Translations.Dialogs.DialogMessageHotReloadDebuggerDetectedPause : Translations.Dialogs.DialogMessageHotReloadDebuggerDetectedRecompile,
                            ok: userAutoRefreshDisabled ? Translations.Dialogs.DialogButtonHotReloadDebuggerDetectedPause : Translations.Dialogs.DialogButtonHotReloadDebuggerDetectedRecompile,
                            cancel: Translations.Dialogs.DialogCloseHotReloadDebuggerDetected,
                            alt: Translations.Dialogs.DialogButtonHotReloadDebuggerDetectedAdvancedOptions) == 2
                    ) {
                        if (EditorUtility.DisplayDialog(
                                title: Translations.Dialogs.DialogTitleHotReloadDebuggerOptions,
                                message: Translations.Dialogs.DialogMessageHotReloadDebuggerOptions,
                                ok: Translations.Dialogs.DialogButtonHotReloadDebuggerOptionsContinue,
                                cancel: userAutoRefreshDisabled ? Translations.Dialogs.DialogButtonHotReloadDebuggerOptionsCancelPause : Translations.Dialogs.DialogButtonHotReloadDebuggerOptionsCancelRecompile)
                        ) {
                            CodePatcher.I.debuggerCompatibilityEnabled = true;
                            HotReloadPrefs.AutoDisableHotReloadWithDebugger = false;
                            return;
                        }
                    }
                }
                if (!HotReloadState.WarnedDebuggerAttached) {
                    HotReloadState.WarnedDebuggerAttached = true;
                    // passed both prompts - hot reload is paused
                    if (CodePatcher.I.PatchesApplied > 0 || !userAutoRefreshDisabled) {
                        Log.Info(Translations.Errors.InfoDebuggerAttachedFullRecompile);
                    } else {
                        // warn about Hot Reload being paused
                        Log.Info(Translations.Errors.InfoDebuggerAttachedPauseHotReload);
                    }
                }
                if (CodePatcher.I.PatchesApplied > 0) {
                    // recompile if any patches were made to avoid debugger session being broken
                    HotReloadRunTab.Recompile();
                }
                return;
            } else {
                HotReloadState.WarnedDebuggerAttached = false;
            }

            if(ServerHealthCheck.I.IsServerHealthy) {
                // NOTE: avoid calling this method when HR is not running to avoid allocations
                RequestServerInfo();
                TryPrepareBuildInfo();
                if (!requestingCompile && (!config.patchEditModeOnlyOnEditorFocus || Application.isPlaying || UnityEditorInternal.InternalEditorUtility.isApplicationActive)) {
                    RequestHelper.PollMethodPatches(HotReloadState.LastPatchId, resp => HandleResponseReceived(resp));
                }
                RequestHelper.PollPatchStatus(resp => {
                    patchStatus = resp.patchStatus;
                    if (patchStatus == PatchStatus.Compiling) {
                        startWaitingForCompile = null;
                    }
                    if (patchStatus == PatchStatus.Patching) {
                        firstPatchAttempted = true;
                        if (HotReloadPrefs.ShowPatchingNotifications) {
                            EditorWindowHelper.ShowNotification(EditorWindowHelper.NotificationStatus.Patching, maxDuration: 10);
                        }
                    } else if (HotReloadPrefs.ShowPatchingNotifications) {
                        EditorWindowHelper.RemoveNotification();
                    }
                }, patchStatus);
                if (HotReloadPrefs.AllAssetChanges) {
                    RequestHelper.PollAssetChanges(HandleAssetChange);
                }
#if UNITY_2020_1_OR_NEWER
                if (!disableInlineChecks) {
                    CheckInlinedMethods();
                }
#endif
            }
            CheckEditorSettings();
        }
        
#if UNITY_2020_1_OR_NEWER
        //only disabled for integration tests
        internal static bool disableInlineChecks = false;
        internal static HashSet<MethodBase> inlinedMethodsFound = new HashSet<MethodBase>();
        internal static void CheckInlinedMethods() {
            if (MultiplayerPlaymodeHelper.IsClone) {
                return;
            }
            if (CompilationPipeline.codeOptimization != CodeOptimization.Release) {
                return;
            }
            HashSet<MethodBase> newInlinedMethods = null;
            try {
                foreach (var method in CodePatcher.I.OriginalPatchMethods) {
                    if (inlinedMethodsFound.Contains(method)) {
                        continue;
                    }
                    var isMethodSynthesized = method.Name.Contains("<") || method.DeclaringType?.Name.Contains("<") == true && method.Name == ".ctor";
                    if (!(method is ConstructorInfo) && !isMethodSynthesized && MethodUtils.IsMethodInlined(method)) {
                        if (newInlinedMethods == null) {
                            newInlinedMethods = new HashSet<MethodBase>();
                        }
                        newInlinedMethods.Add(method);
                    }
                }
                if (newInlinedMethods?.Count > 0) {
                    if (!HotReloadPrefs.LoggedInlinedMethodsDialogue) {
                        Log.Warning(Translations.Errors.WarningInlinedMethods);
                        HotReloadPrefs.LoggedInlinedMethodsDialogue = true;
                    }
                    HotReloadTimelineHelper.CreateInlinedMethodsEntry(entryType: EntryType.Foldout, patchedMethodsDisplayNames: newInlinedMethods.Select(mb => $"{mb.DeclaringType?.Name}::{mb.Name}").ToArray());
                    CodePatcher.I.anyFailures = true;
                    if (HotReloadPrefs.AutoRecompileUnsupportedChangesImmediately || UnityEditorInternal.InternalEditorUtility.isApplicationActive) {
                        TryRecompileUnsupportedChanges();
                    }
                    HotReloadSuggestionsHelper.SetSuggestionActive(HotReloadSuggestionKind.SwitchToDebugModeForInlinedMethods);
                    foreach (var newInlinedMethod in newInlinedMethods) {
                        inlinedMethodsFound.Add(newInlinedMethod);
                    }
                    SendEditorTelemetryIfEnabled(new Stat(StatSource.Client, StatLevel.Debug, StatFeature.Patching, StatEventType.Inlined));
                }
            } catch (Exception e) {
                Log.Warning(Translations.Errors.WarningInlineMethodChecker, e.Message);
            }
        }
#endif

        static void CheckEditorSettings() {
            if (quitting) {
                return;
            }
            CheckAutoRefresh();
            CheckScriptCompilation();
            CheckPlaymodeTint();
            CheckAssetDatabaseRefresh();
        }

        static void CheckAutoRefresh() {
            if (MultiplayerPlaymodeHelper.IsClone) {
                return;
            }
            var disabledDuringDebugger = Debugger.IsAttached && !CodePatcher.I.debuggerCompatibilityEnabled;
            if (HotReloadPrefs.AllowDisableUnityAutoRefresh && ServerHealthCheck.I.IsServerHealthy && !disabledDuringDebugger) {
                AutoRefreshSettingChecker.Apply();
                AutoRefreshSettingChecker.Check();
            } else {
                AutoRefreshSettingChecker.Reset();
            }
        }
        
        static void CheckScriptCompilation() {
            if (MultiplayerPlaymodeHelper.IsClone) {
                return;
            }
            var disabledDuringDebugger = Debugger.IsAttached && !CodePatcher.I.debuggerCompatibilityEnabled;
            if (HotReloadPrefs.AllowDisableUnityAutoRefresh && ServerHealthCheck.I.IsServerHealthy && !disabledDuringDebugger) {
                ScriptCompilationSettingChecker.Apply();
                ScriptCompilationSettingChecker.Check();
            } else {
                ScriptCompilationSettingChecker.Reset();
            }
        }
        
        static string[] assetExtensionBlacklist = new[] {
            ".cs",
            // we can add setting to allow scenes to get hot reloaded for users who collaborate (their scenes change externally)
            ".unity",
            // safer to ignore meta files completely until there's a use-case
            ".meta",
            // debug files
            ".mdb",
            ".pdb",
            //use assetBlacklist instead
            // ".compute",
            // ".shader", 
        };

        public static string[] compileFiles = new[] {
            ".asmdef",
            ".asmref",
            ".rsp",
            ".additionalfile",
        };

        public static string[] plugins = new[] {
            // native plugins
            ".dll",
            ".bundle",
            ".dylib",
            ".so",
            // plugin scripts
            ".cpp",
            // don't need to recompile these
            // ".h",
            ".aar",
            ".jar",
            ".a",
            ".java"
        };
        
        static void HandleAssetChange(string assetPath, bool tooManyChanges) {
            if (MultiplayerPlaymodeHelper.IsClone) {
                return;
            }
            // ignore directories
            if (Directory.Exists(assetPath)) {
                return;
            }
            // ignore temp compile files
            if (assetPath.Contains("UnityDirMonSyncFile") 
                || assetPath.EndsWith("~", StringComparison.Ordinal) 
                || assetPath.Contains("StreamingAssets")  
            ) {
                return;
            }
            foreach (var compileFile in compileFiles) {
                if (assetPath.EndsWith(compileFile, StringComparison.Ordinal)) {
                    HotReloadTimelineHelper.CreateErrorEventEntry(string.Format(Translations.Utility.AssemblyFileEditError, assetPath), entryType: EntryType.Foldout);
                    CodePatcher.I.anyFailures = true;
                    // we need to ignore compile errors because changes to compile files can inherently introduce them and without recompiling there is no way to resolve them
                    CodePatcher.I.ignoreCompileErrorOnRecompileUnsupported = true;
                    _applyingFailed = true;
                    if (HotReloadPrefs.AutoRecompileUnsupportedChangesImmediately || UnityEditorInternal.InternalEditorUtility.isApplicationActive) {
                        TryRecompileUnsupportedChanges();
                    }
                    return;
                }
            }
            // Add plugin changes to unsupported changes list
            foreach (var plugin in plugins) {
                if (assetPath.EndsWith(plugin, StringComparison.Ordinal)) {
                    HotReloadTimelineHelper.CreateErrorEventEntry(string.Format(Translations.Utility.NativePluginEditError, assetPath), entryType: EntryType.Foldout);
                    CodePatcher.I.anyFailures = true;
                    _applyingFailed = true;
                    if (HotReloadPrefs.AutoRecompileUnsupportedChangesImmediately || UnityEditorInternal.InternalEditorUtility.isApplicationActive) {
                        TryRecompileUnsupportedChanges();
                    }
                    return;
                }
            }

            // ignore file extensions that trigger domain reload
            if (!HotReloadPrefs.IncludeShaderChanges) { 
                if (assetPath.EndsWith(".shader", StringComparison.Ordinal) || assetPath.EndsWith(".compute", StringComparison.Ordinal)) {
                    return;
                }
            }
            foreach (var blacklisted in assetExtensionBlacklist) {
                if (assetPath.EndsWith(blacklisted, StringComparison.Ordinal)) {
                    return;
                }
            }
            if (config?.assetBlacklist != null) {
                foreach (var blacklisted in config.assetBlacklist) {
                    if (assetPath.EndsWith(blacklisted, StringComparison.Ordinal)) {
                        return;
                    }
                }
            }
            // On a large burst we skip the bulk asset re-import (it can storm/loop) — but only after the
            // compile-file and plugin checks above, so those still trigger a recompile.
            if (tooManyChanges) {
                return;
            }
            var path = ToPath(assetPath);
            if (path == null) {
                return;
            }
            try {
                if (!File.Exists(assetPath)) {
                    AssetDatabase.DeleteAsset(path);
                } else {
                    AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
                }
            } catch (Exception e){
                Log.Warning(Translations.Errors.WarningRefreshingAssetFailed, assetPath, e);
            }
        }

        static string ToPath(string assetPath) {
            var relativePath = GetRelativePath(assetPath, Path.GetFullPath("Assets"));
            var relativePathPackages = GetRelativePath(assetPath, Path.GetFullPath("Packages"));
            // ignore files outside assets and packages folders
            if (relativePath.StartsWith("..", StringComparison.Ordinal)) {
                relativePath = null;
            }
            if (relativePathPackages.StartsWith("..", StringComparison.Ordinal)) {
                relativePathPackages = null;
                #if UNITY_2021_1_OR_NEWER
                // Might be inside a package "file:"
                try {
                    foreach (var package in UnityEditor.PackageManager.PackageInfo.GetAllRegisteredPackages()) {
                        if (assetPath.Replace("\\", "/").StartsWith(package.resolvedPath.Replace("\\", "/"), StringComparison.Ordinal)) {
                            relativePathPackages = $"Packages/{package.name}/{assetPath.Substring(package.resolvedPath.Length)}";
                            break;
                        }
                    }
                } catch {
                    // ignore
                }
                #endif
            }
            return relativePath ?? relativePathPackages;
        }

        public static string GetRelativePath(string filespec, string folder) {
            Uri pathUri = new Uri(filespec);
            Uri folderUri = new Uri(folder);
            return Uri.UnescapeDataString(folderUri.MakeRelativeUri(pathUri).ToString().Replace('/', Path.DirectorySeparatorChar));
        }
        
        static void CheckPlaymodeTint() {
            if (MultiplayerPlaymodeHelper.IsClone) {
                return;
            }
            if (config.changePlaymodeTint && ServerHealthCheck.I.IsServerHealthy && Application.isPlaying) {
                PlaymodeTintSettingChecker.Apply();
                PlaymodeTintSettingChecker.Check();
            } else {
                PlaymodeTintSettingChecker.Reset();
            }
        }
        
        static void CheckAssetDatabaseRefresh() {
            if (config.disableCompilingFromEditorScripts && ServerHealthCheck.I.IsServerHealthy) {
                CompileMethodDetourer.Apply();
            } else {
                CompileMethodDetourer.Reset();
            }
        }

        static void HandleResponseReceived(MethodPatchResponse response) {
            RegisterPatchesResult patchResult = null;
            if (response.patches?.Length > 0 
                || response.alteredFields.Length > 0 
                || response.removedFieldInitializers.Length > 0 
                || response.addedFieldInitializerInitializers.Length > 0
                || response.addedFieldInitializerFields.Length > 0
            ) {
                LogBurstHint(response);
                // don't save patches in virtual players since we will use main editor instance for that
                var persist = !MultiplayerPlaymodeHelper.IsClone;
                patchResult = CodePatcher.I.RegisterPatches(response, persist: persist);
            }
            
            CodePatcher.I.RegisterFailures(response, patchResult);
            
            if (patchResult?.inspectorModified == true) {
                // repaint all views calls all gui callbacks but doesn't rebuild the visual tree
                // which is needed to hide removed fields
                UnityFieldDrawerPatchHelper.repaintVisualTree = true;
                InternalEditorUtility.RepaintAllViews();
            }

            // Keep in sync with HasPartiallySupportedChangesFiltered
            var partiallySupportedChangesFiltered = new List<PartiallySupportedChange>(response.partiallySupportedChanges ?? Array.Empty<PartiallySupportedChange>());
            partiallySupportedChangesFiltered.RemoveAll(x => !HotReloadTimelineHelper.GetPartiallySupportedChangePref(x));
            if (!HotReloadPrefs.DisplayNewMonobehaviourMethodsAsPartiallySupported && partiallySupportedChangesFiltered.Remove(PartiallySupportedChange.AddMonobehaviourMethod)) {
                if (HotReloadSuggestionsHelper.CanShowServerSuggestion(HotReloadSuggestionKind.AddMonobehaviourMethod)) {
                    HotReloadSuggestionsHelper.SetServerSuggestionShown(HotReloadSuggestionKind.AddMonobehaviourMethod);
                }
            }
            var failuresDeduplicated = new HashSet<string>(response.failures ?? Array.Empty<string>());

            foreach (var hotReloadSuggestionKind in response.suggestions) {
                if (HotReloadSuggestionsHelper.CanShowServerSuggestion(hotReloadSuggestionKind)) {
                    HotReloadSuggestionsHelper.SetServerSuggestionShown(hotReloadSuggestionKind);
                }
            }

            var allMethods = patchResult?.patchedSMethods.Select(m => GetExtendedMethodName(m));
            if (allMethods == null) {
                allMethods = response.removedMethod?.Select(m => GetExtendedMethodName(m)).Distinct(StringComparer.OrdinalIgnoreCase) ?? Array.Empty<string>();
            } else {
                allMethods = allMethods.Concat(response.removedMethod?.Select(m => GetExtendedMethodName(m)) ?? Array.Empty<string>()).Distinct(StringComparer.OrdinalIgnoreCase);
            }
            
            var allFields = (patchResult?.addedFields.Select(f => GetExtendedFieldName(f)) ?? Array.Empty<string>())
                            .Concat(response.alteredFields?.Select(f => GetExtendedFieldName(f)).Distinct(StringComparer.OrdinalIgnoreCase) ?? Array.Empty<string>())
                            .Concat(response.patches?.SelectMany(p => p?.propertyAttributesFieldUpdated ?? Array.Empty<SField>()).Select(f => GetExtendedFieldName(f)).Distinct(StringComparer.OrdinalIgnoreCase) ?? Array.Empty<string>())
                            .Distinct(StringComparer.OrdinalIgnoreCase);
            
            var patchedMembersDisplayNames = allMethods.Concat(allFields).ToArray();
            
            _compileError = response.failures?.Any(failure => failure.Contains("error CS")) ?? false;
            _applyingFailed = response.failures?.Length > 0 || patchResult?.patchFailures.Count > 0 || patchResult?.patchExceptions.Count > 0;
            _appliedPartially = !_applyingFailed && partiallySupportedChangesFiltered.Count > 0;
            _appliedUndetected = patchedMembersDisplayNames.Length == 0;

            if (!_compileError) {
                lastCompileErrorLog = null;
            }

            var autoRecompiled = false;
            if (_compileError) {
                HotReloadTimelineHelper.EventsTimeline.RemoveAll(e => e.alertType == AlertType.CompileError);
                foreach (var failure in failuresDeduplicated) {
                    if (failure.Contains("error CS")) {
                        HotReloadTimelineHelper.CreateErrorEventEntry(failure);
                    }
                }
                if (lastCompileErrorLog != null) {
                    if (!disableServerLogs) {
                        Log.Error(lastCompileErrorLog);
                    }
                    lastCompileErrorLog = null;
                }
                SendEditorTelemetryIfEnabled(new Stat(StatSource.Client, StatLevel.Debug, StatFeature.Reload, StatEventType.CompileError), new EditorExtraData {
                    { StatKey.PatchId, response.id },
                });
            } else if (_applyingFailed) {
                if (partiallySupportedChangesFiltered.Count > 0) {
                    foreach (var responsePartiallySupportedChange in partiallySupportedChangesFiltered) {
                        HotReloadTimelineHelper.CreatePartiallyAppliedEventEntry(responsePartiallySupportedChange, entryType: EntryType.Child);
                    }
                }
                foreach (var failure in failuresDeduplicated) {
                    HotReloadTimelineHelper.CreateErrorEventEntry(failure, entryType: EntryType.Child);
                }
                if (patchResult?.patchFailures.Count > 0) {
                    foreach (var failure in patchResult.patchFailures) {
                        SMethod method = failure.Item1;
                        string error = failure.Item2;
                        HotReloadTimelineHelper.CreatePatchFailureEventEntry(error, methodName: GetMethodName(method), methodSimpleName: method.simpleName, entryType: EntryType.Child);
                    }
                }
                if (patchResult?.patchExceptions.Count > 0) {
                    foreach (var error in patchResult.patchExceptions) {
                        HotReloadTimelineHelper.CreateErrorEventEntry(error, entryType: EntryType.Child);
                    }
                }
                HotReloadTimelineHelper.CreateReloadFinishedWithWarningsEventEntry(patchedMembersDisplayNames: patchedMembersDisplayNames);
                HotReloadSuggestionsHelper.SetSuggestionsShown(HotReloadSuggestionKind.UnsupportedChanges);
                if (HotReloadPrefs.AutoRecompileUnsupportedChangesImmediately || UnityEditorInternal.InternalEditorUtility.isApplicationActive) {
                    autoRecompiled = TryRecompileUnsupportedChanges();
                }
                SendEditorTelemetryIfEnabled(new Stat(StatSource.Client, StatLevel.Debug, StatFeature.Reload, StatEventType.Failure), new EditorExtraData {
                    { StatKey.PatchId, response.id },
                });
            } else if (_appliedPartially) {
                foreach (var responsePartiallySupportedChange in partiallySupportedChangesFiltered) {
                    HotReloadTimelineHelper.CreatePartiallyAppliedEventEntry(responsePartiallySupportedChange, entryType: EntryType.Child, detailed: false);
                }
                HotReloadTimelineHelper.CreateReloadPartiallyAppliedEventEntry(patchedMethodsDisplayNames: patchedMembersDisplayNames);
                
                if (HotReloadPrefs.AutoRecompileUnsupportedChangesImmediately || UnityEditorInternal.InternalEditorUtility.isApplicationActive) {
                    autoRecompiled = TryRecompileUnsupportedChanges();
                }
                SendEditorTelemetryIfEnabled(new Stat(StatSource.Client, StatLevel.Debug, StatFeature.Reload, StatEventType.Partial), new EditorExtraData {
                    { StatKey.PatchId, response.id },
                });
            } else if (_appliedUndetected)  {
                HotReloadTimelineHelper.CreateReloadUndetectedChangeEventEntry();
                SendEditorTelemetryIfEnabled(new Stat(StatSource.Client, StatLevel.Debug, StatFeature.Reload, StatEventType.Undetected), new EditorExtraData {
                    { StatKey.PatchId, response.id },
                });
            } else {
                HotReloadTimelineHelper.CreateReloadFinishedEventEntry(patchedMethodsDisplayNames: patchedMembersDisplayNames);
                SendEditorTelemetryIfEnabled(new Stat(StatSource.Client, StatLevel.Debug, StatFeature.Reload, StatEventType.Finished), new EditorExtraData {
                    { StatKey.PatchId, response.id },
                });
            }
            
            if (!autoRecompiled && patchResult?.inspectorFieldAdded == true && HotReloadPrefs.AutoRecompileInspectorFieldsEdit && !Application.isPlaying) {
                HotReloadSuggestionsHelper.SetSuggestionsShown(HotReloadSuggestionKind.UnsupportedChanges);
                RecompileUnsupportedChanges();
                autoRecompiled = true;
                HotReloadTimelineHelper.CreateErrorEventEntry(Translations.Utility.InspectorFieldChangeError, entryType: EntryType.Child);
                HotReloadTimelineHelper.CreateReloadFinishedWithWarningsEventEntry();
                Log.Info(Translations.Errors.InfoInspectorFieldRecompile);
            }

            // When patching different assembly, compile error will get removed, even though it's still there
            // It's a shortcut we take for simplicity
            if (!_compileError) {
                HotReloadTimelineHelper.EventsTimeline.RemoveAll(x => x.alertType == AlertType.CompileError);
            }

            foreach (string responseFailure in response.failures) {
                if (responseFailure.Contains("error CS") && !disableServerLogs) {
                    Log.Error(responseFailure);
                } else if (autoRecompiled) {
                    Log.Info(responseFailure);
                } else {
                    Log.Warning(responseFailure);
                }
            }
            if (patchResult?.patchFailures.Count > 0) {
                foreach (var patchResultPatchFailure in patchResult.patchFailures) {
                    if (autoRecompiled) {
                        Log.Info(patchResultPatchFailure.Item2);
                    } else {
                        Log.Warning(patchResultPatchFailure.Item2);
                    }
                }
            }
            if (patchResult?.patchExceptions.Count > 0) {
                foreach (var patchResultPatchException in patchResult.patchExceptions) {
                    if (autoRecompiled) {
                        Log.Info(patchResultPatchException);
                    } else {
                        Log.Warning(patchResultPatchException);
                    }
                }
            }
            
            // attempt to recompile if previous Unity compilation had compilation errors
            // because new changes might've fixed those errors
            if (compileChecker.hasCompileErrors) {
                HotReloadRunTab.Recompile();
            }

            if (HotReloadWindow.Current) {
                HotReloadWindow.Current.Repaint();
            }
            HotReloadState.LastPatchId = response.id;
            OnPatchHandled?.Invoke((response, patchResult));
        }

        // Keep in sync with HandleResponseReceived
        static bool HasPartiallySupportedChangesFiltered(PartiallySupportedChange[] partiallySupportedChanges) {
            foreach (var change in partiallySupportedChanges) {
                if (HotReloadTimelineHelper.GetPartiallySupportedChangePref(change) &&
                    (change != PartiallySupportedChange.AddMonobehaviourMethod || HotReloadPrefs.DisplayNewMonobehaviourMethodsAsPartiallySupported)
                ) {
                    return true;
                }
            }
            return false;
        }

        static string GetExtendedMethodName(SMethod method) {
            var colonIndex = method.displayName.IndexOf("::", StringComparison.Ordinal);
            if (colonIndex > 0) {
                var beforeColon = method.displayName.Substring(0, colonIndex);
                var spaceIndex = beforeColon.LastIndexOf(".", StringComparison.Ordinal);
                if (spaceIndex > 0) {
                    var className = beforeColon.Substring(spaceIndex + 1);
                    return className + "::" + method.simpleName;
                }
            }
            return method.simpleName;
        }
        
        static string GetExtendedFieldName(SField field) {
            string typeName = field.declaringType.typeName;
            var simpleTypeIndex = typeName.LastIndexOf(".", StringComparison.Ordinal);
            if (simpleTypeIndex > 0) {
                typeName = typeName.Substring(simpleTypeIndex + 1);
            }
            return $"{typeName}::{field.fieldName}";
        }

        static string GetMethodName(SMethod method) {
            var spaceIndex = method.displayName.IndexOf(" ", StringComparison.Ordinal);
            if (spaceIndex > 0) {
                return method.displayName.Substring(spaceIndex);
            }
            return method.displayName;
        }

        
        [Conditional("UNITY_2022_2_OR_NEWER")]
        static void LogBurstHint(MethodPatchResponse response) {
            if(HotReloadPrefs.LoggedBurstHint) {
                return;
            }
            foreach (var patch in response.patches) {
                if(patch.unityJobs.Length > 0) {
                    Debug.LogWarning(string.Format(Translations.Errors.WarningUnityJobHotReloaded, Constants.TroubleshootingURL));
                    HotReloadPrefs.LoggedBurstHint = true;
                    break;
                }
            }
        }

        private static DateTime? startWaitingForCompile;
        static void OnCompilationFinished() {
            ServerHealthCheck.instance.CheckHealth();
            var compileSessionId = compileChecker.CompilationSessionId;
            if(ServerHealthCheck.I.IsServerHealthy) {
                startWaitingForCompile = DateTime.UtcNow;
                firstPatchAttempted = false;
                RequestCompile(compileSessionId).Forget();
            } else {
                // server not running, no request to send so clear this here
                compileChecker.OnCompilationRequestFinished();
            }
            ClearPersistence();
        }
        
        static void ClearPersistence() {
            if (MultiplayerPlaymodeHelper.IsClone) {
                return;
            }
            CodePatcher.I.ClearPatchesThreaded();
        }

        static bool requestingCompile;
        static async Task RequestCompile(string compileSessionId) {
            if (MultiplayerPlaymodeHelper.IsClone) {
                return;
            }
            requestingCompile = true;
            try {
                await RequestHelper.RequestClearPatches(compileSessionId);
                await ProjectGeneration.ProjectGeneration.GenerateSlnAndCsprojFiles(Application.dataPath);
                await RequestHelper.RequestCompile(compileSessionId, scenePath => {
                    var path = ToPath(scenePath);
                    if (File.Exists(scenePath) && path != null) {
                        AssetDatabase.ImportAsset(path, ImportAssetOptions.Default);
                    }
                });
            } finally {
                ThreadUtility.RunOnMainThread(compileChecker.OnCompilationRequestFinished);
                requestingCompile = false;
            }
        }
        
        private static bool stopping;
        private static bool starting;
        private static DateTime? startupCompletedAt;
        private static Tuple<float, string> startupProgress;
        
        internal static bool Started => ServerHealthCheck.I.IsServerHealthy && DownloadProgress == 1 && StartupProgress?.Item1 == 1;
        internal static bool Starting => (StartedServerRecently() || ServerHealthCheck.I.IsServerHealthy) && !Started && starting && patchStatus != PatchStatus.CompileError;
        internal static bool Stopping => stopping && Running;
        internal static bool Compiling => DateTime.UtcNow - startWaitingForCompile < TimeSpan.FromSeconds(5) || patchStatus == PatchStatus.Compiling || HotReloadRunTab.recompiling;
        internal static Tuple<float, string> StartupProgress => startupProgress;
        
        
        /// <summary>
        /// We have a button to stop the Hot Reload server.<br/>
        /// Store task to ensure only one stop attempt at a time. 
        /// </summary>
        private static DateTime? serverStartedAt;
        private static DateTime? serverStoppedAt;
        private static DateTime? serverRestartedAt;
        private static bool StartedServerRecently() {
            return DateTime.UtcNow - serverStartedAt < ServerHealthCheck.HeartBeatTimeout;
        }
        
        internal static bool StoppedServerRecently() {
            return DateTime.UtcNow - serverStoppedAt < ServerHealthCheck.HeartBeatTimeout || (!StartedServerRecently() && (startupProgress?.Item1 ?? 0) == 0);
        }
        
        internal static bool RestartedServerRecently() {
            return DateTime.UtcNow - serverRestartedAt < ServerHealthCheck.HeartBeatTimeout;
        }

        private static bool requestingStart;
        private static async Task StartCodePatcher(LoginData loginData = null) {
            if (requestingStart || StartedServerRecently())  {
                return;
            }
            stopping = false;
            starting = true;
            var exposeToNetwork = HotReloadPrefs.ExposeServerToLocalNetwork;
            var allAssetChanges = HotReloadPrefs.AllAssetChanges;
            var disableConsoleWindow = HotReloadPrefs.DisableConsoleWindow;
            var isReleaseMode = RequestHelper.IsReleaseMode();
            var detailedErrorReporting = !HotReloadPrefs.DisableDetailedErrorReporting;
            var disableTelemetry = HotReloadPrefs.DisableTelemetry;
#if UNITY_EDITOR_WIN
            var useWatchman = HotReloadPrefs.UseWatchman;
#endif
            CodePatcher.I.ClearPatchedMethods();
            RecordActiveDaysForRateApp();
            try {
                requestingStart = true;
                startupProgress = Tuple.Create(0f, Translations.UI.StartingHotReloadMessage);
                serverStartedAt = DateTime.UtcNow;
                await HotReloadCli.StartAsync(
                    exposeToNetwork, 
                    allAssetChanges, 
                    disableConsoleWindow, 
                    isReleaseMode, 
                    detailedErrorReporting,
                    disableTelemetry,
#if UNITY_EDITOR_WIN
                    useWatchman,
#endif
                    loginData
                ).ConfigureAwait(false);
            }
            catch (Exception ex) {
#if UNITY_EDITOR_WIN
                if (ex is Win32Exception && ex.Message.Contains("An Application Control policy has blocked this file")) {
                    Log.Error("Hot Reload is not compatible with Windows Smart App Control yet. Please disable it to use Hot Reload. We are working on obtaining certificates for compatibility.");
                    return;
                }
#endif
                ThreadUtility.LogException(ex);
            }
            finally {
                requestingStart = false;
            }
        }
        
        private static bool requestingStop;
        internal static async Task StopCodePatcher(bool recompileOnDone = false) {
            stopping = true;
            starting = false;
            if (requestingStop) {
                if (recompileOnDone) {
                    await ThreadUtility.SwitchToMainThread();
                    HotReloadRunTab.Recompile();
                }
                return;
            }
            CodePatcher.I.ClearPatchedMethods();
            HotReloadSuggestionsHelper.SetSuggestionInactive(HotReloadSuggestionKind.EditorsWithoutHRRunning);
            try {
                requestingStop = true;
                await HotReloadCli.StopAsync().ConfigureAwait(false);
                serverStoppedAt = DateTime.UtcNow;
                await ThreadUtility.SwitchToMainThread();
                if (recompileOnDone) {
                    HotReloadRunTab.Recompile();
                }
                startupProgress = null;
            }
            catch (Exception ex) {
                ThreadUtility.LogException(ex);
            }
            finally {
                requestingStop = false;
            }
        }
        
        private static bool requestingRestart;
        internal static async Task RestartCodePatcher() {
            if (requestingRestart) {
                return;
            }
            try {
                requestingRestart = true;
                await StopCodePatcher();
                await DownloadAndRun();
                serverRestartedAt = DateTime.UtcNow;
            }
            finally {
                requestingRestart = false;
            }
        }
        
        
        private static bool requestingDownloadAndRun;
        private static bool requestingResetAndLogin;
        internal static float DownloadProgress => serverDownloader.Progress;
        internal static bool DownloadRequired => DownloadProgress < 1f;
        internal static bool DownloadStarted => serverDownloader.Started;
        internal static bool RequestingDownloadAndRun => requestingDownloadAndRun;
        internal static bool RequestingResetAndLogin => requestingResetAndLogin;
        internal static CancellationTokenSource downloadCancelToken;
        
        internal static async Task RemoteResetAndLogin(string email, string password) {
            if (requestingResetAndLogin) {
                return;
            }
            try {
                requestingResetAndLogin = true;
                var resp = await RequestHelper.RequestRemoteLicenseReset(email, password, 10);
                if (resp.error != null) {
                    if (resp.error.Contains("License already reset")) {
                        Log.Info("License was reset previously. Please reach out to support to reset license manually");
                    } else {
                        Log.Info("License reset failed. Please reach out to support to reset license manually");
                    }
                    return;
                }
                await EditorCodePatcher.RequestLogin(email, password);
            } finally {
                requestingResetAndLogin = false;
            }
        }
        
        internal static async Task<bool> DownloadAndRun(LoginData loginData = null, bool recompileOnDone = false) {
            if (requestingDownloadAndRun) {
                return false;
            }
            stopping = false;
            requestingDownloadAndRun = true;
            try {
                if (DownloadRequired) {
                    downloadCancelToken = new CancellationTokenSource(); 
                    var ok = await serverDownloader.PromptForDownload(downloadCancelToken.Token);
                    if (!ok) {
                        return false;
                    }
                }
                await StartCodePatcher(loginData);
                await ThreadUtility.SwitchToMainThread();
                if (HotReloadPrefs.DeactivateHotReload) {
                    HotReloadPrefs.DeactivateHotReload = false;
                    HotReloadRunTab.Recompile();
                }
                return true;
            } finally {
                requestingDownloadAndRun = false;
            }
        }

        private static void InitServerInfo() {
            // only needed for clones
            if (!MultiplayerPlaymodeHelper.IsClone) {
                return;
            }
            var serverInfoRaw = File.ReadAllText(PackageConst.ServerInfoFilePath);
            PatchServerInfo serverInfo = null;
            if (!string.IsNullOrEmpty(serverInfoRaw)) {
                serverInfo = JsonConvert.DeserializeObject<PatchServerInfo>(serverInfoRaw);
            }
            if (serverInfo != null) {
                RequestHelper.SetServerInfo(serverInfo);
            }
        }
        
        private const int SERVER_POLL_FREQUENCY_ON_STARTUP_MS = 500;
        private const int SERVER_POLL_FREQUENCY_AFTER_STARTUP_MS = 2000;
        private static int GetPollFrequency() {
            return (startupProgress != null && startupProgress.Item1 < 1) || StartedServerRecently()
                ? SERVER_POLL_FREQUENCY_ON_STARTUP_MS
                : SERVER_POLL_FREQUENCY_AFTER_STARTUP_MS;
        }
        
        internal static bool RequestingLoginInfo { get; set; }
        
        [CanBeNull] internal static LoginStatusResponse Status { get; private set; }
        internal static void HandleStatus(LoginStatusResponse resp) {
            if (resp == null) {
                return;
            }
            Attribution.RegisterLogin(resp);
            
            bool consumptionsChanged = Status?.freeSessionRunning != resp.freeSessionRunning || Status?.freeSessionEndTime != resp.freeSessionEndTime;
            bool expiresAtChanged = Status?.licenseExpiresAt != resp.licenseExpiresAt;
            if (!EditorCodePatcher.LoginNotRequired 
                && !resp.isLicensed
                && resp.consumptionsUnavailableReason == ConsumptionsUnavailableReason.UnrecoverableError
                && Status?.consumptionsUnavailableReason != ConsumptionsUnavailableReason.UnrecoverableError
            ) {
                Log.Error(Translations.Errors.ErrorFreeChargesUnavailable);
            }
            if (!RequestingLoginInfo && resp.requestError == null) {
                Status = resp;
            }
            if (resp.lastLicenseError == null) {
                // If we got success, we should always show an error next time it comes up
                HotReloadPrefs.ErrorHidden = false;
            }

            if (!string.IsNullOrEmpty(resp.hardwareId)) {
                HotReloadPrefs.HardwareId = resp.hardwareId;
            }

            var oldStartupProgress = startupProgress;
            var newStartupProgress = Tuple.Create(
                resp.startupProgress,
                string.IsNullOrEmpty(resp.startupStatus) ? Translations.UI.StartingHotReloadMessage : resp.startupStatus);

            startupProgress = newStartupProgress;
            // ReSharper disable once CompareOfFloatsByEqualityOperator
            if (startupCompletedAt == null && newStartupProgress.Item1 == 1f) {
                startupCompletedAt = DateTime.UtcNow;
            }
            
            if (oldStartupProgress == null
                || Math.Abs(oldStartupProgress.Item1 - newStartupProgress.Item1) > 0
                || oldStartupProgress.Item2 != newStartupProgress.Item2
                || consumptionsChanged
                || expiresAtChanged
            ) {
                // Send project files state now that server can receive requests (only needed for player builds)
                TryPrepareBuildInfo();
            }
        }
        
        internal static  async Task RequestLogin(string email, string password) {
            RequestingLoginInfo = true;
            try {
                int i = 0;
                while (!Running && i < 100) {
                    await Task.Delay(100);
                    i++;
                }

                Status = await RequestHelper.RequestLogin(email, password, 10);

                // set to false so new error is shown
                HotReloadPrefs.ErrorHidden = false;
                if (Status?.isLicensed == true) {
                    HotReloadPrefs.LicenseEmail = email;
                    HotReloadPrefs.LicensePassword = Status.initialPassword ?? password;
                }
            } finally {
                RequestingLoginInfo = false;
            }
        }
        private static bool requestingServerInfo;
        private static long lastServerPoll;
        private static bool running;
        internal static bool Running => ServerHealthCheck.I.IsServerHealthy;
        
        internal static void RequestServerInfo() {
            if (requestingServerInfo || MultiplayerPlaymodeHelper.IsClone) {
                return;
            }
            RequestServerInfoAsync().Forget();
        }
        
        private static async Task RequestServerInfoAsync() {
            requestingServerInfo = true;
            try {
                await RequestServerInfoCore();
            } finally {
                requestingServerInfo = false;
            }
        }

        private static async Task RequestServerInfoCore() {
            var pollFrequency = GetPollFrequency();
            // Delay until we've hit the poll request frequency
            var waitMs = (int)Mathf.Clamp(pollFrequency - ((DateTime.Now.Ticks / (float)TimeSpan.TicksPerMillisecond) - lastServerPoll), 0, pollFrequency);
            await Task.Delay(waitMs);

            if (!ServerHealthCheck.I.IsServerHealthy) {
                return;
            }

            
            var resp = await RequestHelper.GetLoginStatus(30);
            HandleStatus(resp);

            lastServerPoll = DateTime.Now.Ticks / TimeSpan.TicksPerMillisecond;
        }
        
        internal static void SendEditorTelemetryIfEnabled(Stat stat, EditorExtraData extraData = null) {
            if (HotReloadPrefs.DisableTelemetry) {
                return;
            }
            RequestHelper.RequestEditorEventWithRetry(stat, extraData).Forget();
        }
    }
    
    // IMPORTANT: don't change the names of the methods
    internal static class UnityFieldDrawerPatchHelper {
        internal static void PatchCustom(Rect contentRect, UnityEditor.Editor __instance) {
            if (__instance.target) {
                FieldDrawerUtil.DrawFromObject(__instance.target);
            }
        }

        internal static void PatchDefault(UnityEditor.Editor __instance) {
            if (__instance.target) {
                FieldDrawerUtil.DrawFromObject(__instance.target);
            }
        }

        internal static bool repaintVisualTree;
        internal static void PatchFillDefaultInspector(VisualElement container, SerializedObject serializedObject, UnityEditor.Editor editor) {
            HideChildren(container, serializedObject);
            if (editor.target) {
                var child = new IMGUIContainer((() =>
                {
                    FieldDrawerUtil.DrawFromObject(editor.target);
                    if (repaintVisualTree) {
                        HideChildren(container, serializedObject);
                        ResetInvalidatedInspectorFields(container, serializedObject);
                        // Mark dirty to repaint the visual tree
                        container.MarkDirtyRepaint();
                        repaintVisualTree = false;
                    }
                }));
                child.name = "SingularityGroup.HotReload.FieldDrawer";
                container.Add(child);
            }
        }

        static List<VisualElement> childrenToRemove = new List<VisualElement>();
        static void HideChildren(VisualElement container, SerializedObject serializedObject) {
            if (container == null) {
                return;
            } 
            childrenToRemove.Clear();
            foreach (var child in container.Children()) {
                if (!(child is PropertyField propertyField)) {
                    continue;
                }
                try {
                    if (serializedObject != null && serializedObject.targetObject && UnityFieldHelper.IsFieldHidden(serializedObject.targetObject.GetType(), serializedObject.FindProperty(propertyField.bindingPath)?.name ?? "")) {
                        childrenToRemove.Add(child);
                    }
                } catch (NullReferenceException) {
                    // serializedObject.targetObject throws nullref in cases where e.g. exising playmode
                }
            }
            foreach (var child in childrenToRemove) {
                container.Remove(child);
            }
            childrenToRemove.Clear();
        }
        
        static void ResetInvalidatedInspectorFields(VisualElement container, SerializedObject serializedObject) {
            if (container == null || serializedObject == null) {
                return;
            } 
            foreach (var child in container.Children()) {
                if (!(child is PropertyField propertyField)) {
                    continue;
                }
                try {
                    var prop = serializedObject.FindProperty(propertyField.bindingPath);
                    if (prop != null && serializedObject.targetObject && UnityFieldHelper.HasFieldInspectorCacheInvalidation(serializedObject.targetObject.GetType(), prop.name ?? "")) {
                        child.GetType().GetMethod("Reset", BindingFlags.NonPublic | BindingFlags.Instance, null, new[] { typeof(SerializedProperty) }, null)?.Invoke(child, new object[] { prop });
                    }
                } catch (NullReferenceException) {
                    // serializedObject.targetObject throws nullref in cases where e.g. exising playmode
                }
            }
        }
        
        internal static bool GetHandlerPrefix(
            SerializedProperty property,
            ref object __result
        ) {
            if (property == null || property.serializedObject == null || !property.serializedObject.targetObject) {
                // do nothing
                return true;
            }
            if (UnityFieldHelper.TryInvalidateFieldInspectorCache(property.serializedObject.targetObject.GetType(), property.name)) {
                __result = null;
                return false;
            }
            return true;
        }

        internal static bool GetFieldAttributesPrefix(
            FieldInfo field,
            ref List<PropertyAttribute> __result
        ) {
            if (field == null) {
                // do nothing
                return true;
            }
            List<PropertyAttribute> result;
            if (UnityFieldHelper.TryGetInspectorFieldAttributes(field, out result)) {
                __result = result;
                return false;
            }
            return true;
        }

        internal static bool PropertyFieldPrefix(
            Rect position,
            UnityEditor.SerializedProperty property,
            GUIContent label,
            bool includeChildren,
            Rect visibleArea,
            ref bool __result
        ) {
            if (property == null || property.serializedObject == null || !property.serializedObject.targetObject) {
                // do nothing
                return true;
            }
            if (UnityFieldHelper.IsFieldHidden(property.serializedObject.targetObject.GetType(), property.name)) {
                // make sure field doesn't take any space
                __result = false;
                return false; // Skip original method
            }
            return true; // Continue with original method
        }

        internal static bool GetHightPrefix(
            UnityEditor.SerializedProperty property, GUIContent label, bool includeChildren,
            ref float __result
        ) {
            if (property == null || property.serializedObject == null || !property.serializedObject.targetObject) {
                // do nothing
                return true;
            }
            if (UnityFieldHelper.IsFieldHidden(property.serializedObject.targetObject.GetType(), property.name)) {
                // make sure field doesn't take any space
                __result = 0.0f;
                return false; // Skip original method
            }
            return true; // Continue with original method
        }
    }
}
