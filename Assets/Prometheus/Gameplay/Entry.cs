using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Xuan.Prometheus.Asset;
using Xuan.Prometheus.Effects;

namespace Xuan.Prometheus
{
    /// <summary>
    /// 游戏的正式场景入口，负责把 Inspector 配置转换成启动参数并驱动 GameCore 生命周期。
    /// 场景中应只存在一个 Entry，运行时跨场景保留它所在的独立根对象。
    /// </summary>
    [DefaultExecutionOrder(-99)]
    public sealed class Entry : MonoBehaviour
    {
        [SerializeField] private string packageName = AssetKit.DefaultPackageName;
        [SerializeField] private string playerAddress = "Character_Yefa";
        [SerializeField] private string enemyAddress = "Enemy_Slime";
        /// <summary>
        /// 正式玩法使用的持久化 Effect 配置库；修改其引用的 Effect 资产后，下次启动玩法即可生效。
        /// </summary>
        [SerializeField] private EffectLibrary effectLibrary;
        [SerializeField] private List<Transform> enemySpawnPoints = new List<Transform>();
        [SerializeField, Min(0)] private int enemySpawnLimit = 1;

        private static Entry current;
        private GameCore core;

        /// <summary>
        /// 当前入口持有的 GameCore；Start 协程执行前可能为空。
        /// </summary>
        public GameCore Core => core;

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
        /// 创建 GameCore，并等待资源和初始玩法实体全部准备完成。
        /// </summary>
        private IEnumerator Start()
        {
            if (current != this)
                yield break;

            core = new GameCore();
            GameplayStartupOptions options = new GameplayStartupOptions(packageName, transform, effectLibrary, playerAddress, enemyAddress, enemySpawnPoints, enemySpawnLimit);
            yield return core.Initialize(options);
        }

        /// <summary>
        /// 将 Unity 帧循环转交给普通 C# GameCore，未完成初始化时 GameCore 会安全跳过更新。
        /// </summary>
        private void Update()
        {
            core?.OnUpdate(Time.deltaTime);
        }

        /// <summary>
        /// 入口被销毁时按依赖逆序释放玩法实体和资源句柄，并允许后续场景重新建立入口。
        /// </summary>
        private void OnDestroy()
        {
            if (current != this)
                return;

            core?.Dispose();
            core = null;
            current = null;
        }
    }
}
