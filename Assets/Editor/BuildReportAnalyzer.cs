using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;
using BuildReport = UnityEditor.Build.Reporting.BuildReport;

/// <summary>
/// Captures Unity Player builds and produces a self-contained report that correlates Unity, archive, and YooAsset size data.
/// </summary>
public sealed class PrometheusBuildReportAnalyzer : IPostprocessBuildWithReport
{
    /// <summary>Stores generated reports under an ignored project folder so analysis never becomes a game asset.</summary>
    private const string ReportRoot = "Logs/BuildReports";

    /// <summary>Runs after other post-build processors so their work is represented by the final report.</summary>
    public int callbackOrder => 10000;

    /// <summary>Automatically analyzes every completed Player build without changing the project's build pipeline.</summary>
    public void OnPostprocessBuild(BuildReport report)
    {
        try
        {
            string reportPath = Generate(report, report.summary.outputPath);
            Debug.Log($"[BuildReportAnalyzer] Build report generated: {reportPath}");
        }
        catch (Exception exception)
        {
            Debug.LogError($"[BuildReportAnalyzer] Failed to generate the build report.\n{exception}");
        }
    }

    /// <summary>Analyzes the latest Unity build and optionally overrides the package path used for archive inspection.</summary>
    public static string AnalyzeLatestBuild(string packagePath = null)
    {
        BuildReport report = BuildReport.GetLatestReport();
        if (report == null) throw new InvalidOperationException("Unity does not have a recent BuildReport. Build the Player once before running this analysis.");
        string resolvedPackagePath = string.IsNullOrWhiteSpace(packagePath) ? report.summary.outputPath : packagePath;
        return Generate(report, resolvedPackagePath);
    }

