using System;
using System.Collections.Generic;
using UnityEngine;

namespace Polyart
{
    public abstract class StateManager<EState> : MonoBehaviour where EState : Enum
    {
        protected Dictionary<EState, BaseState<EState>> states = new Dictionary<EState, BaseState<EState>>();

        protected BaseState<EState> currentState;

        protected bool isTransitioningState = false;

        private void Start()
        {
            currentState.EnterState();
        }

        private void Update()
        {
            EState nextStateKey = currentState.GetNextState();

            if (nextStateKey.Equals(currentState.stateKey)) 
            {
                currentState.UpdateState();
            }
            else
            {
                TransitionToNextState(nextStateKey);
            }
        }

        public void TransitionToNextState(EState stateKey)
        {
            isTransitioningState = true;
            currentState.ExitState();
            currentState = states[stateKey];
            currentState.EnterState();
            isTransitioningState = false;
        }

    }
}