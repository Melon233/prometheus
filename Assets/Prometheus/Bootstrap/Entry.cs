using Cysharp.Threading.Tasks;
using UnityEngine;

namespace Xuan.Prometheus.Bootstrap
{
    /// <summary>
    /// 游戏的正式场景入口，只负责创建并驱动 Core 生命周期。
    /// 跨场景运行时对象统一挂在 PersistentRoot 下。入口只回答"由谁组合这一局"。
    /// 场景中应只存在一个 Entry，运行时跨场景保留它所在的独立根对象。
    /// </summary>
    [DefaultExecutionOrder(-100)]
    public sealed class Entry : MonoBehaviour
    {
        /// <summary>保存当前场景中负责驱动正式启动链路的唯一入口组件。</summary>
        private static Entry current;

        /// <summary>由当前入口创建并驱动的唯一运行时核心。</summary>
        private Core runtimeCore;

        /// <summary>本次启动的流程状态机。</summary>
        private GameFlow flow;

        /// <summary>获取当前所处的启动阶段；流程尚未创建时为 Boot。</summary>
        public GameFlowStage FlowStage => flow == null ? GameFlowStage.Boot : flow.Stage;

        /// <summary>获取本次启动的流程与它持有的世界栈；启动尚未开始时为空。</summary>
        public GameFlow Flow => flow;

        /// <summary>当前入口持有的 Core；Start 协程执行前可能为空。</summary>
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
        /// 创建 Core，把启动过程整个交给 <see cref="GameFlow"/>。
        ///
        /// 入口本身只回答两件事：谁是唯一入口，以及由谁驱动 Core 的帧循环。
        /// 「启动经过哪些阶段」属于流程而不属于入口，因此不写在这里。
        /// </summary>
        private async void Start()
        {
            if (current != this) return;
            runtimeCore = new Core();
            flow = new GameFlow(runtimeCore);
            await flow.RunAsync();
        }

        /// <summary>将 Unity 帧循环转交给普通 C# Core，未完成初始化时 Core 不会驱动 Kit。</summary>
        private void Update()
        {
            runtimeCore?.OnUpdate(Time.deltaTime);
        }

        /// <summary>入口被销毁时按依赖逆序释放玩法实体和资源句柄，并允许后续场景重新建立入口。</summary>
        private void OnDestroy()
        {
            if (current != this) return;
            runtimeCore?.Dispose();
            runtimeCore = null;
            flow?.Dispose();
            flow = null;
            current = null;
        }
    }
}