    /// <summary>Builds the complete analysis model and writes both machine-readable JSON and a browsable HTML report.</summary>
    public static string Generate(BuildReport report, string packagePath)
    {
        if (report == null) throw new ArgumentNullException(nameof(report));
        PrometheusBuildAnalysis analysis = CreateAnalysis(report, packagePath);
        string timestamp = DateTime.Now.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture);
        string directory = Path.GetFullPath(Path.Combine(ReportRoot, $"{timestamp}-{SanitizeFileName(analysis.Summary.Platform)}"));
        Directory.CreateDirectory(directory);
        string jsonPath = Path.Combine(directory, "build-report.json");
        string htmlPath = Path.Combine(directory, "index.html");
        File.WriteAllText(jsonPath, JsonUtility.ToJson(analysis, true), new UTF8Encoding(false));
        File.WriteAllText(htmlPath, CreateHtml(analysis), new UTF8Encoding(false));
        EditorPrefs.SetString("Prometheus.BuildReportAnalyzer.LastReport", htmlPath);
        return htmlPath;
    }

    /// <summary>Collects Unity BuildReport data before enriching it with archive and YooAsset details.</summary>
    private static PrometheusBuildAnalysis CreateAnalysis(BuildReport report, string packagePath)
    {
        BuildSummary summary = report.summary;
        var analysis = new PrometheusBuildAnalysis
        {
            GeneratedAt = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture),
            UnityVersion = Application.unityVersion,
            Summary = new PrometheusBuildSummary
            {
                Platform = summary.platform.ToString(),
                PlatformGroup = summary.platformGroup.ToString(),
                Result = summary.result.ToString(),
                OutputPath = packagePath ?? string.Empty,
                BuildStartedAt = summary.buildStartedAt.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture),
                BuildEndedAt = summary.buildEndedAt.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture),
                DurationSeconds = summary.totalTime.TotalSeconds,
                ReportedBuildBytes = Convert.ToInt64(summary.totalSize, CultureInfo.InvariantCulture),
                OutputFileBytes = GetFileLength(packagePath),
                WarningCount = summary.totalWarnings,
                ErrorCount = summary.totalErrors,
                BuildOptions = summary.options.ToString(),
                DevelopmentBuild = (summary.options & BuildOptions.Development) != 0,
                DetailedBuildReport = (summary.options & BuildOptions.DetailedBuildReport) != 0,
                StripEngineCode = PlayerSettings.stripEngineCode
            }
        };
        CollectSteps(report, analysis);
        CollectFiles(report, analysis);
        CollectPackedAssets(report, analysis);
        CollectArchive(packagePath, analysis);
        CollectYooAssetReport(summary.platform.ToString(), packagePath, analysis);
        CreateFindings(analysis);
        return analysis;
    }

    /// <summary>Copies the timed build-step tree and its diagnostic messages into serializable rows.</summary>
    private static void CollectSteps(BuildReport report, PrometheusBuildAnalysis analysis)
    {
        foreach (BuildStep step in report.steps)
        {
            var row = new PrometheusBuildStep { Name = step.name, Depth = step.depth, DurationSeconds = step.duration.TotalSeconds };
            foreach (BuildStepMessage message in step.messages) row.Messages.Add(new PrometheusBuildMessage { Type = message.type.ToString(), Content = message.content });
            analysis.Steps.Add(row);
        }
    }

    /// <summary>Copies Unity's physical build outputs and their semantic roles into the report.</summary>
    private static void CollectFiles(BuildReport report, PrometheusBuildAnalysis analysis)
    {
        foreach (BuildFile file in report.GetFiles()) analysis.Files.Add(new PrometheusBuildFile { Path = file.path, Role = file.role, SizeBytes = Convert.ToInt64(file.size, CultureInfo.InvariantCulture) });
        analysis.Files = analysis.Files.OrderByDescending(item => item.SizeBytes).ToList();
    }

    /// <summary>Aggregates serialized Player assets by source path while retaining per-packed-file overhead.</summary>
    private static void CollectPackedAssets(BuildReport report, PrometheusBuildAnalysis analysis)
    {
        var sourceTotals = new Dictionary<string, PrometheusPackedAsset>(StringComparer.OrdinalIgnoreCase);
        foreach (PackedAssets packedFile in report.packedAssets)
        {
            analysis.PackedFiles.Add(new PrometheusPackedFile { ShortPath = packedFile.shortPath, OverheadBytes = Convert.ToInt64(packedFile.overhead, CultureInfo.InvariantCulture), ContentCount = packedFile.contents.Length });
            foreach (PackedAssetInfo content in packedFile.contents)
            {
                string sourcePath = string.IsNullOrEmpty(content.sourceAssetPath) ? "<generated or built-in>" : content.sourceAssetPath;
                string key = $"{sourcePath}|{content.type}";
                if (!sourceTotals.TryGetValue(key, out PrometheusPackedAsset asset))
                {
                    asset = new PrometheusPackedAsset { SourceAssetPath = sourcePath, Type = content.type.ToString() };
                    sourceTotals.Add(key, asset);
                }
                asset.PackedSizeBytes += Convert.ToInt64(content.packedSize, CultureInfo.InvariantCulture);
                asset.ObjectCount++;
            }
        }
        analysis.PackedAssets = sourceTotals.Values.OrderByDescending(item => item.PackedSizeBytes).ToList();
        analysis.PackedFiles = analysis.PackedFiles.OrderByDescending(item => item.OverheadBytes).ToList();
    }

    /// <summary>Reads APK and AAB ZIP entries to distinguish compressed download cost from installed size.</summary>
    private static void CollectArchive(string packagePath, PrometheusBuildAnalysis analysis)
    {
        if (string.IsNullOrWhiteSpace(packagePath) || !File.Exists(packagePath) || !IsZipPackage(packagePath)) return;
        using (ZipArchive archive = ZipFile.OpenRead(packagePath))
        {
            foreach (ZipArchiveEntry entry in archive.Entries)
            {
                if (string.IsNullOrEmpty(entry.Name)) continue;
                analysis.ArchiveEntries.Add(new PrometheusArchiveEntry { Path = entry.FullName, Group = ClassifyArchiveEntry(entry.FullName), CompressedBytes = entry.CompressedLength, UncompressedBytes = entry.Length });
            }
        }
        analysis.ArchiveEntries = analysis.ArchiveEntries.OrderByDescending(item => item.CompressedBytes).ToList();
        analysis.ArchiveGroups = analysis.ArchiveEntries.GroupBy(item => item.Group).Select(group => new PrometheusSizeGroup { Name = group.Key, FileCount = group.Count(), CompressedBytes = group.Sum(item => item.CompressedBytes), UncompressedBytes = group.Sum(item => item.UncompressedBytes) }).OrderByDescending(item => item.CompressedBytes).ToList();
        analysis.Summary.ArchiveEntryCompressedBytes = analysis.ArchiveEntries.Sum(item => item.CompressedBytes);
        analysis.Summary.ArchiveContainerOverheadBytes = Math.Max(0, analysis.Summary.OutputFileBytes - analysis.Summary.ArchiveEntryCompressedBytes);
    }

    /// <summary>Loads the newest matching YooAsset report and correlates its bundle file names with embedded archive entries.</summary>
    private static void CollectYooAssetReport(string platform, string packagePath, PrometheusBuildAnalysis analysis)
    {
        string bundlesRoot = Path.GetFullPath("Bundles");
        if (!Directory.Exists(bundlesRoot)) return;
        string platformRoot = Path.Combine(bundlesRoot, platform);
        string searchRoot = Directory.Exists(platformRoot) ? platformRoot : bundlesRoot;
        TryReadEmbeddedYooIdentity(packagePath, out string embeddedPackageName, out string embeddedPackageVersion);
        IEnumerable<FileInfo> reportFiles = new DirectoryInfo(searchRoot).EnumerateFiles("*.report", SearchOption.AllDirectories);
        FileInfo reportFile = reportFiles.Where(file => string.IsNullOrEmpty(embeddedPackageName) || string.Equals(file.Directory?.Parent?.Name, embeddedPackageName, StringComparison.OrdinalIgnoreCase)).Where(file => string.IsNullOrEmpty(embeddedPackageVersion) || string.Equals(file.Directory?.Name, embeddedPackageVersion, StringComparison.OrdinalIgnoreCase)).OrderByDescending(file => file.LastWriteTimeUtc).FirstOrDefault();
        if (reportFile == null) reportFile = reportFiles.OrderByDescending(file => file.LastWriteTimeUtc).FirstOrDefault();
        if (reportFile == null) return;
        YooAssetReportDto yooReport = JsonUtility.FromJson<YooAssetReportDto>(File.ReadAllText(reportFile.FullName));
        if (yooReport == null || yooReport.Summary == null) return;
        var embeddedNames = new HashSet<string>(analysis.ArchiveEntries.Where(item => item.Group == "YooAsset offline content").Select(item => Path.GetFileName(item.Path)), StringComparer.OrdinalIgnoreCase);
        analysis.YooAsset = new PrometheusYooAssetAnalysis
        {
            ReportPath = reportFile.FullName,
            PackageName = yooReport.Summary.BuildPackageName,
            PackageVersion = yooReport.Summary.BuildPackageVersion,
            BuildPipeline = yooReport.Summary.BuildPipeline,
            BuildSeconds = yooReport.Summary.BuildSeconds,
            MainAssetCount = yooReport.Summary.MainAssetTotalCount,
            AssetFileCount = yooReport.Summary.AssetFileTotalCount,
            BundleCount = yooReport.Summary.AllBundleTotalCount,
            TotalBundleBytes = yooReport.Summary.AllBundleTotalSize,
            AutoCollectShaders = yooReport.Summary.AutoCollectShaders,
            Compression = FormatYooCompression(yooReport.Summary.CompressOption)
        };
        foreach (YooAssetBundleDto bundle in yooReport.BundleInfos)
        {
            var item = new PrometheusYooBundle
            {
                BundleName = bundle.BundleName,
                FileName = bundle.FileName,
                SizeBytes = bundle.FileSize,
                Embedded = embeddedNames.Contains(bundle.FileName),
                DependencyCount = bundle.DependBundles == null ? 0 : bundle.DependBundles.Count,
                ReferenceCount = bundle.ReferenceBundles == null ? 0 : bundle.ReferenceBundles.Count,
                Contents = bundle.BundleContents == null ? string.Empty : string.Join("; ", bundle.BundleContents.Select(content => content.AssetPath).Where(path => !string.IsNullOrEmpty(path)))
            };
            analysis.YooAsset.Bundles.Add(item);
        }
        analysis.YooAsset.Bundles = analysis.YooAsset.Bundles.OrderByDescending(item => item.SizeBytes).ToList();
        analysis.YooAsset.EmbeddedBundleBytes = analysis.YooAsset.Bundles.Where(item => item.Embedded).Sum(item => item.SizeBytes);
        analysis.YooAsset.EmbeddedBundleCount = analysis.YooAsset.Bundles.Count(item => item.Embedded);
        var bundleSizes = analysis.YooAsset.Bundles.ToDictionary(item => item.BundleName, item => item.SizeBytes, StringComparer.OrdinalIgnoreCase);
        foreach (YooAssetAssetDto asset in yooReport.AssetInfos)
        {
            long dependencyBytes = asset.DependBundles == null ? 0 : asset.DependBundles.Distinct(StringComparer.OrdinalIgnoreCase).Where(bundleSizes.ContainsKey).Sum(bundleName => bundleSizes[bundleName]);
            analysis.YooAsset.MainAssets.Add(new PrometheusYooMainAsset { Address = asset.Address, AssetPath = asset.AssetPath, MainBundleName = asset.MainBundleName, MainBundleBytes = asset.MainBundleSize, DependencyBundleCount = asset.DependBundles == null ? 0 : asset.DependBundles.Distinct(StringComparer.OrdinalIgnoreCase).Count(), DependencyBundleBytes = dependencyBytes, TotalClosureBytes = asset.MainBundleSize + dependencyBytes });
        }
        analysis.YooAsset.MainAssets = analysis.YooAsset.MainAssets.OrderByDescending(item => item.TotalClosureBytes).ToList();
    }

    /// <summary>Creates evidence-based findings from thresholds that are meaningful for mobile package delivery.</summary>
    private static void CreateFindings(PrometheusBuildAnalysis analysis)
    {
        long packageBytes = analysis.Summary.OutputFileBytes;
        PrometheusSizeGroup yooGroup = analysis.ArchiveGroups.FirstOrDefault(group => group.Name == "YooAsset offline content");
        if (yooGroup != null && packageBytes > 0)
        {
            double ratio = (double)yooGroup.CompressedBytes / packageBytes;
            analysis.Findings.Add(new PrometheusBuildFinding { Severity = ratio >= 0.5 ? "High" : "Medium", Title = "离线 YooAsset 资源主导包体", Evidence = $"离线资源占 {FormatBytes(yooGroup.CompressedBytes)}，即 APK 的 {FormatPercent(ratio)}。", Recommendation = "StreamingAssets 只保留首启必需 Bundle。可选场景、角色和特效应改为 Host/Web 下载，或者用明确的内置标签筛选，不要嵌入整个资源包。" });
        }
        PrometheusYooBundle largestBundle = analysis.YooAsset?.Bundles.FirstOrDefault();
        if (largestBundle != null && largestBundle.SizeBytes >= 20L * 1024 * 1024)
        {
            analysis.Findings.Add(new PrometheusBuildFinding { Severity = "High", Title = "单个 YooAsset Bundle 体积异常", Evidence = $"{largestBundle.BundleName} 达到 {FormatBytes(largestBundle.SizeBytes)}，内容为 {largestBundle.Contents}。", Recommendation = "检查场景生成数据、静态合批、光照/探针数据和大型依赖。在生命周期允许时拆分场景或独立下载内容。" });
        }
        PrometheusYooMainAsset sampleAsset = analysis.YooAsset?.MainAssets.FirstOrDefault(asset => (asset.AssetPath.IndexOf("sample", StringComparison.OrdinalIgnoreCase) >= 0 || asset.AssetPath.IndexOf("demo", StringComparison.OrdinalIgnoreCase) >= 0 || asset.AssetPath.IndexOf("test", StringComparison.OrdinalIgnoreCase) >= 0) && asset.TotalClosureBytes >= 5L * 1024 * 1024);
        if (sampleAsset != null) analysis.Findings.Add(new PrometheusBuildFinding { Severity = "High", Title = "示例或测试资源进入首包", Evidence = $"{sampleAsset.AssetPath} 的依赖闭包为 {FormatBytes(sampleAsset.TotalClosureBytes)}，涉及 {sampleAsset.DependencyBundleCount} 个依赖 Bundle。闭包之间可能共享资源，因此该数值不能直接相加。", Recommendation = "确认运行时是否需要该资源；不需要时将它移出收集目录或增加收集过滤规则。若仍需保留，应将其改为非内置标签并按需下载。" });
        PrometheusSizeGroup nativeGroup = analysis.ArchiveGroups.FirstOrDefault(group => group.Name == "Native libraries");
        if (nativeGroup != null) analysis.Findings.Add(new PrometheusBuildFinding { Severity = "Info", Title = "原生运行库基线", Evidence = $"原生库增加 {FormatBytes(nativeGroup.CompressedBytes)} 下载体积，解压后为 {FormatBytes(nativeGroup.UncompressedBytes)}。", Recommendation = "除非确实需要其他 ABI，否则只保留 ARM64；发布包使用非调试 FMOD 库，并通过多次报告比较 libil2cpp/libunity 的变化。" });
        if (!analysis.Summary.StripEngineCode) analysis.Findings.Add(new PrometheusBuildFinding { Severity = "Medium", Title = "Unity 引擎代码裁剪未开启", Evidence = "PlayerSettings.stripEngineCode 为 false。", Recommendation = "在 link.xml 覆盖和真机回归稳定后开启 Strip Engine Code；它能降低原生引擎代码体积，但可能暴露遗漏的保留规则。" });
        if (analysis.Summary.DevelopmentBuild) analysis.Findings.Add(new PrometheusBuildFinding { Severity = "Medium", Title = "当前是 Development Build", Evidence = $"构建选项：{analysis.Summary.BuildOptions}。", Recommendation = "正式包体对比应使用关闭 Development Build 的发布构建。" });
        if (analysis.YooAsset != null && analysis.YooAsset.Compression == "LZ4" && yooGroup != null && yooGroup.CompressedBytes == yooGroup.UncompressedBytes) analysis.Findings.Add(new PrometheusBuildFinding { Severity = "Medium", Title = "内置 YooAsset Bundle 使用 LZ4", Evidence = $"{FormatBytes(yooGroup.CompressedBytes)} 的 Bundle 在 APK 中压缩前后完全相同，ZIP 层没有继续缩小这些文件。", Recommendation = "为首包内置 Bundle 单独对比 LZMA 的包体和首启耗时；远端或高频加载 Bundle 通常继续使用 LZ4。最终选择必须经过目标手机加载性能验证。" });
        if (!analysis.Summary.DetailedBuildReport) analysis.Findings.Add(new PrometheusBuildFinding { Severity = "Info", Title = "未请求 Detailed Build Report", Evidence = "构建选项中没有 BuildOptions.DetailedBuildReport，因此可能缺少场景到资源的引用归因。", Recommendation = "需要场景级引用追踪时，在发起构建的代码中加入 DetailedBuildReport。" });
        if (analysis.Summary.OutputFileBytes > 0 && analysis.Summary.ReportedBuildBytes > analysis.Summary.OutputFileBytes * 2) analysis.Findings.Add(new PrometheusBuildFinding { Severity = "Info", Title = "Unity 报告体积不是 APK 下载体积", Evidence = $"Unity BuildReport 为 {FormatBytes(analysis.Summary.ReportedBuildBytes)}，最终 APK 为 {FormatBytes(analysis.Summary.OutputFileBytes)}。", Recommendation = "BuildReport 的 totalSize 会统计构建产物/暂存文件的未压缩规模；发布下载体积以 APK/AAB ZIP 的 Compressed 列为准，安装占用参考 Uncompressed 列。" });
        if (analysis.Summary.OutputFileBytes > 0 && analysis.Summary.ArchiveContainerOverheadBytes >= Math.Max(10L * 1024 * 1024, analysis.Summary.OutputFileBytes / 10)) analysis.Findings.Insert(0, new PrometheusBuildFinding { Severity = "High", Title = "APK 容器存在异常空洞或填充", Evidence = $"APK 文件为 {FormatBytes(analysis.Summary.OutputFileBytes)}，有效 ZIP 条目压缩总量仅 {FormatBytes(analysis.Summary.ArchiveEntryCompressedBytes)}，差值达到 {FormatBytes(analysis.Summary.ArchiveContainerOverheadBytes)}。", Recommendation = "清理 Gradle launcher/build 与 Unity Android 增量构建缓存后执行完整构建。不要把单纯 zipalign 后的未签名文件作为发布包；应由 Gradle 重新打包并签名。" });
    }

    /// <summary>Reads the embedded YooAsset package identity so an older APK is matched to its exact package report.</summary>
    private static void TryReadEmbeddedYooIdentity(string packagePath, out string packageName, out string packageVersion)
    {
        packageName = string.Empty;
        packageVersion = string.Empty;
        if (string.IsNullOrWhiteSpace(packagePath) || !File.Exists(packagePath) || !IsZipPackage(packagePath)) return;
        using (ZipArchive archive = ZipFile.OpenRead(packagePath))
        {
            ZipArchiveEntry versionEntry = archive.Entries.FirstOrDefault(entry => entry.FullName.Replace('\\', '/').StartsWith("assets/yoo/", StringComparison.OrdinalIgnoreCase) && entry.Name.EndsWith(".version", StringComparison.OrdinalIgnoreCase));
            if (versionEntry == null) return;
            string[] segments = versionEntry.FullName.Replace('\\', '/').Split('/');
            if (segments.Length >= 4) packageName = segments[2];
            using (var reader = new StreamReader(versionEntry.Open(), Encoding.UTF8, true, 1024, false)) packageVersion = reader.ReadToEnd().Trim();
        }
    }

    /// <summary>Creates a portable HTML dashboard with searchable detail tables and no external dependencies.</summary>
    private static string CreateHtml(PrometheusBuildAnalysis analysis)
    {
        var html = new StringBuilder(256 * 1024);
        html.Append("<!doctype html><html lang=\"zh-CN\"><head><meta charset=\"utf-8\"><meta name=\"viewport\" content=\"width=device-width,initial-scale=1\"><title>Prometheus Build Report</title>");
        html.Append("<style>body{margin:0;background:#f4f5f7;color:#24272d;font:14px/1.5 -apple-system,BlinkMacSystemFont,'Segoe UI',sans-serif}header{background:#20242a;color:#fff;padding:24px 32px}main{max-width:1500px;margin:auto;padding:24px 32px 60px}h1{margin:0 0 6px;font-size:24px}h2{margin:28px 0 10px;font-size:18px}.sub{color:#aeb6c2}.metrics{display:grid;grid-template-columns:repeat(auto-fit,minmax(170px,1fr));gap:10px}.metric,.finding{background:#fff;border:1px solid #dfe2e6;border-radius:6px;padding:14px}.metric b{display:block;font-size:19px}.finding{margin:8px 0;border-left:4px solid #6b7280}.finding.High{border-left-color:#c53b3b}.finding.Medium{border-left-color:#d68a13}.finding.Info{border-left-color:#3676b8}.finding h3{margin:0 0 4px;font-size:15px}.finding p{margin:3px 0}.toolbar{display:flex;gap:8px;margin:8px 0}.toolbar input{width:min(520px,100%);padding:7px 9px;border:1px solid #c8cdd4;border-radius:4px}section{overflow:auto}table{width:100%;border-collapse:collapse;background:#fff;border:1px solid #dfe2e6}th,td{padding:7px 9px;border-bottom:1px solid #eceef1;text-align:left;vertical-align:top}th{position:sticky;top:0;background:#e9ebee;white-space:nowrap}td.path{word-break:break-all}td.num{text-align:right;white-space:nowrap}.depth{color:#707782}.muted{color:#707782}</style></head><body>");
        html.Append($"<header><h1>Prometheus Build Report</h1><div class=\"sub\">{Html(analysis.GeneratedAt)} · Unity {Html(analysis.UnityVersion)} · {Html(analysis.Summary.Result)}</div></header><main>");
        html.Append("<div class=\"metrics\">");
        AppendMetric(html, "Package", FormatBytes(analysis.Summary.OutputFileBytes));
        AppendMetric(html, "Unity reported", FormatBytes(analysis.Summary.ReportedBuildBytes));
        AppendMetric(html, "Duration", FormatDuration(analysis.Summary.DurationSeconds));
        AppendMetric(html, "Platform", analysis.Summary.Platform);
        AppendMetric(html, "Warnings / Errors", $"{analysis.Summary.WarningCount} / {analysis.Summary.ErrorCount}");
        AppendMetric(html, "YooAsset embedded", analysis.YooAsset == null ? "n/a" : FormatBytes(analysis.YooAsset.EmbeddedBundleBytes));
        AppendMetric(html, "Archive overhead", FormatBytes(analysis.Summary.ArchiveContainerOverheadBytes));
        html.Append("</div>");
        html.Append("<h2>分析结论</h2>");
        foreach (PrometheusBuildFinding finding in analysis.Findings) html.Append($"<article class=\"finding {Html(finding.Severity)}\"><h3>{Html(finding.Severity)} · {Html(finding.Title)}</h3><p><b>证据：</b>{Html(finding.Evidence)}</p><p><b>建议：</b>{Html(finding.Recommendation)}</p></article>");
        html.Append("<h2>APK / AAB 组成</h2>");
        AppendSearch(html, "archive-groups");
        html.Append("<section><table id=\"archive-groups\"><thead><tr><th>Category</th><th>Files</th><th>Compressed</th><th>Uncompressed</th><th>Package share</th></tr></thead><tbody>");
        foreach (PrometheusSizeGroup group in analysis.ArchiveGroups) html.Append($"<tr><td>{Html(group.Name)}</td><td class=\"num\">{group.FileCount}</td><td class=\"num\">{FormatBytes(group.CompressedBytes)}</td><td class=\"num\">{FormatBytes(group.UncompressedBytes)}</td><td class=\"num\">{FormatPercent(analysis.Summary.OutputFileBytes == 0 ? 0 : (double)group.CompressedBytes / analysis.Summary.OutputFileBytes)}</td></tr>");
        html.Append("</tbody></table></section>");
        html.Append("<h2>YooAsset 分析</h2>");
        if (analysis.YooAsset == null) html.Append("<p class=\"muted\">No matching YooAsset .report file was found.</p>");
        else
        {
            html.Append($"<p class=\"muted\">{Html(analysis.YooAsset.PackageName)} / {Html(analysis.YooAsset.PackageVersion)} · {analysis.YooAsset.BundleCount} bundles · {analysis.YooAsset.MainAssetCount} main assets · report: {Html(analysis.YooAsset.ReportPath)}</p>");
            html.Append("<h2>YooAsset 主资源依赖闭包</h2><p class=\"muted\">闭包包含主 Bundle 及其直接依赖 Bundle；不同主资源可能共享依赖，因此各行不能直接求和。</p>");
            AppendSearch(html, "yoo-main-assets");
            html.Append("<section><table id=\"yoo-main-assets\"><thead><tr><th>Address</th><th>Asset</th><th>Main bundle</th><th>Main size</th><th>Dependency bundles</th><th>Dependency size</th><th>Total closure</th></tr></thead><tbody>");
            foreach (PrometheusYooMainAsset asset in analysis.YooAsset.MainAssets) html.Append($"<tr><td>{Html(asset.Address)}</td><td class=\"path\">{Html(asset.AssetPath)}</td><td class=\"path\">{Html(asset.MainBundleName)}</td><td class=\"num\">{FormatBytes(asset.MainBundleBytes)}</td><td class=\"num\">{asset.DependencyBundleCount}</td><td class=\"num\">{FormatBytes(asset.DependencyBundleBytes)}</td><td class=\"num\">{FormatBytes(asset.TotalClosureBytes)}</td></tr>");
            html.Append("</tbody></table></section><h2>YooAsset Bundle 明细</h2>");
            AppendSearch(html, "yoo-bundles");
            html.Append("<section><table id=\"yoo-bundles\"><thead><tr><th>Bundle</th><th>File</th><th>Size</th><th>Embedded</th><th>Deps / Refs</th><th>Contents</th></tr></thead><tbody>");
            foreach (PrometheusYooBundle bundle in analysis.YooAsset.Bundles) html.Append($"<tr><td class=\"path\">{Html(bundle.BundleName)}</td><td>{Html(bundle.FileName)}</td><td class=\"num\">{FormatBytes(bundle.SizeBytes)}</td><td>{(bundle.Embedded ? "Yes" : "No")}</td><td class=\"num\">{bundle.DependencyCount} / {bundle.ReferenceCount}</td><td class=\"path\">{Html(bundle.Contents)}</td></tr>");
            html.Append("</tbody></table></section>");
        }
        html.Append("<h2>Unity Player 内置资源</h2>");
        AppendSearch(html, "packed-assets");
        html.Append("<section><table id=\"packed-assets\"><thead><tr><th>Source asset</th><th>Type</th><th>Objects</th><th>Packed size</th></tr></thead><tbody>");
        foreach (PrometheusPackedAsset asset in analysis.PackedAssets) html.Append($"<tr><td class=\"path\">{Html(asset.SourceAssetPath)}</td><td>{Html(asset.Type)}</td><td class=\"num\">{asset.ObjectCount}</td><td class=\"num\">{FormatBytes(asset.PackedSizeBytes)}</td></tr>");
        html.Append("</tbody></table></section>");
        html.Append("<h2>Archive 文件明细</h2>");
        AppendSearch(html, "archive-entries");
        html.Append("<section><table id=\"archive-entries\"><thead><tr><th>Path</th><th>Category</th><th>Compressed</th><th>Uncompressed</th></tr></thead><tbody>");
        foreach (PrometheusArchiveEntry entry in analysis.ArchiveEntries) html.Append($"<tr><td class=\"path\">{Html(entry.Path)}</td><td>{Html(entry.Group)}</td><td class=\"num\">{FormatBytes(entry.CompressedBytes)}</td><td class=\"num\">{FormatBytes(entry.UncompressedBytes)}</td></tr>");
        html.Append("</tbody></table></section>");
        html.Append("<h2>构建过程</h2>");
        AppendSearch(html, "build-steps");
        html.Append("<section><table id=\"build-steps\"><thead><tr><th>Depth</th><th>Step</th><th>Duration</th><th>Messages</th></tr></thead><tbody>");
        foreach (PrometheusBuildStep step in analysis.Steps) html.Append($"<tr><td class=\"num depth\">{step.Depth}</td><td>{Html(step.Name)}</td><td class=\"num\">{FormatDuration(step.DurationSeconds)}</td><td class=\"path\">{Html(string.Join(" | ", step.Messages.Select(message => $"[{message.Type}] {message.Content}")))}</td></tr>");
        html.Append("</tbody></table></section>");
        html.Append("<h2>Unity 输出文件</h2>");
        AppendSearch(html, "build-files");
        html.Append("<section><table id=\"build-files\"><thead><tr><th>Path</th><th>Role</th><th>Size</th></tr></thead><tbody>");
        foreach (PrometheusBuildFile file in analysis.Files) html.Append($"<tr><td class=\"path\">{Html(file.Path)}</td><td>{Html(file.Role)}</td><td class=\"num\">{FormatBytes(file.SizeBytes)}</td></tr>");
        html.Append("</tbody></table></section>");
        html.Append("</main><script>document.querySelectorAll('[data-table]').forEach(function(input){input.addEventListener('input',function(){var q=input.value.toLowerCase();document.querySelectorAll('#'+input.dataset.table+' tbody tr').forEach(function(row){row.style.display=row.textContent.toLowerCase().includes(q)?'':'none';});});});</script></body></html>");
        return html.ToString();
    }

    /// <summary>Adds one summary metric to the HTML dashboard.</summary>
    private static void AppendMetric(StringBuilder html, string label, string value)
    {
        html.Append($"<div class=\"metric\"><span class=\"muted\">{Html(label)}</span><b>{Html(value)}</b></div>");
    }

    /// <summary>Adds a client-side filter input for a detail table.</summary>
    private static void AppendSearch(StringBuilder html, string tableId)
    {
        html.Append($"<div class=\"toolbar\"><input data-table=\"{Html(tableId)}\" type=\"search\" placeholder=\"筛选当前表格...\"></div>");
    }

    /// <summary>Classifies common Unity Android archive paths into actionable size groups.</summary>
    private static string ClassifyArchiveEntry(string path)
    {
        string normalized = path.Replace('\\', '/');
        if (normalized.Contains("/assets/yoo/") || normalized.StartsWith("assets/yoo/", StringComparison.OrdinalIgnoreCase)) return "YooAsset offline content";
        if (normalized.Contains("/lib/") || normalized.StartsWith("lib/", StringComparison.OrdinalIgnoreCase)) return "Native libraries";
        if (normalized.Contains("assets/bin/Data/")) return "Unity Player data";
        if (normalized.Contains("assets/FMOD/")) return "FMOD banks";
        if (Path.GetFileName(normalized).StartsWith("classes", StringComparison.OrdinalIgnoreCase) && normalized.EndsWith(".dex", StringComparison.OrdinalIgnoreCase)) return "DEX managed code";
        if (normalized.Contains("/res/") || normalized.StartsWith("res/", StringComparison.OrdinalIgnoreCase) || normalized.EndsWith("resources.arsc", StringComparison.OrdinalIgnoreCase)) return "Android resources";
        return "Other";
    }

    /// <summary>Returns whether the selected output uses the ZIP container understood by this analyzer.</summary>
    private static bool IsZipPackage(string path)
    {
        string extension = Path.GetExtension(path);
        return extension.Equals(".apk", StringComparison.OrdinalIgnoreCase) || extension.Equals(".aab", StringComparison.OrdinalIgnoreCase) || extension.Equals(".zip", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Reads a file length without failing analysis when the package has been moved or deleted.</summary>
    private static long GetFileLength(string path)
    {
        return string.IsNullOrWhiteSpace(path) || !File.Exists(path) ? 0 : new FileInfo(path).Length;
    }

    /// <summary>Formats byte counts using binary units so totals match operating-system file properties.</summary>
    internal static string FormatBytes(long bytes)
    {
        string[] units = { "B", "KiB", "MiB", "GiB", "TiB" };
        double value = Math.Abs((double)bytes);
        int unit = 0;
        while (value >= 1024 && unit < units.Length - 1) { value /= 1024; unit++; }
        if (bytes < 0) value = -value;
        return $"{value:0.##} {units[unit]}";
    }

    /// <summary>Formats elapsed seconds in a compact form suitable for build-step tables.</summary>
    private static string FormatDuration(double seconds)
    {
        TimeSpan duration = TimeSpan.FromSeconds(seconds);
        return duration.TotalMinutes >= 1 ? $"{(int)duration.TotalMinutes}m {duration.Seconds:00}s" : $"{duration.TotalSeconds:0.###}s";
    }

    /// <summary>Formats a ratio as a percentage with one decimal place.</summary>
    private static string FormatPercent(double ratio)
    {
        return ratio.ToString("P1", CultureInfo.InvariantCulture);
    }

    /// <summary>Converts YooAsset's serialized compression enum into a stable report label.</summary>
    private static string FormatYooCompression(int compression)
    {
        if (compression == 0) return "Uncompressed";
        if (compression == 1) return "LZMA";
        if (compression == 2) return "LZ4";
        return $"Unknown ({compression})";
    }

    /// <summary>Escapes text before inserting project paths and diagnostics into HTML.</summary>
    private static string Html(string value)
    {
        if (string.IsNullOrEmpty(value)) return string.Empty;
        return value.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;").Replace("\"", "&quot;").Replace("'", "&#39;");
    }

    /// <summary>Removes invalid characters before using platform names in report directories.</summary>
    private static string SanitizeFileName(string value)
    {
        var invalid = new HashSet<char>(Path.GetInvalidFileNameChars());
        return new string((value ?? "Unknown").Select(character => invalid.Contains(character) ? '_' : character).ToArray());
    }
}

