#if UNITY_EDITOR
using UnityEngine;
using UnityEditor;

namespace VLB
{
    public static class EditorMenuItems
    {
        const string kPrefix = "GameObject/Light/\U0001F4A1 Volumetric Light Beam/";

        static class SD
        {
            const string kNewBeamPrefix = kPrefix + "SD Beam/";

            [MenuItem(kNewBeamPrefix + "3D Beam", false, 100)]
            static void Menu_CreateNewBeam(MenuCommand menuCommand) { EditorExtensions.OnNewGameObjectCreated(EditorExtensions.SD.NewBeam(), menuCommand); }

            [MenuItem(kNewBeamPrefix + "3D Beam and Spotlight", false, 101)]
            static void Menu_CreateSpotLightAndBeam(MenuCommand menuCommand) { EditorExtensions.OnNewGameObjectCreated(EditorExtensions.SD.NewSpotLightAndBeam(), menuCommand); }

            [MenuItem(kNewBeamPrefix + "2D Beam", false, 102)]
            static void Menu_CreateNewBeam2D(MenuCommand menuCommand) { EditorExtensions.OnNewGameObjectCreated(EditorExtensions.SD.NewBeam2D(), menuCommand); }
        }

        static class HD
        {
            const string kNewBeamPrefix = kPrefix + "HD Beam/";

            [MenuItem(kNewBeamPrefix + "3D Beam", false, 200)]
            static void Menu_CreateNewBeam(MenuCommand menuCommand) { EditorExtensions.OnNewGameObjectCreated(EditorExtensions.HD.NewBeam(), menuCommand); }

            [MenuItem(kNewBeamPrefix + "3D Beam and Spotlight", false, 201)]
            static void Menu_CreateSpotLightAndBeam(MenuCommand menuCommand) { EditorExtensions.OnNewGameObjectCreated(EditorExtensions.HD.NewSpotLightAndBeam(), menuCommand); }

            [MenuItem(kNewBeamPrefix + "2D Beam", false, 202)]
            static void Menu_CreateNewBeam2D(MenuCommand menuCommand) { EditorExtensions.OnNewGameObjectCreated(EditorExtensions.HD.NewBeam2D(), menuCommand); }
        }



        const string kAddVolumetricBeam = "CONTEXT/Light/\U0001F4A1 Attach a Volumetric Beam ";
        static bool CanAddVolumetricBeam(Light light) { return !Application.isPlaying && light != null && light.type == LightType.Spot && light.GetComponent<VolumetricLightBeamAbstractBase>() == null; }

        static T Menu_AttachBeam_Command<T>(MenuCommand menuCommand) where T : VolumetricLightBeamAbstractBase
        {
            var light = menuCommand.context as Light;
            T comp = null;
            if (CanAddVolumetricBeam(light))
                comp = Undo.AddComponent<T>(light.gameObject);
            return comp;
        }

        [MenuItem(kAddVolumetricBeam + "SD", false)]
        static void Menu_AttachBeamSD_Command(MenuCommand menuCommand) { Menu_AttachBeam_Command<VolumetricLightBeamSD>(menuCommand); }

        [MenuItem(kAddVolumetricBeam + "HD", false)]
        static void Menu_AttachBeamHD_Command(MenuCommand menuCommand) {
            var beamHD = Menu_AttachBeam_Command<VolumetricLightBeamHD>(menuCommand);
            if (beamHD) beamHD.scalable = false;
        }

        [MenuItem(kAddVolumetricBeam + "SD", true)]
        [MenuItem(kAddVolumetricBeam + "HD", true)]
        static bool Menu_AttachBeam_Validate() { return CanAddVolumetricBeam(GetActiveLight()); }


        /////////////////////////////
        // DOCUMENTATION
        /////////////////////////////
        const string kDocumentationSuffix = "/\u2754 Documentation";

        [MenuItem("CONTEXT/" + VolumetricLightBeamSD.ClassName + kDocumentationSuffix)]
        static void Menu_BeamSD_Doc(MenuCommand menuCommand) { Application.OpenURL(Consts.Help.SD.UrlBeam); }

