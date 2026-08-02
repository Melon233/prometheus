namespace Xuan.Prometheus
{
    public class LoginState : State
    {
        public override void OnEnter()
        {
            // Ioc.AssetKit.LoadSceneSync("Login");  // 加载登录场景
            // Ioc.UIKit.OpenPanel<LoginPanel>();
        }

        public override void OnExit()
        {
            // Ioc.UIKit.ClosePanel<LoginPanel>();  // 关闭登录面板
        }
    }
}