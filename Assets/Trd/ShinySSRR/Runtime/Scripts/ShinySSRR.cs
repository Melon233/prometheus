/// <summary>
/// Shiny SSR - Screen Space Reflections for URP - (c) Kronnect
/// </summary>

using System;
using System.Collections.Generic;
using Unity.Collections;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

#if UNITY_2023_3_OR_NEWER
using UnityEngine.Rendering.RenderGraphModule;
#endif

namespace ShinySSRR {


    public enum SmoothnessMetallicPassSource {
        Shiny,
        UserCustomPass
    }

    public partial class ShinySSRR : ScriptableRendererFeature {

        public partial class SmoothnessMetallicPass : ScriptableRenderPass {

            const string m_ProfilerTag = "Shiny SSR Smoothness Metallic Pass";

            static class ShaderParams {
                public const string SmoothnessMetallicRTName = "_SmoothnessMetallicRT";
                public static int SmoothnessMetallicRT = Shader.PropertyToID(SmoothnessMetallicRTName);
            }

            FilteringSettings filterSettings;
            readonly List<ShaderTagId> shaderTagIdList = new List<ShaderTagId>();
            RTHandle smootnessMetallicRT;

            public SmoothnessMetallicPass () {
                RenderTargetIdentifier rti = new RenderTargetIdentifier(ShaderParams.SmoothnessMetallicRT, 0, CubemapFace.Unknown, -1);
                smootnessMetallicRT = RTHandles.Alloc(rti, name: ShaderParams.SmoothnessMetallicRTName);
                shaderTagIdList.Add(new ShaderTagId("SmoothnessMetallic"));
                filterSettings = new FilteringSettings(RenderQueueRange.opaque);
            }

#if UNITY_2023_3_OR_NEWER
            class PassData {
                public RendererListHandle rendererListHandle;
                public UniversalCameraData cameraData;
            }

            public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData) {

                using (var builder = renderGraph.AddUnsafePass<PassData>(m_ProfilerTag, out var passData)) {

                    builder.AllowPassCulling(false);

                    UniversalCameraData cameraData = frameData.Get<UniversalCameraData>();
                    passData.cameraData = cameraData;
                    UniversalLightData lightData = frameData.Get<UniversalLightData>();
                    UniversalRenderingData renderingData = frameData.Get<UniversalRenderingData>();

                    SortingCriteria sortingCriteria = SortingCriteria.CommonOpaque;
                    var drawingSettings = CreateDrawingSettings(shaderTagIdList, renderingData, cameraData, lightData, sortingCriteria);

                    RendererListParams listParams = new RendererListParams(renderingData.cullResults, drawingSettings, filterSettings);
                    passData.rendererListHandle = renderGraph.CreateRendererList(listParams);
                    builder.UseRendererList(passData.rendererListHandle);

                    builder.SetRenderFunc((PassData passData, UnsafeGraphContext context) => {

                        RenderTextureDescriptor desc = passData.cameraData.cameraTargetDescriptor;
                        desc.colorFormat = RenderTextureFormat.RGHalf; // r = smoothness, g = metallic
                        desc.depthBufferBits = 24;
                        desc.msaaSamples = 1;

                        CommandBuffer cmd = CommandBufferHelpers.GetNativeCommandBuffer(context.cmd);

                        cmd.GetTemporaryRT(ShaderParams.SmoothnessMetallicRT, desc, FilterMode.Point);
                        cmd.SetGlobalTexture(ShaderParams.SmoothnessMetallicRT, smootnessMetallicRT);

                        RenderTargetIdentifier rti = new RenderTargetIdentifier(ShaderParams.SmoothnessMetallicRT, 0, CubemapFace.Unknown, -1);
                        cmd.SetRenderTarget(rti);
                        cmd.ClearRenderTarget(true, true, Color.black);

                        cmd.DrawRendererList(passData.rendererListHandle);

                    });
                }
            }
#endif


            public override void FrameCleanup (CommandBuffer cmd) {
                if (cmd == null) return;
                cmd.ReleaseTemporaryRT(ShaderParams.SmoothnessMetallicRT);
            }

            public void CleanUp () {
                RTHandles.Release(smootnessMetallicRT);
            }
        }

        partial class SSRPass : ScriptableRenderPass {

            enum Pass {
                CopyExact = 0,
                SSRSurf = 1,
                Resolve = 2,
                BlurHoriz = 3,
                BlurVert = 4,
                Debug = 5,
                Combine = 6,
                CombineWithCompare = 7,
                GBuffPass = 8,
                Copy = 9,
                TemporalAccum = 10,
                DebugDepth = 11,
                DebugNormals = 12,
                CopyDepth = 13,
                DebugTransparentNormals = 14
            }

            const string SHINY_CBUFNAME = "Shiny SSR";
            const float GOLDEN_RATIO = 0.618033989f;
            ScriptableRenderer renderer;
            static Material mat, matExclude;
            Texture noiseTex;

            [NonSerialized]
            static public ShinyScreenSpaceRaytracedReflections settings;

            static ShinySSRR ssrFeature;
            static readonly Plane[] frustumPlanes = new Plane[6];
            const int MIP_COUNT = 5;
            static int[] rtPyramid;
            static readonly Dictionary<Camera, RenderTexture> prevs = new Dictionary<Camera, RenderTexture>();
            Texture2D metallicGradientTex, smoothnessGradientTex;

            class PassData {
                public CommandBuffer cmd;
                public Camera cam;
                public RenderTextureDescriptor sourceDesc;
#if UNITY_2022_2_OR_NEWER
                public RTHandle source;
#else
                public RenderTargetIdentifier source;
#endif
#if UNITY_2023_3_OR_NEWER
                public TextureHandle colorTexture, depthTexture, cameraNormalsTexture, motionVectorTexture;
                public TextureHandle gBuffer0, gBuffer1, gBuffer2;
#endif
            }

            readonly PassData passData = new PassData();

