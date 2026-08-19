using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using UnityEngine;
using Xuan.Prometheus.Asset;

namespace Xuan.Prometheus
{
    /// <summary>
    /// 游戏的正式场景入口，负责把入口场景中的外部配置转换成启动参数并驱动 Core 生命周期。
    /// 场景中应只存在一个 Entry，运行时跨场景保留它所在的独立根对象。
    /// </summary>
    [DefaultExecutionOrder(-100)]
    public sealed class Entry : MonoBehaviour
    {
        /// <summary>正式入口初始化的 YooAsset 资源包名称。</summary>
        [SerializeField] private string packageName = AssetKit.DefaultPackageName;
        /// <summary>GameplayKit 在 AfterNew 中通过 AssetKit 加载的正式玩法场景地址。</summary>
        [SerializeField] private string gameplaySceneAddress = "SampleScene";
        /// <summary>GameplayKit 初始化 EffectSystem 时通过 AssetKit 加载的配置地址。</summary>
        [SerializeField] private string effectLibraryAddress = "EffectLibrary";
        /// <summary>第一个固定小队槽位使用的角色资源地址，同时保留旧场景中的 playerAddress 序列化数据。</summary>
        [SerializeField] private string playerAddress = "Yefa";
        /// <summary>第二个固定小队槽位使用的角色资源地址。</summary>
        [SerializeField] private string secondPlayerAddress = "Yousaer";
        /// <summary>第三个固定小队槽位使用的角色资源地址。</summary>
        [SerializeField] private string thirdPlayerAddress = "Senyin";
        /// <summary>初始敌人统一使用的预制体资源地址。</summary>
        [SerializeField] private string enemyAddress = "Slime";
        /// <summary>玩法场景加载前已经确定的敌人出生世界坐标。</summary>
        [SerializeField] private List<Vector3> enemySpawnPositions = new List<Vector3>();
        /// <summary>初始敌人最大生成数量；零表示使用全部配置坐标。</summary>
        [SerializeField, Min(0)] private int enemySpawnLimit = 1;

        /// <summary>保存当前场景中负责驱动正式启动链路的唯一入口组件。</summary>
        private static Entry current;
        /// <summary>由当前入口创建并驱动的唯一运行时核心。</summary>
        private Core runtimeCore;

        /// <summary>
        /// 当前入口持有的 Core；Start 协程执行前可能为空。
        /// </summary>
        public Core RuntimeCore => runtimeCore;

        /// <summary>
        /// 在任何 Start 执行前建立唯一入口，并让整个入口根对象跨场景保留。
        /// </summary>
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
        /// 创建并配置 Core，通过 WhenAll 等待每个 Kit 的 AfterNewAsync，随后统一执行同步 AfterNew。
        /// </summary>
        private async void Start()
        {
            if (current != this) return;

            runtimeCore = new Core();
            string[] teamMemberAddresses = { playerAddress, secondPlayerAddress, thirdPlayerAddress };
            GameplayStartupOptions options = new GameplayStartupOptions(packageName, transform, gameplaySceneAddress, effectLibraryAddress, teamMemberAddresses, enemyAddress, enemySpawnPositions, enemySpawnLimit);
            runtimeCore.Configure(options);
            await UniTask.WhenAll(runtimeCore.CreateAfterNewTasks());
            runtimeCore.AfterNew();
            Core.UI.OpenPanel<HudPanel>();
        }

        /// <summary>
        /// 将 Unity 帧循环转交给普通 C# Core，未完成初始化时 Core 不会驱动 Kit。
        /// </summary>
        private void Update()
        {
            runtimeCore?.OnUpdate(Time.deltaTime);
        }

        /// <summary>
        /// 入口被销毁时按依赖逆序释放玩法实体和资源句柄，并允许后续场景重新建立入口。
        /// </summary>
        private void OnDestroy()
        {
            if (current != this)
                return;

            runtimeCore?.Dispose();
            runtimeCore = null;
            current = null;
        }
    }
}
