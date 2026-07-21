using System;
using System.IO;

namespace SingularityGroup.HotReload {
	// Largely copied from CommandLineParameters.ReadIsClone of UnityEditor.MultiplayerModule.dll
    internal static class MultiplayerPlaymodeHelper {
#if UNITY_EDITOR && UNITY_2023_1_OR_NEWER
        public static bool? isClone;
        public static bool IsClone => isClone == null ? (isClone = HasCommandLineArgument(Environment.GetCommandLineArgs(), "--virtual-project-clone")).Value : isClone.Value;
#else
        public static bool IsClone => false;
#endif

        public static bool HasCommandLineArgument(string[] commandLineArgs, string argumentName) {
	        foreach (string commandLineArg in commandLineArgs) {
		        if (commandLineArg == argumentName) {
			        return true;
		        }
	        }
	        return false;
        }

        public static string PathToMainProject(string path) {
	        if (IsClone) {
		        // Library/VP/<clone-id> is the base path
		        return Path.Combine("..", "..", "..", path);
	        }
	        return path;
        }
    }
}