            public bool Setup (ScriptableRenderer renderer, ShinySSRR ssrFeature, bool isSceneView) {

                settings = VolumeManager.instance.stack.GetComponent<ShinyScreenSpaceRaytracedReflections>();
                if (settings == null || !settings.IsActive()) return false;

                if (isSceneView && !settings.showInSceneView.value) return false;

                SSRPass.ssrFeature = ssrFeature;

                this.renderer = renderer;
                this.renderPassEvent = ssrFeature.renderPassEvent;
                if (mat == null) {
                    Shader shader = Shader.Find("Hidden/Kronnect/SSR_URP");
                    mat = CoreUtils.CreateEngineMaterial(shader);
                }
                if (matExclude == null) {
                    Shader shader = Shader.Find("Hidden/Kronnect/SSR/Exclude");
                    if (shader != null) matExclude = CoreUtils.CreateEngineMaterial(shader);
                }
                if (noiseTex == null) {
                    noiseTex = Resources.Load<Texture>("SSR/blueNoiseSSR64");
                }
                mat.SetTexture(ShaderParams.NoiseTex, noiseTex);

                // set global settings
                mat.SetVector(ShaderParams.SSRSettings2, new Vector4(settings.jitter.value, settings.contactHardening.value + 0.0001f, settings.reflectionsMultiplier.value, 0f));
                mat.SetVector(ShaderParams.SSRSettings4, new Vector4(settings.separationPos.value, settings.reflectionsMinIntensity.value, settings.reflectionsMaxIntensity.value, settings.specularControl.value ? settings.specularSoftenPower.value: 1f));
                mat.SetVector(ShaderParams.SSRBlurStrength, new Vector4(settings.blurStrength.value.x, settings.blurStrength.value.y, settings.vignetteSize.value, settings.vignettePower.value));
                mat.SetVector(ShaderParams.SSRSettings5, new Vector4(settings.thicknessFine.value * settings.thickness.value, settings.smoothnessThreshold.value, settings.skyboxIntensity.value, settings.opaqueReflectionsBlending.value));
                float metallicBoost = settings.metallicBoost.value;
                mat.SetVector(ShaderParams.SSRSettings6, new Vector4(settings.nearCameraAttenuationStart.value, settings.nearCameraAttenuationRange.value, metallicBoost, settings.metallicBoostThreshold.value));
                if (settings.specularControl.value) {
                    mat.EnableKeyword(ShaderParams.SKW_DENOISE);
                }
                else {
                    mat.DisableKeyword(ShaderParams.SKW_DENOISE);
                }
                mat.SetFloat(ShaderParams.MinimumBlur, settings.minimumBlur.value);
                mat.SetFloat(ShaderParams.EdgeAwareEnabled, settings.edgeAwareBlur.value ? 1f : 0f);
                mat.SetInt(ShaderParams.StencilValue, settings.stencilValue.value);
                mat.SetInt(ShaderParams.StencilCompareFunction, settings.stencilCheck.value ? (int)settings.stencilCompareFunction.value : (int)CompareFunction.Always);

                mat.DisableKeyword(ShaderParams.SKW_METALLIC_BOOST);
                if (settings.reflectionStyle.value == ReflectionStyle.DarkReflections) {
                    mat.EnableKeyword(ShaderParams.SKW_DARK_REFLECTIONS);
                }
                else {
                    mat.DisableKeyword(ShaderParams.SKW_DARK_REFLECTIONS);
                    if (metallicBoost > 0 && ssrFeature.useDeferred) {
                        mat.EnableKeyword(ShaderParams.SKW_METALLIC_BOOST);
                    }
                }

                if (settings.computeBackFaces.value) {
                    Shader.EnableKeyword(ShaderParams.SKW_BACK_FACES);
                    Shader.SetGlobalFloat(ShaderParams.MinimumThickness, settings.thicknessMinimum.value);
                }
                else {
                    Shader.DisableKeyword(ShaderParams.SKW_BACK_FACES);
                }

                SkyboxReflectionMode skyboxMode = settings.skyboxReflectionMode.value;
                if (skyboxMode == SkyboxReflectionMode.VisibleSkyOnly && settings.skyboxIntensity.value > 0) {
                    Shader.DisableKeyword(ShaderParams.SKW_SKYBOX);
                    Shader.EnableKeyword(ShaderParams.SKW_SKYBOX_VISIBLE_ONLY);
                }
                else if ((skyboxMode == SkyboxReflectionMode.Cubemap || skyboxMode == SkyboxReflectionMode.CustomCubemap) && settings.skyboxIntensity.value > 0) {
                    Shader.EnableKeyword(ShaderParams.SKW_SKYBOX);
                    Shader.DisableKeyword(ShaderParams.SKW_SKYBOX_VISIBLE_ONLY);
                }
                else {
                    Shader.DisableKeyword(ShaderParams.SKW_SKYBOX);
                    Shader.DisableKeyword(ShaderParams.SKW_SKYBOX_VISIBLE_ONLY);
                }

                if (ssrFeature.useDeferred || isSmoothnessMetallicPassActive) {
                    if (settings.jitter.value > 0) {
                        mat.EnableKeyword(ShaderParams.SKW_JITTER);
                    }
                    else {
                        mat.DisableKeyword(ShaderParams.SKW_JITTER);
                    }
                    if (settings.refineThickness.value) {
                        mat.EnableKeyword(ShaderParams.SKW_REFINE_THICKNESS);
                    }
                    else {
                        mat.DisableKeyword(ShaderParams.SKW_REFINE_THICKNESS);
                    }
                    if (isSmoothnessMetallicPassActive) {
                        mat.EnableKeyword(ShaderParams.SKW_CUSTOM_SMOOTHNESS_METALLIC_PASS);
                    }
                    else {
                        mat.DisableKeyword(ShaderParams.SKW_CUSTOM_SMOOTHNESS_METALLIC_PASS);
                    }
                    mat.SetVector(ShaderParams.SSRSettings, new Vector4(settings.thickness.value, settings.sampleCount.value, settings.binarySearchIterations.value, settings.maxRayLength.value));
                    mat.SetFloat(ShaderParams.TransparentSmoothness, settings.transparentSmoothness.value);
                }

                // needed for skybox solving (fresnel and fuzyness are also used in solve pass in any rendering path)
                mat.SetVector(ShaderParams.MaterialData, new Vector4(0, settings.fresnel.value, settings.fuzzyness.value + 1f, settings.decay.value));

                if (settings.reflectionsWorkflow.value == ReflectionsWorkflow.MetallicAndSmoothness) {
                    mat.EnableKeyword(ShaderParams.SKW_METALLIC_WORKFLOW);
                    UpdateGradientTextures();
                }
                else {
                    mat.DisableKeyword(ShaderParams.SKW_METALLIC_WORKFLOW);
                }

                if (rtPyramid == null || rtPyramid.Length != MIP_COUNT) {
                    rtPyramid = new int[MIP_COUNT];
                    for (int k = 0; k < rtPyramid.Length; k++) {
                        rtPyramid[k] = Shader.PropertyToID("_BlurRTMip" + k);
                    }
                }

                return true;
            }


            void UpdateGradientTextures () {
                UpdateGradientTexture(ref metallicGradientTex, settings.reflectionsIntensityCurve.value, ref ShinyScreenSpaceRaytracedReflections.metallicGradientCachedId);
                UpdateGradientTexture(ref smoothnessGradientTex, settings.reflectionsSmoothnessCurve.value, ref ShinyScreenSpaceRaytracedReflections.smoothnessGradientCachedId);
                Shader.SetGlobalTexture(ShaderParams.MetallicGradientTex, metallicGradientTex);
                Shader.SetGlobalTexture(ShaderParams.SmoothnessGradientTex, smoothnessGradientTex);
            }

            Color[] colors;
            void UpdateGradientTexture (ref Texture2D tex, AnimationCurve curve, ref float cachedId) {
                if (colors == null || colors.Length != 256) {
                    colors = new Color[256];
                    cachedId = -1;
                }
                if (tex == null) {
                    tex = new Texture2D(256, 1, TextureFormat.RHalf, false, true);
                    cachedId = -1;
                }
                // quick test, evaluate 3 curve points
                float sum = curve.Evaluate(0) + curve.Evaluate(0.5f) + curve.Evaluate(1f) + 1;
                if (sum == cachedId) return;
                cachedId = sum;

                for (int k = 0; k < 256; k++) {
                    float t = (float)k / 255;
                    float v = curve.Evaluate(t);
                    colors[k].r = v;
                }

                tex.SetPixels(colors);
                tex.Apply();
            }


            ScriptableRenderPassInput GetRequiredInputs () {
                ScriptableRenderPassInput inputs = ScriptableRenderPassInput.Depth;
                if (isUsingScreenSpaceNormals || isSmoothnessMetallicPassActive) {
                    inputs |= ScriptableRenderPassInput.Normal;
                }
#if UNITY_2021_3_OR_NEWER
                if (settings.temporalFilter.value && Application.isPlaying) {
                    inputs |= ScriptableRenderPassInput.Motion;
                }
#endif
                return inputs;
            }


#if UNITY_2023_3_OR_NEWER

