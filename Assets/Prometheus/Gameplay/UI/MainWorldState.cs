namespace Xuan.Prometheus
{
    public class MainWorldState : State
    {
        public override void OnEnter()
        {
            Ioc.AssetKit.LoadSceneSync("MainWorld");  // 加载主世界场景
            Ioc.UIKit.OpenPanel<HudPanel>();  // 打开HUD面板
        }

        public override void OnExit()
        {
            Ioc.UIKit.ClosePanel<HudPanel>();  // 关闭HUD面板
        }
    }
}