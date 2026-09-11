using UnityEngine;
using UnityEngine.Rendering.Universal;

namespace Xuan.Prometheus.Bootstrap
{
    /// <summary>
    /// 局外相机：在**没有玩法相机**的那段时间里保证屏幕仍然有人清。
    ///
    /// 它存在的理由是一处生命周期不匹配：<c>UIKit</c> 是 App 段的 Kit，而创建玩法相机的
    /// <c>CameraSystem</c> 是 Session 段的 System。开屏、热更、登录全都发生在会话建立**之前**，
    /// 那段时间场景里一台相机都没有。
    ///
    /// 注意它**不是"UI 相机"**：启动界面与登录界面用的都是 <c>ScreenSpaceOverlay</c> Canvas，
    /// 这种 Canvas 不经相机渲染，实测没有相机也照样画得出来。所以本相机的唯一职责是**清屏**——
    /// 叫 UI 相机会让人误以为 UI 依赖它。
    ///
    /// 不加它会怎样：现在画面看着正常，是因为启动界面那张全屏不透明底图替相机做了清屏。
    /// 这在一个地方会破——启动界面淡入时整体 alpha 小于 1，连底图自己都是半透明的，
    /// 背后就是没人清过的 backbuffer。编辑器里 Game view 会兜底，设备上不保证。
    /// </summary>
    public sealed class OuterCamera
    {
        /// <summary>排序深度；低于玩法相机的 -1，因此两者万一同时启用也是本相机先清屏。</summary>
        private const float Depth = -100f;

        /// <summary>相机宿主对象；跨场景保留，因为局外这段时间会跨越一次场景加载。</summary>
        private GameObject host;

        private Camera camera;

        /// <summary>当前是否正在渲染。</summary>
        public bool IsActive => host != null && host.activeSelf;

        /// <summary>创建并启用局外相机；重复调用是幂等的。</summary>
        public void Ensure()
        {
            if (host != null)
            {
                SetActive(true);
                return;
            }

            host = new GameObject(nameof(OuterCamera));
            Object.DontDestroyOnLoad(host);
            camera = host.AddComponent<Camera>();
            // 只清屏，不渲染任何东西：cullingMask 为空，因此投影方式与裁剪面都无关紧要。
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = Color.black;
            camera.cullingMask = 0;
            camera.depth = Depth;
            camera.useOcclusionCulling = false;
            camera.allowHDR = false;
            camera.allowMSAA = false;

            UniversalAdditionalCameraData cameraData = host.GetComponent<UniversalAdditionalCameraData>() ?? host.AddComponent<UniversalAdditionalCameraData>();
            cameraData.renderPostProcessing = false;
            cameraData.renderShadows = false;

            // 刻意**不**打 MainCamera 标签：打了之后 Camera.main 会指向本相机，
            // UIKit 的世界空间 Canvas 会把它当成观察相机绑上去，世界 UI 的定位就全错了。
            //
            // 也刻意**不**挂 AudioListener：玩法相机自带一个，两个同时存在会产生重复监听器警告。
            // 局外没有音频播放，因此这段时间没有监听器是正确状态而不是缺失。
        }

        /// <summary>启用或禁用局外相机；玩法相机存在期间它应当让位。</summary>
        /// <param name="active">是否启用。</param>
        public void SetActive(bool active)
        {
            if (host != null && host.activeSelf != active) host.SetActive(active);
        }

        /// <summary>销毁局外相机。</summary>
        public void Dispose()
        {
            if (host == null) return;
            if (Application.isPlaying) Object.Destroy(host);
            else Object.DestroyImmediate(host);
            host = null;
            camera = null;
        }
    }
}
