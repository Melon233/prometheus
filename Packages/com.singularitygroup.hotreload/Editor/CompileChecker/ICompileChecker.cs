using System;

namespace SingularityGroup.HotReload.Editor {
    interface ICompileChecker {
        event Action onCompilationFinished;
        bool hasCompileErrors { get; }
        void OnCompilationRequestFinished();
        string CompilationSessionId { get; }
    }
    
    static class CompileChecker {
        internal static ICompileChecker Create() {
            return new DefaultCompileChecker();
        }
    }
}