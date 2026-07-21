using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEditor;
using UnityEngine;
using UnityEditor.UIElements;
using UnityEngine.UIElements;
using SingularityGroup.HotReload.Editor.Localization;
using Application = UnityEngine.Application;
#if UNITY_2021_3_OR_NEWER
using System.IO;
using System.IO.Compression;
using SingularityGroup.HotReload.Editor.Cli;
using SingularityGroup.HotReload.EditorDependencies;
using CompressionLevel = System.IO.Compression.CompressionLevel;
#endif

namespace SingularityGroup.HotReload.Editor {

	internal enum ReportMode {
		BugReport,
		Feedback
	}

	internal class HotReloadBugReportWindow : EditorWindow {
		static List<string> SeverityChoices => new List<string> {
			Translations.BugReport.SeverityNormal,
			Translations.BugReport.SeverityLow,
			Translations.BugReport.SeverityHigh,
		};

		static List<string> FrequencyChoices => new List<string> {
			Translations.BugReport.FrequencyFirstTime,
			Translations.BugReport.FrequencySometimes,
			Translations.BugReport.FrequencyAlways,
		};

		[SerializeField] ReportMode _mode = ReportMode.BugReport;

		[SerializeField] VisualTreeAsset visualTree;
		[SerializeField] StyleSheet styleSheet;
		
		string _discordUrl;
		string _contactUrl;

		TextField _titleField;
		PopupField<string> _severityPopup;
		PopupField<string> _frequencyPopup;
		TextField _emailField;
		IMGUIContainer _detailsContainer;
		[SerializeField] string _detailsText = "";
		[SerializeField] string _emailText = "";
		[SerializeField] string _titleText = "";
		Label _detailsLabel;
		Label _validationLabel;
		Button _submitButton;
		VisualElement _severitySection;
		VisualElement _frequencySection;
		ScrollView _scrollView;
		VisualElement _detailsSlot;

		VisualElement _successScreen;
		VisualElement _failureScreen;
		Label _failureErrorLabel;
		Button _tabBugReport;
		Button _tabFeedback;

		bool _isOnResultScreen;

		static Vector2 GetMinSize(ReportMode mode) {
			return mode == ReportMode.Feedback
				? new Vector2(420, 500)
				: new Vector2(420, 620);
		}

		#region Public API
		/// <summary>
		/// Opens the window with the given mode and configuration.
		/// Prefer using <see cref="ReportWindowAPI"/> for a cleaner call site.
		/// </summary>
		public static HotReloadBugReportWindow Open(
			ReportMode mode,
			string discordUrl = null,
			string contactUrl = null,
			string title = null,
			string email = null,
			string details = null
		) {
			var wnd = GetWindow<HotReloadBugReportWindow>(utility: true);
			wnd._mode = mode;
			wnd._discordUrl = discordUrl;
			wnd._contactUrl = contactUrl;
			wnd.titleContent = new GUIContent(
				mode == ReportMode.Feedback
					? Translations.BugReport.WindowTitleFeedback
					: Translations.BugReport.WindowTitleBugReport);
			wnd.minSize = wnd.maxSize = GetMinSize(mode);

			wnd._titleText = title;
			wnd._emailText = email;
			wnd._detailsText = details;

			wnd.RebuildUI();
			return wnd;
		}

		/// <summary>
		/// Prefills form fields on an already-open window. Null values are ignored.
		/// </summary>
		public void Prefill() {
			if (_titleText != null && _titleField != null) {
				_titleField.SetValueWithoutNotify(_titleText);
			}
			if (_emailText != null && _emailField != null) {
				_emailField.SetValueWithoutNotify(_emailText);
			}
		}
		#endregion

		void OnEnable() {
			RebuildUI();
		}

		bool HasUnsavedFormContent() {
			if (_isOnResultScreen) {
				return false;
			}
			if (!string.IsNullOrWhiteSpace(_titleField.value)) {
				return true;
			}
			if (!string.IsNullOrWhiteSpace(_detailsText) && _detailsText != Translations.BugReport.PlaceholderDetails) {
				return true;
			}

			return false;
		}