/// <summary>Provides a small Editor window for manual report generation and report discovery.</summary>
public sealed class PrometheusBuildReportAnalyzerWindow : EditorWindow
{
    /// <summary>Stores an optional APK or AAB path selected for analysis.</summary>
    private string packagePath = string.Empty;

    /// <summary>Opens the analyzer window from the project build menu.</summary>
    [MenuItem("Prometheus/Build/Build Report Analyzer")]
    private static void Open()
    {
        GetWindow<PrometheusBuildReportAnalyzerWindow>("Build Report Analyzer");
    }

    /// <summary>Draws manual analysis, package selection, and report navigation controls.</summary>
    private void OnGUI()
    {
        EditorGUILayout.LabelField("Prometheus Build Report Analyzer", EditorStyles.boldLabel);
        EditorGUILayout.HelpBox("Player Build 完成后会自动输出 HTML 和 JSON。手动分析使用 Unity 最近一次 BuildReport，并可指定 APK/AAB 以拆解压缩包内容。", MessageType.Info);
        EditorGUILayout.Space();
        packagePath = EditorGUILayout.TextField("Package", packagePath);
        using (new EditorGUILayout.HorizontalScope())
        {
            if (GUILayout.Button("选择 APK / AAB")) SelectPackage();
            if (GUILayout.Button("使用最近构建路径")) UseLatestBuildPath();
        }
        if (GUILayout.Button("分析最近一次构建", GUILayout.Height(30))) AnalyzeLatest();
        if (GUILayout.Button("打开最近报告")) OpenLastReport();
        EditorGUILayout.Space();
        EditorGUILayout.LabelField("输出目录", Path.GetFullPath("Logs/BuildReports"));
    }

