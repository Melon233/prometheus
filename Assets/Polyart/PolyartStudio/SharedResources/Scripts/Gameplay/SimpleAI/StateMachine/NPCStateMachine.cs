using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.Assertions;

namespace Polyart { 
    public class NPCStateMachine : StateManager<NPCStateMachine.NPCState>
    {
        public enum NPCState
        {
            Idle,
            Walk,
            Run,
            Rest,
            Sleep,
            Eat
        }

        public struct NPCStateContext
        {
            public NavMeshAgent agent;
            public NPCAnimationController animController;
            public Dictionary<NPCState, float> stateWeights;
            public bool instantBlend;
        }

        public float idleDurationMin = 5f;
        public float idleDurationMax = 10f;
        public AnimationClip idleAnim;
        public float idleWeight = 0.1f;

        public float walkSpeed = 0.75f;
        public AnimationClip walkAnim;
        public float walkWeight = 0.4f;

        public float runSpeed = 2f;
        public AnimationClip runAnim;
        public float runWeight = 0.2f;

        public float restDurationMin = 5f;
        public float restDurationMax = 10f;
        public AnimationClip restStartAnim;
        public AnimationClip restLoopAnim;
        public AnimationClip restEndAnim;
        public float restWeight = 0.2f;

        public float sleepDurationMin = 5f;
        public float sleepDurationMax = 10f;
        public AnimationClip sleepStartAnim;
        public AnimationClip sleepLoopAnim;
        public AnimationClip sleepEndAnim;
        public float sleepWeight = 0.1f;

        public AnimationClip eatAnim;
        public float eatWeight = 0.2f;

        public bool instantBlend = false;

        private NPCStateContext context;

        public NavMeshAgent agent;

        public bool[] supportedStates;
        private void AddReferences()
        {
            if (GetComponent<NPCAnimationController>() == null)
            {
                gameObject.AddComponent<NPCAnimationController>();
            }

            if (GetComponent<Animator>() == null)
            {
                gameObject.AddComponent<Animator>();
            }

            if (GetComponent<NavMeshAgent>() == null)
            {
                gameObject.AddComponent<NavMeshAgent>();
            }
        }

        private void OnEnable()
        {
            Initialize();
        }

        private void OnValidate()
        {
            Initialize();
        }

        private void Initialize()
        {
            AddReferences();
            GetReferences();
            InitializeSupportedStates();
            SetStateWeights();
            InitializeStates();
        }

        private void SetStateWeights()
        {
            context.stateWeights = new Dictionary<NPCState, float>();

            AddStateWeight(NPCState.Idle, idleWeight);
            AddStateWeight(NPCState.Walk, walkWeight);
            AddStateWeight(NPCState.Run, runWeight);
            AddStateWeight(NPCState.Rest, restWeight);
            AddStateWeight(NPCState.Sleep, sleepWeight);
            AddStateWeight(NPCState.Eat, eatWeight);
        }

        private void AddStateWeight(NPCState state, float weight)
        {
            context.stateWeights.Add(state, supportedStates[(int)state] ? weight : 0f);
        }

        private void InitializeSupportedStates()
        {
            if (supportedStates != null && supportedStates.Length == Enum.GetValues(typeof(NPCState)).Length)
                return;

            supportedStates = new bool[Enum.GetValues(typeof(NPCState)).Length];
            for (int i=0; i< supportedStates.Length; i++)
            {
                supportedStates[i] = false;
            }

            supportedStates[(int)NPCState.Idle] = true;
        }

        private void GetReferences()
        {
            context = new NPCStateContext();
            context.agent = GetComponent<NavMeshAgent>();
            context.animController = GetComponent<NPCAnimationController>();

            Assert.IsNotNull(context.animController, "Animator is Null");
            Assert.IsNotNull(context.agent, "Agent is Null");

            context.instantBlend = instantBlend;
        }

        private void InitializeStates()
        {
            if (states != null)
            {
                states.Clear();
            }

            states.Add(NPCState.Idle, new NPCIdleState(context, NPCState.Idle, supportedStates[(int)NPCState.Idle], idleAnim, idleDurationMin, idleDurationMax));
            states.Add(NPCState.Walk, new NPCMoveState(context, NPCState.Walk, supportedStates[(int)NPCState.Walk], walkAnim, walkSpeed));
            states.Add(NPCState.Run, new NPCMoveState(context, NPCState.Run, supportedStates[(int)NPCState.Run], runAnim, runSpeed));
            states.Add(NPCState.Rest, new NPCCycleState(context, NPCState.Rest, supportedStates[(int)NPCState.Rest], restDurationMin, restDurationMax, restStartAnim, restLoopAnim, restEndAnim));
            states.Add(NPCState.Sleep, new NPCCycleState(context, NPCState.Sleep, supportedStates[(int)NPCState.Sleep], sleepDurationMin, sleepDurationMax, sleepStartAnim, sleepLoopAnim, sleepEndAnim));
            states.Add(NPCState.Eat, new NPCOneShotState(context, NPCState.Eat, supportedStates[(int)NPCState.Eat], eatAnim));

            currentState = states[NPCState.Idle];
        }

        private void OnDisable()
        {
            if (context.animController != null)
            {
                context.animController.DisableAnimController();
            }
        }
    }

}