        [MenuItem("CONTEXT/" + VolumetricLightBeamHD.ClassName + kDocumentationSuffix)]
        static void Menu_BeamHD_Doc(MenuCommand menuCommand) { Application.OpenURL(Consts.Help.HD.UrlBeam); }

        [MenuItem("CONTEXT/" + VolumetricDustParticles.ClassName + kDocumentationSuffix)]
        static void Menu_DustParticles_Doc(MenuCommand menuCommand) { Application.OpenURL(Consts.Help.UrlDustParticles); }

        [MenuItem("CONTEXT/" + DynamicOcclusionRaycasting.ClassName + kDocumentationSuffix)]
        static void Menu_DynamicOcclusionRaycasting_Doc(MenuCommand menuCommand) { Application.OpenURL(Consts.Help.SD.UrlDynamicOcclusionRaycasting); }

        [MenuItem("CONTEXT/" + DynamicOcclusionDepthBuffer.ClassName + kDocumentationSuffix)]
        static void Menu_DynamicOcclusionDepthBuffer_Doc(MenuCommand menuCommand) { Application.OpenURL(Consts.Help.SD.UrlDynamicOcclusionDepthBuffer); }

        [MenuItem("CONTEXT/" + SkewingHandleSD.ClassName + kDocumentationSuffix)]
        static void Menu_SkewingHandle_Doc(MenuCommand menuCommand) { Application.OpenURL(Consts.Help.SD.UrlSkewingHandle); }

        [MenuItem("CONTEXT/" + TriggerZone.ClassName + kDocumentationSuffix)]
        static void Menu_TriggerZone_Doc(MenuCommand menuCommand) { Application.OpenURL(Consts.Help.UrlTriggerZone); }

        [MenuItem("CONTEXT/" + EffectFlicker.ClassName + kDocumentationSuffix)]
        static void Menu_EffectFlicker_Doc(MenuCommand menuCommand) { Application.OpenURL(Consts.Help.UrlEffectFlicker); }

        [MenuItem("CONTEXT/" + EffectPulse.ClassName + kDocumentationSuffix)]
        static void Menu_EffectPulse_Doc(MenuCommand menuCommand) { Application.OpenURL(Consts.Help.UrlEffectPulse); }

        [MenuItem("CONTEXT/" + VolumetricCookieHD.ClassName + kDocumentationSuffix)]
        static void Menu_CookieHD_Doc(MenuCommand menuCommand) { Application.OpenURL(Consts.Help.HD.UrlCookie); }

        [MenuItem("CONTEXT/" + VolumetricShadowHD.ClassName + kDocumentationSuffix)]
        static void Menu_ShadowHD_Doc(MenuCommand menuCommand) { Application.OpenURL(Consts.Help.HD.UrlShadow); }

        [MenuItem("CONTEXT/" + TrackRealtimeChangesOnLightHD.ClassName + kDocumentationSuffix)]
        static void Menu_TrackRealtimeChangesOnLight_Doc(MenuCommand menuCommand) { Application.OpenURL(Consts.Help.HD.UrlTrackRealtimeChangesOnLight); }

        [MenuItem("CONTEXT/" + Config.ClassName + kDocumentationSuffix)]
        static void Menu_Config_Doc(MenuCommand menuCommand) { Application.OpenURL(Consts.Help.UrlConfig); }

        /////////////////////////////
        // GLOBAL CONFIG
        /////////////////////////////
        const string kOpenConfigSuffix = "/\u2699 Open Global Config";

        [MenuItem("CONTEXT/" + VolumetricLightBeamAbstractBase.ClassName + kOpenConfigSuffix)]
        [MenuItem("CONTEXT/" + VolumetricDustParticles.ClassName + kOpenConfigSuffix)]
        [MenuItem("CONTEXT/" + DynamicOcclusionAbstractBase.ClassName + kOpenConfigSuffix)]
        [MenuItem("CONTEXT/" + SkewingHandleSD.ClassName + kOpenConfigSuffix)]
        [MenuItem("CONTEXT/" + TriggerZone.ClassName + kOpenConfigSuffix)]
        [MenuItem("CONTEXT/" + EffectAbstractBase.ClassName + kOpenConfigSuffix)]
        [MenuItem("CONTEXT/" + VolumetricCookieHD.ClassName + kOpenConfigSuffix)]
        [MenuItem("CONTEXT/" + VolumetricShadowHD.ClassName + kOpenConfigSuffix)]
        [MenuItem("CONTEXT/" + TrackRealtimeChangesOnLightHD.ClassName + kOpenConfigSuffix)]
        public static void Menu_Beam_Config(MenuCommand menuCommand) { Config.EditorSelectInstance(); }

