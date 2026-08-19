using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace ShinySSRR {

    [ExecuteAlways]
    public class SkyboxBaker : MonoBehaviour {

        const string BAKER_CAM_NAME = "SSR_SkyboxBaker";

        public static bool needSkyboxUpdate = true;
        RenderTexture skyboxCubemap;
        float lastSkyboxSnapshotTime;
        ShinyScreenSpaceRaytracedReflections settings;
        Camera cam;
        RenderTextureFormat currentSkyboxFormat;

        Camera bakerCam;
        GameObject bakerGO;

        void OnEnable() {
            needSkyboxUpdate = true;
            cam = GetComponent<Camera>();
        }

        void OnDisable() {
            ReleaseBakerCamera();
        }

        void OnDestroy() {
            ReleaseSkyboxCubemap();
            ReleaseBakerCamera();
        }

        void LateUpdate() {
            settings = VolumeManager.instance?.stack?.GetComponent<ShinyScreenSpaceRaytracedReflections>();

            if (settings == null || settings.skyboxIntensity.value <= 0 || cam == null) return;

            if (settings.skyboxUpdateMode.value == SkyboxUpdateMode.Continuous) {
                needSkyboxUpdate = true;
            }
            else if (settings.skyboxUpdateMode.value == SkyboxUpdateMode.Interval && Time.time - lastSkyboxSnapshotTime >= settings.skyboxUpdateInterval.value) {
                lastSkyboxSnapshotTime = Time.time;
                needSkyboxUpdate = true;
            }

            if (needSkyboxUpdate && cam.cameraType == CameraType.Game) {
                needSkyboxUpdate = false;
                UpdateSkyboxCubemap();
            }
        }

        void ReleaseSkyboxCubemap() {
            if (skyboxCubemap == null) return;
            skyboxCubemap.Release();
            DestroyImmediate(skyboxCubemap);
            skyboxCubemap = null;
        }

        void ReleaseBakerCamera() {
            if (bakerGO == null) return;
            if (Application.isPlaying) {
                Destroy(bakerGO);
            } else {
                DestroyImmediate(bakerGO);
            }
            bakerGO = null;
            bakerCam = null;
        }

        void CleanupOrphanedBakerCameras() {
            if (cam == null) return;
            Transform camT = cam.transform;
            for (int i = camT.childCount - 1; i >= 0; i--) {
                Transform child = camT.GetChild(i);
                if (child.name == BAKER_CAM_NAME) {
                    if (Application.isPlaying) {
                        Destroy(child.gameObject);
                    } else {
                        DestroyImmediate(child.gameObject);
                    }
                }
            }
        }

        void EnsureBakerCamera() {
            if (bakerCam != null) return;

            ReleaseBakerCamera();
            CleanupOrphanedBakerCameras();

            bakerGO = new GameObject(BAKER_CAM_NAME);
            
            Transform bakerT = bakerGO.transform;
            bakerT.SetParent(cam.transform, false);
            bakerT.localPosition = Vector3.zero;
            bakerT.localRotation = Quaternion.identity;

            bakerCam = bakerGO.AddComponent<Camera>();
            bakerCam.enabled = false;

            var urpData = bakerGO.AddComponent<UniversalAdditionalCameraData>();
            urpData.renderPostProcessing = false;
            urpData.renderShadows = false;
            urpData.requiresColorTexture = false;
            urpData.requiresDepthTexture = false;
            urpData.antialiasing = AntialiasingMode.None;
        }

        public void UpdateSkyboxCubemap() {

            if (settings.skyboxReflectionMode.value == SkyboxReflectionMode.CustomCubemap) {
                Shader.SetGlobalTexture(ShaderParams.SkyboxCubemap, settings.skyboxCustomCubemap.value);
                return;
            }

            if (cam == null) return;

            // Reuse an existing reflection probe's cubemap instead of baking our own
            ShinySkyboxReflectionSource source = ShinySkyboxReflectionSource.current;
            if (source != null) {
                ReflectionProbe srcProbe = source.ResolveProbe();
                if (srcProbe != null) {
                    Texture probeTex = srcProbe.realtimeTexture != null ? srcProbe.realtimeTexture
                                     : srcProbe.bakedTexture != null ? srcProbe.bakedTexture
                                     : srcProbe.texture;
                    if (probeTex != null) {
                        Shader.SetGlobalTexture(ShaderParams.SkyboxCubemap, probeTex);
                        return;
                    }
                }
            }

            EnsureBakerCamera();

            // Bake from the source anchor if present, otherwise follow the camera
            bakerCam.transform.position = source != null ? source.transform.position : cam.transform.position;

            bakerCam.fieldOfView = cam.fieldOfView;
            bakerCam.nearClipPlane = cam.nearClipPlane;
            bakerCam.orthographic = cam.orthographic;
            bakerCam.orthographicSize = cam.orthographicSize;
            bakerCam.clearFlags = cam.clearFlags;
            bakerCam.backgroundColor = cam.backgroundColor;

            int desiredCullingMask = settings.skyboxCullingMask.value;
            bakerCam.cullingMask = desiredCullingMask;
            bakerCam.farClipPlane = desiredCullingMask == 0 
                ? cam.nearClipPlane + 0.1f 
                : cam.farClipPlane;

            int res = 16 << Mathf.Clamp((int)settings.skyboxResolution.value, 0, 9);
            RenderTextureFormat rtFormat = settings.skyboxHDR.value ? RenderTextureFormat.DefaultHDR : RenderTextureFormat.Default;

            if (skyboxCubemap == null || skyboxCubemap.width != res || currentSkyboxFormat != rtFormat) {
                currentSkyboxFormat = rtFormat;
                ReleaseSkyboxCubemap();
                skyboxCubemap = new RenderTexture(res, res, 0, rtFormat) {
                    dimension = TextureDimension.Cube
                };
            }

            bakerCam.RenderToCubemap(skyboxCubemap);
            Shader.SetGlobalTexture(ShaderParams.SkyboxCubemap, skyboxCubemap);
        }

    }

}