            public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData) {

                using (var builder = renderGraph.AddUnsafePass<PassData>("Shiny SSR Pass", out var passData)) {

                    builder.AllowPassCulling(false);

                    ScriptableRenderPassInput inputs = GetRequiredInputs();
                    ConfigureInput(inputs);

                    UniversalCameraData cameraData = frameData.Get<UniversalCameraData>();
                    passData.cam = cameraData.camera;
                    passData.sourceDesc = cameraData.cameraTargetDescriptor;

                    UniversalResourceData resourceData = frameData.Get<UniversalResourceData>();

                    passData.colorTexture = resourceData.activeColorTexture;
                    builder.UseTexture(resourceData.activeColorTexture, AccessFlags.ReadWrite);
                    passData.depthTexture = resourceData.cameraDepthTexture;
                    builder.UseTexture(resourceData.cameraDepthTexture, AccessFlags.Read);

                    if ((inputs & ScriptableRenderPassInput.Normal) != 0) {
                        builder.UseTexture(resourceData.cameraNormalsTexture, AccessFlags.Read);
                        passData.cameraNormalsTexture = resourceData.cameraNormalsTexture;
                    }
                    if ((inputs & ScriptableRenderPassInput.Motion) != 0) {
                        builder.UseTexture(resourceData.motionVectorColor, AccessFlags.Read);
                        passData.motionVectorTexture = resourceData.motionVectorColor;
                    }
                    if (ssrFeature.useDeferred) {
                        if (resourceData.gBuffer[0].IsValid()) {
                        builder.UseTexture(resourceData.gBuffer[0]);
                        passData.gBuffer0 = resourceData.gBuffer[0];   
                    }
                    if (resourceData.gBuffer[1].IsValid()) {
                        builder.UseTexture(resourceData.gBuffer[1]);
                        passData.gBuffer1 = resourceData.gBuffer[1];   
                    }
                    if (resourceData.gBuffer[2].IsValid()) {
                        builder.UseTexture(resourceData.gBuffer[2]);
                        passData.gBuffer2 = resourceData.gBuffer[2];   
                    }
                    }

                    builder.SetRenderFunc((PassData passData, UnsafeGraphContext context) => {

                        if (passData.depthTexture.IsValid()) {
                            mat.SetTexture(ShaderParams.CameraDepthTexture, passData.depthTexture);
                        }
                        if (passData.cameraNormalsTexture.IsValid()) {
                            mat.SetTexture(ShaderParams.CameraNormalsTexture, passData.cameraNormalsTexture);
                        }
                        if (passData.motionVectorTexture.IsValid()) {
                            mat.SetTexture(ShaderParams.MotionVectorTexture, passData.motionVectorTexture);
                        }
                        if (ssrFeature.useDeferred) {
                            if (passData.gBuffer0.IsValid()) {
                            mat.SetTexture(ShaderParams.CameraGBuffer0, passData.gBuffer0);
                        }
                        if (passData.gBuffer1.IsValid()) {
                            mat.SetTexture(ShaderParams.CameraGBuffer1, passData.gBuffer1);
                        }
                        if (passData.gBuffer2.IsValid()) {
                            mat.SetTexture(ShaderParams.CameraGBuffer2, passData.gBuffer2);
                        }
                        }

                        passData.source = passData.colorTexture;
                        passData.cmd = CommandBufferHelpers.GetNativeCommandBuffer(context.cmd);
                        ExecuteInternal(passData);
                    });
                }
            }

#endif