    /// <summary>Lets the user choose an Android package without modifying project settings.</summary>
    private void SelectPackage()
    {
        string selected = EditorUtility.OpenFilePanelWithFilters("Select Android package", string.IsNullOrEmpty(packagePath) ? Path.GetFullPath(".") : Path.GetDirectoryName(packagePath), new[] { "Android package", "apk,aab", "ZIP archive", "zip", "All files", "*" });
        if (!string.IsNullOrEmpty(selected)) packagePath = selected;
    }

    /// <summary>Copies the latest report output path into the package field.</summary>
    private void UseLatestBuildPath()
    {
        BuildReport report = BuildReport.GetLatestReport();
        if (report == null) { ShowNotification(new GUIContent("没有最近的 BuildReport")); return; }
        packagePath = report.summary.outputPath;
    }

    /// <summary>Generates and opens a fresh analysis from the latest Unity BuildReport.</summary>
    private void AnalyzeLatest()
    {
        try
        {
            string htmlPath = PrometheusBuildReportAnalyzer.AnalyzeLatestBuild(string.IsNullOrWhiteSpace(packagePath) ? null : packagePath);
            EditorUtility.RevealInFinder(htmlPath);
            Application.OpenURL(new Uri(htmlPath).AbsoluteUri);
        }
        catch (Exception exception)
        {
            Debug.LogException(exception);
            EditorUtility.DisplayDialog("Build Report Analyzer", exception.Message, "OK");
        }
    }

