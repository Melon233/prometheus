using System;
using System.Threading;
using Cysharp.Threading.Tasks;

namespace Xuan.Prometheus.Narrative
{
    /// <summary>
    /// 在玩法世界里演一段剧情图的统一入口。
    ///
    /// 它把「按地址加载剧情图 → 装配运行时端口 → 打开对话界面 → 进入舞台 → 演出 → 逆序还原」
    /// 这条固定流程收在一处。第三期 NpcSystem 的交互编排会调用它，本期它也可以被任何
    /// 需要播放一段剧情的入口直接调用。
    ///
    /// 舞台作用域由本方法持有：<c>INarrativeSystem.Stage</c> 是可写属性，契约要求调用方
    /// 用 <c>await using</c> 持有作用域并在退出前置空，剧情系统自己不创建也不销毁舞台。
    /// </summary>
    public static class NarrativePlayback
    {
        /// <summary>
        /// 加载并演绎一段剧情图。
        /// </summary>
        /// <param name="narrative">负责演绎的剧情系统；由调用方传入，本入口不做服务定位。</param>
        /// <param name="graphLocation">剧情图资产的 YooAsset 地址；剧情图按地址加载而非硬引用，避免整章剧情随场景常驻。</param>
        /// <param name="dialogueHost">对话界面宿主；由 UI 层实现并经组合根传入。</param>
        /// <param name="cancellationToken">外部取消令牌；取消时舞台与界面同样走完整套还原。</param>
        /// <returns>本次演绎的结束原因。</returns>
        public static async UniTask<StoryResult> PlayAsync(INarrativeSystem narrative, string graphLocation, IDialogueHost dialogueHost, CancellationToken cancellationToken = default)
        {
            if (narrative == null) throw new ArgumentNullException(nameof(narrative));
            if (string.IsNullOrWhiteSpace(graphLocation)) throw new ArgumentException("Story graph location cannot be empty.", nameof(graphLocation));
            if (dialogueHost == null) throw new ArgumentNullException(nameof(dialogueHost));

            NarrativeRuntimePorts ports = new NarrativeRuntimePorts();
            bool dialogueOpened = false;
            try
            {
                StoryGraph graph = await ports.Assets.LoadAsync<StoryGraph>(graphLocation, cancellationToken);
                IStoryAction root = graph.BuildOrThrow();

                // 视图必须在首次演绎之前注入，且演绎期间不可替换，因此在进入舞台之前就打开界面。
                narrative.SetView(dialogueHost.Open());
                dialogueOpened = true;

                await using (StageScope scope = await Stage.EnterAsync(graph.BuildStage(), ports.Services, cancellationToken))
                {
                    narrative.Stage = scope;
                    try
                    {
                        return await narrative.PlayAsync(root, graph.StoryId, cancellationToken);
                    }
                    finally
                    {
                        // 必须在作用域退出之前置空：还原动作会销毁作用域持有的运行时对象，
                        // 之后再读 Stage 拿到的就是一个已经失效的作用域。
                        narrative.Stage = null;
                    }
                }
            }
            finally
            {
                if (dialogueOpened) dialogueHost.Close();
                ports.Dispose();
            }
        }
    }
}