            static void ExecuteInternal (PassData passData) {

                RenderTextureDescriptor sourceDesc = passData.sourceDesc;
                sourceDesc.colorFormat = settings.lowPrecision.value ? RenderTextureFormat.ARGB32 : RenderTextureFormat.ARGBHalf;
                sourceDesc.width /= settings.downsampling.value;
                sourceDesc.height /= settings.downsampling.value;
                sourceDesc.msaaSamples = 1;

                float goldenFactor = GOLDEN_RATIO;
                if (settings.animatedJitter.value) {
                    goldenFactor *= (Time.frameCount % 480);
                }
                Shader.SetGlobalVector(ShaderParams.SSRSettings3, new Vector4(sourceDesc.width, sourceDesc.height, goldenFactor, settings.depthBias.value));

                CommandBuffer cmd = passData.cmd;
                Camera cam = passData.cam;

                if (cam.orthographic) {
                    Shader.EnableKeyword(ShaderParams.SKW_ORTHO_SUPPORT);
                }
                else {
                    Shader.DisableKeyword(ShaderParams.SKW_ORTHO_SUPPORT);
                }

                // Clamp far attenuation so start+range <= camera far clip - 1
                float farClip = cam != null ? cam.farClipPlane : 10000f;
                float maxSum = Mathf.Max(0f, farClip - 1f);
                float farStart = Mathf.Clamp(settings.farCameraAttenuationStart.value, 0f, maxSum);
                float farRange = Mathf.Clamp(settings.farCameraAttenuationRange.value, 0f, maxSum - farStart);
                float vignetteRadial = settings.vignetteMode.value == VignetteMode.Radial ? 1f : 0f;
                cmd.SetGlobalVector(ShaderParams.SSRSettings7, new Vector4(farStart, farRange, settings.hdrClamp.value, vignetteRadial));

#if UNITY_2022_2_OR_NEWER
                RTHandle source = passData.source;
#else
                RenderTargetIdentifier source = passData.source;
#endif
                bool depthReady = false;

                bool useReflectionsScripts = true;
                bool blendOpaqueReflections = settings.opaqueReflectionsBlending.value > 0;
                int count = Reflections.instances.Count;

                bool usingDeferredPass = ssrFeature.useDeferred && !settings.skipDeferredPass.value;
                SkyboxContributionPass skyboxContribution = isSmoothnessMetallicPassActive ? SkyboxContributionPass.Deferred : settings.skyboxContributionPass.value;
                if (usingDeferredPass || isSmoothnessMetallicPassActive) {
                    useReflectionsScripts = settings.useReflectionsScripts.value && !isSmoothnessMetallicPassActive;

                    if (!depthReady) { ComputeDepth(cmd, sourceDesc, cam); depthReady = true; }

                    // pass camera matrices
                    mat.SetMatrix(ShaderParams.WorldToViewDir, cam.worldToCameraMatrix);
                    mat.SetMatrix(ShaderParams.ViewToWorldDir, cam.cameraToWorldMatrix);

                    if ((settings.skyboxReflectionMode.value == SkyboxReflectionMode.Cubemap || settings.skyboxReflectionMode.value == SkyboxReflectionMode.CustomCubemap) &&
                        settings.skyboxIntensity.value > 0 &&
                        skyboxContribution == SkyboxContributionPass.Forward) {
                        cmd.DisableShaderKeyword(ShaderParams.SKW_SKYBOX);
                    }

                    if (settings.useCustomBounds.value) {
                        mat.EnableKeyword(ShaderParams.SKW_LIMIT_BOUNDS);
                        mat.SetVector(ShaderParams.BoundsMin, settings.boundsMin.value);
                        mat.SetVector(ShaderParams.BoundsSize, settings.boundsMax.value - settings.boundsMin.value);
                    }
                    else {
                        mat.DisableKeyword(ShaderParams.SKW_LIMIT_BOUNDS);
                    }

                    // prepare ssr target
                    cmd.GetTemporaryRT(ShaderParams.RayCast, sourceDesc, FilterMode.Point);
                    if (count > 0 && useReflectionsScripts) {
                        cmd.SetRenderTarget(ShaderParams.RayCast);
                        cmd.ClearRenderTarget(true, false, new Color(0, 0, 0, 0));
                    }

                    // raytrace using gbuffers
                    FullScreenBlit(cmd, source, ShaderParams.RayCast, Pass.GBuffPass);

                    // Resolve reflections for opaque
                    blendOpaqueReflections = blendOpaqueReflections && count > 0 && useReflectionsScripts && settings.reflectionStyle.value == ReflectionStyle.ShinyReflections;
                    if (blendOpaqueReflections) {
                    RenderTextureDescriptor copyDesc = sourceDesc;
                    copyDesc.depthBufferBits = 0;

                    if ((settings.skyboxReflectionMode.value == SkyboxReflectionMode.Cubemap || settings.skyboxReflectionMode.value == SkyboxReflectionMode.CustomCubemap) && settings.skyboxIntensity.value > 0) {
                        mat.SetVector(ShaderParams.CameraViewDir, cam.transform.forward);
                    }

                    cmd.GetTemporaryRT(ShaderParams.ReflectionsOpaqueTex, copyDesc);
                        FullScreenBlit(cmd, source, ShaderParams.ReflectionsOpaqueTex, Pass.Resolve); 
                        cmd.SetRenderTarget(ShaderParams.RayCast, 0, CubemapFace.Unknown, -1);
                    }
                }

                // early exit if no reflection objects
                if (count == 0 && !usingDeferredPass && !isSmoothnessMetallicPassActive) return;

                if (count > 0 && useReflectionsScripts) {
                    bool firstSSR = !usingDeferredPass;

                    if ((settings.skyboxReflectionMode.value == SkyboxReflectionMode.Cubemap || settings.skyboxReflectionMode.value == SkyboxReflectionMode.CustomCubemap) &&
                        settings.skyboxIntensity.value > 0 &&
                        skyboxContribution == SkyboxContributionPass.Deferred) {
                        cmd.DisableShaderKeyword(ShaderParams.SKW_SKYBOX);
                    }

                    GeometryUtility.CalculateFrustumPlanes(cam, frustumPlanes);
                    int reflectionsScriptsLayerMask = settings.reflectionsScriptsLayerMask.value;
                    float reflectionsScriptsMaxDistance = settings.farCameraAttenuationStart.value + settings.farCameraAttenuationRange.value;
                    Vector3 camPos = cam.transform.position;

                    for (int k = 0; k < count; k++) {
                        Reflections go = Reflections.instances[k];
                        if (go == null || (reflectionsScriptsLayerMask & (1 << go.gameObject.layer)) == 0) continue;
                        int rendererCount = go.ssrRenderers.Count;
                        for (int j = 0; j < rendererCount; j++) {
                            Reflections.SSR_Renderer ssrRenderer = go.ssrRenderers[j];

                            Renderer goRenderer = ssrRenderer.renderer;
                            if (goRenderer == null || !goRenderer.isVisible) continue;

                            Vector3 goPos = goRenderer.bounds.center;
                            Vector3 rendererSize = goRenderer.bounds.extents;
                            float maxSize = Mathf.Max(rendererSize.x, rendererSize.y);
                            maxSize  = Mathf.Max(maxSize, rendererSize.z);
                            float maxDist = reflectionsScriptsMaxDistance + maxSize;
                            if (Vector3.SqrMagnitude(goPos - camPos) > maxDist * maxDist) continue;
                                                        
                            if (Reflections.needUpdateMaterials) {
                                ssrRenderer.CheckMaterialChanges(mat);
                            }

                            // if object is part of static batch, check collider bounds (if existing)
                            if (goRenderer.isPartOfStaticBatch) {
                                if (ssrRenderer.hasStaticBounds) {
                                    // check artifically computed bounds
                                    if (!GeometryUtility.TestPlanesAABB(frustumPlanes, ssrRenderer.staticBounds)) continue;
                                }
                                else if (ssrRenderer.collider != null) {
                                    // check if object is visible by current camera using collider bounds
                                    if (!GeometryUtility.TestPlanesAABB(frustumPlanes, ssrRenderer.collider.bounds)) continue;
                                }
                            }
                            else {
                                // check if object is visible by current camera using renderer bounds
                                if (!GeometryUtility.TestPlanesAABB(frustumPlanes, goRenderer.bounds)) continue;
                            }

                            if (!ssrRenderer.isInitialized) {
                                ssrRenderer.Init(mat);
                                ssrRenderer.UpdateMaterialProperties(go, settings);
                            }
#if UNITY_EDITOR
                            else if (!Application.isPlaying) {
                                ssrRenderer.CheckMaterialChanges(mat);
                                ssrRenderer.UpdateMaterialProperties(go, settings);
                            }
                            else if (Reflections.currentEditingReflections == go) {
                                ssrRenderer.UpdateMaterialProperties(go, settings);
                            }
#endif
                            else if (Reflections.needUpdateMaterials) {
                                ssrRenderer.UpdateMaterialProperties(go, settings);
                            }

                            if (ssrRenderer.exclude) continue;

                            if (firstSSR) {
                                firstSSR = false;

                                if (!depthReady) { ComputeDepth(cmd, sourceDesc, cam); depthReady = true; }

                                // prepare ssr target
                                cmd.GetTemporaryRT(ShaderParams.RayCast, sourceDesc, FilterMode.Point);
                                cmd.SetRenderTarget(ShaderParams.RayCast, 0, CubemapFace.Unknown, -1);
                                cmd.ClearRenderTarget(true, true, new Color(0, 0, 0, 0));
                            }
                            for (int s = 0; s < ssrRenderer.ssrMaterials.Length; s++) {
                                if (go.subMeshMask <= 0 || ((1 << s) & go.subMeshMask) != 0) {
                                    Material ssrMat = ssrRenderer.ssrMaterials[s];
                                    cmd.DrawRenderer(goRenderer, ssrMat, s, (int)Pass.SSRSurf);
                                }
                            }
                        }
                    }

                    Reflections.needUpdateMaterials = false;

                    if (firstSSR) return;
                }


                // Erase excluded objects
                int countExcluded = excludedReflections.Count;
                for (int k = 0; k < countExcluded; k++) {
                    ExcludeReflections er = excludedReflections[k];
                    if (er == null || !er.enabled || er.renderers == null) continue;
                    int rCount = er.renderers.Length;
                    for (int j = 0; j < rCount; j++) {
                        Renderer r = er.renderers[j];
                        if (!r.isVisible) continue;
                        cmd.DrawRenderer(r, matExclude);
                    }
                }

                RenderTargetIdentifier reflectionsTex;
                if (settings.reflectionStyle.value == ReflectionStyle.ShinyReflections) {
                    // Resolve reflections
                    RenderTextureDescriptor copyDesc = sourceDesc;
                    copyDesc.depthBufferBits = 0;

                    if ((settings.skyboxReflectionMode.value == SkyboxReflectionMode.Cubemap || settings.skyboxReflectionMode.value == SkyboxReflectionMode.CustomCubemap) && settings.skyboxIntensity.value > 0) {
                        mat.SetVector(ShaderParams.CameraViewDir, cam.transform.forward);
                    }

                    if (blendOpaqueReflections) {
                        cmd.EnableShaderKeyword(ShaderParams.SKW_BLEND_REFLECTIONS);
                    }
                    cmd.GetTemporaryRT(ShaderParams.ReflectionsTex, copyDesc);
                    FullScreenBlit(cmd, source, ShaderParams.ReflectionsTex, Pass.Resolve);
                    if (blendOpaqueReflections) {
                        cmd.DisableShaderKeyword(ShaderParams.SKW_BLEND_REFLECTIONS);
                    }
                    
                    RenderTargetIdentifier input = ShaderParams.ReflectionsTex;

#if UNITY_2021_3_OR_NEWER
                    prevs.TryGetValue(cam, out RenderTexture prev);

                    if (settings.temporalFilter.value && cam.cameraType == CameraType.Game && Application.isPlaying) {

                        if (prev != null && (prev.width != copyDesc.width || prev.height != copyDesc.height)) {
                            prev.Release();
                            prev = null;
                        }

                        RenderTextureDescriptor acumDesc = copyDesc;
                        Pass acumPass = Pass.TemporalAccum;

                        if (prev == null) {
                            prev = new RenderTexture(acumDesc);
                            prev.Create();
                            prevs[cam] = prev;
                            acumPass = Pass.Copy;
                            cmd.SetRenderTarget(prev, 0, CubemapFace.Unknown, -1);
                            cmd.ClearRenderTarget(false, true, Color.clear);
                        }

                        mat.SetFloat(ShaderParams.TemporalResponseSpeed, settings.temporalFilterResponseSpeed.value);
                        Shader.SetGlobalTexture(ShaderParams.PrevResolveNameId, prev);
                        cmd.GetTemporaryRT(ShaderParams.TempAcum, acumDesc, FilterMode.Bilinear);
                        FullScreenBlit(cmd, ShaderParams.ReflectionsTex, ShaderParams.TempAcum, acumPass);
                        FullScreenBlit(cmd, ShaderParams.TempAcum, prev, Pass.Copy); // do not use CopyExact as its fragment clamps color values - also, cmd.CopyTexture does not work correctly here
                        input = ShaderParams.TempAcum;
                    }
                    else if (prev != null) {
                        prev.Release();
                        DestroyImmediate(prev);
                    }
#endif
                    reflectionsTex = input;

                    // Pyramid blur
                    int blurDownsampling = settings.blurDownsampling.value;
                    copyDesc.width /= blurDownsampling;
                    copyDesc.height /= blurDownsampling;
                    for (int k = 0; k < MIP_COUNT; k++) {
                        copyDesc.width = Mathf.Max(2, copyDesc.width / 2);
                        copyDesc.height = Mathf.Max(2, copyDesc.height / 2);
                        cmd.GetTemporaryRT(rtPyramid[k], copyDesc, FilterMode.Bilinear);
                        cmd.GetTemporaryRT(ShaderParams.BlurRT, copyDesc, FilterMode.Bilinear);
                        FullScreenBlit(cmd, input, ShaderParams.BlurRT, Pass.BlurHoriz);
                        FullScreenBlit(cmd, ShaderParams.BlurRT, rtPyramid[k], Pass.BlurVert);
                        cmd.ReleaseTemporaryRT(ShaderParams.BlurRT);
                        input = rtPyramid[k];
                    }
                }
                else {
                    reflectionsTex = ShaderParams.RayCast;
                }

                // Output
                int finalPass;
                switch (settings.debugView.value) {
                    case DebugView.Final: finalPass = (int)Pass.Combine; break;
                    case DebugView.OnlyReflections: finalPass = (int)Pass.Debug; break;
                    case DebugView.SideBySideComparison: finalPass = (int)Pass.CombineWithCompare; break;
                    case DebugView.Depth: finalPass = (int)Pass.DebugDepth; break;
                    case DebugView.DeferredNormals: finalPass = (int)Pass.DebugNormals; break;
                    case DebugView.TransparentNormals: finalPass = (int)Pass.DebugTransparentNormals; break;
                    default:
                        finalPass = (int)Pass.Debug; break;
                }
                
                mat.SetFloat(ShaderParams.DebugDepthMultiplier, settings.debugDepthMultiplier.value);
                FullScreenBlit(cmd, reflectionsTex, source, (Pass)finalPass);

                if (settings.stopNaN.value) {
                    RenderTextureDescriptor nanDesc = passData.sourceDesc;
                    nanDesc.depthBufferBits = 0;
                    nanDesc.msaaSamples = 1;
                    cmd.GetTemporaryRT(ShaderParams.NaNBuffer, nanDesc);
                    FullScreenBlit(cmd, source, ShaderParams.NaNBuffer, Pass.CopyExact);
                    FullScreenBlit(cmd, ShaderParams.NaNBuffer, source, Pass.CopyExact);
                }

                // Clean up
                for (int k = 0; k < rtPyramid.Length; k++) {
                    cmd.ReleaseTemporaryRT(rtPyramid[k]);
                }
                cmd.ReleaseTemporaryRT(ShaderParams.ReflectionsTex);
                cmd.ReleaseTemporaryRT(ShaderParams.RayCast);
                cmd.ReleaseTemporaryRT(ShaderParams.DownscaledDepthRT);
            }