        /////////////////////////////
        // ADDITIONAL COMPONENTS
        /////////////////////////////
        const string kAddParticlesSD = "CONTEXT/" + VolumetricLightBeamSD.ClassName + "/Add Dust Particles";
        [MenuItem(kAddParticlesSD, false)] static void Menu_AddDustParticles_CommandSD(MenuCommand menuCommand) => Menu_AddDustParticles_Command_Common(menuCommand);
        [MenuItem(kAddParticlesSD, true)] static bool Menu_AddDustParticles_ValidateSD() => Menu_AddDustParticles_Validate_Common();

        const string kAddParticlesHD = "CONTEXT/" + VolumetricLightBeamHD.ClassName + "/Add Dust Particles";
        [MenuItem(kAddParticlesHD, false)] static void Menu_AddDustParticles_CommandHD(MenuCommand menuCommand) => Menu_AddDustParticles_Command_Common(menuCommand);
        [MenuItem(kAddParticlesHD, true)] static bool Menu_AddDustParticles_ValidateHD() => Menu_AddDustParticles_Validate_Common();

        static void Menu_AddDustParticles_Command_Common(MenuCommand menuCommand) { EditorExtensions.AddComponentFromEditor<VolumetricDustParticles>(menuCommand.context as VolumetricLightBeamAbstractBase); }
        static bool Menu_AddDustParticles_Validate_Common() { return EditorExtensions.CanAddComponentFromEditor<VolumetricDustParticles>(GetActiveVolumetricLightBeam()); }

        const string kAddDynamicOcclusionRaycasting = "CONTEXT/" + VolumetricLightBeamSD.ClassName + "/Add Dynamic Occlusion (Raycasting)";
        [MenuItem(kAddDynamicOcclusionRaycasting, false)] static void Menu_AddDynamicOcclusionRaycasting_Command(MenuCommand menuCommand) { EditorExtensions.AddComponentFromEditor<DynamicOcclusionRaycasting>(menuCommand.context as VolumetricLightBeamSD); }
        [MenuItem(kAddDynamicOcclusionRaycasting, true)] static bool Menu_AddDynamicOcclusionRaycasting_Validate() { return Config.Instance.featureEnabledDynamicOcclusion && EditorExtensions.CanAddComponentFromEditor<DynamicOcclusionAbstractBase>(GetActiveVolumetricLightBeam()); }

        const string kAddDynamicOcclusionDepthBuffer = "CONTEXT/" + VolumetricLightBeamSD.ClassName + "/Add Dynamic Occlusion (Depth Buffer)";
        [MenuItem(kAddDynamicOcclusionDepthBuffer, false)] static void Menu_AddDynamicOcclusionDepthBuffer_Command(MenuCommand menuCommand) { EditorExtensions.AddComponentFromEditor<DynamicOcclusionDepthBuffer>(menuCommand.context as VolumetricLightBeamSD); }
        [MenuItem(kAddDynamicOcclusionDepthBuffer, true)] static bool Menu_AddDynamicOcclusionDepthBuffer_Validate() { return Config.Instance.featureEnabledDynamicOcclusion && EditorExtensions.CanAddComponentFromEditor<DynamicOcclusionAbstractBase>(GetActiveVolumetricLightBeam()); }

        const string kAddTriggerZoneSD = "CONTEXT/" + VolumetricLightBeamSD.ClassName + "/Add Trigger Zone";
        [MenuItem(kAddTriggerZoneSD, false)] static void Menu_AddTriggerZone_CommandSD(MenuCommand menuCommand) => Menu_AddTriggerZone_Command_Common(menuCommand);
        [MenuItem(kAddTriggerZoneSD, true)] static bool Menu_AddTriggerZone_ValidateSD() => Menu_AddTriggerZone_Validate_Common();

