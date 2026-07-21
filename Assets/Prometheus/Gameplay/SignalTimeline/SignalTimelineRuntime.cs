using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace Xuan.Prometheus
{
    /// <summary>
    /// Runtime information shared with every handler while one Signal Timeline is playing.
    /// </summary>
    public sealed class SignalContext
    {
        public SignalContext(GameObject owner, GameObject target = null, object userData = null)
        {
            Owner = owner;
            Target = target;
            UserData = userData;
        }

        public GameObject Owner { get; }
        public Transform Origin => Owner != null ? Owner.transform : null;
        public GameObject Target { get; }
        public object UserData { get; }
        public SignalTimelineAsset Timeline { get; internal set; }
        public float TimelineTime { get; internal set; }
    }

    /// <summary>Receives Signals emitted by a SignalTimelinePlayer.</summary>
    public interface ISignalHandler
    {
        bool CanHandle(Signal signal);
        void Handle(Signal signal, SignalContext context);
    }
}
