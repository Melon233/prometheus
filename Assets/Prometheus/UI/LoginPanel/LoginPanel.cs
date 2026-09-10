using System;
using Cysharp.Threading.Tasks;

namespace Xuan.Prometheus
{
    /// <summary>
    /// 登录界面。
    ///
    /// 它是一个正常的 <see cref="UIPanel"/>，因为登录发生在资源包**就绪之后**——
    /// 开屏与热更则不行，那两层必须随包体直出（见 <c>Bootstrap/BootScreen</c>）。
    ///
    /// 当前是**本地假登录**：点击即进入，不做账号校验、不拉存档、不碰网络层。
    /// 这与任务系统「先本地权威，后期再接服务器」的决定一致。
    /// 接入真实登录时，改动集中在 <see cref="OnEnterBtnClick"/> 一处：
    /// 把「直接完成」换成「发请求、等响应、失败给提示」，界面结构与流程契约都不需要动。
    ///
    /// 本面板**不得访问 <c>Core.Gameplay</c> 的任何 System**：登录时会话尚未建立，
    /// 那正是三段生命周期里 App 段与 Session 段的分界线。
    /// </summary>
    [UIPanelConfig("LoginPanel", UIPanelLayer.Overlay)]
    public sealed class LoginPanel : LoginPanelBase
    {
        /// <summary>等待玩家确认进入的完成源；面板重新打开时重建。</summary>
        private UniTaskCompletionSource enterCompletion;

        /// <summary>
        /// 等待玩家点击「进入游戏」。
        /// 启动流程 await 它，因此登录界面天然成为 App 段与 Session 段之间的闸门。
        /// </summary>
        public UniTask WaitForEnterAsync()
        {
            enterCompletion ??= new UniTaskCompletionSource();
            return enterCompletion.Task;
        }

        /// <summary>面板打开时重置等待状态，使重新登录能再次等待。</summary>
        protected override void OnOpen()
        {
            enterCompletion ??= new UniTaskCompletionSource();
            HintText.text = "本地登录（占位）";
            EnterBtn.interactable = true;
        }

        /// <summary>
        /// 面板关闭时结束尚未完成的等待，避免启动流程永远悬在这里。
        /// 关闭而未点击属于异常路径（例如会话被外部销毁），因此以取消而不是成功结束。
        /// </summary>
        protected override void OnClose()
        {
            UniTaskCompletionSource pending = enterCompletion;
            enterCompletion = null;
            pending?.TrySetCanceled();
        }

        /// <summary>处理进入游戏点击：本地假登录直接放行。</summary>
        protected override void OnEnterBtnClick()
        {
            // 立即置灰，避免玩家在会话建立期间重复点击——建立会话与加载场景是异步的，
            // 这段时间界面还在，但已经不该再接受输入。
            EnterBtn.interactable = false;
            HintText.text = "正在进入世界…";
            enterCompletion?.TrySetResult();
        }
    }
}
