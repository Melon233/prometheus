using System;
using UnityEngine;

namespace Xuan.Prometheus.Narrative
{
    /// <summary>
    /// 一次运行时演出所需的全套能力端口。
    ///
    /// 端口的装配放在玩法层而不是 <c>NarrativeSystem</c> 内部：这些实现要引用
    /// <c>ICameraSystem</c>、<c>IInputSystem</c>、<c>ITeamSystem</c>、<c>IPoiSystem</c>，
    /// 让剧情系统自己装配它们等于让它认识半个玩法层，与它「零向外依赖」的边界相悖。
    ///
    /// 生命周期与一次演出严格对齐：构造即建立宿主节点，释放即销毁并归还全部资源。
    /// </summary>
    internal sealed class NarrativeRuntimePorts : IDisposable
    {
        /// <summary>承载本次演出运行时对象（特效兜底父节点）的节点名。</summary>
        private const string HostName = "[NarrativePorts]";

        private GameObject host;
        private NarrativeRuntimeScreen screen;
        private GameplayCameraPort camera;
        private AssetKitPort assets;

        /// <summary>建立本次演出的全部端口并组装成舞台服务。</summary>
        internal NarrativeRuntimePorts()
        {
            host = new GameObject(HostName);
            host.transform.SetParent(PersistentRoot.Shared, false);

            screen = new NarrativeRuntimeScreen();
            Actors = new GameplayActorResolver();
            camera = new GameplayCameraPort(Actors);
            assets = new AssetKitPort();

            Services = new StageServices(screen, Actors)
            {
                Camera = camera,
                Assets = assets,
                Vfx = new NarrativeVfxPort(assets, host.transform),
                Audio = new FmodNarrativeAudioPort(),
                World = new GameplayWorldPort()
            };
        }

        /// <summary>获取本次演出使用的舞台服务。</summary>
        internal StageServices Services { get; }

        /// <summary>获取角色解析器，供调用方在演出前登记具名锚点。</summary>
        internal GameplayActorResolver Actors { get; }

        /// <summary>获取资源端口，供调用方按地址加载剧情图等演出资源。</summary>
        internal INarrativeAssetPort Assets => assets;

        /// <summary>
        /// 按与建立相反的顺序释放全部端口。
        /// 先停表现（特效、音频），再解引用（角色、资源），最后销毁宿主与镜头，
        /// 保证释放过程中不会有仍在播放的对象引用到已销毁的节点。
        /// </summary>
        public void Dispose()
        {
            if (host == null) return;
            Services.Vfx?.StopAll();
            Services.Audio?.StopAll();
            Actors.ReleaseAll();
            assets.ReleaseAll();
            camera.Dispose();
            screen.Dispose();
            StageScope.DestroyObject(host);
            host = null;
        }
    }
}
