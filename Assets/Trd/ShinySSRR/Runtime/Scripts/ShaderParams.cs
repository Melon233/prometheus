/// <summary>
/// Shiny SSRR - Screen Space Reflections for URP - (c) 2021 Kronnect
/// </summary>

using UnityEngine;

namespace ShinySSRR {

    public static class ShaderParams {

        // input textures
        public static int MainTex = Shader.PropertyToID("_MainTex");
        public static int NoiseTex = Shader.PropertyToID("_NoiseTex");
        public static int BumpMap = Shader.PropertyToID("_BumpMap");
        public static int BumpMap_ST = Shader.PropertyToID("_BumpMap_ST");
        public static int BumpMap_Scale = Shader.PropertyToID("_BumpScale");
        public static int BaseMap_ST = Shader.PropertyToID("_BaseMap_ST");
        public static int OcclusionMap = Shader.PropertyToID("_OcclusionMap");
        public static int OcclusionStrength = Shader.PropertyToID("_OcclusionStrength");
        public static int MetallicGradientTex = Shader.PropertyToID("_MetallicGradientTex");
        public static int SmoothnessGradientTex = Shader.PropertyToID("_SmoothnessGradientTex");
        public static int SkyboxCubemap = Shader.PropertyToID("_SkyboxCubemap");

        // RG supplemental ids
        public static int CameraDepthTexture = Shader.PropertyToID("_CameraDepthTexture");
        public static int CameraNormalsTexture = Shader.PropertyToID("_CameraNormalsTexture");
        public static int MotionVectorTexture = Shader.PropertyToID("_MotionVectorTexture");
        public static int CameraGBuffer0 = Shader.PropertyToID("_GBuffer0");
        public static int CameraGBuffer1 = Shader.PropertyToID("_GBuffer1");
        public static int CameraGBuffer2 = Shader.PropertyToID("_GBuffer2");

        // shader uniforms
        public static int Color = Shader.PropertyToID("_Color");
        public static int BaseColor = Shader.PropertyToID("_BaseColor");
        public static int Smoothness = Shader.PropertyToID("_Smoothness");
        public static int Reflectivity = Shader.PropertyToID("_Metallic");
        public static int SmoothnessMap = Shader.PropertyToID("_SmoothnessMap");
        public static int MetallicGlossMap = Shader.PropertyToID("_MetallicGlossMap");
        public static int MaterialData = Shader.PropertyToID("_MaterialData");
        public static int DistortionData = Shader.PropertyToID("_DistortionData");
        public static int SSRSettings = Shader.PropertyToID("_SSRSettings");
        public static int SSRSettings2 = Shader.PropertyToID("_SSRSettings2");
        public static int SSRSettings3 = Shader.PropertyToID("_SSRSettings3");
        public static int SSRSettings4 = Shader.PropertyToID("_SSRSettings4");
        public static int SSRSettings5 = Shader.PropertyToID("_SSRSettings5");
        public static int SSRSettings6 = Shader.PropertyToID("_SSRSettings6");
        public static int SSRSettings7 = Shader.PropertyToID("_SSRSettings7");
        public static int SSRBlurStrength = Shader.PropertyToID("_SSRBlurStrength");
        public static int EdgeAwareEnabled = Shader.PropertyToID("_EdgeAwareEnabled");
        public static int WorldToViewDir = Shader.PropertyToID("_WorldToViewDir");
        public static int ViewToWorldDir = Shader.PropertyToID("_ViewToWorldDir");
        public static int CameraViewDir = Shader.PropertyToID("_CameraViewDir");
        public static int MinimumBlur = Shader.PropertyToID("_MinimumBlur");
        public static int StencilValue = Shader.PropertyToID("_StencilValue");
        public static int StencilCompareFunction = Shader.PropertyToID("_StencilCompareFunction");
        public static int TemporalResponseSpeed = Shader.PropertyToID("_TemporalResponseSpeed");
        public static int MinimumThickness = Shader.PropertyToID("_MinimumThickness");
        public static int BoundsMin = Shader.PropertyToID("_SSRBoundsMin");
        public static int BoundsSize = Shader.PropertyToID("_SSRBoundsSize");
        public static int DarkReflectionsIntensity = Shader.PropertyToID("_DarkReflectionsIntensity");
        public static int DebugDepthMultiplier = Shader.PropertyToID("_DebugDepthMultiplier");
        public static int MaxBrightness = Shader.PropertyToID("_MaxBrightness");
        public static int TransparentSmoothness = Shader.PropertyToID("_TransparentSmoothness");
        public static int HasTransparentNormals = Shader.PropertyToID("_HasTransparentNormals");