            static void ComputeDepth (CommandBuffer cmd, RenderTextureDescriptor desc, Camera cam) {
                bool useFullPrecision = settings.depthPrecision.value == DepthPrecision.Full || cam.orthographic;
                if (settings.computeBackFaces.value) {
                    desc.colorFormat = useFullPrecision ? RenderTextureFormat.RGFloat : RenderTextureFormat.RGHalf;
                } else {
                    desc.colorFormat = useFullPrecision ? RenderTextureFormat.RFloat : RenderTextureFormat.RHalf;
                }
                desc.sRGB = false;
                desc.depthBufferBits = 0;
                cmd.GetTemporaryRT(ShaderParams.DownscaledDepthRT, desc, FilterMode.Point);
                FullScreenBlit(cmd, ShaderParams.DownscaledDepthRT, Pass.CopyDepth);
            }


            static Mesh _fullScreenMesh;

            static Mesh fullscreenMesh {
                get {
                    if (_fullScreenMesh != null) {
                        return _fullScreenMesh;
                    }
                    float num = 1f;
                    float num2 = 0f;
                    Mesh val = new Mesh();
                    _fullScreenMesh = val;
                    _fullScreenMesh.SetVertices(new List<Vector3> {
            new Vector3 (-1f, -1f, 0f),
            new Vector3 (-1f, 1f, 0f),
            new Vector3 (1f, -1f, 0f),
            new Vector3 (1f, 1f, 0f)
        });
                    _fullScreenMesh.SetUVs(0, new List<Vector2> {
            new Vector2 (0f, num2),
            new Vector2 (0f, num),
            new Vector2 (1f, num2),
            new Vector2 (1f, num)
        });
                    _fullScreenMesh.SetIndices(new int[6] { 0, 1, 2, 2, 1, 3 }, (MeshTopology)0, 0, false);
                    _fullScreenMesh.UploadMeshData(true);
                    return _fullScreenMesh;
                }
            }

            static void FullScreenBlit (CommandBuffer cmd, RenderTargetIdentifier destination, Pass pass) {
                cmd.SetRenderTarget(destination, 0, CubemapFace.Unknown, -1);
                cmd.DrawMesh(fullscreenMesh, Matrix4x4.identity, mat, 0, (int)pass);
            }

            static void FullScreenBlit (CommandBuffer cmd, RenderTargetIdentifier source, RenderTargetIdentifier destination, Pass pass) {
                cmd.SetRenderTarget(destination, 0, CubemapFace.Unknown, -1);
                cmd.SetGlobalTexture(ShaderParams.MainTex, source);
                cmd.DrawMesh(fullscreenMesh, Matrix4x4.identity, mat, 0, (int)pass);
            }

            /// Cleanup any allocated resources that were created during the execution of this render pass.
            public override void FrameCleanup (CommandBuffer cmd) {
            }