        const string kAddTriggerZoneHD = "CONTEXT/" + VolumetricLightBeamHD.ClassName + "/Add Trigger Zone";
        [MenuItem(kAddTriggerZoneHD, false)] static void Menu_AddTriggerZone_CommandHD(MenuCommand menuCommand) => Menu_AddTriggerZone_Command_Common(menuCommand);
        [MenuItem(kAddTriggerZoneHD, true)] static bool Menu_AddTriggerZone_ValidateHD() => Menu_AddTriggerZone_Validate_Common();

        static void Menu_AddTriggerZone_Command_Common(MenuCommand menuCommand) { EditorExtensions.AddComponentFromEditor<TriggerZone>(menuCommand.context as VolumetricLightBeamAbstractBase); }
        static bool Menu_AddTriggerZone_Validate_Common() { return EditorExtensions.CanAddComponentFromEditor<TriggerZone>(GetActiveVolumetricLightBeam()); }

        const string kAddEffectFlickerSD = "CONTEXT/" + VolumetricLightBeamSD.ClassName + "/Add Effect Flicker";
        [MenuItem(kAddEffectFlickerSD, false)] static void Menu_EffectFlicker_CommandSD(MenuCommand menuCommand) => Menu_EffectFlicker_Command_Common(menuCommand);
        [MenuItem(kAddEffectFlickerSD, true)] static bool Menu_EffectFlicker_ValidateSD() => Menu_EffectFlicker_Validate_Common();

        const string kAddEffectFlickerHD = "CONTEXT/" + VolumetricLightBeamHD.ClassName + "/Add Effect Flicker";
        [MenuItem(kAddEffectFlickerHD, false)] static void Menu_EffectFlicker_CommandHD(MenuCommand menuCommand) => Menu_EffectFlicker_Command_Common(menuCommand);
        [MenuItem(kAddEffectFlickerHD, true)] static bool Menu_EffectFlicker_ValidateHD() => Menu_EffectFlicker_Validate_Common();

        static void Menu_EffectFlicker_Command_Common(MenuCommand menuCommand) { EditorExtensions.AddComponentFromEditor<EffectFlicker>(menuCommand.context as VolumetricLightBeamAbstractBase); }
        static bool Menu_EffectFlicker_Validate_Common() { return EditorExtensions.CanAddComponentFromEditor<EffectAbstractBase>(GetActiveVolumetricLightBeam()); }

        const string kAddEffectPulseSD = "CONTEXT/" + VolumetricLightBeamSD.ClassName + "/Add Effect Pulse";
        [MenuItem(kAddEffectPulseSD, false)] static void Menu_EffectPulse_CommandSD(MenuCommand menuCommand) => Menu_EffectPulse_Command_Common(menuCommand);
        [MenuItem(kAddEffectPulseSD, true)] static bool Menu_EffectPulse_ValidateSD() => Menu_EffectPulse_Validate_Common();

        const string kAddEffectPulseHD = "CONTEXT/" + VolumetricLightBeamHD.ClassName + "/Add Effect Pulse";
        [MenuItem(kAddEffectPulseHD, false)] static void Menu_EffectPulse_CommandHD(MenuCommand menuCommand) => Menu_EffectPulse_Command_Common(menuCommand);
        [MenuItem(kAddEffectPulseHD, true)] static bool Menu_EffectPulse_ValidateHD() => Menu_EffectPulse_Validate_Common();

        static void Menu_EffectPulse_Command_Common(MenuCommand menuCommand) { EditorExtensions.AddComponentFromEditor<EffectPulse>(menuCommand.context as VolumetricLightBeamAbstractBase); }
        static bool Menu_EffectPulse_Validate_Common() { return EditorExtensions.CanAddComponentFromEditor<EffectAbstractBase>(GetActiveVolumetricLightBeam()); }

