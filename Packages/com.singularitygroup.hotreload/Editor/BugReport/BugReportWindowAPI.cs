using System;
using System.Threading.Tasks;
using UnityEngine;

namespace SingularityGroup.HotReload.Editor {
	internal static class ReportWindowAPI {
		public static void OpenBugReport(string title = null, string description  = null) {
			HotReloadBugReportWindow.Open(
				ReportMode.BugReport,
				discordUrl: Constants.DiscordInviteUrl,
				contactUrl: Constants.ContactURL,
				email: HotReloadPrefs.LicenseEmail,
				details: description,
				title: title
			);
		}
		
		public static void OpenFeedback(
			Func<Report, Task<string>> submitHandler = null,
			string discordUrl = null,
			string contactUrl = null,
			string title = null,
			string email = null,
			string feedback = null
		) {
			HotReloadBugReportWindow.Open(
				ReportMode.Feedback,
				discordUrl: Constants.DiscordInviteUrl,
				contactUrl: Constants.ContactURL,
				email: HotReloadPrefs.LicenseEmail
			);
		}
	}
}
