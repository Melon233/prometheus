using System;
using System.Linq;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// Configures Vulkan as the primary Android graphics API, OpenGL ES 3 as the fallback, and excludes confirmed incompatible Vulkan drivers.
/// </summary>
public static class AndroidGraphicsApiConfigurator
{
    // This stable asset path keeps the PlayerSettings reference deterministic and version-controlled.
    private const string FilterAssetPath = "Assets/Editor/AndroidVulkanDeviceFilterLists.asset";

    // Unity normalizes the faulty ARM Mali-G51 r18p0 driver to this semantic version for device filtering.
    private const string ProblemDriverVersion = "18.0.0";

    /// <summary>
    /// Creates or updates the Vulkan device filter asset and writes the required Android graphics API order.
    /// </summary>
    [MenuItem("Prometheus/Build/Configure Android Graphics APIs")]
    public static void Configure()
    {
        // Disable automatic API selection so Unity attempts Vulkan first and OpenGL ES 3 second.
        PlayerSettings.SetUseDefaultGraphicsAPIs(BuildTarget.Android, false);
        PlayerSettings.SetGraphicsAPIs(BuildTarget.Android, new[] { GraphicsDeviceType.Vulkan, GraphicsDeviceType.OpenGLES3 });

        // Reuse the existing asset so repeated configuration does not change its GUID or break the PlayerSettings reference.
        VulkanDeviceFilterLists filterLists = AssetDatabase.LoadAssetAtPath<VulkanDeviceFilterLists>(FilterAssetPath);
        if (filterLists == null)
        {
            // VulkanDeviceFilterLists exposes a public constructor and can be stored as a project asset.
            filterLists = new VulkanDeviceFilterLists();
            AssetDatabase.CreateAsset(filterLists, FilterAssetPath);
        }

        // Deny only the driver combination reproduced on HUAWEI STK-L21, while keeping newer Mali-G51 drivers on Vulkan.
        filterLists.vulkanDeviceDenyFilters = new[]
        {
            new VulkanDeviceFilterData
            {
                vendorName = "^ARM$",
                deviceName = "^Mali-G51$",
                driverVersionString = ProblemDriverVersion
            }
        };
        filterLists.vulkanDeviceAllowFilters = Array.Empty<VulkanDeviceFilterData>();
        filterLists.vulkanGraphicsJobsDeviceFilters = Array.Empty<VulkanGraphicsJobsDeviceFilterData>();

        // Validate every filter expression before persisting and binding the asset to Android PlayerSettings.
        filterLists.EnsureValidOrThrow();
        EditorUtility.SetDirty(filterLists);
        PlayerSettings.Android.androidVulkanDeviceFilterListAsset = filterLists;
        AssetDatabase.SaveAssets();

        // Emit a searchable confirmation for local and automated build logs.
        Debug.Log($"Android Graphics APIs configured: Vulkan -> OpenGLES3; Vulkan deny filter: ARM/Mali-G51/{ProblemDriverVersion}.");
    }

    /// <summary>
    /// Checks whether the current Android graphics API order and Vulkan filter asset satisfy the project requirements.
    /// </summary>
    public static bool IsConfigured(out string error)
    {
        // Require exactly Vulkan and OpenGL ES 3, with Vulkan in the first position.
        GraphicsDeviceType[] graphicsApis = PlayerSettings.GetGraphicsAPIs(BuildTarget.Android);
        GraphicsDeviceType[] expectedApis = { GraphicsDeviceType.Vulkan, GraphicsDeviceType.OpenGLES3 };
        if (PlayerSettings.GetUseDefaultGraphicsAPIs(BuildTarget.Android) || !graphicsApis.SequenceEqual(expectedApis))
        {
            error = "Android Graphics APIs must be explicitly ordered as Vulkan, OpenGLES3.";
            return false;
        }

        // Require PlayerSettings to reference the version-controlled filter asset used by the build.
        VulkanDeviceFilterLists filterLists = PlayerSettings.Android.androidVulkanDeviceFilterListAsset;
        if (filterLists == null || AssetDatabase.GetAssetPath(filterLists) != FilterAssetPath)
        {
            error = $"PlayerSettings.Android.androidVulkanDeviceFilterListAsset must reference {FilterAssetPath}.";
            return false;
        }

        // Require the confirmed ARM Mali-G51 r18p0 incompatibility rule in the deny list.
        bool hasProblemDriverFilter = filterLists.vulkanDeviceDenyFilters.Any(filter => filter.vendorName == "^ARM$" && filter.deviceName == "^Mali-G51$" && filter.driverVersionString == ProblemDriverVersion);
        if (!hasProblemDriverFilter)
        {
            error = "The Vulkan deny list does not contain the ARM Mali-G51 r18p0 driver filter.";
            return false;
        }

        // Delegate expression validation to Unity so invalid filters cannot reach a Player build.
        filterLists.EnsureValidOrThrow();
        error = string.Empty;
        return true;
    }
}

/// <summary>
/// Prevents invalid Android graphics API or Vulkan filter settings from entering an Android build.
/// </summary>
public sealed class AndroidGraphicsApiBuildValidator : IPreprocessBuildWithReport
{
    // Run early so configuration failures appear before expensive content processing.
    public int callbackOrder => -1000;

    /// <summary>
    /// Validates Android builds while leaving builds for other platforms unchanged.
    /// </summary>
    public void OnPreprocessBuild(BuildReport report)
    {
        // This policy applies only to Android Player builds.
        if (report.summary.platform != BuildTarget.Android)
        {
            return;
        }

        // Stop a misconfigured build and point directly to the repair menu command.
        if (!AndroidGraphicsApiConfigurator.IsConfigured(out string error))
        {
            throw new BuildFailedException($"{error} Run Prometheus/Build/Configure Android Graphics APIs before building.");
        }
    }
}
