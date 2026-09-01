using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public struct InteractionData
{
    public RaycastHit hit;
    public GameObject interactor;
}
public interface IInteractable
{
    public void Interact(InteractionData data);
}
