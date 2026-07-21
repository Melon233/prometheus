namespace SingularityGroup.HotReload.Editor.Localization {
    internal static partial class Translations {
        public static class BugReport {
            // Window titles
            public static string WindowTitleBugReport;
            public static string WindowTitleFeedback;
            
            public static string TabBugReport;
            public static string TabFeedback;

            // Form section labels
            public static string LabelTitle;
            public static string LabelIssueSeverity;
            public static string LabelHowOften;
            public static string LabelContactEmail;
            public static string LabelContactEmailFinePrint;
            public static string LabelDetails;
            public static string LabelFeedback;

            // Severity choices
            public static string SeverityNormal;
            public static string SeverityLow;
            public static string SeverityHigh;

            // Frequency choices
            public static string FrequencyFirstTime;
            public static string FrequencySometimes;
            public static string FrequencyAlways;

            // Submit button
            public static string ButtonSubmit;
            public static string ButtonSubmitting;

            // Validation
            public static string ValidationTitleRequired;

            // Success screen
            public static string SuccessTitle;
            public static string SuccessMessage;

            // Failure screen
            public static string FailureTitle;
            public static string FailureMessage;

            // Shared buttons
            public static string ButtonJoinDiscord;
            public static string ButtonContactUs;

            // Discard dialog
            public static string DiscardDialogTitle;
            public static string DiscardDialogMessage;
            public static string DiscardDialogConfirm;
            public static string DiscardDialogCancel;

            // Misc
            public static string NoSubmitHandlerError;
            
            // Exceptions
            public static string SubmitHandlerError;

            public static string PlaceholderDetails;

            public static string LicenseActivationFailed;
            public static string LicenseActivationFailedWithError;

            public static void LoadEnglish() {
                WindowTitleBugReport = "Bug Report (Hot Reload)";
                WindowTitleFeedback = "Feedback (Hot Reload)";
                
                TabBugReport = "Bug Report";
                TabFeedback = "Feedback";

                LabelTitle = "Title";
                LabelIssueSeverity = "Issue Severity";
                
                LicenseActivationFailed = "License activation failed";
                LicenseActivationFailedWithError = "License activation failed with error: {0}";
                
                LabelHowOften = "How often does it happen?";
                LabelContactEmail = "Contact Email (optional)";
                LabelContactEmailFinePrint = "This email will only be used to follow up on this bug report and will not be stored or shared for any other purpose.";
                LabelDetails = "Details";
                LabelFeedback = "Feedback";

                SeverityNormal = "Normal";
                SeverityLow = "Low";
                SeverityHigh = "High";

                FrequencyFirstTime = "This is the first time";
                FrequencySometimes = "Sometimes but not always";
                FrequencyAlways = "Always";

                ButtonSubmit = "Submit";
                ButtonSubmitting = "Submitting...";

                ValidationTitleRequired = "You must provide a title to be able to send the report.";

                SuccessTitle = "Report Submitted";
                SuccessMessage = "Thank you! We've accepted your report and are processing it.";

                FailureTitle = "Submission Failed";
                FailureMessage = "Please reach out to us directly so we can help resolve your issue.";

                ButtonJoinDiscord = "Join Our Discord";
                ButtonContactUs = "Contact Us";

                DiscardDialogTitle = "Discard Report?";
                DiscardDialogMessage = "You have unsaved content in this report. Discard changes and close?";
                DiscardDialogConfirm = "Discard";
                DiscardDialogCancel = "Cancel";

                NoSubmitHandlerError = "No way to handle bug report";

                SubmitHandlerError = "Failure occurred while handling bug report: {0}";
                
                PlaceholderDetails = @"## Steps to reproduce:
1. 
2. 
3. 

## What happened?

## What should have happened?

";
            }

            public static void LoadSimplifiedChinese() {
                WindowTitleBugReport = "错误报告 (Hot Reload)";
                WindowTitleFeedback = "反馈 (Hot Reload)";
                
                TabBugReport = "错误报告";
                TabFeedback = "反馈";

                LabelTitle = "标题";
                LabelIssueSeverity = "问题严重性";
                LabelHowOften = "发生频率如何？";
                LabelContactEmail = "联系邮箱（可选）";
                LabelContactEmailFinePrint = "此邮箱仅用于跟进本次错误报告，不会被存储或用于其他任何目的。";
                LabelDetails = "详情";
                LabelFeedback = "反馈";

                SeverityNormal = "普通";
                SeverityLow = "低";
                SeverityHigh = "高";

                FrequencyFirstTime = "这是第一次发生";
                FrequencySometimes = "偶尔发生";
                FrequencyAlways = "总是发生";

                ButtonSubmit = "提交";
                ButtonSubmitting = "提交中…";

                ValidationTitleRequired = "必须填写标题才能发送报告。";

                SuccessTitle = "报告已提交";
                SuccessMessage = "感谢您！我们已收到您的报告并正在处理中。";

                FailureTitle = "提交失败";
                FailureMessage = "请直接联系我们，以便我们协助解决您的问题。";

                ButtonJoinDiscord = "加入我们的 Discord";
                ButtonContactUs = "联系我们";

                DiscardDialogTitle = "放弃报告？";
                DiscardDialogMessage = "此报告中有未保存的内容。是否放弃更改并关闭？";
                DiscardDialogConfirm = "放弃";
                DiscardDialogCancel = "取消";

                NoSubmitHandlerError = "无法处理错误报告";

                SubmitHandlerError = "处理错误报告时发生错误：{0}";
                
                LicenseActivationFailed = "许可证激活失败";
                LicenseActivationFailedWithError = "许可证激活失败，错误信息：{0}";
                
                PlaceholderDetails = @"## 复现步骤：
1. 
2. 
3. 

## 实际发生了什么？

## 预期应发生什么？

";
            }
        }
    }
}