            public void CleanUp () {
                CoreUtils.Destroy(mat);
                CoreUtils.Destroy(matExclude);
                if (prevs != null) {
                    foreach (RenderTexture rt in prevs.Values) {
                        if (rt != null) {
                            rt.Release();
                            DestroyImmediate(rt);
                        }
                    }
                    prevs.Clear();
                }
                CoreUtils.Destroy(metallicGradientTex);
                CoreUtils.Destroy(smoothnessGradientTex);
            }
        }

        partial class SSRBackfacesPass : ScriptableRenderPass {

            const string m_ProfilerTag = "Shiny SSR Backfaces Pass";
            const string m_DepthOnlyShader = "Hidden/Kronnect/SSR/DepthOnly";
            static readonly int _Cull = Shader.PropertyToID("_Cull");

            static FilteringSettings filterSettings;
            static readonly List<ShaderTagId> shaderTagIdList = new List<ShaderTagId>();
            Material depthOnlyMaterial;
            RTHandle m_Depth;
            ShinyScreenSpaceRaytracedReflections settings;

            public SSRBackfacesPass () {
                RenderTargetIdentifier rti = new RenderTargetIdentifier(ShaderParams.DownscaledBackDepthRT, 0, CubemapFace.Unknown, -1);
                m_Depth = RTHandles.Alloc(rti, name: ShaderParams.DownscaledBackDepthTextureName);
                shaderTagIdList.Clear();
                shaderTagIdList.Add(new ShaderTagId("SRPDefaultUnlit"));
                shaderTagIdList.Add(new ShaderTagId("UniversalForward"));
                shaderTagIdList.Add(new ShaderTagId("LightweightForward"));
                filterSettings = new FilteringSettings(RenderQueueRange.opaque, -1);
            }

            public void Setup (ShinyScreenSpaceRaytracedReflections settings) {
                this.renderPassEvent = RenderPassEvent.BeforeRenderingOpaques;
                this.settings = settings;
                filterSettings.layerMask = settings.computeBackFacesLayerMask.value;
            }

#if UNITY_2023_3_OR_NEWER
            class PassData {
                public RendererListHandle rendererListHandle;
                public RenderTextureDescriptor sourceDesc;
            }

            public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData) {

                using (var builder = renderGraph.AddUnsafePass<PassData>(m_ProfilerTag, out var passData)) {

                    builder.AllowPassCulling(false);

                    UniversalCameraData cameraData = frameData.Get<UniversalCameraData>();
                    UniversalLightData lightData = frameData.Get<UniversalLightData>();
                    UniversalRenderingData renderingData = frameData.Get<UniversalRenderingData>();
                    passData.sourceDesc = cameraData.cameraTargetDescriptor;

                    SortingCriteria sortingCriteria = SortingCriteria.CommonOpaque;
                    var drawingSettings = CreateDrawingSettings(shaderTagIdList, renderingData, cameraData, lightData, sortingCriteria);
                    drawingSettings.perObjectData = PerObjectData.None;
                    if (depthOnlyMaterial == null) {
                        Shader depthOnly = Shader.Find(m_DepthOnlyShader);
                        depthOnlyMaterial = new Material(depthOnly);
                        depthOnlyMaterial.SetInt(_Cull, (int)CullMode.Front);
                    }
                    drawingSettings.overrideMaterial = depthOnlyMaterial;
                    drawingSettings.overrideMaterialPassIndex = 0;

                    RendererListParams listParams = new RendererListParams(renderingData.cullResults, drawingSettings, filterSettings);
                    passData.rendererListHandle = renderGraph.CreateRendererList(listParams);
                    builder.UseRendererList(passData.rendererListHandle);

                    builder.SetRenderFunc((PassData passData, UnsafeGraphContext context) => {

                        CommandBuffer cmd = CommandBufferHelpers.GetNativeCommandBuffer(context.cmd);

                        RenderTextureDescriptor depthDesc = passData.sourceDesc;
                        int downsampling = settings.downsampling.value;
                        depthDesc.width = Mathf.CeilToInt(depthDesc.width / downsampling);
                        depthDesc.height = Mathf.CeilToInt(depthDesc.height / downsampling);
                        depthDesc.colorFormat = RenderTextureFormat.Depth;
                        depthDesc.depthBufferBits = 24;
                        depthDesc.msaaSamples = 1;

                        cmd.GetTemporaryRT(ShaderParams.DownscaledBackDepthRT, depthDesc, FilterMode.Point);
                        cmd.SetGlobalTexture(ShaderParams.DownscaledBackDepthRT, m_Depth);
                        RenderTargetIdentifier rti = new RenderTargetIdentifier(ShaderParams.DownscaledBackDepthRT, 0, CubemapFace.Unknown, -1);
                        cmd.SetRenderTarget(rti);
                        cmd.ClearRenderTarget(true, true, Color.black);

                        cmd.DrawRendererList(passData.rendererListHandle);
                    });
                }
            }

#endif

            public override void FrameCleanup (CommandBuffer cmd) {
                if (cmd == null) return;
                cmd.ReleaseTemporaryRT(ShaderParams.DownscaledBackDepthRT);
            }


            public void CleanUp () {
                RTHandles.Release(m_Depth);
                CoreUtils.Destroy(depthOnlyMaterial);
            }

        }

        public partial class DepthRenderPass : ScriptableRenderPass {

            static class ShaderParams {
                public const string CustomDepthTextureName = "_SSRCustomDepthTexture";
                public static int CustomDepthTexture = Shader.PropertyToID(CustomDepthTextureName);
                public static int CustomDepthAlphaCutoff = Shader.PropertyToID("_AlphaCutOff");
                public static int CustomDepthBaseMap = Shader.PropertyToID("_BaseMap");

                public const string SKW_DEPTH_PREPASS = "SSR_DEPTH_PREPASS";
            }

            const string m_ProfilerTag = "SSR Custom Depth PrePass";
            const string m_DepthOnlyShader = "Hidden/Kronnect/SSR/DepthOnly";

            public static int transparentLayerMask;

            static FilteringSettings filterSettings;
            static readonly List<ShaderTagId> shaderTagIdList = new List<ShaderTagId>();
            static readonly int cullPropertyId = Shader.PropertyToID("_Cull");

            RTHandle m_Depth;
            static Material depthOnlyMaterial;

            const bool useOptimizedDepthOnlyShader = true;

            public DepthRenderPass () {
                RenderTargetIdentifier rti = new RenderTargetIdentifier(ShaderParams.CustomDepthTexture, 0, CubemapFace.Unknown, -1);
                m_Depth = RTHandles.Alloc(rti, name: ShaderParams.CustomDepthTextureName);
                shaderTagIdList.Clear();
                shaderTagIdList.Add(new ShaderTagId("SRPDefaultUnlit"));
                shaderTagIdList.Add(new ShaderTagId("UniversalForward"));
                shaderTagIdList.Add(new ShaderTagId("LightweightForward"));
                filterSettings = new FilteringSettings(RenderQueueRange.transparent, 0);
            }

            /// <summary>
            /// Creates the shared transparent-depth override as a double-sided material because Spine horizontal facing reverses triangle winding while its visible material intentionally renders both sides.
            /// </summary>
            static Material GetOrCreateDepthOnlyMaterial () {
                if (depthOnlyMaterial == null) {
                    Shader depthOnly = Shader.Find(m_DepthOnlyShader);
                    depthOnlyMaterial = new Material(depthOnly);
                    depthOnlyMaterial.SetInt(cullPropertyId, (int)CullMode.Off);
                }
                return depthOnlyMaterial;
            }

            
            public void Setup (int transparentLayerMask) {
                DepthRenderPass.transparentLayerMask = transparentLayerMask;
                if (transparentLayerMask != 0) {
                    Shader.EnableKeyword(ShaderParams.SKW_DEPTH_PREPASS);
                }
                else {
                    Shader.DisableKeyword(ShaderParams.SKW_DEPTH_PREPASS);
                }
            }


#if UNITY_2023_3_OR_NEWER

            class PassData {
                public RendererListHandle rendererListHandle;
                public UniversalCameraData cameraData;
            }

            public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData) {

                using (var builder = renderGraph.AddUnsafePass<PassData>(m_ProfilerTag, out var passData)) {

                    builder.AllowPassCulling(false);

                    UniversalResourceData resourceData = frameData.Get<UniversalResourceData>();
                    UniversalRenderingData renderingData = frameData.Get<UniversalRenderingData>();
                    UniversalLightData lightData = frameData.Get<UniversalLightData>();
                    UniversalCameraData cameraData = frameData.Get<UniversalCameraData>();
                    passData.cameraData = cameraData;

                    SortingCriteria sortingCriteria = SortingCriteria.CommonTransparent;
                    var drawingSettings = CreateDrawingSettings(shaderTagIdList, renderingData, cameraData, lightData, sortingCriteria);
                    drawingSettings.perObjectData = PerObjectData.None;
                    if (useOptimizedDepthOnlyShader) {
                        drawingSettings.overrideMaterial = GetOrCreateDepthOnlyMaterial();
                    }
                    
                    if (transparentLayerMask != filterSettings.layerMask) {
                        filterSettings = new FilteringSettings(RenderQueueRange.transparent, transparentLayerMask);
                    }

                    RendererListParams listParams = new RendererListParams(renderingData.cullResults, drawingSettings, filterSettings);
                    passData.rendererListHandle = renderGraph.CreateRendererList(listParams);
                    builder.UseRendererList(passData.rendererListHandle);

                    builder.SetRenderFunc((PassData passData, UnsafeGraphContext context) => {

                        CommandBuffer cmd = CommandBufferHelpers.GetNativeCommandBuffer(context.cmd);

                        RenderTextureDescriptor depthDesc = passData.cameraData.cameraTargetDescriptor;
                        depthDesc.colorFormat = RenderTextureFormat.Depth;
                        depthDesc.depthBufferBits = 24;
                        depthDesc.msaaSamples = 1;

                        cmd.GetTemporaryRT(ShaderParams.CustomDepthTexture, depthDesc, FilterMode.Point);
                        RenderTargetIdentifier rti = new RenderTargetIdentifier(ShaderParams.CustomDepthTexture, 0, CubemapFace.Unknown, -1);
                        cmd.SetRenderTarget(rti);
                        cmd.ClearRenderTarget(true, true, Color.black);

                        context.cmd.DrawRendererList(passData.rendererListHandle);

                        int transparentSupportCount = transparentSupport.Count;
                        for (int i = 0; i < transparentSupportCount; i++) {
                            ShinyTransparentSupport ts = transparentSupport[i];
                            Renderer[] renderers = ts.renderers;
                            if (renderers == null) continue;
                            int[] passIndices = ts.depthPassIndices;
                            int renderersLength = renderers.Length;
                            for (int r = 0; r < renderersLength; r++) {
                                Renderer renderer = renderers[r];
                                if (renderer == null) continue;
                                int passIndex = passIndices != null && r < passIndices.Length ? passIndices[r] : -1;
                                if (passIndex >= 0) {
                                    cmd.DrawRenderer(renderer, renderer.sharedMaterial, 0, passIndex);
                                } else {
                                    cmd.DrawRenderer(renderer, depthOnlyMaterial);
                                }
                            }
                        }


                    });
                }
            }

