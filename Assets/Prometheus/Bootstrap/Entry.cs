using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using Unity.Scripting.LifecycleManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Xuan.Prometheus.World;

namespace Xuan.Prometheus.Bootstrap
{
    /// <summary>
    /// 游戏的正式场景入口：创建 Core，按固定顺序直接加载全部模块并进入主世界，随后驱动 Core 帧循环。
    /// 启动是一条线性链路（Kit 初始化 → 配表 → 玩法会话 → 主世界场景 → 世界相位 → HUD），
    /// 不经过开屏、热更或登录界面；跨场景运行时对象统一挂在 PersistentRoot 下。
    /// 场景中应只存在一个 Entry，运行时跨场景保留它所在的独立根对象。
    /// 声明为 partial 是 AutoStaticsCleanup 源生成器的要求。
    /// </summary>
    [DefaultExecutionOrder(-100)]
    public sealed partial class Entry : MonoBehaviour
    {
        /// <summary>世界目录的 YooAsset 地址；主世界的场景地址与出生位姿都由它提供，入口不写死。</summary>
        private const string WorldCatalogAddress = "WorldCatalog";

        /// <summary>
        /// 保存当前场景中负责驱动正式启动链路的唯一入口组件。
        /// 关闭域重载时进入播放模式自动回到 null，不依赖上一局 OnDestroy 是否执行到清空这一步。
        /// </summary>
        [AutoStaticsCleanup]
        private static Entry current;

        /// <summary>由当前入口创建并驱动的唯一运行时核心。</summary>
        private Core runtimeCore;

        /// <summary>
        /// 启动链路的生命周期令牌，在 <see cref="OnDestroy"/> 中先于 Core 释放取消。
        /// 退出播放模式时尚未走完的步骤在当前 await 处以取消异常终止，不会在 Core 释放后继续访问 Kit。
        /// </summary>
        private readonly CancellationTokenSource lifetime = new CancellationTokenSource();

        /// <summary>当前入口持有的 Core；Start 执行前可能为空。</summary>
        public Core RuntimeCore => runtimeCore;

        /// <summary>在任何 Start 执行前建立唯一入口，并让整个入口根对象跨场景保留。</summary>
        private void Awake()
        {
            if (current != null && current != this)
            {
                Destroy(gameObject);
                return;
            }

            current = this;
            DontDestroyOnLoad(gameObject);
        }

        /// <summary>
        /// 创建 Core 并启动线性启动链路。
        /// 以 Forget 启动而不是 async void：入口被销毁时链路以取消异常结束，UniTask 默认不上报取消异常，其余异常仍会输出到 Console。
        /// </summary>
        private void Start()
        {
            if (current != this) return;
            runtimeCore = new Core();
            BootAsync(lifetime.Token).Forget();
        }

        /// <summary>
        /// 按固定顺序加载全部模块并进入主世界。
        /// 每一步都依赖上一步的结果，因此顺序即契约：
        /// 配表必须在资源包就绪后、任何玩法 System 构造前加载（ARCH-CONFIG-001）；
        /// 世界相位必须在场景加载完成后驱动，System 才能扫描到场景内容。
        /// </summary>
        /// <param name="cancellationToken">入口生命周期令牌；各步骤本身不可取消，取消只让链路在当前 await 处停止，不在 Core 释放后继续访问 Kit。</param>
        private async UniTaskVoid BootAsync(CancellationToken cancellationToken)
        {
            // App 段：全部 Kit 异步初始化并行执行，完成后统一进入同步 AfterNew。
            await UniTask.WhenAll(runtimeCore.CreateAfterNewTasks()).AttachExternalCancellation(cancellationToken);
            runtimeCore.AfterNew();
            Core.Config.Load();

            // Session 段：构造全部 System，各自加载自己的配置。
            await Core.Gameplay.CreateSessionAsync(new PrometheusSystemInstaller()).AttachExternalCancellation(cancellationToken);

            // World 段：加载主世界场景后驱动世界相位，最后按世界定义决定是否打开 HUD。
            WorldDefinition mainWorld = (await LoadWorldCatalogAsync().AttachExternalCancellation(cancellationToken)).RequireMainWorld();
            Scene scene = await Core.Asset.LoadSceneAsync(mainWorld.SceneAddress).AttachExternalCancellation(cancellationToken);
            await Core.Gameplay.EnterWorldAsync(new WorldContext(mainWorld.WorldId, mainWorld.SceneAddress, scene, mainWorld.SpawnPosition, mainWorld.SpawnRotation)).AttachExternalCancellation(cancellationToken);
            if (mainWorld.ShowsHud) Core.UI.OpenPanel<HudPanel>();
        }

        /// <summary>加载并校验世界目录；目录配置错误在启动时直接暴露。</summary>
        private static async UniTask<WorldCatalog> LoadWorldCatalogAsync()
        {
            WorldCatalog catalog = null;
            await Core.Asset.LoadAssetAsync<WorldCatalog>(WorldCatalogAddress, asset => catalog = asset, error => throw new InvalidOperationException(error)).ToUniTask();
            catalog.Validate();
            return catalog;
        }

        /// <summary>将 Unity 帧循环转交给普通 C# Core，未完成初始化时 Core 不会驱动 Kit。</summary>
        private void Update()
        {
            runtimeCore?.OnUpdate(Time.deltaTime);
        }

        /// <summary>
        /// 入口被销毁时按依赖逆序释放玩法实体和资源句柄，并允许后续场景重新建立入口。
        /// 启动链路必须先于 Core 取消：取消时挂在令牌上的等待同步结束，链路据此停在当前步骤。
        /// </summary>
        private void OnDestroy()
        {
            if (current != this) return;
            lifetime.Cancel();
            lifetime.Dispose();
            runtimeCore?.Dispose();
            runtimeCore = null;
            current = null;
        }
    }
}
