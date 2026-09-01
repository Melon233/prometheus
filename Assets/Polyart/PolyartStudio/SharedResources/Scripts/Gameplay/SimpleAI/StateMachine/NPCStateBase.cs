using System.Collections;
using System.Collections.Generic;
using Polyart;
using UnityEngine;
using static Polyart.NPCStateMachine;

public abstract class NPCStateBase : BaseState<NPCStateMachine.NPCState>
{
    protected NPCStateContext context;
    protected bool isStateImplemented;

    public NPCStateBase(NPCStateContext context, NPCStateMachine.NPCState key, bool isStateImplemented) : base(key)
    {
        this.context = context;
        this.isStateImplemented = isStateImplemented;
    }

    protected NPCState GetRandomEnumFromSubset(NPCState[] allowedStates)
    {
        Dictionary<NPCState, float> filteredWeights = new Dictionary<NPCState, float>();

        // Filter dictionary to include only allowed states
        foreach (NPCState state in allowedStates)
        {
            if (context.stateWeights.TryGetValue(state, out float weight))
            {
                if (weight > 0f)
                    filteredWeights[state] = weight;
            }
        }

        return GetRandomEnum(filteredWeights);
    }

    private NPCState GetRandomEnum(Dictionary<NPCState, float> weightedOptions)
    {
        float totalWeight = 0f;

        // Calculate total weight
        foreach (var item in weightedOptions)
        {
            totalWeight += item.Value;
        }

        // Random selection based on weight
        float randomValue = Random.Range(0, totalWeight);
        
        float cumulativeWeight = 0f;

        foreach (var item in weightedOptions)
        {
            cumulativeWeight += item.Value;
            if (randomValue < cumulativeWeight)
            {
                //Debug.Log($"{item.Key}, {randomValue}");
                return item.Key;
            }
        }

        return NPCState.Idle; // Fallback
    }


}