        // targets
        public static int ColorTex = Shader.PropertyToID("_ColorTex");
        public static int RayCast = Shader.PropertyToID("_RayCastRT");
        public static int BlurRT = Shader.PropertyToID("_BlurRT");
        public static int ReflectionsTex = Shader.PropertyToID("_ReflectionsRT");
        public static int ReflectionsOpaqueTex = Shader.PropertyToID("_ReflectionsOpaqueRT");
        public static int NaNBuffer = Shader.PropertyToID("_NaNBuffer");
        public static int PrevResolveNameId = Shader.PropertyToID("_PrevResolve");
        public static int TempAcum = Shader.PropertyToID("_TempAcum");
        public static int DownscaledDepthRT = Shader.PropertyToID("_DownscaledShinyDepthRT");
        public const string DownscaledBackDepthTextureName = "_DownscaledShinyBackDepthRT";
        public static int DownscaledBackDepthRT = Shader.PropertyToID(DownscaledBackDepthTextureName);
        public const string TransparentNormalsRTName = "_TransparentNormalsRT";
        public static int TransparentNormalsRT = Shader.PropertyToID(TransparentNormalsRTName);

        // shader keywords
        public const string SKW_JITTER = "SSR_JITTER";
        public const string SKW_NORMALMAP = "SSR_NORMALMAP";
        public const string SKW_SCREEN_SPACE_NORMALS = "SSR_SCREEN_SPACE_NORMALS";
        public const string SKW_OCCLUSIONMAP = "SSR_OCCLUSIONMAP";
        public const string SKW_DENOISE = "SSR_DENOISE";
        public const string SKW_SMOOTHNESSMAP = "SSR_SMOOTHNESSMAP";
        public const string SKW_REFINE_THICKNESS = "SSR_THICKNESS_FINE";
        public const string SKW_METALLIC_WORKFLOW = "SSR_METALLIC_WORKFLOW";
        public const string SKW_BACK_FACES = "SSR_BACK_FACES";
        public const string SKW_CUSTOM_SMOOTHNESS_METALLIC_PASS = "SSR_CUSTOM_SMOOTHNESS_METALLIC_PASS";
        public const string SKW_TRANSPARENT_NORMAL_PREPASS = "SSR_TRANSPARENT_NORMAL_PREPASS";
        public const string SKW_SKYBOX = "SSR_SKYBOX";
        public const string SKW_SKYBOX_VISIBLE_ONLY = "SSR_SKYBOX_VISIBLE_ONLY";
        public const string SKW_LIMIT_BOUNDS = "SSR_LIMIT_BOUNDS";
        public const string SKW_DARK_REFLECTIONS = "SSR_DARK_REFLECTIONS";
        public const string SKW_METALLIC_BOOST = "SSR_METALLIC_BOOST";
        public const string SKW_BLEND_REFLECTIONS = "SSR_BLEND_REFLECTIONS";
        public const string SKW_ORTHO_SUPPORT = "SSR_ORTHO_SUPPORT";
        public const string SKW_VIGNETTE_RADIAL = "SSR_VIGNETTE_RADIAL";
    }

}