		void OnDestroy() {
			if (_detailsContainer != null && _detailsSlot != null) {
				_detailsSlot.Remove(_detailsContainer);
				_detailsContainer = null;
			}
			if (!HasUnsavedFormContent()) {
				return;
			}

			bool discard = EditorUtility.DisplayDialog(
				Translations.BugReport.DiscardDialogTitle,
				Translations.BugReport.DiscardDialogMessage,
				Translations.BugReport.DiscardDialogConfirm,
				Translations.BugReport.DiscardDialogCancel);

			if (!discard) {
				_emailText = _emailField.value;
				_titleText = _titleField.value;
				EditorApplication.delayCall += () => {
					Open(_mode, _discordUrl, _contactUrl);
				};
			} else {
				_detailsText = null;
				_emailText = null;
				_titleText = null;
			}
		}

		void RebuildUI() {
			DetachCallbacks();
			if (visualTree == null) {
				Log.Warning($"Could not open bug report. Please reach out to our support: {_contactUrl}");
				return;
			}

			rootVisualElement.Clear();
			visualTree.CloneTree(rootVisualElement);
			if (styleSheet != null) {
				rootVisualElement.styleSheets.Add(styleSheet);
			}

			QueryElements();
			AttachIMGUIContainers();
			ApplyTranslations();
			CreatePopupFields();
			ApplyMode();
			SetupSubmit();
			SetupResultScreenButtons();
			FixSingleLineFieldAlignment(_titleField);
			FixSingleLineFieldAlignment(_emailField);
			Prefill();
			AttachCallbacks();

			ShowFormView();
		}

		void AttachIMGUIContainers() {
			_detailsSlot = rootVisualElement.Q<VisualElement>("details-field-slot");
			// Build the IMGUI textarea and inject it
			_detailsContainer = new IMGUIContainer(DrawDetailsField);
			_detailsSlot.Add(_detailsContainer);
		}

		void QueryElements() {
			_titleField = rootVisualElement.Q<TextField>("title-field");
			_emailField = rootVisualElement.Q<TextField>("email-field");
			_detailsLabel = rootVisualElement.Q<Label>("details-label");
			_validationLabel = rootVisualElement.Q<Label>("validation-label");
			_submitButton = rootVisualElement.Q<Button>("submit-button");
			_severitySection = rootVisualElement.Q<VisualElement>("severity-section");
			_frequencySection = rootVisualElement.Q<VisualElement>("frequency-section");
			_scrollView = rootVisualElement.Q<ScrollView>("scroll-view");

			_successScreen = rootVisualElement.Q<VisualElement>("success-screen");
			_failureScreen = rootVisualElement.Q<VisualElement>("failure-screen");
			_failureErrorLabel = rootVisualElement.Q<Label>("failure-error-label");
			_tabBugReport = rootVisualElement.Q<Button>("tab-bug-report");
			_tabFeedback = rootVisualElement.Q<Button>("tab-feedback");
		}

		private void DrawDetailsField() {
			// GUILayout.ExpandHeight lets it grow; clamp min so it doesn't collapse
			_detailsText = EditorGUILayout.TextArea(
				_detailsText,
				GUILayout.ExpandWidth(true),
				GUILayout.MinHeight(160)
			);
		}

		void ApplyTranslations() {
			// Form labels
			rootVisualElement.Q<Label>(className: "section-label") // Title section — first one
				?.SetText(Translations.BugReport.LabelTitle);
			rootVisualElement.Q("severity-section")?.Q<Label>(className: "section-label")
				?.SetText(Translations.BugReport.LabelIssueSeverity);
			rootVisualElement.Q("frequency-section")?.Q<Label>(className: "section-label")
				?.SetText(Translations.BugReport.LabelHowOften);
			rootVisualElement.Q("tabs")?.Q<Button>("tab-bug-report")
				?.SetText(Translations.BugReport.TabBugReport);
			rootVisualElement.Q("tabs")?.Q<Button>("tab-feedback")
				?.SetText(Translations.BugReport.TabFeedback);

			// Contact section — scoped by class since it has no name
			var contactSection = rootVisualElement.Q(className: "contact-section");
			contactSection?.Q<Label>(className: "section-label")?.SetText(Translations.BugReport.LabelContactEmail);
			contactSection?.Q<Label>(className: "fine-print")?.SetText(Translations.BugReport.LabelContactEmailFinePrint);

			// Submit button
			_submitButton.text = Translations.BugReport.ButtonSubmit;

			// Success screen
			var successContent = _successScreen?.Q(className: "result-content");
			successContent?.Q<Label>(className: "result-title")?.SetText(Translations.BugReport.SuccessTitle);
			successContent?.Q<Label>(className: "result-message")?.SetText(Translations.BugReport.SuccessMessage);
			_successScreen?.Q<Button>("success-discord-button")?.SetText(Translations.BugReport.ButtonJoinDiscord);
			

			// Failure screen
			var failureContent = _failureScreen?.Q(className: "result-content");
			failureContent?.Q<Label>(className: "result-title")?.SetText(Translations.BugReport.FailureTitle);
			// result-message--error is the dynamic error label, skip it; translate the static one
			var failureMessages = failureContent?.Query<Label>(className: "result-message").ToList();
			if (failureMessages != null) {
				foreach (var lbl in failureMessages) {
					if (!lbl.ClassListContains("result-message--error")) {
						lbl.text = Translations.BugReport.FailureMessage;
					}
				}
			}
			_failureScreen?.Q<Button>("failure-discord-button")?.SetText(Translations.BugReport.ButtonJoinDiscord);
			_failureScreen?.Q<Button>("failure-contact-button")?.SetText(Translations.BugReport.ButtonContactUs);
		}

