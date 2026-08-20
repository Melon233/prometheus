using System;
using FMODUnity;
using Unity.Cinemachine;
using Unity.Cinemachine.TargetTracking;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using Xuan.Prometheus.Logic;
using Xuan.Prometheus.Rendering;

namespace Xuan.Prometheus
{
    /// <summary>集中创建并管理单局唯一的输出相机、Cinemachine 跟随镜头、音频监听器和当前角色跟随目标。</summary>
    public sealed class CameraSystem : XSystem
    {
        /// <summary>保存旧角色 Prefab 相机相对角色根节点的位置，用于维持迁移前的构图和跟随距离。</summary>
        private static readonly Vector3 FollowLocalPosition = new Vector3(0f, 3.4884024f, -4.0738688f);

        /// <summary>保存旧角色 Prefab 相机相对角色根节点的旋转，用于维持迁移前的俯视角度和角色朝向继承关系。</summary>
        private static readonly Quaternion FollowLocalRotation = new Quaternion(-0.25689107f, 0.0063765603f, -0.0016927216f, -0.96641785f);

        /// <summary>保存 SSGI 相机组件的完整运行时类型名；该插件位于预定义 Assembly-CSharp，运行时程序集不能直接建立编译期引用。</summary>
        private const string SsgiCameraTypeName = "MF.SSGI.SSGICamera, Assembly-CSharp";

        /// <summary>保存玩法入口提供的常驻根节点，使相机对象与当前 Core 生命周期一致。</summary>
        private readonly Transform runtimeRoot;

        /// <summary>保存当前系统创建的运行时根对象。</summary>
        private GameObject cameraSystemRoot;

        /// <summary>保存 Cinemachine 实际驱动并负责渲染画面的 Unity Camera。</summary>
        private Camera outputCamera;

        /// <summary>保存输出相机的 URP 附加数据，使画质或渲染路径切换能够立即刷新相机级开关。</summary>
        private UniversalAdditionalCameraData outputCameraData;

        /// <summary>保存单局唯一的 Cinemachine Brain。</summary>
        private CinemachineBrain brain;

        /// <summary>保存负责复刻旧相机局部坐标关系的 Cinemachine Camera。</summary>
        private CinemachineCamera followCamera;

        /// <summary>保存负责按角色局部坐标计算镜头位置的 Cinemachine Follow 组件。</summary>
        private CinemachineFollow followBody;

        /// <summary>保存由 CameraSystem 创建并动态挂接到当前上场角色的跟随参考节点。</summary>
        private GameObject followTarget;

        /// <summary>保存当前玩法世界的实体查询入口。</summary>
        private EntitySystem entitySystem;

        /// <summary>保存当前系统订阅的小队切换事件总线。</summary>
        private IEventKit eventKit;

        /// <summary>标记当前系统已经完成释放，避免失效事件继续修改相机目标。</summary>
        private bool isDisposed;

        /// <summary>使用玩法入口的常驻根节点创建相机系统配置。</summary>
        /// <param name="runtimeRoot">承载当前单局运行时对象的根节点。</param>
        public CameraSystem(Transform runtimeRoot)
        {
            this.runtimeRoot = runtimeRoot != null ? runtimeRoot : throw new ArgumentNullException(nameof(runtimeRoot));
        }

        /// <summary>获取当前单局负责实际渲染的 Unity Camera。</summary>
        public Camera OutputCamera => outputCamera;

        /// <summary>获取当前单局负责坐标跟随的 Cinemachine Camera。</summary>
        public CinemachineCamera FollowCamera => followCamera;

        /// <summary>创建完整相机运行时对象并在初始小队成员发布前订阅切换事件。</summary>
        /// <param name="gameplayKit">持有当前相机系统的单局玩法世界。</param>
        public override void AfterNew(IGameplayKit gameplayKit)
        {
            if (isDisposed) throw new ObjectDisposedException(nameof(CameraSystem));
            if (gameplayKit == null) throw new ArgumentNullException(nameof(gameplayKit));
            entitySystem = gameplayKit.GetSystem<EntitySystem>();
            eventKit = Core.Event ?? throw new InvalidOperationException("CameraSystem requires EventKit.");
            CreateCameraObjects();
            PrometheusRenderQualityController.QualityChanged += OnRenderQualityChanged;
            eventKit.AddListener<ActiveTeamMemberChangedEvent>(Event.ActiveTeamMemberChanged, OnActiveTeamMemberChanged);
        }

