using System.Collections.Generic;

namespace Xuan.Prometheus
{
    /// <summary>定义有限状态机的状态注册、移除和切换能力，调用方不接触具体 Kit 实现。</summary>
    public interface IFsmKit : IKitContract
    {
        /// <summary>按状态名称注册一个可切换状态。</summary>
        void AddState(State state);

        /// <summary>移除一个已经注册的状态。</summary>
        void RemoveState(State state);

        /// <summary>退出当前状态并进入指定状态。</summary>
        void ChangeState(State state);
    }
    public abstract class State
    {
        public IIoc Ioc { get; set; }
        public string StateName { get; set; }
        public abstract void OnEnter();
        public abstract void OnExit();
    }
    internal sealed class FsmKit : Kit, IFsmKit
    {
        private Dictionary<string, State> stateDict = new Dictionary<string, State>();
        private State currentState = null;

        public void AddState(State state)
        {
            stateDict.Add(state.StateName, state);  // 添加状态到字典
        }
        public void RemoveState(State state)
        {
            stateDict.Remove(state.StateName);  // 移除状态
        }
        public void ChangeState(State state)
        {
            currentState?.OnExit();  // 当前状态退出
            currentState = state;  // 切换状态
            currentState.OnEnter();  // 当前状态进入
        }

        public override void AfterNew()
        {
        }

        public override void OnUpdate(float dt)
        {
        }

        public override void Dispose()
        {
        }
    }
}
