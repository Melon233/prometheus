namespace Xuan.Prometheus
{
    public class LoginState : State
    {
        public override void OnEnter()
        {
            // await Core.Asset.LoadSceneAsync("Login");  // 通过当前 Core 的 AssetKit 异步加载登录场景。
            // Ioc.UIKit.OpenPanel<LoginPanel>();
        }

        public override void OnExit()
        {
            // Ioc.UIKit.ClosePanel<LoginPanel>();  // 关闭登录面板
        }
    }
}