        /// <summary>释放小队事件和全部系统创建的运行时对象；角色 Prefab 不再持有任何相机资源。</summary>
        public override void Dispose()
        {
            if (isDisposed) return;
            if (eventKit != null) eventKit.RemoveListener<ActiveTeamMemberChangedEvent>(Event.ActiveTeamMemberChanged, OnActiveTeamMemberChanged);
            PrometheusRenderQualityController.QualityChanged -= OnRenderQualityChanged;
            DestroyRuntimeObject(followTarget);
            DestroyRuntimeObject(cameraSystemRoot);
            followTarget = null;
            followBody = null;
            followCamera = null;
            brain = null;
            outputCamera = null;
            outputCameraData = null;
            cameraSystemRoot = null;
            entitySystem = null;
            eventKit = null;
            isDisposed = true;
        }

        /// <summary>收到上场成员变化后，把唯一跟随参考节点迁移到新角色并立即对齐旧 Prefab 相机的局部姿态。</summary>
        /// <param name="eventData">包含新上场成员 EntityId 的同步切换事件。</param>
        private void OnActiveTeamMemberChanged(ActiveTeamMemberChangedEvent eventData)
        {
            if (eventData == null) throw new ArgumentNullException(nameof(eventData));
            if (eventData.CurrentEntityId == 0)
            {
                DetachFollowTarget();
                return;
            }
            if (!entitySystem.TryGetEntity(eventData.CurrentEntityId, out Entity entity)) throw new InvalidOperationException($"CameraSystem cannot find active team member Entity {eventData.CurrentEntityId}.");
            BindFollowTarget(entity);
        }

        /// <summary>创建输出 Camera、完整迁移旧相机渲染组件，并配置无阻尼无混合的 Cinemachine 坐标跟随链路。</summary>
        private void CreateCameraObjects()
        {
            cameraSystemRoot = new GameObject("[CameraSystem]");
            cameraSystemRoot.transform.SetParent(runtimeRoot, false);
            GameObject outputCameraObject = new GameObject("Main Camera");
            outputCameraObject.tag = "MainCamera";
            outputCameraObject.transform.SetParent(cameraSystemRoot.transform, false);
            outputCameraObject.transform.localPosition = FollowLocalPosition;
            outputCameraObject.transform.localRotation = FollowLocalRotation;
            outputCamera = outputCameraObject.AddComponent<Camera>();
            ConfigureOutputCamera(outputCamera);
            outputCameraObject.AddComponent<AudioListener>();
            outputCameraObject.AddComponent<StudioListener>();
            outputCameraData = outputCameraObject.AddComponent<UniversalAdditionalCameraData>();
            ConfigureUniversalCamera(outputCameraData);
            // if (PrometheusRenderQualityController.CurrentPlatform == PrometheusRenderPlatform.Pc) AddSsgiCamera(outputCameraObject);
            PrometheusRenderQualityController.ApplyCurrentCameraQuality(outputCamera, outputCameraData);
            brain = outputCameraObject.AddComponent<CinemachineBrain>();
            brain.UpdateMethod = CinemachineBrain.UpdateMethods.LateUpdate;
            brain.BlendUpdateMethod = CinemachineBrain.BrainUpdateMethods.LateUpdate;
            brain.DefaultBlend = new CinemachineBlendDefinition(CinemachineBlendDefinition.Styles.Cut, 0f);
            GameObject followCameraObject = new GameObject("Player Follow Camera");
            followCameraObject.transform.SetParent(cameraSystemRoot.transform, false);
            followCamera = followCameraObject.AddComponent<CinemachineCamera>();
            followCamera.Priority = 100;
            followCamera.Lens = LensSettings.FromCamera(outputCamera);
            followBody = followCameraObject.AddComponent<CinemachineFollow>();
            followBody.FollowOffset = Quaternion.Inverse(FollowLocalRotation) * FollowLocalPosition;
            followBody.TrackerSettings = CreateTrackerSettings();
            CinemachineRotateWithFollowTarget rotationControl = followCameraObject.AddComponent<CinemachineRotateWithFollowTarget>();
            rotationControl.Damping = 0f;
            followTarget = new GameObject("Camera Follow Target");
            followTarget.transform.SetParent(cameraSystemRoot.transform, false);
            followTarget.SetActive(false);
        }

        /// <summary>配置与旧角色 Prefab 相机一致的基础渲染参数。</summary>
        /// <param name="camera">由当前系统创建的输出相机。</param>
        private static void ConfigureOutputCamera(Camera camera)
        {
            camera.clearFlags = CameraClearFlags.Skybox;
            camera.backgroundColor = new Color(0.19215687f, 0.3019608f, 0.4745098f, 0f);
            camera.gateFit = Camera.GateFitMode.Horizontal;
            camera.fieldOfView = 60f;
            camera.nearClipPlane = 0.3f;
            camera.farClipPlane = 1000f;
            camera.depth = -1f;
            camera.cullingMask = -1;
            camera.renderingPath = RenderingPath.UsePlayerSettings;
            camera.targetDisplay = 0;
            camera.stereoTargetEye = StereoTargetEyeMask.Both;
            camera.allowHDR = false;
            camera.allowMSAA = false;
            camera.allowDynamicResolution = false;
            camera.useOcclusionCulling = true;
            camera.focalLength = 50f;
            camera.sensorSize = new Vector2(36f, 24f);
            camera.lensShift = Vector2.zero;
        }

