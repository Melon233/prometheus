using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class ChoppableTreeSettings : MonoBehaviour
{
    public int hitsToBreak = 5;
    [Range(0f, 1f)]
    public float branchSpawnProbability = 1f;
}
