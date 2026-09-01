using System;

namespace Polyart
{
    public abstract class BaseState<EState> where EState : Enum
    {
        public EState stateKey {  get; private set; }

        public BaseState(EState key)
        {
            stateKey = key;
        }
        public abstract void EnterState();
        public abstract void ExitState();
        public abstract void UpdateState();
        public abstract EState GetNextState();
    }
}