		#region View switching
		void ShowFormView() {
			_isOnResultScreen = false;
			_scrollView.style.display = DisplayStyle.Flex;
			_successScreen.RemoveFromClassList("result-screen--visible");
			_failureScreen.RemoveFromClassList("result-screen--visible");
		}

		void ShowSuccessScreen() {
			_isOnResultScreen = true;
			_scrollView.style.display = DisplayStyle.None;
			_tabFeedback.style.display = DisplayStyle.None;
			_tabBugReport.style.display = DisplayStyle.None;
			_failureScreen.RemoveFromClassList("result-screen--visible");
			_successScreen.AddToClassList("result-screen--visible");
		}

		void ShowFailureScreen(string errorMessage) {
			_scrollView.style.display = DisplayStyle.None;
			_tabFeedback.style.display = DisplayStyle.None;
			_tabBugReport.style.display = DisplayStyle.None;
			_successScreen.RemoveFromClassList("result-screen--visible");

			if (_failureErrorLabel != null) {
				if (string.IsNullOrEmpty(errorMessage)) {
					_failureErrorLabel.style.display = DisplayStyle.None;
				} else {
					_failureErrorLabel.text = errorMessage;
					_failureErrorLabel.style.display = DisplayStyle.Flex;
				}
			}

			_failureScreen.AddToClassList("result-screen--visible");
			_isOnResultScreen = true;
		}
		#endregion

		void ApplyMode() {
			bool isBugReport = _mode == ReportMode.BugReport;
			this.minSize = this.maxSize = GetMinSize(_mode);

			if (isBugReport) {
				_severitySection.style.display = DisplayStyle.Flex;
				_frequencySection.style.display = DisplayStyle.Flex;
				_severityPopup.style.display = DisplayStyle.Flex;
				_frequencyPopup.style.display = DisplayStyle.Flex;
				_detailsLabel.text = Translations.BugReport.LabelDetails;
				_tabBugReport.AddToClassList("tab--active");
				_tabFeedback.RemoveFromClassList("tab--active");
				if (string.IsNullOrEmpty(_detailsText)) {
				   _detailsText = Translations.BugReport.PlaceholderDetails;
				   _detailsContainer.MarkDirtyRepaint();
				}
			} else {
				_severitySection.style.display = DisplayStyle.None;
				_frequencySection.style.display = DisplayStyle.None;
				_severityPopup.style.display = DisplayStyle.None;
				_frequencyPopup.style.display = DisplayStyle.None;
				_detailsLabel.text = Translations.BugReport.LabelFeedback;
				_tabFeedback.AddToClassList("tab--active");
				_tabBugReport.RemoveFromClassList("tab--active");
				if (_detailsText == Translations.BugReport.PlaceholderDetails) {
				   _detailsText = string.Empty;
				   _detailsContainer.MarkDirtyRepaint();
				}
			}
		}

		void CreatePopupFields() {
			var severityContainer = rootVisualElement.Q<VisualElement>("severity-container");
			_severityPopup = new PopupField<string>(
				string.Empty,
				SeverityChoices,
				0);
			_severityPopup.AddToClassList("input-field");
			severityContainer.Add(_severityPopup);

			var frequencyContainer = rootVisualElement.Q<VisualElement>("frequency-container");
			_frequencyPopup = new PopupField<string>(
				string.Empty,
				FrequencyChoices,
				0);
			_frequencyPopup.AddToClassList("input-field");
			frequencyContainer.Add(_frequencyPopup);
		}

