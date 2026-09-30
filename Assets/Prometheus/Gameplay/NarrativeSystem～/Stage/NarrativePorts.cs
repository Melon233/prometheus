using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;

namespace Xuan.Prometheus.Narrative
{
    /// <summary>
    /// 屏幕级表现端口：淡入淡出、黑边与 HUD 显隐。
    /// 全部状态以可直接读写的属性暴露，使落终态不需要经过任何过程动画。
    /// </summary>
    public interface INarrativeScreen
    {
        /// <summary>获取或设置全屏黑幕不透明度；0 为完全透明，1 为全黑。</summary>
        float FadeAlpha { get; set; }

        /// <summary>获取或设置上下黑边占屏幕高度的比例；0 为无黑边。</summary>
        float LetterboxRatio { get; set; }

        /// <summary>获取或设置玩法 HUD 是否可见。</summary>
        bool HudVisible { get; set; }

        /// <summary>在指定时长内把黑幕不透明度过渡到目标值。</summary>
        UniTask FadeAsync(float target, float duration, CancellationToken cancellationToken);

        /// <summary>在指定时长内把黑边比例过渡到目标值。</summary>
        UniTask LetterboxAsync(float target, float duration, CancellationToken cancellationToken);
    }

    /// <summary>演出镜头端口；具名镜头由实现方在场景中登记。</summary>
    public interface INarrativeCameraPort
    {
        /// <summary>接管演出镜头控制权，返回的句柄释放后恢复玩法镜头。</summary>
        IDisposable AcquireControl(int priority);

        /// <summary>立即切换到具名镜头。</summary>
        void SnapTo(string cameraId);

        /// <summary>在指定时长内混合到具名镜头。</summary>
        UniTask BlendToAsync(string cameraId, float duration, CancellationToken cancellationToken);

        /// <summary>播放一次镜头抖动；该表现不留下任何世界状态。</summary>
        UniTask ShakeAsync(float amplitude, float duration, CancellationToken cancellationToken);

        /// <summary>判断是否存在指定的具名镜头，供编辑期与运行期校验使用。</summary>
        bool HasCamera(string cameraId);
    }

    /// <summary>一个已生成特效实例的句柄。</summary>
    public interface INarrativeVfxHandle
    {
        /// <summary>获取该特效实例是否仍然存在。</summary>
        bool IsAlive { get; }

        /// <summary>获取生成该实例所用的资源地址。</summary>
        string Location { get; }
    }

    /// <summary>特效端口；按资源地址生成一次性或持续特效。</summary>
    public interface INarrativeVfxPort
    {
        /// <summary>在指定位置生成一个特效实例。</summary>
        UniTask<INarrativeVfxHandle> SpawnAsync(string location, Vector3 position, Quaternion rotation, Transform parent, CancellationToken cancellationToken);

        /// <summary>停止并回收一个特效实例；重复调用保持幂等。</summary>
        void Stop(INarrativeVfxHandle handle);

        /// <summary>停止全部由本端口生成的特效实例。</summary>
        void StopAll();
    }

    /// <summary>一个持续音频事件的句柄。</summary>
    public interface INarrativeAudioHandle
    {
        /// <summary>获取该事件是否仍在播放。</summary>
        bool IsAlive { get; }

        /// <summary>获取事件键。</summary>
        string EventKey { get; }
    }

    /// <summary>音频端口；一次性事件与持续事件分开管理。</summary>
    public interface INarrativeAudioPort
    {
        /// <summary>播放一次性音效。</summary>
        void PlayOneShot(string eventKey, Vector3 position);

        /// <summary>播放一个可淡入的持续事件。</summary>
        INarrativeAudioHandle PlayPersistent(string eventKey, float fadeInSeconds);

        /// <summary>淡出并停止一个持续事件；重复调用保持幂等。</summary>
        void StopPersistent(INarrativeAudioHandle handle, float fadeOutSeconds);

        /// <summary>停止全部由本端口启动的持续事件。</summary>
        void StopAll();
    }

    /// <summary>世界接管端口：演出期间屏蔽玩法输入并冻结附近 AI。</summary>
    public interface INarrativeWorldPort
    {
        /// <summary>接管玩法输入，返回的句柄释放后恢复。</summary>
        IDisposable LockGameplayInput();

        /// <summary>冻结指定范围内的 AI 与怪物，返回的句柄释放后解冻。</summary>
        IDisposable FreezeAi(Vector3 center, float radius);
    }

    /// <summary>资源端口；剧情资源一律按地址异步加载，禁止在配置资产里硬引用大资源。</summary>
    public interface INarrativeAssetPort
    {
        /// <summary>按地址异步加载一个资源。</summary>
        UniTask<TAsset> LoadAsync<TAsset>(string location, CancellationToken cancellationToken) where TAsset : UnityEngine.Object;

        /// <summary>
        /// 同步读取一个已经加载完成的资源。
        /// 落终态是同步过程，无法等待加载，因此凡是被状态类叶子引用的资源都必须先在 StageSpec.Preload 中声明。
        /// </summary>
        bool TryGet<TAsset>(string location, out TAsset asset) where TAsset : UnityEngine.Object;

        /// <summary>释放一个已加载资源的引用。</summary>
        void Release(string location);

        /// <summary>释放全部由本端口加载的资源。</summary>
        void ReleaseAll();
    }
}
