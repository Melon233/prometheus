using System;

namespace Xuan.Prometheus.Actor
{
    /// <summary>
    /// Defines the independently blockable capabilities exposed by a gameplay actor.
    /// </summary>
    [Flags]
    public enum ActorCapability : int
    {
        /// <summary>
        /// Represents an actor with no enabled capability.
        /// </summary>
        None = 0,

        /// <summary>
        /// Allows the actor to consume gameplay input from its current controller.
        /// </summary>
        Input = 1 << 0,

        /// <summary>
        /// Allows the actor to perform translational movement.
        /// </summary>
        Move = 1 << 1,

        /// <summary>
        /// Allows the actor to rotate toward a requested direction or target.
        /// </summary>
        Rotate = 1 << 2,

        /// <summary>
        /// Allows the actor to start a basic attack.
        /// </summary>
        BasicAttack = 1 << 3,

        /// <summary>
        /// Allows the actor to start an active skill.
        /// </summary>
        ActiveSkill = 1 << 4,

        /// <summary>
        /// Allows the actor to dodge.
        /// </summary>
        Dodge = 1 << 5,

        /// <summary>
        /// Allows the actor to jump.
        /// </summary>
        Jump = 1 << 6,

        /// <summary>
        /// Allows the actor to interact with gameplay objects.
        /// </summary>
        Interact = 1 << 7,

        /// <summary>
        /// Allows the actor to drive its gameplay camera subject and camera requests.
        /// </summary>
        Camera = 1 << 8,

        /// <summary>
        /// Allows the actor to receive and react to hit results.
        /// </summary>
        ReceiveHit = 1 << 9,

        /// <summary>
        /// Combines every capability declared by this runtime version.
        /// </summary>
        All = Input | Move | Rotate | BasicAttack | ActiveSkill | Dodge | Jump | Interact | Camera | ReceiveHit
    }
}
