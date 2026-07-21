using System.IO;
using SingularityGroup.HotReload.DTO;

namespace SingularityGroup.HotReload {
    internal static class PackageConst {
        //CI changes this property to 'true' for asset store builds.
        //Don't touch unless you know what you are doing
        public static bool IsAssetStoreBuild => true;

        
        public const string Version = "1.13.22";
        // Never higher than Version
        // Used for the download
        public const string ServerVersion = "1.13.22";
        public const string PackageName = "com.singularitygroup.hotreload";
        // IMPORTANT: if this is changed also change set-chinese.clj & set-chinese.sh
        public const string DefaultLocale = Locale.English;
        // avoids unreachable code warnings from using const
        public static string DefaultLocaleField = DefaultLocale;
        public static readonly string LibraryCachePath = MultiplayerPlaymodeHelper.PathToMainProject("Library/" + PackageName);
        public const string ConfigFileName = "hot-reload-config.json";
        public static readonly string ConfigFilePath = Path.Combine(MultiplayerPlaymodeHelper.PathToMainProject(ConfigFileName));
        public const string ServerInfoFileName = "serverinfo.json";
        public static readonly string ServerInfoFilePath = Path.Combine(LibraryCachePath, ServerInfoFileName);
    }
}
