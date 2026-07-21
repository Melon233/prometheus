// Place outside an Editor folder, for example: Assets/Scripts/Combat/AnimationEffectTimeline.cs

using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public enum AnimationTimelineEventType
{
    Effect,
    Audio
}

[Serializable]
public sealed class AnimationTimelineEvent
{
    [Range(0f, 1f)]
    [Tooltip("Position within the clip. 0 = start, 1 = end.")]
    public float normalizedTime;

    public AnimationTimelineEventType type;

    [Header("Effect")]
    public GameObject effectPrefab;
    [Tooltip("Optional socket name, resolved by AnimationEffectTimelinePlayer.")]
    public string socketName;
    public bool followSocket = true;
    public Vector3 localPosition;
    public Vector3 localEulerAngles;
    [Min(0f)] public float destroyAfterSeconds = 3f;

    [Header("Audio")]
    public AudioClip audioClip;
    [Range(0f, 1f)] public float volume = 1f;
}

[CreateAssetMenu(menuName = "Combat/Animation Effect Timeline", fileName = "AnimationEffectTimeline")]
public sealed class AnimationEffectTimeline : ScriptableObject
{
    [Tooltip("Used by the editor for duration and by the player for event timing.")]
    public AnimationClip animationClip;

    [Tooltip("Optional Animator state name. Leave blank when another system starts the animation.")]
    public string animatorStateName;

    public List<AnimationTimelineEvent> events = new List<AnimationTimelineEvent>();
}

/// <summary>
/// Plays the events in an AnimationEffectTimeline. Call Play(timeline) when the matching
/// animation starts. The Animator is optional if another system controls animation playback.
/// </summary>
public sealed class AnimationEffectTimelinePlayer : MonoBehaviour
{
    [Serializable]
    public struct Socket
    {
        public string name;
        public Transform transform;
    }

    [SerializeField] Animator animator;
    [SerializeField] AudioSource audioSource;
    [SerializeField] Transform defaultEffectParent;
    [SerializeField] Socket[] sockets;

    Coroutine playingRoutine;

    public void Play(AnimationEffectTimeline timeline)
    {
        if (timeline == null || timeline.animationClip == null)
        {
            Debug.LogWarning("Animation effect timeline or AnimationClip is missing.", this);
            return;
        }

        if (playingRoutine != null)
            StopCoroutine(playingRoutine);

        if (animator != null && !string.IsNullOrEmpty(timeline.animatorStateName))
            animator.Play(timeline.animatorStateName, 0, 0f);

        playingRoutine = StartCoroutine(PlayRoutine(timeline));
    }

    IEnumerator PlayRoutine(AnimationEffectTimeline timeline)
    {
        var sortedEvents = new List<AnimationTimelineEvent>(timeline.events);
        sortedEvents.Sort((a, b) => a.normalizedTime.CompareTo(b.normalizedTime));

        var elapsed = 0f;
        var nextEvent = 0;
        var clipLength = timeline.animationClip.length;

        while (nextEvent < sortedEvents.Count)
        {
            elapsed += Time.deltaTime;

            while (nextEvent < sortedEvents.Count &&
                   elapsed >= sortedEvents[nextEvent].normalizedTime * clipLength)
            {
                Fire(sortedEvents[nextEvent]);
                nextEvent++;
            }

            yield return null;
        }

        playingRoutine = null;
    }

    void Fire(AnimationTimelineEvent timelineEvent)
    {
        if (timelineEvent.type == AnimationTimelineEventType.Audio)
        {
            if (timelineEvent.audioClip != null && audioSource != null)
                audioSource.PlayOneShot(timelineEvent.audioClip, timelineEvent.volume);
            return;
        }

        if (timelineEvent.effectPrefab == null)
            return;

        var socket = FindSocket(timelineEvent.socketName);
        GameObject effect;

        if (timelineEvent.followSocket)
        {
            effect = Instantiate(timelineEvent.effectPrefab, socket);
            effect.transform.localPosition = timelineEvent.localPosition;
            effect.transform.localRotation = Quaternion.Euler(timelineEvent.localEulerAngles);
        }
        else
        {
            var position = socket.TransformPoint(timelineEvent.localPosition);
            var rotation = socket.rotation * Quaternion.Euler(timelineEvent.localEulerAngles);
            effect = Instantiate(timelineEvent.effectPrefab, position, rotation);
        }

        if (timelineEvent.destroyAfterSeconds > 0f)
            Destroy(effect, timelineEvent.destroyAfterSeconds);
    }

    Transform FindSocket(string socketName)
    {
        if (!string.IsNullOrEmpty(socketName))
        {
            foreach (var socket in sockets)
            {
                if (socket.transform != null && socket.name == socketName)
                    return socket.transform;
            }
        }

        return defaultEffectParent != null ? defaultEffectParent : transform;
    }
}