    /// <summary>Opens the last generated report when it still exists on disk.</summary>
    private void OpenLastReport()
    {
        string htmlPath = EditorPrefs.GetString("Prometheus.BuildReportAnalyzer.LastReport", string.Empty);
        if (!File.Exists(htmlPath)) { ShowNotification(new GUIContent("没有可用的历史报告")); return; }
        Application.OpenURL(new Uri(htmlPath).AbsoluteUri);
    }
}

/// <summary>Serializable root model shared by the JSON and HTML report writers.</summary>
[Serializable]
internal sealed class PrometheusBuildAnalysis
{
    /// <summary>Records when this analysis was generated.</summary>
    public string GeneratedAt;
    /// <summary>Records the Unity editor version used by the analyzer.</summary>
    public string UnityVersion;
    /// <summary>Contains high-level Player build statistics.</summary>
    public PrometheusBuildSummary Summary;
    /// <summary>Contains evidence-based optimization findings.</summary>
    public List<PrometheusBuildFinding> Findings = new List<PrometheusBuildFinding>();
    /// <summary>Contains the timed Unity build-step tree.</summary>
    public List<PrometheusBuildStep> Steps = new List<PrometheusBuildStep>();
    /// <summary>Contains physical files reported by Unity.</summary>
    public List<PrometheusBuildFile> Files = new List<PrometheusBuildFile>();
    /// <summary>Contains overhead summaries for Unity packed files.</summary>
    public List<PrometheusPackedFile> PackedFiles = new List<PrometheusPackedFile>();
    /// <summary>Contains Player-packed source asset totals.</summary>
    public List<PrometheusPackedAsset> PackedAssets = new List<PrometheusPackedAsset>();
    /// <summary>Contains every file found inside an APK or AAB.</summary>
    public List<PrometheusArchiveEntry> ArchiveEntries = new List<PrometheusArchiveEntry>();
    /// <summary>Contains aggregated APK or AAB content categories.</summary>
    public List<PrometheusSizeGroup> ArchiveGroups = new List<PrometheusSizeGroup>();
    /// <summary>Contains correlated YooAsset package data when available.</summary>
    public PrometheusYooAssetAnalysis YooAsset;
}