		void SelectBugReport() {
			_mode = ReportMode.BugReport;
			ApplyMode();
		}

		void SelectFeedback() {
			_mode = ReportMode.Feedback;
			ApplyMode();
		}
		
		void AttachCallbacks() {
			if (_tabBugReport != null) {
				_tabBugReport.clicked += SelectBugReport;
			}
			if (_tabFeedback != null) {
				_tabFeedback.clicked += SelectFeedback;
			}
			if (_emailField != null) {
				_emailField.RegisterValueChangedCallback(SetEmailText);
			}
			if (_titleField != null) {
				_titleField.RegisterValueChangedCallback(SetTitleText);
			}
		}

		void DetachCallbacks() {
			if (_tabBugReport != null) {
				_tabBugReport.clicked -= SelectBugReport;
			}
			if (_tabFeedback != null) {
				_tabFeedback.clicked -= SelectFeedback;
			}
			if (_emailField != null) {
				_emailField.UnregisterValueChangedCallback(SetEmailText);
			}
			if (_titleField != null) {
				_titleField.UnregisterValueChangedCallback(SetTitleText);
			}
		}

		void SetEmailText(ChangeEvent<string> @event) {
			_emailText = @event.newValue;
		}
		
		void SetTitleText(ChangeEvent<string> @event) {
			_titleText = @event.newValue;
		}

		static void FixSingleLineFieldAlignment(TextField field) {
			field.RegisterCallback<GeometryChangedEvent>(evt => {
				var input = field.Q(className: "unity-text-field__input");
				if (input != null) {
					input.style.alignItems = Align.Center;
				}
			});
		}

		#region Validation & submit
		void SetupSubmit() {
			_submitButton.clickable.clicked += OnSubmitClicked;
		}

		async void OnSubmitClicked() {
			HideValidation();

			if (string.IsNullOrWhiteSpace(_titleField.value)) {
				ShowValidation(Translations.BugReport.ValidationTitleRequired);
				return;
			}

			var details = _detailsText;

			var report = new Report {
				Mode = _mode,
				Id = Guid.NewGuid().ToString("N").Substring(0, 10),
				Title = _titleField.value.Trim(),
				Severity = _severityPopup != null ? _severityPopup.value : null,
				Frequency = _frequencyPopup != null ? _frequencyPopup.value : null,
				ContactEmail = _emailField.value != null ? _emailField.value.Trim() : null,
				Details = details != null ? details.Trim() : null
			};

			_submitButton.SetEnabled(false);
			_submitButton.text = Translations.BugReport.ButtonSubmitting;

			string error;
			try {
				error = await HandleBugReport(report);
			} catch (Exception ex) {
				error = string.Format(Translations.BugReport.SubmitHandlerError, ex.Message);
			}

			if (this == null) {
				return;
			}

			_submitButton.SetEnabled(true);
			_submitButton.text = Translations.BugReport.ButtonSubmit;

			if (error == null) {
				ShowSuccessScreen();
				if (report.Mode == ReportMode.BugReport) {
					// Back up log and patches locally for this bug report
					// They can be later used to simplify reproducing and fixing the issue, but due to our privacy policy they can't be auto uploaded so we need to ask for them
					#if UNITY_2021_3_OR_NEWER
					var logName = LogsHelper.FindRecentLog(HotReloadAboutTab.logsPath);
					try {
						CreateBugReportZip(
							Path.Combine(CliUtils.GetCliTempDir(), "Backup"), 
							logName == null ? null : Path.Combine(HotReloadAboutTab.logsPath, logName), 
							Path.Combine(PackageConst.LibraryCachePath, "BugReports", $"{report.Id}.zip")
						);
					} catch {
						// Fail silently. If zip is not available we will have to ask for a reproduce
					}
					#endif
				}
			} else {
				ShowFailureScreen(error);
			}
		}
		
