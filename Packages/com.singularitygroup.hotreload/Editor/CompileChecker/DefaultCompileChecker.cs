using System;
using System.IO;
using System.Threading.Tasks;
using SingularityGroup.HotReload.Editor.Localization;
using UnityEditor;
using UnityEditor.Compilation;
using UnityEngine;

namespace SingularityGroup.HotReload.Editor {
    class DefaultCompileChecker : ICompileChecker {
        // careful when modifying - also used on the server
        static string recompileFilePath = PackageConst.LibraryCachePath + "/recompile.txt";
        public bool hasCompileErrors { get; private set;  }
        static bool compiling;
        bool recompile;
        readonly string compilationSessionId;
        public string CompilationSessionId => compilationSessionId;
        public DefaultCompileChecker() {
            if (MultiplayerPlaymodeHelper.IsClone) {
                return;
            }
            compilationSessionId = HotReloadState.CompileSessionId;
            CompilationPipeline.compilationStarted += OnCompilationStarted;
            CompilationPipeline.assemblyCompilationFinished += DetectCompileErrors;
            CompilationPipeline.compilationFinished += OnCompilationFinished;
            Task.Run(() => {
                try {
                    var compileSessionId = File.ReadAllText(recompileFilePath);
                    if (compileSessionId == compilationSessionId) {
                        ThreadUtility.RunOnMainThread(() => {
                            recompile = true;
                            _onCompilationFinished?.Invoke();
                        });
                    } else {
                        ThreadUtility.RunOnMainThread(OnCompilationRequestFinished);
                    }
                } catch (DirectoryNotFoundException) {
                    //dir doesn't exist -> no recompile required
                    ThreadUtility.RunOnMainThread(OnCompilationRequestFinished);
                } catch (FileNotFoundException) {
                    //file doesn't exist -> no recompile required
                    ThreadUtility.RunOnMainThread(OnCompilationRequestFinished);
                } catch (Exception ex) {
                    Log.Warning(Translations.Errors.WarningCompileCheckerIssue, ex.GetType().Name, ex.Message);
                    ThreadUtility.RunOnMainThread(OnCompilationRequestFinished);
                }
            });
        }
        
        void DetectCompileErrors(string _, CompilerMessage[] messages) {
            for (int i = 0; i < messages.Length; i++) {
                if (messages[i].type == CompilerMessageType.Error) {
                    hasCompileErrors = true;
                    return;
                }
            }
            hasCompileErrors = false;
        }
        
        void OnCompilationStarted(object _) {
            compiling = true;
            var newCompilationSessionId = Guid.NewGuid().ToString();
            HotReloadState.CompileSessionId = newCompilationSessionId;
            var dirName = Path.GetDirectoryName(PackageConst.LibraryCachePath);
            if (dirName != null) {
                Directory.CreateDirectory(dirName);
            }
            File.WriteAllText(recompileFilePath, newCompilationSessionId);
        }

        public void OnCompilationRequestFinished() {
            if (compiling || HotReloadState.CompileSessionId != compilationSessionId) {
                return;
            }
            try {
                File.Delete(recompileFilePath);
            } catch { /*_*/ }
        }

        void OnCompilationFinished(object _) {
            compiling = false;
            //Don't recompile on compile errors
            if (hasCompileErrors) {
                File.Delete(recompileFilePath);
            }
        }

        Action _onCompilationFinished;
        public event Action onCompilationFinished {
            add {
                if(recompile && value != null) {
                    value();
                }
                _onCompilationFinished += value;
            }
            remove {
                _onCompilationFinished -= value;
            }
        }
    }
}