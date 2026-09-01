using System;
using UnityEngine;

namespace Polyart
{

    public class HarvestInteraction : MonoBehaviour, IInteractable
    {
        public event Action<InteractionData> OnHarvested; // Event to notify GrownState

        public void Interact(InteractionData data)
        {
            // Trigger the event if there are any subscribers
            OnHarvested?.Invoke(data);
        }
    }
}