		private static Task<string> HandleBugReport(Report report) {
			var hwId = HotReloadPrefs.HardwareId;
			if (string.IsNullOrEmpty(hwId)) {
				hwId = "unknown";
			}
			return RequestHelper.SubmitBugReport(new BugReport {
				reportId = report.Id,
				label = report.Mode == ReportMode.Feedback ? "Feedback" : "Bug Report",
				title = report.Title,
				description = report.Details,
				email = report.ContactEmail,
				hwId = hwId,
				hotReloadVersion = PackageConst.Version,
				unityVersion = Application.unityVersion,
				operatingSystemVersionInfo = SystemInfo.operatingSystem,
			});
		}
		
		#if UNITY_2021_3_OR_NEWER
		private static void CreateBugReportZip(
			string sourceDir,
			string sourceFile,
			string outputZipPath
		) {
			var files = Directory.GetFiles(sourceDir, "*", SearchOption.AllDirectories);
			if (files.Length == 0 && sourceFile == null) {
				return;
			}
            Directory.CreateDirectory(new FileInfo(outputZipPath).DirectoryName!);

			using (var stream = new FileStream(outputZipPath, FileMode.Create, FileAccess.Write, FileShare.None, 65536)) {
				using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: false)) {
					if (sourceFile != null) {
						 AddFileToArchive(archive, sourceFile, Path.GetFileName(sourceFile));
					}
					foreach (string filePath in Directory.GetFiles(sourceDir, "*", SearchOption.AllDirectories)) {
						string relativePath = Path.GetRelativePath(sourceDir, filePath);
						string entryName = $"Patches/{relativePath}".Replace('\\', '/');
						AddFileToArchive(archive, filePath, entryName);
					}
				}
			}
		}

		private static void AddFileToArchive(ZipArchive archive, string filePath, string entryName) {
			var entry = archive.CreateEntry(entryName, CompressionLevel.Optimal);
			using (var entryStream = entry.Open()) {
				using (var fileStream = new FileStream(
					       filePath,
					       FileMode.Open,
					       FileAccess.Read,
					       FileShare.ReadWrite, // allow concurrent writers
					       65536)) {
					fileStream.CopyTo(entryStream);
				}
			}
		}
		#endif
		
		void ShowValidation(string message) {
			_validationLabel.text = message;
			_validationLabel.AddToClassList("validation-label--visible");
		}

		void HideValidation() {
			_validationLabel.RemoveFromClassList("validation-label--visible");
		}
		#endregion

		#region Result screen buttons
		void SetupResultScreenButtons() {
			var successDiscord = rootVisualElement.Q<Button>("success-discord-button");
			var failureDiscord = rootVisualElement.Q<Button>("failure-discord-button");
			var failureContact = rootVisualElement.Q<Button>("failure-contact-button");

			if (successDiscord != null) {
				successDiscord.clickable.clicked += OnDiscordClicked;
			}

			if (failureDiscord != null) {
				failureDiscord.clickable.clicked += OnDiscordClicked;
			}
			if (failureContact != null) {
				failureContact.clickable.clicked += OnContactClicked;
			}

			bool hasDiscord = !string.IsNullOrEmpty(_discordUrl);
			bool hasContact = !string.IsNullOrEmpty(_contactUrl);

			if (successDiscord != null) {
				successDiscord.style.display = hasDiscord ? DisplayStyle.Flex : DisplayStyle.None;
			}
			if (failureDiscord != null) {
				failureDiscord.style.display = hasDiscord ? DisplayStyle.Flex : DisplayStyle.None;
			}
			if (failureContact != null) {
				failureContact.style.display = hasContact ? DisplayStyle.Flex : DisplayStyle.None;
			}
		}

		void OnDiscordClicked() {
			if (!string.IsNullOrEmpty(_discordUrl)) {
				Application.OpenURL(_discordUrl);
			}
		}

		void OnContactClicked() {
			if (!string.IsNullOrEmpty(_contactUrl)) {
				Application.OpenURL(_contactUrl);
			}
		}
		#endregion
	}

	[Serializable]
	internal class Report {
		public string Id;
		public ReportMode Mode;
		public string Title;
		public string Severity;
		public string Frequency;
		public string ContactEmail;
		public string Details;
	}

}

// Small extension to avoid repetitive null checks on label/button text assignment
namespace SingularityGroup.HotReload.Editor {
	internal static class VisualElementExtensions {
		public static void SetText(this UnityEngine.UIElements.Label label, string text) {
			if (label != null) label.text = text;
		}
		public static void SetText(this UnityEngine.UIElements.Button button, string text) {
			if (button != null) button.text = text;
		}
	}
}
