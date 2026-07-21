using System.Collections.Generic;

namespace Xuan.Prometheus
{
    public interface IFsmKit
    {
    }
    public abstract class State
    {
        public IIoc Ioc { get; set; }
        public string StateName { get; set; }
        public abstract void OnEnter();
        public abstract void OnExit();
    }
    public class FsmKit : Kit, IFsmKit
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
    }
}