#endif
        }

        public partial class TransparentNormalRenderPass : ScriptableRenderPass {

            const string m_ProfilerTag = "SSR Transparent Normal PrePass";

            public static int transparentLayerMask;

            static FilteringSettings filterSettings;
            static readonly List<ShaderTagId> shaderTagIdList = new List<ShaderTagId>();

            RTHandle m_Normals;

            public TransparentNormalRenderPass () {
                RenderTargetIdentifier rti = new RenderTargetIdentifier(ShaderParams.TransparentNormalsRT, 0, CubemapFace.Unknown, -1);
                m_Normals = RTHandles.Alloc(rti, name: ShaderParams.TransparentNormalsRTName);
                shaderTagIdList.Clear();
                shaderTagIdList.Add(new ShaderTagId("DepthNormals"));
                shaderTagIdList.Add(new ShaderTagId("DepthNormalsOnly"));
                filterSettings = new FilteringSettings(RenderQueueRange.transparent, 0);
            }

            public void Setup (int transparentLayerMask) {
                TransparentNormalRenderPass.transparentLayerMask = transparentLayerMask;
                if (transparentLayerMask != 0) {
                    Shader.EnableKeyword(ShaderParams.SKW_TRANSPARENT_NORMAL_PREPASS);
                }
                else {
                    Shader.DisableKeyword(ShaderParams.SKW_TRANSPARENT_NORMAL_PREPASS);
                }
            }

#if UNITY_2023_3_OR_NEWER

            class PassData {
                public RendererListHandle rendererListHandle;
                public UniversalCameraData cameraData;
                public TextureHandle cameraDepth;
            }

            public override void RecordRenderGraph (RenderGraph renderGraph, ContextContainer frameData) {

                using (var builder = renderGraph.AddUnsafePass<PassData>(m_ProfilerTag, out var passData)) {

                    builder.AllowPassCulling(false);

                    UniversalRenderingData renderingData = frameData.Get<UniversalRenderingData>();
                    UniversalLightData lightData = frameData.Get<UniversalLightData>();
                    UniversalCameraData cameraData = frameData.Get<UniversalCameraData>();
                    UniversalResourceData resourceData = frameData.Get<UniversalResourceData>();
                    passData.cameraData = cameraData;
                    passData.cameraDepth = resourceData.activeDepthTexture;
                    builder.UseTexture(resourceData.activeDepthTexture, AccessFlags.Read);

                    SortingCriteria sortingCriteria = SortingCriteria.CommonTransparent;
                    var drawingSettings = CreateDrawingSettings(shaderTagIdList, renderingData, cameraData, lightData, sortingCriteria);
                    drawingSettings.perObjectData = PerObjectData.None;

                    if (transparentLayerMask != filterSettings.layerMask) {
                        filterSettings = new FilteringSettings(RenderQueueRange.transparent, transparentLayerMask);
                    }

                    RendererListParams listParams = new RendererListParams(renderingData.cullResults, drawingSettings, filterSettings);
                    passData.rendererListHandle = renderGraph.CreateRendererList(listParams);
                    builder.UseRendererList(passData.rendererListHandle);

                    builder.SetRenderFunc((PassData passData, UnsafeGraphContext context) => {

                        CommandBuffer cmd = CommandBufferHelpers.GetNativeCommandBuffer(context.cmd);

                        RenderTextureDescriptor desc = passData.cameraData.cameraTargetDescriptor;
                        desc.colorFormat = RenderTextureFormat.ARGBHalf;
                        desc.depthBufferBits = 0;
                        desc.msaaSamples = 1;

                        cmd.GetTemporaryRT(ShaderParams.TransparentNormalsRT, desc, FilterMode.Point);
                        cmd.SetGlobalTexture(ShaderParams.TransparentNormalsRT, m_Normals);

                        RenderTargetIdentifier colorRti = new RenderTargetIdentifier(ShaderParams.TransparentNormalsRT, 0, CubemapFace.Unknown, -1);
                        cmd.SetRenderTarget(colorRti, passData.cameraDepth);
                        cmd.ClearRenderTarget(false, true, Color.black);

                        context.cmd.DrawRendererList(passData.rendererListHandle);
                    });
                }
            }

#endif

            public override void FrameCleanup (CommandBuffer cmd) { }

            public void CleanUp () {
                RTHandles.Release(m_Normals);
            }
        }

        public RenderPassEvent renderPassEvent = RenderPassEvent.AfterRenderingTransparents;
        [Tooltip("Use deferred g-buffers (requires deferred rendering in URP 12 or later)")]
        public bool useDeferred;

        [Tooltip("Master toggle for screen space normals. When OFF, no normals are requested. When ON, the sub-options below control which normals are captured.")]
        public bool enableScreenSpaceNormalsPass;

        [Tooltip("Forward only. Asks URP to generate the _CameraNormalsTexture via its DepthNormals prepass (opaque objects only). In deferred, opaque normals come from the GBuffer for free, so this is a no-op.")]
        public bool screenSpaceNormalsOpaques = true;

        [Tooltip("Renders transparent objects in the selected layer mask into a custom buffer (using their DepthNormals pass) so SSR can read screen space normals at transparent pixels. Works in both forward and deferred. Requires the materials to declare a 'DepthNormals' pass.")]
        public bool includeTransparentsInScreenSpaceNormals;

        [Tooltip("Layers considered transparent for the normals prepass.")]
        public LayerMask transparentNormalsLayerMask = 0;

        [Tooltip("Renders the depth of transparent objects in the selected layer mask into a custom buffer so SSR can ray-march against transparent surfaces. Required for most transparent setups (water, glass, etc).")]
        public bool enableTransparencyDepthPrepass;

        [Tooltip("Layers considered transparent for the depth prepass.")]
        public LayerMask transparencyDepthPrepassLayerMask = 0;

        [Tooltip("Forward only. Gathers per-pixel smoothness/metallic into a global _SmoothnessMetallicRT texture and uses it for reflections, instead of per-object Reflections scripts. See 'Pass Source' for who fills that texture.")]
        public bool customSmoothnessMetallicPass;

        public SmoothnessMetallicPassSource customSmoothnessMetallicPassSource = SmoothnessMetallicPassSource.Shiny;

        [Tooltip("Allows Shiny to be executed even if camera has Post Processing option disabled.")]
        public bool ignorePostProcessingOption = true;

        [Tooltip("On which cameras should Shiny effects be applied")]
        public LayerMask cameraLayerMask = -1;

        [Tooltip("Ignores reflection probes from executing this render feature")]
        public bool ignoreReflectionProbes = true;

        public static bool installed;
        public static bool isDeferredActive;
        public static bool isSmoothnessMetallicPassActive;
        public static bool isUsingScreenSpaceNormals;
        public static bool isEnabled = true;

        SSRPass renderPass;
        SSRBackfacesPass backfacesPass;
        SmoothnessMetallicPass smoothnessMetallicPass;
        DepthRenderPass depthRenderPass;
        TransparentNormalRenderPass transparentNormalRenderPass;
        SkyboxBaker skyboxBaker;

        void OnDestroy () {
            installed = false;
            if (renderPass != null) {
                renderPass.CleanUp();
            }
            if (backfacesPass != null) {
                backfacesPass.CleanUp();
            }
            if (smoothnessMetallicPass != null) {
                smoothnessMetallicPass.CleanUp();
            }
            if (transparentNormalRenderPass != null) {
                transparentNormalRenderPass.CleanUp();
            }
            if (skyboxBaker != null) {
                DestroyImmediate(skyboxBaker);
            }
        }

        public override void Create () {
            if (renderPass == null) {
                renderPass = new SSRPass();
            }
            if (backfacesPass == null) {
                backfacesPass = new SSRBackfacesPass();
            }
            if (smoothnessMetallicPass == null) {
                smoothnessMetallicPass = new SmoothnessMetallicPass();
                smoothnessMetallicPass.renderPassEvent = RenderPassEvent.BeforeRenderingOpaques;
            }
            if (depthRenderPass == null) {
                depthRenderPass = new DepthRenderPass();
            }
            if (transparentNormalRenderPass == null) {
                transparentNormalRenderPass = new TransparentNormalRenderPass();
                transparentNormalRenderPass.renderPassEvent = RenderPassEvent.BeforeRenderingTransparents;
            }
        }


        public override void AddRenderPasses (ScriptableRenderer renderer, ref RenderingData renderingData) {
            installed = true;
            if (!isEnabled) return;

            isDeferredActive = useDeferred;
            isSmoothnessMetallicPassActive = customSmoothnessMetallicPass && !useDeferred;
            isUsingScreenSpaceNormals = enableScreenSpaceNormalsPass && (screenSpaceNormalsOpaques || includeTransparentsInScreenSpaceNormals) && !useDeferred;

            Camera cam = renderingData.cameraData.camera;
            if ((cameraLayerMask.value & (1 << cam.gameObject.layer)) == 0) return;

            if (!renderingData.postProcessingEnabled && !ignorePostProcessingOption) return;

            if (ignoreReflectionProbes && cam.cameraType == CameraType.Reflection) return;

            if (renderingData.cameraData.cameraTargetDescriptor.colorFormat == RenderTextureFormat.Depth) return;

            if (renderPass.Setup(renderer, this, renderingData.cameraData.cameraType == CameraType.SceneView)) {
                if ((SSRPass.settings.skyboxReflectionMode.value == SkyboxReflectionMode.Cubemap || SSRPass.settings.skyboxReflectionMode.value == SkyboxReflectionMode.CustomCubemap) && SSRPass.settings.skyboxIntensity.value > 0) {
                    CheckSkyboxBaker();
                }
                if (isSmoothnessMetallicPassActive && customSmoothnessMetallicPassSource == SmoothnessMetallicPassSource.Shiny) {
                    renderer.EnqueuePass(smoothnessMetallicPass);
                }
                int depthLayerMask = ResolveTransparencyDepthLayerMask();
                if (depthLayerMask != 0) {
                    depthRenderPass.Setup(depthLayerMask);
                    renderer.EnqueuePass(depthRenderPass);
                } else {
                    depthRenderPass.Setup(0);
                }
                int normalsLayerMask = ResolveTransparentNormalsLayerMask();
                if (normalsLayerMask != 0) {
                    transparentNormalRenderPass.Setup(normalsLayerMask);
                    renderer.EnqueuePass(transparentNormalRenderPass);
                } else {
                    transparentNormalRenderPass.Setup(0);
                }
                Shader.SetGlobalFloat(ShaderParams.HasTransparentNormals, normalsLayerMask != 0 ? 1f : 0f);

                if (SSRPass.settings.computeBackFaces.value) {
                    backfacesPass.Setup(SSRPass.settings);
                    renderer.EnqueuePass(backfacesPass);
                }
                renderer.EnqueuePass(renderPass);
            }
        }

        void CheckSkyboxBaker () {
            if (skyboxBaker != null) return;

            skyboxBaker = Misc.FindObjectOfType<SkyboxBaker>(true);
            if (skyboxBaker != null) return;

            Camera cam = Camera.main;
            if (cam == null) return;

            skyboxBaker = cam.gameObject.AddComponent<SkyboxBaker>();
        }

        /// <summary>
        /// Performs a refresh of the Reflections script and the materials used
        /// </summary>
        public void Refresh () {
            Reflections.needUpdateMaterials = true;
        }

        // Migration fallback: the normals prepass and its layer mask used to live in the volume.
        // Render feature settings take precedence; volume values are honored if the render feature is unconfigured.
        static bool s_warnedNormalPrepassMigration;
        int ResolveTransparentNormalsLayerMask () {
            if (enableScreenSpaceNormalsPass && includeTransparentsInScreenSpaceNormals) {
                return transparentNormalsLayerMask.value;
            }
            return 0;
        }

        // Migration fallback: transparency depth prepass used to live in the volume.
        static bool s_warnedDepthPrepassMigration;
        int ResolveTransparencyDepthLayerMask () {
            if (enableTransparencyDepthPrepass) {
                return transparencyDepthPrepassLayerMask.value;
            }
#pragma warning disable CS0618
            var settings = SSRPass.settings;
            if (settings != null && settings.transparencySupport.value && settings.transparentDepthPrepass.value && settings.transparentLayerMask.value != 0) {
                if (!s_warnedDepthPrepassMigration) {
                    enableTransparencyDepthPrepass = true;
                    transparencyDepthPrepassLayerMask = settings.transparentLayerMask.value;
                    #if UNITY_EDITOR
                        UnityEditor.EditorUtility.SetDirty(this);
                        UnityEditor.EditorUtility.SetDirty(settings);
                    #endif
                    settings.transparencySupport.value = false;
                    Debug.LogWarning("[Shiny SSR] 'Transparency Support' has moved from the volume to the render feature. Please enable 'Depth Prepass' in the Shiny SSR Render Feature and set the layer mask there. The volume options still work for now and will be removed in a future release.");
                    s_warnedDepthPrepassMigration = true;
                }
                return settings.transparentLayerMask.value;
            }
#pragma warning restore CS0618
            return 0;
        }


        static readonly List<ExcludeReflections> excludedReflections = new List<ExcludeReflections>();

        public static void RegisterExcludeReflections (ExcludeReflections o) {
            if (!excludedReflections.Contains(o)) {
                excludedReflections.Add(o);
            }
        }

        public static void UnregisterExcludeReflections (ExcludeReflections o) {
            if (excludedReflections.Contains(o)) {
                excludedReflections.Remove(o);
            }
        }


        static readonly List<ShinyTransparentSupport> transparentSupport = new List<ShinyTransparentSupport>();

        public static void RegisterTransparentSupport (ShinyTransparentSupport o) {
            if (!transparentSupport.Contains(o)) {
                transparentSupport.Add(o);
            }
        }

        public static void UnregisterTransparentSupport (ShinyTransparentSupport o) {
            if (transparentSupport.Contains(o)) {
                transparentSupport.Remove(o);
            }
        }



    }

}