        /// <summary>配置与旧角色 Prefab 相机一致的 URP Renderer、后处理、抖动和纹理覆盖策略。</summary>
        /// <param name="cameraData">输出相机上的 URP 附加数据。</param>
        private static void ConfigureUniversalCamera(UniversalAdditionalCameraData cameraData)
        {
            cameraData.renderShadows = false;
            cameraData.requiresDepthOption = CameraOverrideOption.UsePipelineSettings;
            cameraData.requiresColorOption = CameraOverrideOption.UsePipelineSettings;
            cameraData.renderType = CameraRenderType.Base;
            cameraData.SetRenderer(-1);
            cameraData.volumeLayerMask = 1;
            cameraData.volumeTrigger = null;
            cameraData.renderPostProcessing = false;
            cameraData.antialiasing = AntialiasingMode.None;
            cameraData.antialiasingQuality = AntialiasingQuality.High;
            cameraData.stopNaN = false;
            cameraData.dithering = false;
            cameraData.allowXRRendering = true;
            cameraData.GetComponent<Camera>().SetVolumeFrameworkUpdateMode(VolumeFrameworkUpdateMode.ViaScripting);
        }

        /// <summary>画质变化后把当前平台档位重新应用到唯一输出相机。</summary>
        /// <param name="qualityLevel">已经由渲染控制器应用完成的新画质等级。</param>
        private void OnRenderQualityChanged(PrometheusRenderQualityLevel qualityLevel)
        {
            PrometheusRenderQualityController.ApplyCurrentCameraQuality(outputCamera, outputCameraData);
        }

        /// <summary>创建零阻尼且完整继承目标局部坐标系的跟随设置，使 Cinemachine 行为等同旧相机父子层级。</summary>
        /// <returns>可直接赋给 Cinemachine Follow 的目标追踪配置。</returns>
        private static TrackerSettings CreateTrackerSettings()
        {
            return new TrackerSettings { BindingMode = BindingMode.LockToTarget, PositionDamping = Vector3.zero, AngularDampingMode = AngularDampingMode.Quaternion, RotationDamping = Vector3.zero, QuaternionDamping = 0f };
        }

        /// <summary>通过完整类型名添加旧相机使用的 SSGI 标记组件，并在插件缺失时立即暴露项目配置错误。</summary>
        /// <param name="outputCameraObject">需要参与 SSGI 渲染的输出相机对象。</param>
        private static void AddSsgiCamera(GameObject outputCameraObject)
        {
            Type ssgiCameraType = Type.GetType(SsgiCameraTypeName, true);
            outputCameraObject.AddComponent(ssgiCameraType);
        }

        /// <summary>把系统持有的参考节点挂到当前角色根节点，并用 Cinemachine 立即接管等价的世界坐标和旋转。</summary>
        /// <param name="entity">当前上场且已经绑定场景对象的角色实体。</param>
        private void BindFollowTarget(Entity entity)
        {
            if (entity == null) throw new ArgumentNullException(nameof(entity));
            if (entity.bindGo == null) throw new InvalidOperationException($"CameraSystem cannot follow Entity {entity.EntityId} without a bound GameObject.");
            Transform characterTransform = entity.bindGo.transform;
            Transform targetTransform = followTarget.transform;
            followTarget.SetActive(false);
            targetTransform.SetParent(characterTransform, false);
            targetTransform.localPosition = Vector3.zero;
            targetTransform.localRotation = FollowLocalRotation;
            targetTransform.localScale = Vector3.one;
            followTarget.SetActive(true);
            followCamera.Follow = targetTransform;
            Vector3 cameraPosition = characterTransform.TransformPoint(FollowLocalPosition);
            Quaternion cameraRotation = characterTransform.rotation * FollowLocalRotation;
            followCamera.ForceCameraPosition(cameraPosition, cameraRotation);
        }

        /// <summary>在小队没有可用上场成员时解除 Cinemachine 目标，并把参考节点收回系统根对象。</summary>
        private void DetachFollowTarget()
        {
            followCamera.Follow = null;
            followTarget.SetActive(false);
            followTarget.transform.SetParent(cameraSystemRoot.transform, false);
        }

        /// <summary>按照当前 Unity 运行环境销毁一个由 CameraSystem 独占创建的场景对象。</summary>
        /// <param name="runtimeObject">需要释放的运行时对象。</param>
        private static void DestroyRuntimeObject(GameObject runtimeObject)
        {
            if (runtimeObject == null) return;
            if (Application.isPlaying) UnityEngine.Object.Destroy(runtimeObject);
            else UnityEngine.Object.DestroyImmediate(runtimeObject);
        }
    }
}
