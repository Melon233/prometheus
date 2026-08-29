namespace Xuan.Prometheus
{
    public class MainWorldState : State
    {
        public override void OnEnter()
        {
            // await Core.Asset.LoadSceneAsync("MainWorld");  // 通过当前 Core 的 AssetKit 异步加载主世界场景。
            // Ioc.UIKit.OpenPanel<HudPanel>();  // 打开HUD面板
        }

        public override void OnExit()
        {
            // Ioc.UIKit.ClosePanel<HudPanel>();  // 关闭HUD面板
        }
    }
}
