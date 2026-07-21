using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;
using Xuan.Prometheus.Component;

namespace Xuan.Prometheus
{
    // public class BTCtx
    // {
    //     public NavMeshAgent agent;
    //     public float atk;
    //     public float atkInterval;
    //     public int curTargetIdx;
    //     public float detectRadius;
    //     public float hAngle;
    //     public Transform self;
    //     public float speedFactor;
    //     public float vAngle;
    //     public List<Transform> wayPoints;
    // }
    public interface ICtx
    {

    }
    public enum NodeStatus
    {
        Running,
        Success,
        Failure
    }

    public abstract class Node
    {
        protected List<Node> children = new();
        protected ICtx ctx;
        public abstract NodeStatus Execute();

        public void AddChild(Node child)
        {
            children.Add(child);
            child.ctx = ctx;
        }
    }
}