        const string kAddShadow = "CONTEXT/" + VolumetricLightBeamHD.ClassName + "/Add Volumetric Shadow";
        [MenuItem(kAddShadow, false)] static void Menu_AddShadow_Command(MenuCommand menuCommand) { EditorExtensions.AddComponentFromEditor<VolumetricShadowHD>(menuCommand.context as VolumetricLightBeamHD); }
        [MenuItem(kAddShadow, true)] static bool Menu_AddShadow_Validate() { return Config.Instance.featureEnabledShadow && EditorExtensions.CanAddComponentFromEditor<VolumetricShadowHD>(GetActiveVolumetricLightBeam()); }

        const string kAddCookie = "CONTEXT/" + VolumetricLightBeamHD.ClassName + "/Add Volumetric Cookie";
        [MenuItem(kAddCookie, false)] static void Menu_AddCookie_Command(MenuCommand menuCommand) { EditorExtensions.AddComponentFromEditor<VolumetricCookieHD>(menuCommand.context as VolumetricLightBeamHD); }
        [MenuItem(kAddCookie, true)] static bool Menu_AddCookie_Validate() { return Config.Instance.featureEnabledCookie && EditorExtensions.CanAddComponentFromEditor<VolumetricCookieHD>(GetActiveVolumetricLightBeam()); }

        const string kAddTrackRealtime = "CONTEXT/" + VolumetricLightBeamHD.ClassName + "/Add Track Realtime Changes on Light";
        [MenuItem(kAddTrackRealtime, false)] static void Menu_AddTrackRealtime_Command(MenuCommand menuCommand) { EditorExtensions.AddComponentFromEditor<TrackRealtimeChangesOnLightHD>(menuCommand.context as VolumetricLightBeamHD); }
        [MenuItem(kAddTrackRealtime, true)] static bool Menu_AddTrackRealtime_Validate() { return EditorExtensions.CanAddComponentFromEditor<TrackRealtimeChangesOnLightHD>(GetActiveVolumetricLightBeam()) && GetActiveVolumetricLightBeam().GetComponent<Light>() != null; }

        static Light GetActiveLight() { return Selection.activeGameObject != null ? Selection.activeGameObject.GetComponent<Light>() : null; }
        static VolumetricLightBeamAbstractBase GetActiveVolumetricLightBeam() { return Selection.activeGameObject != null ? Selection.activeGameObject.GetComponent<VolumetricLightBeamAbstractBase>() : null; }




        /////////////////////////////
        // EDIT MENU
        /////////////////////////////
        const string kEditMenu = "Edit/Volumetric Light Beam/";

        [MenuItem(kEditMenu + "\u2699 Open Config", false, 20001)]
        static void Menu_EditOpenConfig() { Config.EditorSelectInstance(); }

#if UNITY_2019_3_OR_NEWER
        [MenuItem(kEditMenu + "Enable scene Pickability on all beams", false, 20101)]
        static void Menu_SetAllBeamsPickabilityEnabled() { SetAllBeamsPickability(true); }

        [MenuItem(kEditMenu + "Disable scene Pickability on all beams", false, 20102)]
        static void Menu_SetAllBeamsPickabilityDisable() { SetAllBeamsPickability(false); }

        [MenuItem(kEditMenu + "Enable scene Visibility on all beams", false, 20201)]
        static void Menu_SetAllBeamsVisibilityEnabled() { SetAllBeamsVisibility(true); }

        [MenuItem(kEditMenu + "Disable scene Visibility on all beams", false, 20202)]
        static void Menu_SetAllBeamsVisibilityDisable() { SetAllBeamsVisibility(false); }

        static void SetAllBeamsVisibility(bool enabled)
        {
            var beams = Resources.FindObjectsOfTypeAll<VolumetricLightBeamAbstractBase>();

            foreach (var beam in beams)
                beam.gameObject.SetSceneVisibilityState(enabled);
        }

        static void SetAllBeamsPickability(bool enabled)
        {
            var beams = Resources.FindObjectsOfTypeAll<VolumetricLightBeamAbstractBase>();

            foreach (var beam in beams)
                beam.gameObject.SetScenePickabilityState(enabled);
        }
#endif // UNITY_2019_3_OR_NEWER
    }
}
#endif

