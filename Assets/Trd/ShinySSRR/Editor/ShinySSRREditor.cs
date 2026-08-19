using UnityEngine;
using UnityEditor;
using UnityEditor.Rendering;
using System.Text;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace ShinySSRR {

#if UNITY_2022_2_OR_NEWER
    [CustomEditor(typeof(ShinyScreenSpaceRaytracedReflections))]
#else
    [VolumeComponentEditor(typeof(ShinyScreenSpaceRaytracedReflections))]
#endif
    public class ShinySSRREditor : VolumeComponentEditor {

        SerializedDataParameter reflectionStyle, reflectionsMultiplier, showInSceneView, reflectionsWorkflow;
        SerializedDataParameter reflectionsIntensityCurve, reflectionsSmoothnessCurve, smoothnessThreshold, reflectionsMinIntensity, reflectionsMaxIntensity, hdrClamp;
        SerializedDataParameter downsampling, depthBias, computeBackFaces, thicknessMinimum, computeBackFacesLayerMask;
        SerializedDataParameter debugView, separationPos, lowPrecision, stopNaN, debugDepthMultiplier, depthPrecision;
        SerializedDataParameter stencilCheck, stencilValue, stencilCompareFunction;
        SerializedDataParameter temporalFilter, temporalFilterResponseSpeed;
        SerializedDataParameter sampleCount, maxRayLength, thickness, binarySearchIterations, refineThickness, thicknessFine, decay, jitter, animatedJitter;
        SerializedDataParameter fresnel, fuzzyness, contactHardening, minimumBlur;
        SerializedDataParameter blurDownsampling, blurStrength, edgeAwareBlur, specularControl, specularSoftenPower, metallicBoost, metallicBoostThreshold;
        SerializedDataParameter skyboxReflectionMode, skyboxIntensity, skyboxResolution, skyboxUpdateMode, skyboxUpdateInterval, skyboxContributionPass, skyboxCustomCubemap;
        SerializedDataParameter skyboxHDR, skyboxCullingMask;
        SerializedDataParameter useCustomBounds, boundsMin, boundsMax;
        SerializedDataParameter vignetteSize, vignettePower, vignetteMode;
        SerializedDataParameter nearCameraAttenuationStart, nearCameraAttenuationRange, farCameraAttenuationStart, farCameraAttenuationRange;
        SerializedDataParameter useReflectionsScripts, reflectionsScriptsLayerMask, skipDeferredPass;
        SerializedDataParameter transparencySupport, transparentLayerMask, transparentDepthPrepass, transparentNormalPrepass, transparentSmoothness;
        SerializedDataParameter opaqueReflectionsBlending;

        Reflections[] reflections;
        public Texture bulbOnIcon, bulbOffIcon, deleteIcon, arrowRight;
        readonly StringBuilder sb = new StringBuilder();
        static GUIStyle sectionHeaderStyle;
        static readonly GUIContent sectionGeneralContent = new GUIContent("General Settings");
        static readonly GUIContent sectionQualityContent = new GUIContent("Quality Settings");
        static readonly GUIContent sectionIntensityContent = new GUIContent("Reflection Intensity");
        static readonly GUIContent sectionBlurContent = new GUIContent("Reflection Blurring");
        static readonly GUIContent sectionDebugContent = new GUIContent("Debug & Advanced");
        static readonly GUIContent sectionReflectionsContent = new GUIContent("Reflections Scripts");
        const string PrefPrefix = "ShinySSRR.Section.";
        const string PrefGeneral = PrefPrefix + "General";
        const string PrefQuality = PrefPrefix + "Quality";
        const string PrefIntensity = PrefPrefix + "Intensity";
        const string PrefBlur = PrefPrefix + "Blur";
        const string PrefDebug = PrefPrefix + "Debug";
        const string PrefReflections = PrefPrefix + "Reflections";
        static bool sectionGeneralExpanded = true;
        static bool sectionQualityExpanded = true;
        static bool sectionIntensityExpanded = true;
        static bool sectionBlurExpanded = true;
        static bool sectionDebugExpanded;
        static bool sectionReflectionsExpanded = true;

#if UNITY_6000_4_OR_NEWER
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() {
            sectionHeaderStyle = null;
            sectionGeneralExpanded = true;
            sectionQualityExpanded = true;
            sectionIntensityExpanded = true;
            sectionBlurExpanded = true;
            sectionDebugExpanded = false;
            sectionReflectionsExpanded = true;
        }
#endif

        static GUIStyle SectionHeaderStyle {
            get {
                if (sectionHeaderStyle == null) {
                    sectionHeaderStyle = new GUIStyle("ShurikenModuleTitle") {
                        font = EditorStyles.boldLabel.font,
                        border = new RectOffset(15, 7, 4, 4),
                        fixedHeight = 22f,
                        contentOffset = new Vector2(20f, -2f)
                    };
                }
                return sectionHeaderStyle;
            }
        }

        static bool DrawSectionHeader(GUIContent title, bool expanded, string prefKey) {
            Rect rect = GUILayoutUtility.GetRect(16f, 22f, SectionHeaderStyle);
            GUI.Box(rect, title, SectionHeaderStyle);
            Rect toggleRect = new Rect(rect.x + 4f, rect.y + 3f, 13f, 13f);
            bool newExpanded = GUI.Toggle(toggleRect, expanded, GUIContent.none, EditorStyles.foldout);
            Event evt = Event.current;
            if (evt.type == EventType.MouseDown && rect.Contains(evt.mousePosition) && !toggleRect.Contains(evt.mousePosition)) {
                newExpanded = !newExpanded;
                evt.Use();
            }
            if (newExpanded != expanded) {
                EditorPrefs.SetBool(prefKey, newExpanded);
            }
            return newExpanded;
        }

        public override void OnEnable () {
            base.OnEnable();

            var o = new PropertyFetcher<ShinyScreenSpaceRaytracedReflections>(serializedObject);

            showInSceneView = Unpack(o.Find(x => x.showInSceneView));
            reflectionStyle = Unpack(o.Find(x => x.reflectionStyle));
            reflectionsMultiplier = Unpack(o.Find(x => x.reflectionsMultiplier));
            reflectionsWorkflow = Unpack(o.Find(x => x.reflectionsWorkflow));
            reflectionsIntensityCurve = Unpack(o.Find(x => x.reflectionsIntensityCurve));
            reflectionsSmoothnessCurve = Unpack(o.Find(x => x.reflectionsSmoothnessCurve));
            smoothnessThreshold = Unpack(o.Find(x => x.smoothnessThreshold));
            reflectionsMinIntensity = Unpack(o.Find(x => x.reflectionsMinIntensity));
            reflectionsMaxIntensity = Unpack(o.Find(x => x.reflectionsMaxIntensity));
            hdrClamp = Unpack(o.Find(x => x.hdrClamp));
            computeBackFaces = Unpack(o.Find(x => x.computeBackFaces));
            computeBackFacesLayerMask = Unpack(o.Find(x => x.computeBackFacesLayerMask));
            thicknessMinimum = Unpack(o.Find(x => x.thicknessMinimum));
            downsampling = Unpack(o.Find(x => x.downsampling));
            depthBias = Unpack(o.Find(x => x.depthBias));
            debugView = Unpack(o.Find(x => x.debugView));
            debugDepthMultiplier = Unpack(o.Find(x => x.debugDepthMultiplier));
            depthPrecision = Unpack(o.Find(x => x.depthPrecision));
            separationPos = Unpack(o.Find(x => x.separationPos));
            lowPrecision = Unpack(o.Find(x => x.lowPrecision));
            stopNaN = Unpack(o.Find(x => x.stopNaN));
            stencilCheck = Unpack(o.Find(x => x.stencilCheck));
            stencilValue = Unpack(o.Find(x => x.stencilValue));
            stencilCompareFunction = Unpack(o.Find(x => x.stencilCompareFunction));
            temporalFilter = Unpack(o.Find(x => x.temporalFilter));
            temporalFilterResponseSpeed = Unpack(o.Find(x => x.temporalFilterResponseSpeed));
            sampleCount = Unpack(o.Find(x => x.sampleCount));
            maxRayLength = Unpack(o.Find(x => x.maxRayLength));
            binarySearchIterations = Unpack(o.Find(x => x.binarySearchIterations));
            thickness = Unpack(o.Find(x => x.thickness));
            thicknessFine = Unpack(o.Find(x => x.thicknessFine));
            refineThickness = Unpack(o.Find(x => x.refineThickness));
            decay = Unpack(o.Find(x => x.decay));
            fresnel = Unpack(o.Find(x => x.fresnel));
            fuzzyness = Unpack(o.Find(x => x.fuzzyness));
            contactHardening = Unpack(o.Find(x => x.contactHardening));
            minimumBlur = Unpack(o.Find(x => x.minimumBlur));
            jitter = Unpack(o.Find(x => x.jitter));
            animatedJitter = Unpack(o.Find(x => x.animatedJitter));
            blurDownsampling = Unpack(o.Find(x => x.blurDownsampling));
            blurStrength = Unpack(o.Find(x => x.blurStrength));
            edgeAwareBlur = Unpack(o.Find(x => x.edgeAwareBlur));
            specularControl = Unpack(o.Find(x => x.specularControl));
            specularSoftenPower = Unpack(o.Find(x => x.specularSoftenPower));
            metallicBoost = Unpack(o.Find(x => x.metallicBoost));
            metallicBoostThreshold = Unpack(o.Find(x => x.metallicBoostThreshold));
            skyboxReflectionMode = Unpack(o.Find(x => x.skyboxReflectionMode));
            skyboxIntensity = Unpack(o.Find(x => x.skyboxIntensity));
            skyboxResolution = Unpack(o.Find(x => x.skyboxResolution));
            skyboxUpdateMode = Unpack(o.Find(x => x.skyboxUpdateMode));
            skyboxUpdateInterval = Unpack(o.Find(x => x.skyboxUpdateInterval));
            skyboxContributionPass = Unpack(o.Find(x => x.skyboxContributionPass));
            skyboxCustomCubemap = Unpack(o.Find(x => x.skyboxCustomCubemap));
            skyboxHDR = Unpack(o.Find(x => x.skyboxHDR));
            skyboxCullingMask = Unpack(o.Find(x => x.skyboxCullingMask));
            useCustomBounds = Unpack(o.Find(x => x.useCustomBounds));
            nearCameraAttenuationStart = Unpack(o.Find(x => x.nearCameraAttenuationStart));
            nearCameraAttenuationRange = Unpack(o.Find(x => x.nearCameraAttenuationRange));
            farCameraAttenuationStart = Unpack(o.Find(x => x.farCameraAttenuationStart));
            farCameraAttenuationRange = Unpack(o.Find(x => x.farCameraAttenuationRange));
            boundsMin = Unpack(o.Find(x => x.boundsMin));
            boundsMax = Unpack(o.Find(x => x.boundsMax));
            vignetteSize = Unpack(o.Find(x => x.vignetteSize));
            vignettePower = Unpack(o.Find(x => x.vignettePower));
            vignetteMode = Unpack(o.Find(x => x.vignetteMode));
            useReflectionsScripts = Unpack(o.Find(x => x.useReflectionsScripts));
            reflectionsScriptsLayerMask = Unpack(o.Find(x => x.reflectionsScriptsLayerMask));
            skipDeferredPass = Unpack(o.Find(x => x.skipDeferredPass));
#pragma warning disable CS0618
            transparencySupport = Unpack(o.Find(x => x.transparencySupport));
            transparentLayerMask = Unpack(o.Find(x => x.transparentLayerMask));
            transparentDepthPrepass = Unpack(o.Find(x => x.transparentDepthPrepass));
#pragma warning restore CS0618
            transparentSmoothness = Unpack(o.Find(x => x.transparentSmoothness));
            opaqueReflectionsBlending = Unpack(o.Find(x => x.opaqueReflectionsBlending));

            reflections = Misc.FindObjectsOfType<Reflections>(true);

            bulbOnIcon = Resources.Load<Texture>("bulbOn");
            bulbOffIcon = Resources.Load<Texture>("bulbOff");
            arrowRight = Resources.Load<Texture>("arrowRight");
            deleteIcon = Resources.Load<Texture>("delete");

            sectionGeneralExpanded = EditorPrefs.GetBool(PrefGeneral, sectionGeneralExpanded);
            sectionQualityExpanded = EditorPrefs.GetBool(PrefQuality, sectionQualityExpanded);
            sectionIntensityExpanded = EditorPrefs.GetBool(PrefIntensity, sectionIntensityExpanded);
            sectionBlurExpanded = EditorPrefs.GetBool(PrefBlur, sectionBlurExpanded);
            sectionDebugExpanded = EditorPrefs.GetBool(PrefDebug, sectionDebugExpanded);
            sectionReflectionsExpanded = EditorPrefs.GetBool(PrefReflections, sectionReflectionsExpanded);
        }

        public override void OnInspectorGUI () {

            UniversalRenderPipelineAsset pipe = GraphicsSettings.currentRenderPipeline as UniversalRenderPipelineAsset;
            if (pipe == null) {
                EditorGUILayout.HelpBox("Universal Rendering Pipeline asset is not set in 'Project Settings / Graphics or Quality' !", MessageType.Error);
                EditorGUILayout.Separator();
                GUI.enabled = false;
            }
            else if (!ShinySSRR.installed) {
                EditorGUILayout.HelpBox("Shiny SSR Render Feature must be added to the rendering pipeline renderer.", MessageType.Error);
                if (GUILayout.Button("Go to Universal Rendering Pipeline Asset")) {
                    Selection.activeObject = pipe;
                }
                EditorGUILayout.Separator();
                GUI.enabled = false;
            }

            if (pipe != null) {
                EditorGUILayout.BeginVertical(GUI.skin.box);
                if (GUILayout.Button("Show URP Renderer Settings")) {
                    var so = new SerializedObject(pipe);
                    var prop = so.FindProperty("m_RendererDataList");
                    if (prop != null && prop.arraySize > 0) {
                        var o = prop.GetArrayElementAtIndex(0);
                        if (o != null) {
                            Selection.SetActiveObjectWithContext(o.objectReferenceValue, null);
                            GUIUtility.ExitGUI();
                        }
                    }
                }
                EditorGUILayout.EndVertical();
            }


            serializedObject.Update();

            int reflectionsCount = reflections != null ? reflections.Length : 0;

            bool useShinyReflections = reflectionStyle.value.intValue == (int)ReflectionStyle.ShinyReflections;

            sectionGeneralExpanded = DrawSectionHeader(sectionGeneralContent, sectionGeneralExpanded, PrefGeneral);
            if (sectionGeneralExpanded) {
                using (new EditorGUILayout.VerticalScope()) {
                    PropertyField(reflectionStyle, new GUIContent("Reflections Style"));
                    useShinyReflections = reflectionStyle.value.intValue == (int)ReflectionStyle.ShinyReflections;
                    PropertyField(reflectionsWorkflow, new GUIContent("Workflow"));
                    PropertyField(showInSceneView);
                    if (ShinySSRR.isDeferredActive) {
                        PropertyField(useCustomBounds);
                        if (useCustomBounds.value.boolValue) {
                            EditorGUI.indentLevel++;
                            PropertyField(boundsMin);
                            PropertyField(boundsMax);
                            EditorGUI.indentLevel--;
                        }
                    }
                }
            }

            sectionQualityExpanded = DrawSectionHeader(sectionQualityContent, sectionQualityExpanded, PrefQuality);
            if (sectionQualityExpanded) {
                using (new EditorGUILayout.VerticalScope()) {
                    EditorGUILayout.BeginHorizontal();
                    EditorGUILayout.LabelField("Apply Preset:", GUILayout.Width(EditorGUIUtility.labelWidth));
                    ShinyScreenSpaceRaytracedReflections ssr = (ShinyScreenSpaceRaytracedReflections)target;
                    if (GUILayout.Button("Fast")) {
                        ssr.ApplyRaytracingPreset(RaytracingPreset.Fast);
                        EditorUtility.SetDirty(target);
                    }
                    if (GUILayout.Button("Medium")) {
                        ssr.ApplyRaytracingPreset(RaytracingPreset.Medium);
                        EditorUtility.SetDirty(target);
                    }
                    if (GUILayout.Button("High")) {
                        ssr.ApplyRaytracingPreset(RaytracingPreset.High);
                        EditorUtility.SetDirty(target);
                    }
                    if (GUILayout.Button("Superb")) {
                        ssr.ApplyRaytracingPreset(RaytracingPreset.Superb);
                        EditorUtility.SetDirty(target);
                    }
                    if (GUILayout.Button("Ultra")) {
                        ssr.ApplyRaytracingPreset(RaytracingPreset.Ultra);
                        EditorUtility.SetDirty(target);
                    }
                    EditorGUILayout.EndHorizontal();
                    PropertyField(sampleCount);
                    PropertyField(maxRayLength);
                    PropertyField(binarySearchIterations);
                    PropertyField(computeBackFaces);
                    if (computeBackFaces.value.boolValue) {
                        EditorGUI.indentLevel++;
                        PropertyField(thicknessMinimum, new GUIContent("Min Thickness"));
                        PropertyField(thickness, new GUIContent("Max Thickness"));
                        PropertyField(computeBackFacesLayerMask, new GUIContent("Layer Mask"));
                        EditorGUI.indentLevel--;
                    }
                    else {
                        PropertyField(thickness, new GUIContent("Max Thickness"));
                    }
                    PropertyField(refineThickness);
                    if (refineThickness.value.boolValue) {
                        EditorGUI.indentLevel++;
                        PropertyField(thicknessFine);
                        EditorGUI.indentLevel--;
                    }
                    PropertyField(jitter);
                    PropertyField(animatedJitter);
#if UNITY_2021_3_OR_NEWER
                    if (useShinyReflections) {
                        PropertyField(temporalFilter);
                        if (temporalFilter.value.boolValue) {
                            EditorGUI.indentLevel++;
                            PropertyField(temporalFilterResponseSpeed, new GUIContent("Response Speed"));
                            EditorGUI.indentLevel--;
                        }
                    }
#endif
                    PropertyField(downsampling);
                    PropertyField(depthBias);
                }
            }

            sectionIntensityExpanded = DrawSectionHeader(sectionIntensityContent, sectionIntensityExpanded, PrefIntensity);
            if (sectionIntensityExpanded) {
                using (new EditorGUILayout.VerticalScope()) {
                    PropertyField(reflectionsMultiplier, new GUIContent("Intensity"));
                    if (reflectionsWorkflow.value.intValue == (int)ReflectionsWorkflow.SmoothnessOnly) {
                        PropertyField(reflectionsMinIntensity, new GUIContent("Min Intensity"));
                        PropertyField(reflectionsMaxIntensity, new GUIContent("Max Intensity"));
                        PropertyField(smoothnessThreshold, new GUIContent("Smoothness Threshold", "Minimum smoothness to receive reflections"));
                    }
                    else {
                        PropertyField(reflectionsIntensityCurve, new GUIContent("Metallic Curve"));
                        PropertyField(reflectionsSmoothnessCurve, new GUIContent("Smoothness Curve"));
                    }
                    PropertyField(fresnel);
                    PropertyField(decay);
                    PropertyField(hdrClamp, new GUIContent("HDR Clamp", "Clamps HDR reflection values to prevent overly bright spots."));
                    if (useShinyReflections) {
                        PropertyField(metallicBoost);
                        if (metallicBoost.value.floatValue > 0) {
                            EditorGUI.indentLevel++;
                            PropertyField(metallicBoostThreshold, new GUIContent("Metallic Threshold"));
                            EditorGUI.indentLevel--;
                        }
                        PropertyField(specularControl);
                        if (specularControl.value.boolValue) {
                            EditorGUI.indentLevel++;
                            PropertyField(specularSoftenPower, new GUIContent("Soften Power"));
                            EditorGUI.indentLevel--;
                        }
                        PropertyField(skyboxReflectionMode, new GUIContent("Reflect Sky"));
                        if (skyboxReflectionMode.value.intValue != (int)SkyboxReflectionMode.Disabled) {
                            EditorGUI.indentLevel++;
                            PropertyField(skyboxIntensity, new GUIContent("Intensity"));
                            if (skyboxReflectionMode.value.intValue == (int)SkyboxReflectionMode.Cubemap) {
                                EditorGUILayout.HelpBox("Shiny bakes a cubemap at runtime from the camera position - no manual baking required. To bake from a fixed point instead or to reuse an existing Reflection Probe's cubemap, add a 'Shiny SSR Skybox Reflection Source' component to a GameObject in the scene.", MessageType.Info);
                            }
                            if (skyboxReflectionMode.value.intValue == (int)SkyboxReflectionMode.CustomCubemap && skyboxIntensity.value.floatValue > 0) {
                                PropertyField(skyboxCustomCubemap, new GUIContent("Cubemap"));
                            }
                            else if (skyboxReflectionMode.value.intValue == (int)SkyboxReflectionMode.Cubemap && skyboxIntensity.value.floatValue > 0) {
                                PropertyField(skyboxUpdateMode, new GUIContent("Update Mode"));
                                if (skyboxUpdateMode.value.intValue == (int)SkyboxUpdateMode.Interval) {
                                    PropertyField(skyboxUpdateInterval, new GUIContent("Interval"));
                                }
                                PropertyField(skyboxResolution, new GUIContent("Resolution"));
                                PropertyField(skyboxHDR, new GUIContent("HDR"));
                                PropertyField(skyboxCullingMask, new GUIContent("Culling Mask"));
                                if (skyboxCullingMask.value.intValue != 0) {
                                    EditorGUILayout.HelpBox("Culling Mask different from 0 should be used only if you need to capture special backgrounds such as mesh-based/dome skyboxes. Leave it to 0 to capture the default skybox set in Render Settings.", MessageType.Info);
                                }
                            }
#if UNITY_2021_3_OR_NEWER
                            if (skyboxReflectionMode.value.intValue == (int)SkyboxReflectionMode.Cubemap || skyboxReflectionMode.value.intValue == (int)SkyboxReflectionMode.CustomCubemap) {
                                PropertyField(skyboxContributionPass, new GUIContent("Contribution Pass"));
                            }
#endif
                            EditorGUI.indentLevel--;
                        }
                    }
                    PropertyField(nearCameraAttenuationStart, new GUIContent("Start Distance"));
                    if (nearCameraAttenuationStart.value.floatValue > 0) {
                        EditorGUI.indentLevel++;
                        PropertyField(nearCameraAttenuationRange, new GUIContent("Range"));
                        EditorGUI.indentLevel--;
                    }
                    PropertyField(farCameraAttenuationStart, new GUIContent("Far Fade Start Distance"));
                    EditorGUI.indentLevel++;
                    PropertyField(farCameraAttenuationRange, new GUIContent("Range"));
                    EditorGUI.indentLevel--;

                    PropertyField(vignetteMode);
                    PropertyField(vignetteSize);
                    PropertyField(vignettePower);
                }
            }

            sectionBlurExpanded = DrawSectionHeader(sectionBlurContent, sectionBlurExpanded, PrefBlur);
            if (sectionBlurExpanded) {
                using (new EditorGUILayout.VerticalScope()) {
                    PropertyField(fuzzyness, new GUIContent("Fuzziness"));
                    if (useShinyReflections) {
                        PropertyField(contactHardening);
                    }
                    PropertyField(minimumBlur);
                    if (useShinyReflections) {
                        PropertyField(blurDownsampling);
                        PropertyField(blurStrength);
                        PropertyField(edgeAwareBlur);
                    }
                }
            }

            sectionDebugExpanded = DrawSectionHeader(sectionDebugContent, sectionDebugExpanded, PrefDebug);
            if (sectionDebugExpanded) {
                using (new EditorGUILayout.VerticalScope()) {
                    PropertyField(debugView);
                    if (debugView.value.intValue == (int)DebugView.SideBySideComparison) {
                        EditorGUI.indentLevel++;
                        PropertyField(separationPos);
                        EditorGUI.indentLevel--;
                    }
                    if (debugView.value.intValue == (int)DebugView.Depth) {
                        EditorGUI.indentLevel++;
                        PropertyField(debugDepthMultiplier);
                        EditorGUI.indentLevel--;
                    }
                    PropertyField(depthPrecision, new GUIContent("Depth Precision", "Set Half for improved performance. Full for better accuracy (used always with orthographic cameras)."));
                    PropertyField(lowPrecision, new GUIContent("Low Precision", "Uses low precision color buffer for reflections instead of HDR for performance"));
                    PropertyField(stopNaN, new GUIContent("Stop NaN"));
                    PropertyField(stencilCheck);
                    if (stencilCheck.value.boolValue) {
                        EditorGUI.indentLevel++;
                        PropertyField(stencilValue, new GUIContent("Value"));
                        PropertyField(stencilCompareFunction, new GUIContent("Compare Funciton"));
                        EditorGUI.indentLevel--;
                    }
                    PropertyField(transparentSmoothness, new GUIContent("Transparent Smoothness", "Default smoothness applied to reflections at transparent pixels driven by the normal prepass."));
                    PropertyField(transparencySupport, new GUIContent("Transparency Support"));
                    if (transparencySupport.value.boolValue) {
                        EditorGUILayout.HelpBox("Transparency Support and the Normal Prepass have moved to the Shiny SSR Render Feature. This toggle is just informative and will be removed in future releases.", MessageType.Warning);
                    }
                }
            }

            sectionReflectionsExpanded = DrawSectionHeader(sectionReflectionsContent, sectionReflectionsExpanded, PrefReflections);
            if (sectionReflectionsExpanded) {
                using (new EditorGUILayout.VerticalScope()) {
                    if (reflectionsCount > 0) {
                        if (!ShinySSRR.isDeferredActive) {
                            EditorGUILayout.HelpBox("Some settings may be overridden by Reflections scripts on specific objects.", MessageType.Info);
                        }
                        EditorGUILayout.LabelField("Reflections scripts in Scene", EditorStyles.helpBox);
                        if (ShinySSRR.isSmoothnessMetallicPassActive) {
                            EditorGUILayout.HelpBox("When 'Custom Smoothness Metallic Pass' option is enabled, Reflections scripts are not used.", MessageType.Info);
                        }
                        else if (ShinySSRR.isDeferredActive) {
                            EditorGUILayout.HelpBox("In deferred mode, you don't need to use Reflections scripts. But they can be used to force adding custom reflections on transparent objects like puddles.", MessageType.Info);
                            PropertyField(useReflectionsScripts);
                            if (useReflectionsScripts.value.boolValue) {
                                PropertyField(skipDeferredPass);
                                PropertyField(opaqueReflectionsBlending);
                            }
                        }
                        PropertyField(reflectionsScriptsLayerMask, new GUIContent("Layer Mask", "Which reflections scripts can be used."));
                        int reflLayerMask = reflectionsScriptsLayerMask.value.intValue;
                        for (int k = 0; k < reflectionsCount; k++) {
                            Reflections refl = reflections[k];
                            if (refl == null) continue;
                            EditorGUILayout.BeginHorizontal();
                            GUI.enabled = refl.gameObject.activeInHierarchy;
                            if (GUILayout.Button(new GUIContent(refl.enabled ? bulbOnIcon : bulbOffIcon, "Toggle on/off this reflection"), EditorStyles.miniButton, GUILayout.Width(35))) {
                                refl.enabled = !refl.enabled;
                            }
                            GUI.enabled = true;
                            if (GUILayout.Button(new GUIContent(deleteIcon, "Remove this reflection script"), EditorStyles.miniButton, GUILayout.Width(35))) {
                                if (EditorUtility.DisplayDialog("Confirmation", "Remove the reflection script on " + refl.gameObject.name + "?", "Ok", "Cancel")) {
                                    GameObject.DestroyImmediate(refl);
                                    reflections[k] = null;
                                    continue;
                                }
                            }
                            if (GUILayout.Button(new GUIContent(arrowRight, "Select this reflection script"), EditorStyles.miniButton, GUILayout.Width(35), GUILayout.Width(40))) {
                                Selection.activeObject = refl.gameObject;
                                EditorGUIUtility.PingObject(refl.gameObject);
                                GUIUtility.ExitGUI();
                            }
                            GUI.enabled = refl.isActiveAndEnabled;
                            sb.Clear();
                            sb.Append(refl.name);
                            if (!refl.gameObject.activeInHierarchy) {
                                sb.Append(" (hidden gameobject)");
                            }
                            if (refl.overrideGlobalSettings) {
                                sb.Append(" (uses custom settings)");
                            }
                            int layer = refl.gameObject.layer;
                            if ((reflLayerMask & (1 << layer)) == 0) {
                                sb.Append(" (not in layer mask)");
                            }
                            GUILayout.Label(sb.ToString());
                            GUI.enabled = true;
                            EditorGUILayout.EndHorizontal();
                        }
                    }
                    else if (reflectionsCount == 0) {
                        if (!ShinySSRR.isDeferredActive) {
                            EditorGUILayout.LabelField("Reflections in Scene", EditorStyles.helpBox);
                            EditorGUILayout.HelpBox("In forward rendering path, add a Reflections script to any object or group of objects that you want to get reflections.", MessageType.Info);
                        }

                    }
                }
            }

            if (serializedObject.ApplyModifiedProperties()) {
                Reflections.needUpdateMaterials = true; // / reflections scripts that do not override global settings need to be updated as well
            }

        }

    }
}