/// <summary>Serializable high-level Player build statistics.</summary>
[Serializable]
internal sealed class PrometheusBuildSummary
{
    /// <summary>Stores the concrete build platform.</summary>
    public string Platform;
    /// <summary>Stores the build platform group.</summary>
    public string PlatformGroup;
    /// <summary>Stores the final Unity build result.</summary>
    public string Result;
    /// <summary>Stores the analyzed output package path.</summary>
    public string OutputPath;
    /// <summary>Stores the build start timestamp.</summary>
    public string BuildStartedAt;
    /// <summary>Stores the build end timestamp.</summary>
    public string BuildEndedAt;
    /// <summary>Stores the full build duration in seconds.</summary>
    public double DurationSeconds;
    /// <summary>Stores Unity's reported build size.</summary>
    public long ReportedBuildBytes;
    /// <summary>Stores the final package file size.</summary>
    public long OutputFileBytes;
    /// <summary>Stores the sum of compressed ZIP entry payloads.</summary>
    public long ArchiveEntryCompressedBytes;
    /// <summary>Stores package bytes not attributed to compressed ZIP entry payloads.</summary>
    public long ArchiveContainerOverheadBytes;
    /// <summary>Stores Unity's build warning count.</summary>
    public int WarningCount;
    /// <summary>Stores Unity's build error count.</summary>
    public int ErrorCount;
    /// <summary>Stores the active BuildOptions flags.</summary>
    public string BuildOptions;
    /// <summary>Indicates whether Development Build was enabled.</summary>
    public bool DevelopmentBuild;
    /// <summary>Indicates whether detailed reporting was requested.</summary>
    public bool DetailedBuildReport;
    /// <summary>Indicates whether Unity engine code stripping was enabled.</summary>
    public bool StripEngineCode;
}

