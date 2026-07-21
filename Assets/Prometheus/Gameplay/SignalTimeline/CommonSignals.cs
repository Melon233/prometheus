using System;
using Spine.Unity;
using UnityEngine;
using Xuan.Prometheus.Component;

namespace Xuan.Prometheus
{
    // These are data-only examples. Their handlers are separate MonoBehaviour scripts.

    [Serializable]
    [SignalMenu("Animation/Play Spine Animation")]
    public sealed class PlaySpineAnimationSignal : Signal
    {
        [SerializeField] private AnimationReferenceAsset _animation;
        [SerializeField] private int _trackIndex;
        [SerializeField] private bool _loop;
        [SerializeField, Min(0f)] private float _mixDuration = 0.2f;

        public string AnimationName => _animation.Animation.Name;
        public int TrackIndex => _trackIndex;
        public bool Loop => _loop;
        public float MixDuration => _mixDuration;
    }

    [Serializable]
    [SignalMenu("VFX/Play")]
    public sealed class PlayVfxSignal : Signal
    {
        [SerializeField] private string _effectId;
        [SerializeField] private string _socketName;
        [SerializeField] private bool _followSocket = true;
        [SerializeField] private Vector3 _localPosition;
        [SerializeField] private Vector3 _localEulerAngles;

        public string EffectId => _effectId;
        public string SocketName => _socketName;
        public bool FollowSocket => _followSocket;
        public Vector3 LocalPosition => _localPosition;
        public Vector3 LocalEulerAngles => _localEulerAngles;
    }

    [Serializable]
    [SignalMenu("Audio/Play")]
    public sealed class PlayAudioSignal : Signal
    {
        [SerializeField] private AudioClip _clip;
        [SerializeField, Range(0f, 1f)] private float _volume = 1f;

        public AudioClip Clip => _clip;
        public float Volume => _volume;
    }

    [Serializable]
    [SignalMenu("Combat/Set Hitbox")]
    public sealed class SetHitboxSignal : Signal
    {
        [SerializeField] private string _hitboxId;
        [SerializeField] private bool _enabled = true;

        public string HitboxId => _hitboxId;
        public bool Enabled => _enabled;
    }

    /// <summary>A small test signal for verifying timeline dispatch.</summary>
    [Serializable]
    [SignalMenu("Debug/Log")]
    public sealed class DebugLogSignal : Signal
    {
        [SerializeField, TextArea] private string _message;

        public string Message => _message;
    }
}