/// <summary>Serializable build finding with evidence and a concrete recommendation.</summary>
[Serializable]
internal sealed class PrometheusBuildFinding
{
    /// <summary>Stores the finding severity.</summary>
    public string Severity;
    /// <summary>Stores the concise finding title.</summary>
    public string Title;
    /// <summary>Stores measured evidence supporting the finding.</summary>
    public string Evidence;
    /// <summary>Stores the recommended corrective action.</summary>
    public string Recommendation;
}

/// <summary>Serializable timed Unity build step.</summary>
[Serializable]
internal sealed class PrometheusBuildStep
{
    /// <summary>Stores the Unity build-step name.</summary>
    public string Name;
    /// <summary>Stores the step depth in Unity's build tree.</summary>
    public int Depth;
    /// <summary>Stores the step duration in seconds.</summary>
    public double DurationSeconds;
    /// <summary>Stores diagnostics emitted by this step.</summary>
    public List<PrometheusBuildMessage> Messages = new List<PrometheusBuildMessage>();
}

/// <summary>Serializable diagnostic emitted during a build step.</summary>
[Serializable]
internal sealed class PrometheusBuildMessage
{
    /// <summary>Stores the diagnostic log type.</summary>
    public string Type;
    /// <summary>Stores the diagnostic message text.</summary>
    public string Content;
}

/// <summary>Serializable physical file emitted by Unity's build pipeline.</summary>
[Serializable]
internal sealed class PrometheusBuildFile
{
    /// <summary>Stores the output file path.</summary>
    public string Path;
    /// <summary>Stores Unity's semantic file role.</summary>
    public string Role;
    /// <summary>Stores the file size in bytes.</summary>
    public long SizeBytes;
}

/// <summary>Serializable overhead summary for one Unity packed file.</summary>
[Serializable]
internal sealed class PrometheusPackedFile
{
    /// <summary>Stores the Unity packed-file name.</summary>
    public string ShortPath;
    /// <summary>Stores packed-file header overhead.</summary>
    public long OverheadBytes;
    /// <summary>Stores the number of packed objects.</summary>
    public int ContentCount;
}

/// <summary>Serializable aggregate of Player-packed objects originating from one source asset.</summary>
[Serializable]
internal sealed class PrometheusPackedAsset
{
    /// <summary>Stores the original project asset path.</summary>
    public string SourceAssetPath;
    /// <summary>Stores the serialized Unity object type.</summary>
    public string Type;
    /// <summary>Stores the total serialized size in Player data.</summary>
    public long PackedSizeBytes;
    /// <summary>Stores the number of serialized objects.</summary>
    public int ObjectCount;
}

/// <summary>Serializable compressed archive entry from an APK or AAB.</summary>
[Serializable]
internal sealed class PrometheusArchiveEntry
{
    /// <summary>Stores the path inside the archive.</summary>
    public string Path;
    /// <summary>Stores the analyzer content category.</summary>
    public string Group;
    /// <summary>Stores compressed download bytes.</summary>
    public long CompressedBytes;
    /// <summary>Stores bytes after archive extraction.</summary>
    public long UncompressedBytes;
}

/// <summary>Serializable aggregate for one archive content category.</summary>
[Serializable]
internal sealed class PrometheusSizeGroup
{
    /// <summary>Stores the aggregate category name.</summary>
    public string Name;
    /// <summary>Stores the number of archive files in the category.</summary>
    public int FileCount;
    /// <summary>Stores total compressed category bytes.</summary>
    public long CompressedBytes;
    /// <summary>Stores total uncompressed category bytes.</summary>
    public long UncompressedBytes;
}

/// <summary>Serializable YooAsset package summary and bundle table.</summary>
[Serializable]
internal sealed class PrometheusYooAssetAnalysis
{
    /// <summary>Stores the matched YooAsset report path.</summary>
    public string ReportPath;
    /// <summary>Stores the YooAsset package name.</summary>
    public string PackageName;
    /// <summary>Stores the YooAsset package version.</summary>
    public string PackageVersion;
    /// <summary>Stores the YooAsset build pipeline name.</summary>
    public string BuildPipeline;
    /// <summary>Stores the YooAsset build duration.</summary>
    public int BuildSeconds;
    /// <summary>Stores the number of main collected assets.</summary>
    public int MainAssetCount;
    /// <summary>Stores the total asset-file count including dependencies.</summary>
    public int AssetFileCount;
    /// <summary>Stores the total generated bundle count.</summary>
    public int BundleCount;
    /// <summary>Stores total generated bundle bytes.</summary>
    public long TotalBundleBytes;
    /// <summary>Stores the number of bundles embedded in the package.</summary>
    public int EmbeddedBundleCount;
    /// <summary>Stores total embedded bundle bytes.</summary>
    public long EmbeddedBundleBytes;
    /// <summary>Indicates whether YooAsset automatically collected shaders.</summary>
    public bool AutoCollectShaders;
    /// <summary>Stores YooAsset's serialized compression option.</summary>
    public string Compression;
    /// <summary>Contains bundle-level source and dependency data.</summary>
    public List<PrometheusYooBundle> Bundles = new List<PrometheusYooBundle>();
    /// <summary>Contains dependency-closure totals for every main collected asset.</summary>
    public List<PrometheusYooMainAsset> MainAssets = new List<PrometheusYooMainAsset>();
}

/// <summary>Serializable YooAsset bundle row correlated with package embedding state.</summary>
[Serializable]
internal sealed class PrometheusYooBundle
{
    /// <summary>Stores the logical YooAsset bundle name.</summary>
    public string BundleName;
    /// <summary>Stores the physical bundle file name.</summary>
    public string FileName;
    /// <summary>Stores the built bundle size.</summary>
    public long SizeBytes;
    /// <summary>Indicates whether the bundle exists inside the analyzed package.</summary>
    public bool Embedded;
    /// <summary>Stores the number of bundles this bundle depends on.</summary>
    public int DependencyCount;
    /// <summary>Stores the number of bundles referencing this bundle.</summary>
    public int ReferenceCount;
    /// <summary>Stores source asset paths included in this bundle.</summary>
    public string Contents;
}

/// <summary>Serializable YooAsset main asset with its bundle dependency closure.</summary>
[Serializable]
internal sealed class PrometheusYooMainAsset
{
    /// <summary>Stores the YooAsset address.</summary>
    public string Address;
    /// <summary>Stores the project-relative main asset path.</summary>
    public string AssetPath;
    /// <summary>Stores the main bundle name.</summary>
    public string MainBundleName;
    /// <summary>Stores the main bundle size.</summary>
    public long MainBundleBytes;
    /// <summary>Stores the distinct dependency bundle count.</summary>
    public int DependencyBundleCount;
    /// <summary>Stores the total dependency bundle size.</summary>
    public long DependencyBundleBytes;
    /// <summary>Stores the main and dependency bundle total.</summary>
    public long TotalClosureBytes;
}

/// <summary>Minimal DTO used to read the YooAsset package report without coupling report output to package editor types.</summary>
[Serializable]
internal sealed class YooAssetReportDto
{
    /// <summary>Contains the YooAsset build summary.</summary>
    public YooAssetSummaryDto Summary;
    /// <summary>Contains YooAsset bundle records.</summary>
    public List<YooAssetBundleDto> BundleInfos = new List<YooAssetBundleDto>();
    /// <summary>Contains YooAsset main asset records.</summary>
    public List<YooAssetAssetDto> AssetInfos = new List<YooAssetAssetDto>();
}

/// <summary>Minimal YooAsset summary fields required for package-size attribution.</summary>
[Serializable]
internal sealed class YooAssetSummaryDto
{
    /// <summary>Stores the source package name.</summary>
    public string BuildPackageName;
    /// <summary>Stores the source package version.</summary>
    public string BuildPackageVersion;
    /// <summary>Stores the YooAsset pipeline name.</summary>
    public string BuildPipeline;
    /// <summary>Stores YooAsset build seconds.</summary>
    public int BuildSeconds;
    /// <summary>Stores the main asset count.</summary>
    public int MainAssetTotalCount;
    /// <summary>Stores the total dependency-expanded asset count.</summary>
    public int AssetFileTotalCount;
    /// <summary>Stores the generated bundle count.</summary>
    public int AllBundleTotalCount;
    /// <summary>Stores total generated bundle bytes.</summary>
    public long AllBundleTotalSize;
    /// <summary>Stores whether shaders were automatically collected.</summary>
    public bool AutoCollectShaders;
    /// <summary>Stores the serialized YooAsset compression enum.</summary>
    public int CompressOption;
}

/// <summary>Minimal YooAsset bundle fields required for source and dependency attribution.</summary>
[Serializable]
internal sealed class YooAssetBundleDto
{
    /// <summary>Stores the logical bundle name.</summary>
    public string BundleName;
    /// <summary>Stores the physical bundle file name.</summary>
    public string FileName;
    /// <summary>Stores the generated bundle size.</summary>
    public long FileSize;
    /// <summary>Stores logical dependency bundle names.</summary>
    public List<string> DependBundles = new List<string>();
    /// <summary>Stores logical reverse-reference bundle names.</summary>
    public List<string> ReferenceBundles = new List<string>();
    /// <summary>Stores source assets included in this bundle.</summary>
    public List<YooAssetContentDto> BundleContents = new List<YooAssetContentDto>();
}

/// <summary>Minimal YooAsset main asset fields required for dependency-closure attribution.</summary>
[Serializable]
internal sealed class YooAssetAssetDto
{
    /// <summary>Stores the YooAsset address.</summary>
    public string Address;
    /// <summary>Stores the project-relative asset path.</summary>
    public string AssetPath;
    /// <summary>Stores the main bundle name.</summary>
    public string MainBundleName;
    /// <summary>Stores the main bundle size.</summary>
    public long MainBundleSize;
    /// <summary>Stores dependency bundle names.</summary>
    public List<string> DependBundles = new List<string>();
}

/// <summary>Minimal YooAsset bundle-content record used to list source assets.</summary>
[Serializable]
internal sealed class YooAssetContentDto
{
    /// <summary>Stores the project-relative source asset path.</summary>
    public string AssetPath;
}
